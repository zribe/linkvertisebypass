using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;

namespace LinkvertiseBypass;

internal sealed record GraphQlRequest([property: JsonPropertyName("operationName")] string OperationName, [property: JsonPropertyName("query")] string Query, [property: JsonPropertyName("variables")] object Variables);
internal sealed record BootstrapResult(string Metadata, string Content, string UserId);

internal sealed class BrowserTransport : IAsyncDisposable
{
    private const string CheqScript = "https://euob.bizseasky.com/sxp/i/df82c4ef6536e4dee60601280bc80588.js?id=14473";
    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;
    private readonly IBrowserContext _context;
    private readonly IPage _page;
    private BrowserTransport(IPlaywright playwright, IBrowser browser, IBrowserContext context, IPage page) => (_playwright, _browser, _context, _page) = (playwright, browser, context, page);

    public static async Task<(BrowserTransport, string)> CreateAsync(string canonical, ProxyConnection? proxy, bool autoInstall)
    {
        var playwright = await Playwright.CreateAsync();
        var launch = new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = ["--disable-background-networking", "--disable-blink-features=AutomationControlled", "--disable-component-update", "--disable-default-apps", "--disable-extensions", "--disable-sync", "--metrics-recording-only", "--mute-audio", "--no-first-run"],
            Proxy = proxy?.PlaywrightProxy()
        };
        IBrowser browser;
        try { browser = await playwright.Chromium.LaunchAsync(launch); }
        catch (Exception error) when (autoInstall && NeedsInstall(error))
        {
            playwright.Dispose();
            InstallBrowser();
            playwright = await Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync(launch);
        }
        var context = await browser.NewContextAsync(new BrowserNewContextOptions { Locale = "en-US", UserAgent = LinkvertiseClient.BrowserUserAgent() });
        context.SetDefaultTimeout(15000);
        context.SetDefaultNavigationTimeout(15000);
        var page = await context.NewPageAsync();
        await page.RouteAsync(canonical, route => route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = "text/html", Body = "<!doctype html><html><head></head><body></body></html>" }));
        await page.GotoAsync(canonical, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var requestId = await page.EvaluateAsync<string>(
            """
            src => new Promise((resolve, reject) => {
            const timer = setTimeout(() => reject(new Error("CHEQ timeout")), 15000);
            window.traffic_validation_cheq_response_ng_jsonp_0 = (_, requestId) => { clearTimeout(timer); resolve(requestId || ""); };
            window.__ctcg_ct_14473_exec = undefined;
            const script = document.createElement("script");
            script.src = src; script.async = true; script.className = "ct_clicktrue_14473";
            script.setAttribute("data-ch", "cheq4ppc"); script.setAttribute("data-jsonp", "traffic_validation_cheq_response_ng_jsonp_0");
            script.onerror = () => reject(new Error("CHEQ script failed")); document.head.appendChild(script);
            })
            """,
            CheqScript);
        if (requestId.Length == 0)
        {
            await context.CloseAsync();
            await browser.CloseAsync();
            playwright.Dispose();
            throw new InvalidOperationException("CHEQ returned no request ID");
        }
        return (new BrowserTransport(playwright, browser, context, page), requestId);
    }

    public async Task<IReadOnlyList<string>> BatchAsync(IReadOnlyList<GraphQlRequest> requests, string referrer)
    {
        var encoded = await _page.EvaluateAsync<string>(
            """
            async payload => {
            const response = await fetch("https://publisher.linkvertise.com/graphql", {method: "POST", credentials: "include", headers: {"accept": "*/*", "content-type": "application/json"}, referrer: payload.referrer, body: payload.request});
            return JSON.stringify({status: response.status, body: await response.text()});
            }
            """,
            new { request = JsonSerializer.Serialize(requests), referrer });
        using var response = JsonDocument.Parse(encoded);
        var status = response.RootElement.GetProperty("status").GetInt32();
        if (status is < 200 or >= 300) throw new InvalidOperationException($"GraphQL HTTP {status}");
        using var items = JsonDocument.Parse(response.RootElement.GetProperty("body").GetString() ?? "");
        if (items.RootElement.ValueKind != JsonValueKind.Array || items.RootElement.GetArrayLength() != requests.Count) throw new InvalidOperationException("invalid GraphQL batch response");
        return items.RootElement.EnumerateArray().Select(item => item.GetRawText()).ToArray();
    }

    public async Task<BootstrapResult> BootstrapAsync(GraphQlRequest metadata, GraphQlRequest content, string referrer)
    {
        var encoded = await _page.EvaluateAsync<string>(
            """
            async payload => {
            const post = body => fetch("https://publisher.linkvertise.com/graphql", {method: "POST", credentials: "include", headers: {"accept": "*/*", "content-type": "application/json"}, referrer: payload.referrer, body}).then(response => response.text());
            const [metadata, content] = await Promise.all([post(payload.metadata), post(payload.content)]);
            return JSON.stringify({metadata, content, userId: "fallbackUserId"});
            }
            """,
            new { metadata = JsonSerializer.Serialize(metadata), content = JsonSerializer.Serialize(content), referrer });
        return JsonSerializer.Deserialize<BootstrapResult>(encoded, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("invalid bootstrap response");
    }

    public Task SendEventsAsync(params string[] urls) => _page.EvaluateAsync("""urls => { for (const url of urls) if (url.startsWith("http")) fetch(url, {credentials: "include", keepalive: true}).catch(() => {}); return true; }""", urls);
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _context.CloseAsync();
        }
        finally
        {
            try
            {
                await _browser.CloseAsync();
            }
            finally
            {
                _playwright.Dispose();
            }
        }
    }
    public static int InstallBrowser() => Microsoft.Playwright.Program.Main(["install", "chromium"]);
    private static bool NeedsInstall(Exception error) => error.Message.Contains("executable doesn't exist", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("playwright install", StringComparison.OrdinalIgnoreCase);
}
