using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TlsClient.Core.Models.Entities;
using TlsClient.Core.Models.Requests;
using TlsClient.Native;

namespace LinkvertiseBypass;

internal sealed record GraphQlRequest([property: JsonPropertyName("operationName")] string OperationName, [property: JsonPropertyName("query")] string Query, [property: JsonPropertyName("variables")] object Variables);
internal sealed record BootstrapResult(string Metadata, string Content, string UserId);

internal sealed partial class TlsTransport : IAsyncDisposable
{
    private const string Endpoint = "https://publisher.linkvertise.com/graphql";
    private const string ClientHint = "\"Chromium\";v=\"146\", \"Google Chrome\";v=\"146\", \"Not_A Brand\";v=\"99\"";
    private static readonly Lock InitializationLock = new();
    private static bool _initialized;
    private readonly NativeTlsClient _client;
    private readonly string _canonical;

    private TlsTransport(string canonical, ProxyConnection? proxy)
    {
        EnsureInitialized();
        _canonical = canonical;
        _client = new NativeTlsClient(new TlsClientOptions(TlsClientIdentifier.Chrome146, LinkvertiseClient.TlsUserAgent)
        {
            ProxyURL = proxy?.AuthenticatedUrl(),
            WithCustomCookieJar = true,
            WithRandomTLSExtensionOrder = true,
            FollowRedirects = true,
            Timeout = TimeSpan.FromSeconds(25)
        });
    }

    public static Task<(TlsTransport, string)> CreateAsync(string canonical, ProxyConnection? proxy)
    {
        var transport = new TlsTransport(canonical, proxy);
        try
        {
            var body = transport.Send(HttpMethod.Get, CheqFingerprint.CollectorUrl(canonical), null, transport.ScriptHeaders());
            var match = RequestIdPattern().Match(body);
            if (!match.Success) throw new InvalidOperationException("CHEQ returned no request ID");
            return Task.FromResult((transport, match.Groups[1].Value));
        }
        catch
        {
            transport._client.Dispose();
            throw;
        }
    }

    public Task<IReadOnlyList<string>> BatchAsync(IReadOnlyList<GraphQlRequest> requests, string referrer)
    {
        var body = Send(HttpMethod.Post, Endpoint, JsonSerializer.Serialize(requests), GraphQlHeaders(referrer));
        using var items = JsonDocument.Parse(body);
        if (items.RootElement.ValueKind != JsonValueKind.Array || items.RootElement.GetArrayLength() != requests.Count) throw new InvalidOperationException("invalid GraphQL batch response");
        return Task.FromResult<IReadOnlyList<string>>(items.RootElement.EnumerateArray().Select(item => item.GetRawText()).ToArray());
    }

    public async Task<BootstrapResult> BootstrapAsync(GraphQlRequest metadata, GraphQlRequest content, string referrer)
    {
        var items = await BatchAsync([metadata, content], referrer);
        return new BootstrapResult(items[0], items[1], "fallbackUserId");
    }

    public async Task SendEventsAsync(params string[] urls)
    {
        var valid = urls.Where(url => Uri.TryCreate(url, UriKind.Absolute, out var value) && value.Scheme is "http" or "https").ToArray();
        for (var index = 0; index < valid.Length; index++)
        {
            try { _ = Send(HttpMethod.Get, valid[index], null, EventHeaders()); }
            catch { }
            if (index + 1 < valid.Length) await Task.Delay(50);
        }
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    private string Send(HttpMethod method, string url, string? body, Dictionary<string, string> headers)
    {
        var response = _client.Request(new Request { RequestUrl = url, RequestMethod = method, RequestBody = body, Headers = headers, HeaderOrder = headers.Keys.ToList() });
        if (!response.IsSuccessStatus) throw new HttpRequestException($"HTTP {(int)response.Status}: {response.Body}");
        return response.Body;
    }

    private static Dictionary<string, string> CommonHeaders() => new()
    {
        ["accept"] = "*/*",
        ["accept-language"] = "en-US,en;q=0.9",
        ["sec-ch-ua"] = ClientHint,
        ["sec-ch-ua-mobile"] = "?0",
        ["sec-ch-ua-platform"] = "\"Windows\"",
        ["user-agent"] = LinkvertiseClient.TlsUserAgent
    };

    private Dictionary<string, string> ScriptHeaders()
    {
        var headers = CommonHeaders();
        headers["referer"] = _canonical;
        headers["sec-fetch-dest"] = "script";
        headers["sec-fetch-mode"] = "no-cors";
        headers["sec-fetch-site"] = "cross-site";
        return headers;
    }

    private static Dictionary<string, string> GraphQlHeaders(string referrer)
    {
        var headers = CommonHeaders();
        headers["content-type"] = "application/json";
        headers["origin"] = "https://linkvertise.com";
        headers["referer"] = referrer;
        headers["sec-fetch-dest"] = "empty";
        headers["sec-fetch-mode"] = "cors";
        headers["sec-fetch-site"] = "same-site";
        return headers;
    }

    private Dictionary<string, string> EventHeaders()
    {
        var headers = CommonHeaders();
        headers["referer"] = _canonical;
        headers["sec-fetch-dest"] = "empty";
        headers["sec-fetch-mode"] = "cors";
        headers["sec-fetch-site"] = "cross-site";
        return headers;
    }

    // The runtime library can come from a published output or the local NuGet package cache.
    private static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (InitializationLock)
        {
            if (_initialized) return;
            var (package, version, file) = NativePackage();
            var packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
            if (string.IsNullOrWhiteSpace(packageRoot)) packageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
            var roots = new[] { AppContext.BaseDirectory, Path.Combine(packageRoot, package, version) };
            var library = roots.Where(Directory.Exists).SelectMany(root => FindLibraries(root, file)).FirstOrDefault() ?? throw new FileNotFoundException($"{package} {version} native library was not found");
            NativeTlsClient.Initialize(library);
            _initialized = true;
        }
    }

    private static IEnumerable<string> FindLibraries(string root, string file)
    {
        try { return Directory.EnumerateFiles(root, file, SearchOption.AllDirectories).ToArray(); }
        catch { return []; }
    }

    private static (string Package, string Version, string File) NativePackage() => (RuntimeInformation.IsOSPlatform(OSPlatform.Windows), RuntimeInformation.IsOSPlatform(OSPlatform.Linux), RuntimeInformation.ProcessArchitecture) switch
    {
        (true, _, Architecture.X64) => ("tlsclient.native.win-x64", "1.16.0", "tls-client.dll"),
        (true, _, Architecture.X86) => ("tlsclient.native.win-x86", "1.16.0", "tls-client.dll"),
        (_, true, Architecture.X64) => ("tlsclient.native.linux-amd64", "1.16.0", "tls-client.so"),
        (_, true, Architecture.Arm64) => ("tlsclient.native.linux-arm64", "1.16.0", "tls-client.so"),
        (_, _, Architecture.X64) when RuntimeInformation.IsOSPlatform(OSPlatform.OSX) => ("tlsclient.native.darwin-amd64", "1.16.0", "tls-client.dylib"),
        (_, _, Architecture.Arm64) when RuntimeInformation.IsOSPlatform(OSPlatform.OSX) => ("tlsclient.native.darwin-arm64", "1.16.0", "tls-client.dylib"),
        _ => throw new PlatformNotSupportedException()
    };

    [GeneratedRegex("\\\"req\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"")]
    private static partial Regex RequestIdPattern();
}
