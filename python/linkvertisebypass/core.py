from __future__ import annotations

import asyncio
import json
import re
import secrets
import time
import uuid
from dataclasses import dataclass, replace
from typing import Any
from urllib.parse import parse_qs, quote, unquote, urlsplit, urlunsplit

import httpx

from .browser import (
    GRAPHQL_ENDPOINT,
    BrowserTransport,
    browser_user_agent,
)
from .models import BypassOptions, BypassResponse, ProxyOptions, ProxyProvider

CONTENT_QUERY = """query getContent($identifier: PublicLinkIdentificationInput!, $task_args: TaskArgument) {
    getContent(input: $identifier, task_args: $task_args) {
        __typename
        ... on ContentAccessTaskSet { tasks {
            __typename id
            ... on PremiumTask { status }
            ... on WaitTask { status remainingWaitingTime adsTotal }
            ... on AdTask { status adIndex adsTotal payloadBag { taboola { session_id } } ads {
                completion_token countdown provider provider_additional_payload { taboola { available_event_url visible_event_url } }
            } }
        } }
        ... on DetailPageTargetData { type url paste }
    }
}"""
PROBE_QUERY = """query probe($identifier: PublicLinkIdentificationInput!, $task_args: TaskArgument) {
    linkByIdentifier(linkIdentificationInput: $identifier) { target_host isPublished }
    getContent(input: $identifier, task_args: $task_args) {
        __typename
        ... on ContentAccessTaskSet { tasks {
            __typename id
            ... on PremiumTask { status }
            ... on WaitTask { status remainingWaitingTime adsTotal }
            ... on AdTask { status adIndex adsTotal payloadBag { taboola { session_id } } ads {
                completion_token countdown provider provider_additional_payload { taboola { available_event_url visible_event_url } }
            } }
        } }
        ... on DetailPageTargetData { type url paste }
    }
}"""
COMPLETE_TASK_MUTATION = """mutation completeTask($identifier: PublicLinkIdentificationInput!, $task_id: String!, $task_args: TaskArgument) {
    completeTask(input: $identifier, task_id: $task_id, task_args: $task_args) {
        __typename id
        ... on AdTask { status }
        ... on PremiumTask { status }
        ... on WaitTask { status remainingWaitingTime adsTotal }
    }
}"""
METADATA_QUERY = """query getLinkByIdentifier($identifier: PublicLinkIdentificationInput!) {
    linkByIdentifier(linkIdentificationInput: $identifier) { target_host isPublished }
}"""
SUPPORTED_HOSTS = {
    "linkvertise.com",
    "link-center.net",
    "link-target.net",
    "link-target.org",
    "link-to.net",
    "link-hub.net",
    "up-to-down.net",
    "direct-link.net",
    "direct-links.net",
    "direct-links.org",
}


class RetryableError(RuntimeError):
    pass


@dataclass(slots=True)
class ProxyConfig:
    provider: ProxyProvider
    server: str
    username: str
    password: str
    country: str
    session_lifetime: str
    port: int
    identity: str = ""

    def playwright_config(self) -> dict[str, str]:
        result = {"server": self.server}
        if self.username:
            result.update(username=self.username, password=self.password)
        return result

    def authenticated_url(self) -> str:
        parts = urlsplit(self.server)
        if not self.username:
            return self.server
        host = (
            f"[{parts.hostname}]"
            if ":" in (parts.hostname or "")
            else parts.hostname or ""
        )
        host += f":{parts.port}" if parts.port else ""
        auth = f"{quote(self.username, safe='')}:{quote(self.password, safe='')}@"
        return urlunsplit((parts.scheme, auth + host, "", "", ""))


def bypass_sync(raw_url: str, options: BypassOptions | None = None) -> BypassResponse:
    return bypass(raw_url, options)


def bypass(raw_url: str, options: BypassOptions | None = None) -> BypassResponse:
    return asyncio.run(bypass_async(raw_url, options))


async def bypass_async(
    raw_url: str, options: BypassOptions | None = None
) -> BypassResponse:
    selected_options = options or BypassOptions.from_environment()
    started = time.perf_counter()
    try:
        async with asyncio.timeout(selected_options.timeout):
            response = await _resolve(raw_url, selected_options)
    except TimeoutError as error:
        raise TimeoutError(
            f"resolution exceeded {selected_options.timeout:g} seconds"
        ) from error
    response.elapsed_seconds = time.perf_counter() - started
    return response


async def _resolve(raw_url: str, options: BypassOptions) -> BypassResponse:
    source, canonical, identifier = _parse_link(raw_url)
    base_proxy = _proxy_from_options(options.proxy)
    used: set[str] = set()
    probes = 0
    attempts = 0
    while True:
        selected = base_proxy
        snapshot = None
        if base_proxy is not None and _rotatable(base_proxy):
            selected, snapshot, count = await _choose_fresh_proxy(
                base_proxy, identifier, canonical, options.proxy, used
            )
            probes += count
        attempts += 1
        try:
            response = await _resolve_once(
                source, canonical, identifier, selected, snapshot, options
            )
            response.proxy_attempts = attempts
            response.proxy_probes = probes
            if selected is not None:
                response.proxy_provider = selected.provider.value
                response.proxy_port = selected.port
            return response
        except Exception as error:
            if (
                base_proxy is None
                or not _rotatable(base_proxy)
                or not _is_retryable(error)
            ):
                raise


async def _resolve_once(
    source: str,
    canonical: str,
    identifier: dict[str, Any],
    proxy: ProxyConfig | None,
    snapshot: dict[str, Any] | None,
    options: BypassOptions,
) -> BypassResponse:
    expected_host = ""
    current = None
    if snapshot is not None:
        metadata = snapshot.get("linkByIdentifier") or {}
        if not metadata.get("isPublished"):
            raise RuntimeError("link is not published")
        expected_host = str(metadata.get("target_host") or "").strip().rstrip(".")
        current = snapshot.get("getContent") or {}
        if current.get("__typename") == "DetailPageTargetData":
            return _target_response(source, canonical, current, expected_host)
    try:
        transport, request_id = await BrowserTransport.create(
            canonical, proxy, options.auto_install_browser
        )
    except Exception as error:
        raise RetryableError(str(error)) from error
    try:
        user_id = "fallbackUserId"
        session_id = ""
        if current is None:
            metadata_payload = _request(
                "getLinkByIdentifier", METADATA_QUERY, {"identifier": identifier}
            )
            content_payload = _request(
                "getContent",
                CONTENT_QUERY,
                {
                    "identifier": identifier,
                    "task_args": _task_args(request_id, user_id, session_id, canonical),
                },
            )
            try:
                bootstrap = await transport.bootstrap(
                    metadata_payload, content_payload, canonical
                )
            except Exception as error:
                raise RetryableError(str(error)) from error
            metadata = (
                _decode(json.loads(bootstrap["metadata"])).get("linkByIdentifier") or {}
            )
            if not metadata.get("isPublished"):
                raise RuntimeError("link is not published")
            expected_host = str(metadata.get("target_host") or "").strip().rstrip(".")
            user_id = bootstrap.get("userId") or user_id
            current = _decode(json.loads(bootstrap["content"])).get("getContent") or {}
        for _ in range(10):
            task_args = _task_args(request_id, user_id, session_id, canonical)
            if current.get("__typename") == "DetailPageTargetData":
                return _target_response(source, canonical, current, expected_host)
            if current.get("__typename") != "ContentAccessTaskSet":
                raise RuntimeError(
                    f"unknown content response {current.get('__typename')!r}"
                )
            wait_task, ad_task = _select_tasks(current.get("tasks") or [])
            if wait_task is not None:
                completion, current = await _transition(
                    transport,
                    identifier,
                    wait_task,
                    task_args,
                    task_args,
                    "",
                    canonical,
                )
                completed = completion.get("completeTask") or {}
                if completed.get("status") != "DONE":
                    seconds = completed.get(
                        "remainingWaitingTime", wait_task.get("remainingWaitingTime")
                    )
                    if isinstance(seconds, (int, float)) and seconds > 0:
                        raise RetryableError(
                            f"proxy cooldown for about {seconds:g} seconds"
                        )
                    raise RetryableError("initial wait task was not released")
                continue
            if ad_task is not None:
                session_id = str(
                    (
                        ((ad_task.get("payloadBag") or {}).get("taboola") or {}).get(
                            "session_id"
                        )
                    )
                    or ""
                )
                completion_token = ""
                ads = ad_task.get("ads") or []
                if ads:
                    offer = ads[0]
                    completion_token = str(offer.get("completion_token") or "")
                    taboola = (offer.get("provider_additional_payload") or {}).get(
                        "taboola"
                    ) or {}
                    await transport.send_events(
                        str(taboola.get("available_event_url") or ""),
                        str(taboola.get("visible_event_url") or ""),
                    )
                    if options.task_delay > 0:
                        await asyncio.sleep(options.task_delay)
                completion_args = _task_args(
                    request_id, user_id, session_id, canonical, completion_token
                )
                next_args = _task_args(request_id, user_id, session_id, canonical)
                completion, current = await _transition(
                    transport,
                    identifier,
                    ad_task,
                    completion_args,
                    next_args,
                    completion_token,
                    canonical,
                )
                if (completion.get("completeTask") or {}).get("status") != "DONE":
                    raise RuntimeError("signed ad completion was rejected")
                continue
            raise RuntimeError("no actionable access task")
        raise RuntimeError("target was not returned after ten task rounds")
    finally:
        await transport.close()


async def _transition(
    transport: BrowserTransport,
    identifier: dict[str, Any],
    task: dict[str, Any],
    args: dict[str, Any],
    next_args: dict[str, Any],
    token: str,
    referrer: str,
) -> tuple[dict[str, Any], dict[str, Any]]:
    if token:
        args["completion_token"] = token
    requests = [
        _request(
            "completeTask",
            COMPLETE_TASK_MUTATION,
            {"identifier": identifier, "task_id": task["id"], "task_args": args},
        ),
        _request(
            "getContent",
            CONTENT_QUERY,
            {"identifier": identifier, "task_args": next_args},
        ),
    ]
    try:
        raw = await transport.graphql_batch(requests, referrer)
    except Exception as error:
        raise RetryableError(str(error)) from error
    return _decode(raw[0]), _decode(raw[1]).get("getContent") or {}


async def _choose_fresh_proxy(
    base: ProxyConfig,
    identifier: dict[str, Any],
    canonical: str,
    options: ProxyOptions,
    used: set[str],
) -> tuple[ProxyConfig, dict[str, Any], int]:
    probes = 0
    while True:
        candidates = _proxy_candidates(base, max(1, options.probe_batch), used)
        tasks = [
            asyncio.create_task(
                _probe(candidate, identifier, canonical, options.probe_timeout)
            )
            for candidate in candidates
        ]
        try:
            for future in asyncio.as_completed(tasks):
                candidate, snapshot, fresh = await future
                probes += 1
                if fresh:
                    return candidate, snapshot, probes
        finally:
            for task in tasks:
                task.cancel()
            await asyncio.gather(*tasks, return_exceptions=True)


async def _probe(
    proxy: ProxyConfig, identifier: dict[str, Any], canonical: str, timeout: float
) -> tuple[ProxyConfig, dict[str, Any], bool]:
    payload = _request(
        "probe",
        PROBE_QUERY,
        {
            "identifier": identifier,
            "task_args": {
                "action_id": _new_action_id(),
                "additional_data": {
                    "taboola": {
                        "user_id": "fallbackUserId",
                        "consent_string": "",
                        "url": canonical,
                        "external_referrer": "",
                        "session_id": "",
                    }
                },
            },
        },
    )
    headers = {
        "Content-Type": "application/json",
        "Origin": "https://linkvertise.com",
        "Referer": canonical,
        "User-Agent": browser_user_agent(),
    }
    try:
        async with httpx.AsyncClient(
            proxy=proxy.authenticated_url(), timeout=timeout, trust_env=False
        ) as client:
            response = await client.post(
                GRAPHQL_ENDPOINT, json=payload, headers=headers
            )
            response.raise_for_status()
            envelope = response.json()
        if envelope.get("errors"):
            return proxy, {}, False
        data = envelope.get("data") or {}
        return proxy, data, _fresh(data.get("getContent") or {})
    except (httpx.HTTPError, json.JSONDecodeError, TypeError, ValueError):
        return proxy, {}, False


def _proxy_from_options(options: ProxyOptions) -> ProxyConfig | None:
    if not options.enabled:
        return None
    server = options.server.strip()
    if options.provider == ProxyProvider.DATAIMPULSE:
        server = server or "http://gw.dataimpulse.com:10000"
    elif options.provider == ProxyProvider.IPROYAL:
        server = server or "http://geo.iproyal.com:12321"
    elif options.provider == ProxyProvider.CUSTOM and not server:
        raise ValueError("custom proxy server is required")
    parts = urlsplit(server)
    if parts.scheme not in {"http", "https"} or not parts.hostname:
        raise ValueError("invalid proxy server")
    username = options.username or unquote(parts.username or "")
    password = options.password or unquote(parts.password or "")
    host = f"[{parts.hostname}]" if ":" in parts.hostname else parts.hostname
    host += f":{parts.port}" if parts.port else ""
    country = options.country.strip().lower()
    lifetime = options.session_lifetime.strip().lower() or "10m"
    if country and not re.fullmatch(r"[a-z]{2}", country):
        raise ValueError("proxy country must be a two-letter code")
    if not re.fullmatch(r"[1-9][0-9]*[smhd]", lifetime):
        raise ValueError("IPRoyal session lifetime must look like 10m, 2h, or 30s")
    if options.provider == ProxyProvider.DATAIMPULSE and country:
        pattern = r"__cr\.[A-Za-z]{2}(?:,[A-Za-z]{2})*"
        username = (
            re.sub(pattern, f"__cr.{country}", username)
            if re.search(pattern, username)
            else username + f"__cr.{country}"
        )
    return ProxyConfig(
        options.provider,
        urlunsplit((parts.scheme, host, "", "", "")),
        username,
        password,
        country,
        lifetime,
        parts.port or 0,
    )


def _proxy_candidates(
    base: ProxyConfig, count: int, used: set[str]
) -> list[ProxyConfig]:
    result = []
    while len(result) < count:
        if base.provider == ProxyProvider.DATAIMPULSE:
            if not used:
                candidate = replace(base, identity=base.server)
            else:
                port = secrets.randbelow(10_001) + 10_000
                parts = urlsplit(base.server)
                host = (
                    f"[{parts.hostname}]"
                    if ":" in (parts.hostname or "")
                    else parts.hostname or ""
                )
                server = urlunsplit((parts.scheme, f"{host}:{port}", "", "", ""))
                candidate = replace(base, server=server, port=port, identity=server)
        else:
            session = secrets.token_hex(4)
            password = re.sub(
                r"_(?:country-[A-Za-z]{2}|session-[A-Za-z0-9]{8}|lifetime-[1-9][0-9]*[smhd]|forcerandom-1)",
                "",
                base.password,
            )
            password += f"_country-{base.country}" if base.country else ""
            password += f"_session-{session}_lifetime-{base.session_lifetime}"
            candidate = replace(base, password=password, identity=session)
        if candidate.identity not in used:
            used.add(candidate.identity)
            result.append(candidate)
    return result


def _rotatable(proxy: ProxyConfig) -> bool:
    return (
        proxy.provider == ProxyProvider.IPROYAL
        or proxy.provider == ProxyProvider.DATAIMPULSE
        and 10_000 <= proxy.port <= 20_000
    )


def _fresh(content: dict[str, Any]) -> bool:
    if content.get("__typename") == "DetailPageTargetData":
        return True
    return any(
        task.get("__typename") == "AdTask"
        or task.get("__typename") == "WaitTask"
        and (task.get("remainingWaitingTime") or 0) <= 0
        for task in content.get("tasks") or []
    )


def _parse_link(raw: str) -> tuple[str, str, dict[str, Any]]:
    source = urlsplit(raw.strip())
    if (
        source.scheme not in {"http", "https"}
        or (source.hostname or "").lower() not in SUPPORTED_HOSTS
    ):
        raise ValueError("unsupported Linkvertise URL")
    parts = [unquote(item) for item in source.path.strip("/").split("/") if item]
    if parts and parts[0].lower() == "access":
        parts = parts[1:]
    if len(parts) < 2:
        raise ValueError("unsupported Linkvertise path")
    if not parts[0].isdigit():
        raise ValueError("invalid Linkvertise user ID")
    user_id = parts[0]
    if len(parts) >= 3 and parts[1:3] == ["random", "dynamic"]:
        query = parse_qs(source.query)
        hash_value = (query.get("r") or [""])[0]
        if hash_value:
            value: dict[str, Any] = {
                "user_id": user_id,
                "hash": hash_value,
                "originates_from_adfly": (query.get("link_origin") or [""])[0]
                == "adfly",
            }
            version = (query.get("v") or [""])[0]
            if version.isdigit():
                value["version"] = int(version)
            canonical = urlunsplit(
                (
                    "https",
                    "linkvertise.com",
                    f"/{quote(user_id)}/random/dynamic",
                    f"r={quote(hash_value)}",
                    "",
                )
            )
            return source.geturl(), canonical, {"userIdAndHash": value}
    slug = parts[1]
    canonical = urlunsplit(
        ("https", "linkvertise.com", f"/{quote(user_id)}/{quote(slug)}", "", "")
    )
    return (
        source.geturl(),
        canonical,
        {"userIdAndUrl": {"user_id": user_id, "url": slug}},
    )


def _target_response(
    source: str, canonical: str, content: dict[str, Any], expected_host: str
) -> BypassResponse:
    value = str(content.get("url") or "").strip()
    destination = urlsplit(value)
    if destination.scheme in {"http", "https"} and destination.netloc:
        actual = (destination.hostname or "").lower().rstrip(".")
        expected = expected_host.lower().rstrip(".")
        if expected and actual != expected and not actual.endswith("." + expected):
            raise RuntimeError(
                f"target host {actual!r} does not match metadata host {expected!r}"
            )
        return BypassResponse(source, canonical, "url", value, expected_host)
    return BypassResponse(source, canonical, "text", str(content.get("paste") or ""))


def _task_args(
    request_id: str,
    user_id: str,
    session_id: str,
    canonical: str,
    completion_token: str = "",
) -> dict[str, Any]:
    result = {
        "request_id": request_id,
        "action_id": _new_action_id(),
        "additional_data": {
            "taboola": {
                "user_id": user_id,
                "consent_string": "",
                "url": canonical,
                "external_referrer": "",
                "session_id": session_id,
            }
        },
    }
    if completion_token:
        result["completion_token"] = completion_token
    return result


def _select_tasks(
    tasks: list[dict[str, Any]],
) -> tuple[dict[str, Any] | None, dict[str, Any] | None]:
    return next(
        (task for task in tasks if task.get("__typename") == "WaitTask"), None
    ), next((task for task in tasks if task.get("__typename") == "AdTask"), None)


def _request(operation: str, query: str, variables: dict[str, Any]) -> dict[str, Any]:
    return {"operationName": operation, "query": query, "variables": variables}


def _decode(envelope: dict[str, Any]) -> dict[str, Any]:
    errors = envelope.get("errors") or []
    if errors:
        raise RuntimeError(
            "GraphQL: " + "; ".join(str(item.get("message") or item) for item in errors)
        )
    return envelope.get("data") or {}


def _new_action_id() -> str:
    return (str(uuid.uuid4()) * 3)[:100]


def _is_retryable(error: Exception) -> bool:
    return isinstance(error, RetryableError) or any(
        value in str(error).lower()
        for value in ("cooldown", "timeout", "net::err_", "proxy", "connection", "cheq")
    )
