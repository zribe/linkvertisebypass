from __future__ import annotations

import asyncio
import json
import re
from typing import Any

from curl_cffi import requests

from .fingerprint import collector_url

GRAPHQL_ENDPOINT = "https://publisher.linkvertise.com/graphql"
# The TLS and HTTP identity must match the embedded CHEQ browser fingerprint on every host OS.
TLS_USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36"
TLS_CLIENT_HINT = '"Chromium";v="146", "Google Chrome";v="146", "Not_A Brand";v="99"'
REQUEST_ID_PATTERN = re.compile(r'"req"\s*:\s*"([^"]+)"')


def tls_user_agent() -> str:
    return TLS_USER_AGENT


class TlsTransport:
    def __init__(self, canonical: str, proxy: str) -> None:
        self._canonical = canonical
        self._proxy = proxy
        self._session = requests.Session()

    @classmethod
    async def create(cls, canonical: str, proxy: Any | None) -> tuple[TlsTransport, str]:
        transport = cls(canonical, proxy.authenticated_url() if proxy else "")
        try:
            response = await transport._request(
                "GET", collector_url(canonical), transport._script_headers()
            )
            match = REQUEST_ID_PATTERN.search(response.text)
            if match is None:
                raise RuntimeError("CHEQ returned no request ID")
            return transport, match.group(1)
        except Exception:
            await transport.close()
            raise

    async def close(self) -> None:
        await asyncio.to_thread(self._session.close)

    async def graphql_batch(
        self, payloads: list[dict[str, Any]], referrer: str
    ) -> list[dict[str, Any]]:
        response = await self._request(
            "POST",
            GRAPHQL_ENDPOINT,
            self._graphql_headers(referrer),
            json.dumps(payloads, separators=(",", ":")),
        )
        items = response.json()
        if not isinstance(items, list) or len(items) != len(payloads):
            count = len(items) if isinstance(items, list) else 0
            raise RuntimeError(
                f"GraphQL batch returned {count} of {len(payloads)} responses"
            )
        return items

    async def bootstrap(
        self, metadata: dict[str, Any], content: dict[str, Any], referrer: str
    ) -> dict[str, str]:
        items = await self.graphql_batch([metadata, content], referrer)
        return {
            "metadata": json.dumps(items[0], separators=(",", ":")),
            "content": json.dumps(items[1], separators=(",", ":")),
            "userId": "fallbackUserId",
        }

    async def send_events(self, *urls: str) -> None:
        valid = [url for url in urls if url.startswith(("http://", "https://"))]
        for index, url in enumerate(valid):
            try:
                await self._request("GET", url, self._event_headers())
            except Exception:
                pass
            if index + 1 < len(valid):
                await asyncio.sleep(0.05)

    async def _request(
        self, method: str, url: str, headers: dict[str, str], data: str | None = None
    ) -> Any:
        options: dict[str, Any] = {
            "headers": headers,
            "impersonate": "chrome146",
            "default_headers": False,
            "allow_redirects": True,
            "timeout": 25,
        }
        if self._proxy:
            options["proxy"] = self._proxy
        if data is not None:
            options["data"] = data
        response = await asyncio.to_thread(
            self._session.request, method, url, **options
        )
        response.raise_for_status()
        return response

    @staticmethod
    def _common_headers() -> dict[str, str]:
        return {
            "accept": "*/*",
            "accept-language": "en-US,en;q=0.9",
            "sec-ch-ua": TLS_CLIENT_HINT,
            "sec-ch-ua-mobile": "?0",
            "sec-ch-ua-platform": '"Windows"',
            "user-agent": TLS_USER_AGENT,
        }

    def _script_headers(self) -> dict[str, str]:
        return self._common_headers() | {
            "referer": self._canonical,
            "sec-fetch-dest": "script",
            "sec-fetch-mode": "no-cors",
            "sec-fetch-site": "cross-site",
        }

    def _graphql_headers(self, referrer: str) -> dict[str, str]:
        return self._common_headers() | {
            "content-type": "application/json",
            "origin": "https://linkvertise.com",
            "referer": referrer,
            "sec-fetch-dest": "empty",
            "sec-fetch-mode": "cors",
            "sec-fetch-site": "same-site",
        }

    def _event_headers(self) -> dict[str, str]:
        return self._common_headers() | {
            "referer": self._canonical,
            "sec-fetch-dest": "empty",
            "sec-fetch-mode": "cors",
            "sec-fetch-site": "cross-site",
        }
