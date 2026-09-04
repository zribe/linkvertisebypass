using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LinkvertiseBypass;

public static class LinkvertiseClient
{
    private const string Endpoint = "https://publisher.linkvertise.com/graphql";
    private const string ContentQuery = """query getContent($identifier: PublicLinkIdentificationInput!, $task_args: TaskArgument) { getContent(input: $identifier, task_args: $task_args) { __typename ... on ContentAccessTaskSet { tasks { __typename id ... on PremiumTask { status } ... on WaitTask { status remainingWaitingTime adsTotal } ... on AdTask { status adIndex adsTotal payloadBag { taboola { session_id } } ads { completion_token countdown provider provider_additional_payload { taboola { available_event_url visible_event_url } } } } } } ... on DetailPageTargetData { type url paste } } }""";
    private const string ProbeQuery = """query probe($identifier: PublicLinkIdentificationInput!, $task_args: TaskArgument) { linkByIdentifier(linkIdentificationInput: $identifier) { target_host isPublished } getContent(input: $identifier, task_args: $task_args) { __typename ... on ContentAccessTaskSet { tasks { __typename id ... on PremiumTask { status } ... on WaitTask { status remainingWaitingTime adsTotal } ... on AdTask { status adIndex adsTotal payloadBag { taboola { session_id } } ads { completion_token countdown provider provider_additional_payload { taboola { available_event_url visible_event_url } } } } } } ... on DetailPageTargetData { type url paste } } }""";
    private const string CompleteMutation = """mutation completeTask($identifier: PublicLinkIdentificationInput!, $task_id: String!, $task_args: TaskArgument) { completeTask(input: $identifier, task_id: $task_id, task_args: $task_args) { __typename id ... on AdTask { status } ... on PremiumTask { status } ... on WaitTask { status remainingWaitingTime adsTotal } } }""";
    private const string MetadataQuery = """query getLinkByIdentifier($identifier: PublicLinkIdentificationInput!) { linkByIdentifier(linkIdentificationInput: $identifier) { target_host isPublished } }""";
    private static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase) { "linkvertise.com", "link-center.net", "link-target.net", "link-target.org", "link-to.net", "link-hub.net", "up-to-down.net", "direct-link.net", "direct-links.net", "direct-links.org" };

    public static int InstallBrowser() => BrowserTransport.InstallBrowser();

    public static BypassResponse Bypass(string rawUrl, BypassOptions? options = null, CancellationToken cancellationToken = default) =>
        BypassAsync(rawUrl, options, cancellationToken).GetAwaiter().GetResult();

    public static async Task<BypassResponse> BypassAsync(string rawUrl, BypassOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= BypassOptions.FromEnvironment();
        var timer = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);
        var response = await ResolveAsync(rawUrl, options, timeout.Token);
        return response with { Elapsed = timer.Elapsed };
    }

    private static async Task<BypassResponse> ResolveAsync(string rawUrl, BypassOptions options, CancellationToken token)
    {
        var (source, canonical, identifier) = ParseLink(rawUrl);
        var pool = ProxyPool.Create(options.Proxy);
        var probes = 0;
        var attempts = 0;
        while (true)
        {
            ProxyConnection? proxy = null;
            JsonObject? snapshot = null;
            if (pool is not null)
            {
                if (pool.Rotatable)
                {
                    var choice = await ChooseProxyAsync(pool, identifier, canonical, options.Proxy, token);
                    (proxy, snapshot) = (choice.Proxy, choice.Snapshot);
                    probes += choice.Probes;
                }
                else proxy = pool.Next(1)[0];
            }
            attempts++;
            try
            {
                var response = await ResolveOnceAsync(source, canonical, identifier, proxy, snapshot, options, token);
                return response with { ProxyAttempts = attempts, ProxyProbes = probes, ProxyProvider = proxy?.Provider.ToString().ToLowerInvariant(), ProxyPort = proxy?.Port ?? 0 };
            }
            catch (Exception error) when (pool is not null && pool.Rotatable && Retryable(error)) { token.ThrowIfCancellationRequested(); }
        }
    }

    private static async Task<BypassResponse> ResolveOnceAsync(Uri source, string canonical, JsonObject identifier, ProxyConnection? proxy, JsonObject? snapshot, BypassOptions options, CancellationToken token)
    {
        var host = "";
        JsonObject? current = null;
        if (snapshot is not null)
        {
            var metadata = snapshot["linkByIdentifier"]?.AsObject() ?? [];
            if (metadata["isPublished"]?.GetValue<bool>() != true) throw new InvalidOperationException("link is not published");
            host = Text(metadata["target_host"]).Trim().TrimEnd('.');
            current = snapshot["getContent"]?.AsObject();
            if (Text(current?["__typename"]) == "DetailPageTargetData") return Target(source, canonical, current!, host);
        }
        BrowserTransport browser;
        string requestId;
        try { (browser, requestId) = await BrowserTransport.CreateAsync(canonical, proxy, options.AutoInstallBrowser); }
        catch (Exception error) { throw new RetryableException(error.Message, error); }
        await using (browser)
        {
            var userId = "fallbackUserId";
            var sessionId = "";
            if (current is null)
            {
                BootstrapResult bootstrap;
                try
                {
                    bootstrap = await browser.BootstrapAsync(Request("getLinkByIdentifier", MetadataQuery, new { identifier = identifier.DeepClone() }), Request("getContent", ContentQuery, new { identifier = identifier.DeepClone(), task_args = Args(requestId, userId, sessionId, canonical) }), canonical);
                }
                catch (Exception error) { throw new RetryableException(error.Message, error); }
                var metadata = Decode(bootstrap.Metadata)["linkByIdentifier"]?.AsObject() ?? [];
                if (metadata["isPublished"]?.GetValue<bool>() != true) throw new InvalidOperationException("link is not published");
                host = Text(metadata["target_host"]).Trim().TrimEnd('.');
                userId = bootstrap.UserId.Length > 0 ? bootstrap.UserId : userId;
                current = Decode(bootstrap.Content)["getContent"]?.AsObject() ?? [];
            }
            for (var round = 0; round < 10; round++)
            {
                token.ThrowIfCancellationRequested();
                if (Text(current["__typename"]) == "DetailPageTargetData") return Target(source, canonical, current, host);
                if (Text(current["__typename"]) != "ContentAccessTaskSet") throw new InvalidOperationException("unknown content response");
                var (wait, ad) = Tasks(current["tasks"] as JsonArray);
                if (wait is not null)
                {
                    var (completion, next) = await TransitionAsync(browser, identifier, wait, Args(requestId, userId, sessionId, canonical), Args(requestId, userId, sessionId, canonical), "", canonical);
                    var completed = completion["completeTask"]?.AsObject() ?? [];
                    if (Text(completed["status"]) != "DONE")
                    {
                        var seconds = Number(completed["remainingWaitingTime"]) ?? Number(wait["remainingWaitingTime"]);
                        if (seconds is > 0) throw new RetryableException($"proxy cooldown for about {seconds} seconds");
                        throw new RetryableException("initial wait task was not released");
                    }
                    current = next;
                    continue;
                }
                if (ad is not null)
                {
                    sessionId = Text(ad["payloadBag"]?["taboola"]?["session_id"]);
                    var completionToken = "";
                    if (ad["ads"] is JsonArray { Count: > 0 } ads && ads[0] is JsonObject offer)
                    {
                        completionToken = Text(offer["completion_token"]);
                        var taboola = offer["provider_additional_payload"]?["taboola"];
                        await browser.SendEventsAsync(Text(taboola?["available_event_url"]), Text(taboola?["visible_event_url"]));
                        if (options.TaskDelay > TimeSpan.Zero) await Task.Delay(options.TaskDelay, token);
                    }
                    var transition = await TransitionAsync(browser, identifier, ad, Args(requestId, userId, sessionId, canonical, completionToken), Args(requestId, userId, sessionId, canonical), completionToken, canonical);
                    if (Text(transition.Completion["completeTask"]?["status"]) != "DONE") throw new InvalidOperationException("signed ad completion was rejected");
                    current = transition.Content;
                    continue;
                }
                throw new InvalidOperationException("no actionable access task");
            }
            throw new InvalidOperationException("target was not returned after ten task rounds");
        }
    }

    private static async Task<(JsonObject Completion, JsonObject Content)> TransitionAsync(BrowserTransport browser, JsonObject identifier, JsonObject task, JsonObject args, JsonObject nextArgs, string completionToken, string referrer)
    {
        if (completionToken.Length > 0) args["completion_token"] = completionToken;
        var raw = await browser.BatchAsync([
            Request("completeTask", CompleteMutation, new { identifier = identifier.DeepClone(), task_id = Text(task["id"]), task_args = args }),
            Request("getContent", ContentQuery, new { identifier = identifier.DeepClone(), task_args = nextArgs })
        ], referrer);
        return (Decode(raw[0]), Decode(raw[1])["getContent"]?.AsObject() ?? []);
    }

    private static async Task<(ProxyConnection Proxy, JsonObject Snapshot, int Probes)> ChooseProxyAsync(ProxyPool pool, JsonObject identifier, string canonical, ProxyOptions options, CancellationToken token)
    {
        var probes = 0;
        while (true)
        {
            using var batch = CancellationTokenSource.CreateLinkedTokenSource(token);
            var pending = pool.Next(Math.Max(1, options.ProbeBatch)).Select(proxy => ProbeAsync(proxy, identifier, canonical, options.ProbeTimeout, batch.Token)).ToList();
            while (pending.Count > 0)
            {
                var task = await Task.WhenAny(pending);
                pending.Remove(task);
                var result = await task;
                probes++;
                if (result.Fresh)
                {
                    await batch.CancelAsync();
                    await Task.WhenAll(pending);
                    return (result.Proxy, result.Snapshot, probes);
                }
            }
            token.ThrowIfCancellationRequested();
        }
    }

    private static async Task<(ProxyConnection Proxy, JsonObject Snapshot, bool Fresh)> ProbeAsync(ProxyConnection proxy, JsonObject identifier, string canonical, TimeSpan timeout, CancellationToken token)
    {
        try
        {
            using var handler = new SocketsHttpHandler { Proxy = proxy.WebProxy(), UseProxy = true, ConnectTimeout = timeout };
            using var client = new HttpClient(handler) { Timeout = timeout };
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.TryAddWithoutValidation("Origin", "https://linkvertise.com");
            request.Headers.Referrer = new Uri(canonical);
            request.Headers.UserAgent.ParseAdd(BrowserUserAgent());
            request.Content = JsonContent.Create(Request("probe", ProbeQuery, new { identifier = identifier.DeepClone(), task_args = new { action_id = ActionId(), additional_data = new { taboola = new { user_id = "fallbackUserId", consent_string = "", url = canonical, external_referrer = "", session_id = "" } } } }));
            using var response = await client.SendAsync(request, token);
            if (!response.IsSuccessStatusCode) return (proxy, [], false);
            var envelope = JsonNode.Parse(await response.Content.ReadAsStringAsync(token))?.AsObject();
            if (envelope is null || envelope["errors"] is JsonArray { Count: > 0 }) return (proxy, [], false);
            var data = envelope["data"]?.AsObject() ?? [];
            return (proxy, data, Fresh(data["getContent"]?.AsObject() ?? []));
        }
        catch { return (proxy, [], false); }
    }

    private static bool Fresh(JsonObject content) => Text(content["__typename"]) == "DetailPageTargetData" || (content["tasks"] as JsonArray)?.OfType<JsonObject>().Any(task => Text(task["__typename"]) == "AdTask" || Text(task["__typename"]) == "WaitTask" && (Number(task["remainingWaitingTime"]) ?? 0) <= 0) == true;

    private static (Uri, string, JsonObject) ParseLink(string raw)
    {
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var source) || source.Scheme is not ("http" or "https") || !Hosts.Contains(source.Host)) throw new ArgumentException("unsupported Linkvertise URL");
        var parts = source.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToList();
        if (parts.Count > 0 && parts[0].Equals("access", StringComparison.OrdinalIgnoreCase)) parts.RemoveAt(0);
        if (parts.Count < 2) throw new ArgumentException("unsupported Linkvertise path");
        if (!ulong.TryParse(parts[0], out _)) throw new ArgumentException("invalid Linkvertise user ID");
        var userId = parts[0];
        if (parts.Count >= 3 && parts[1] == "random" && parts[2] == "dynamic")
        {
            var query = source.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(value => value.Split('=', 2)).ToDictionary(value => Uri.UnescapeDataString(value[0]), value => Uri.UnescapeDataString(value.ElementAtOrDefault(1) ?? ""));
            if (query.TryGetValue("r", out var hash) && hash.Length > 0)
            {
                var value = new JsonObject { ["user_id"] = userId, ["hash"] = hash, ["originates_from_adfly"] = query.GetValueOrDefault("link_origin") == "adfly" };
                if (int.TryParse(query.GetValueOrDefault("v"), out var version)) value["version"] = version;
                return (source, $"https://linkvertise.com/{userId}/random/dynamic?r={Uri.EscapeDataString(hash)}", new JsonObject { ["userIdAndHash"] = value });
            }
        }
        return (source, $"https://linkvertise.com/{userId}/{Uri.EscapeDataString(parts[1])}", new JsonObject { ["userIdAndUrl"] = new JsonObject { ["user_id"] = userId, ["url"] = parts[1] } });
    }

    private static BypassResponse Target(Uri source, string canonical, JsonObject content, string expected)
    {
        var value = Text(content["url"]).Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var destination) && destination.Scheme is "http" or "https")
        {
            if (expected.Length > 0 && !destination.Host.Equals(expected, StringComparison.OrdinalIgnoreCase) && !destination.Host.EndsWith("." + expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("target host does not match metadata host");
            return new BypassResponse { SourceUrl = source.ToString(), CanonicalUrl = canonical, Type = "url", Value = destination.ToString(), VerifiedHost = expected };
        }
        return new BypassResponse { SourceUrl = source.ToString(), CanonicalUrl = canonical, Type = "text", Value = Text(content["paste"]) };
    }

    private static JsonObject Args(string requestId, string userId, string sessionId, string canonical, string completionToken = "")
    {
        var value = new JsonObject { ["request_id"] = requestId, ["action_id"] = ActionId(), ["additional_data"] = new JsonObject { ["taboola"] = new JsonObject { ["user_id"] = userId, ["consent_string"] = "", ["url"] = canonical, ["external_referrer"] = "", ["session_id"] = sessionId } } };
        if (completionToken.Length > 0) value["completion_token"] = completionToken;
        return value;
    }

    private static (JsonObject? Wait, JsonObject? Ad) Tasks(JsonArray? tasks) => (tasks?.OfType<JsonObject>().FirstOrDefault(value => Text(value["__typename"]) == "WaitTask"), tasks?.OfType<JsonObject>().FirstOrDefault(value => Text(value["__typename"]) == "AdTask"));
    private static GraphQlRequest Request(string operation, string query, object variables) => new(operation, query, variables);
    private static JsonObject Decode(string raw)
    {
        var envelope = JsonNode.Parse(raw)?.AsObject() ?? throw new InvalidOperationException("invalid GraphQL response");
        if (envelope["errors"] is JsonArray { Count: > 0 } errors) throw new InvalidOperationException("GraphQL: " + string.Join("; ", errors.Select(value => Text(value?["message"]))));
        return envelope["data"]?.AsObject() ?? [];
    }
    private static string ActionId() => (Guid.NewGuid().ToString() + Guid.NewGuid() + Guid.NewGuid())[..100];
    private static string Text(JsonNode? value) => value?.GetValue<string>() ?? "";
    private static int? Number(JsonNode? value) => value is null ? null : value.GetValue<int>();
    private static bool Retryable(Exception error)
    {
        var message = error.Message.ToLowerInvariant();
        return error is RetryableException || new[] { "cooldown", "timeout", "net::err_", "proxy", "connection", "cheq" }.Any(message.Contains);
    }
    internal static string BrowserUserAgent() => $"Mozilla/5.0 ({(OperatingSystem.IsWindows() ? "Windows NT 10.0; Win64; x64" : OperatingSystem.IsMacOS() ? "Macintosh; Intel Mac OS X 10_15_7" : "X11; Linux x86_64")}) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36";
    private sealed class RetryableException(string message, Exception? inner = null) : Exception(message, inner);
}
