from __future__ import annotations

import os
from dataclasses import dataclass, field
from enum import StrEnum


class ProxyProvider(StrEnum):
    DATAIMPULSE = "dataimpulse"
    IPROYAL = "iproyal"
    CUSTOM = "custom"


def _first_environment(*names: str) -> str:
    return next(
        (value.strip() for name in names if (value := os.getenv(name, "")).strip()), ""
    )


def _environment_bool(name: str, fallback: bool) -> bool:
    value = os.getenv(name, "").strip().lower()
    if not value:
        return fallback
    return value in {"1", "true", "yes", "on"}


@dataclass(slots=True)
class ProxyOptions:
    enabled: bool = False
    provider: ProxyProvider = ProxyProvider.DATAIMPULSE
    server: str = ""
    username: str = ""
    password: str = ""
    country: str = "nl"
    session_lifetime: str = "10m"
    probe_batch: int = 6
    probe_timeout: float = 5.0

    @classmethod
    def direct(cls) -> ProxyOptions:
        return cls(enabled=False)

    @classmethod
    def dataimpulse(
        cls, username: str, password: str, country: str = "nl"
    ) -> ProxyOptions:
        return cls(
            enabled=True,
            provider=ProxyProvider.DATAIMPULSE,
            username=username,
            password=password,
            country=country,
        )

    @classmethod
    def iproyal(
        cls,
        username: str,
        password: str,
        country: str = "nl",
        session_lifetime: str = "10m",
    ) -> ProxyOptions:
        return cls(
            enabled=True,
            provider=ProxyProvider.IPROYAL,
            username=username,
            password=password,
            country=country,
            session_lifetime=session_lifetime,
        )

    @classmethod
    def custom(
        cls, server: str, username: str = "", password: str = ""
    ) -> ProxyOptions:
        return cls(
            enabled=True,
            provider=ProxyProvider.CUSTOM,
            server=server,
            username=username,
            password=password,
        )

    @classmethod
    def from_environment(cls) -> ProxyOptions:
        server = _first_environment("LINKVERTISEBYPASS_PROXY_SERVER", "LINKVERTISEBYPASS_PROXY")
        username = os.getenv("LINKVERTISEBYPASS_PROXY_USERNAME", "")
        password = os.getenv("LINKVERTISEBYPASS_PROXY_PASSWORD", "")
        configured = bool(server or username or password)
        try:
            provider = ProxyProvider(
                os.getenv("LINKVERTISEBYPASS_PROXY_PROVIDER", "dataimpulse").strip().lower()
            )
        except ValueError:
            provider = ProxyProvider.DATAIMPULSE
        return cls(
            enabled=_environment_bool("LINKVERTISEBYPASS_PROXY_ENABLED", configured),
            provider=provider,
            server=server,
            username=username,
            password=password,
            country=os.getenv("LINKVERTISEBYPASS_PROXY_COUNTRY", "nl").strip().lower()
            or "nl",
            session_lifetime=os.getenv("LINKVERTISEBYPASS_PROXY_SESSION_LIFETIME", "10m")
            .strip()
            .lower()
            or "10m",
        )


@dataclass(slots=True)
class BypassOptions:
    proxy: ProxyOptions = field(default_factory=ProxyOptions.from_environment)
    task_delay: float = 0.05
    timeout: float = 180.0
    auto_install_browser: bool = True

    @classmethod
    def from_environment(cls) -> BypassOptions:
        return cls()


@dataclass(slots=True)
class BypassResponse:
    source_url: str
    canonical_url: str
    type: str
    value: str
    verified_host: str = ""
    proxy_provider: str = ""
    proxy_port: int = 0
    proxy_probes: int = 0
    proxy_attempts: int = 0
    elapsed_seconds: float = 0.0

    def to_dict(self) -> dict[str, object]:
        result: dict[str, object] = {
            "sourceUrl": self.source_url,
            "canonicalUrl": self.canonical_url,
            "type": self.type,
            "value": self.value,
            "proxyProbes": self.proxy_probes,
            "proxyAttempts": self.proxy_attempts,
            "elapsedMilliseconds": round(self.elapsed_seconds * 1000),
        }
        if self.verified_host:
            result["verifiedHost"] = self.verified_host
        if self.proxy_provider:
            result["proxyProvider"] = self.proxy_provider
        if self.proxy_port:
            result["proxyPort"] = self.proxy_port
        return result
