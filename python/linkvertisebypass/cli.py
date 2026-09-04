import argparse
import json

from .browser import install_browser
from .core import bypass
from .models import BypassOptions, ProxyProvider


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("url", nargs="?")
    parser.add_argument("--install-browser", action="store_true")
    parser.add_argument("--timeout", type=float, default=180.0)
    parser.add_argument("--proxy", choices=["off", "dataimpulse", "iproyal", "custom"])
    parser.add_argument("--country")
    args = parser.parse_args()
    if args.install_browser:
        install_browser()
        return
    raw_url = args.url or input("Linkvertise URL: ").strip()
    options = BypassOptions.from_environment()
    options.timeout = args.timeout
    if args.proxy:
        options.proxy.enabled = args.proxy != "off"
        if options.proxy.enabled:
            options.proxy.provider = ProxyProvider(args.proxy)
    if args.country:
        options.proxy.country = args.country.lower()
    response = bypass(raw_url, options)
    print(json.dumps(response.to_dict(), indent=2))


if __name__ == "__main__":
    main()
