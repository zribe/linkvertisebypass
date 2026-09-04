from __future__ import annotations

import asyncio
import json
import platform
import subprocess
import sys
from typing import Any

from playwright.async_api import (
    Browser,
    BrowserContext,
    Page,
    Playwright,
    async_playwright,
)

CHEQ_SCRIPT = (
    "https://euob.bizseasky.com/sxp/i/df82c4ef6536e4dee60601280bc80588.js?id=14473"
)
GRAPHQL_ENDPOINT = "https://publisher.linkvertise.com/graphql"


def browser_user_agent() -> str:
    system = platform.system()
    value = (
        "Windows NT 10.0; Win64; x64"
        if system == "Windows"
        else "Macintosh; Intel Mac OS X 10_15_7"
        if system == "Darwin"
        else "X11; Linux x86_64"
    )
    return f"Mozilla/5.0 ({value}) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36"


def install_browser() -> None:
    subprocess.run(
        [sys.executable, "-m", "playwright", "install", "chromium"], check=True
    )


class BrowserTransport:
    def __init__(
        self,
        playwright: Playwright,
        browser: Browser,
        context: BrowserContext,
        page: Page,
    ) -> None:
        self._playwright = playwright
        self._browser = browser
        self._context = context
        self._page = page

    @classmethod
    async def create(
        cls, canonical: str, proxy: Any | None, auto_install: bool
    ) -> tuple[BrowserTransport, str]:
        manager = await async_playwright().start()
        launch: dict[str, Any] = {
            "headless": True,
            "args": [
                "--disable-background-networking",
                "--disable-blink-features=AutomationControlled",
                "--disable-component-update",
                "--disable-default-apps",
                "--disable-extensions",
                "--disable-sync",
                "--metrics-recording-only",
                "--mute-audio",
                "--no-first-run",
            ],
        }
        if proxy is not None:
            launch["proxy"] = proxy.playwright_config()
        try:
            browser = await manager.chromium.launch(**launch)
        except Exception as error:
            if not auto_install or not _needs_browser_install(error):
                await manager.stop()
                raise
            await manager.stop()
            await asyncio.to_thread(install_browser)
            manager = await async_playwright().start()
            browser = await manager.chromium.launch(**launch)
        context = await browser.new_context(
            locale="en-US", user_agent=browser_user_agent()
        )
        context.set_default_timeout(15_000)
        context.set_default_navigation_timeout(15_000)
        page = await context.new_page()

        async def route_handler(route: Any) -> None:
            await route.fulfill(
                status=200,
                content_type="text/html",
                body="<!doctype html><html><head></head><body></body></html>",
            )

        await page.route(canonical, route_handler)
        await page.goto(canonical, wait_until="domcontentloaded")
        request_id = await page.evaluate(
            """src => new Promise((resolve, reject) => {
                const timer = setTimeout(() => reject(new Error("CHEQ timeout")), 15000);
                window.traffic_validation_cheq_response_ng_jsonp_0 = (_, requestId) => {
                    clearTimeout(timer);
                    resolve(requestId || "");
                };
                window.__ctcg_ct_14473_exec = undefined;
                const script = document.createElement("script");
                script.src = src;
                script.async = true;
                script.className = "ct_clicktrue_14473";
                script.setAttribute("data-ch", "cheq4ppc");
                script.setAttribute("data-jsonp", "traffic_validation_cheq_response_ng_jsonp_0");
                script.onerror = () => reject(new Error("CHEQ script failed"));
                document.head.appendChild(script);
            })""",
            CHEQ_SCRIPT,
        )
        if not request_id:
            await context.close()
            await browser.close()
            await manager.stop()
            raise RuntimeError("CHEQ returned no request ID")
        return cls(manager, browser, context, page), str(request_id)

    async def close(self) -> None:
        try:
            await self._context.close()
        finally:
            try:
                await self._browser.close()
            finally:
                await self._playwright.stop()

    async def graphql_batch(
        self, requests: list[dict[str, Any]], referrer: str
    ) -> list[dict[str, Any]]:
        encoded = await self._page.evaluate(
            """async payload => {
                const response = await fetch("https://publisher.linkvertise.com/graphql", {
                    method: "POST", credentials: "include",
                    headers: {"accept": "*/*", "content-type": "application/json"},
                    referrer: payload.referrer, body: payload.request
                });
                return JSON.stringify({status: response.status, body: await response.text()});
            }""",
            {
                "request": json.dumps(requests, separators=(",", ":")),
                "referrer": referrer,
            },
        )
        response = json.loads(encoded)
        if not 200 <= response["status"] < 300:
            raise RuntimeError(f"GraphQL HTTP {response['status']}")
        items = json.loads(response["body"])
        if not isinstance(items, list) or len(items) != len(requests):
            raise RuntimeError(
                f"GraphQL batch returned {len(items) if isinstance(items, list) else 0} of {len(requests)} responses"
            )
        return items

    async def bootstrap(
        self, metadata: dict[str, Any], content: dict[str, Any], referrer: str
    ) -> dict[str, str]:
        encoded = await self._page.evaluate(
            """async payload => {
                const post = body => fetch("https://publisher.linkvertise.com/graphql", {
                    method: "POST", credentials: "include",
                    headers: {"accept": "*/*", "content-type": "application/json"},
                    referrer: payload.referrer, body
                }).then(response => response.text());
                const [metadata, content] = await Promise.all([post(payload.metadata), post(payload.content)]);
                return JSON.stringify({metadata, content, userId: "fallbackUserId"});
            }""",
            {
                "metadata": json.dumps(metadata, separators=(",", ":")),
                "content": json.dumps(content, separators=(",", ":")),
                "referrer": referrer,
            },
        )
        return json.loads(encoded)

    async def send_events(self, *urls: str) -> None:
        filtered = [url for url in urls if url.startswith(("http://", "https://"))]
        if filtered:
            await self._page.evaluate(
                """urls => {
                for (const url of urls) fetch(url, {credentials: "include", keepalive: true}).catch(() => {});
                return true;
            }""",
                filtered,
            )


def _needs_browser_install(error: Exception) -> bool:
    message = str(error).lower()
    return "executable doesn't exist" in message or "playwright install" in message
