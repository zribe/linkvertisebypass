using System.Text.Json.Serialization;

namespace LinkvertiseBypass;

public enum ProxyProvider { DataImpulse, IPRoyal, Custom }

public sealed record ProxyOptions
{
    public bool Enabled { get; set; }
    public ProxyProvider Provider { get; set; } = ProxyProvider.DataImpulse;
    public string Server { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Country { get; set; } = "nl";
    public string SessionLifetime { get; set; } = "10m";
    public int ProbeBatch { get; set; } = 6;
    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public static ProxyOptions Direct() => new();
    public static ProxyOptions DataImpulse(string username, string password, string country = "nl") => new() { Enabled = true, Provider = ProxyProvider.DataImpulse, Username = username, Password = password, Country = country };
    public static ProxyOptions IPRoyal(string username, string password, string country = "nl", string sessionLifetime = "10m") => new() { Enabled = true, Provider = ProxyProvider.IPRoyal, Username = username, Password = password, Country = country, SessionLifetime = sessionLifetime };
    public static ProxyOptions Custom(string server, string username = "", string password = "") => new() { Enabled = true, Provider = ProxyProvider.Custom, Server = server, Username = username, Password = password };

    public static ProxyOptions FromEnvironment()
    {
        var server = FirstEnvironment("LINKVERTISEBYPASS_PROXY_SERVER", "LINKVERTISEBYPASS_PROXY");
        var username = Environment.GetEnvironmentVariable("LINKVERTISEBYPASS_PROXY_USERNAME") ?? "";
        var password = Environment.GetEnvironmentVariable("LINKVERTISEBYPASS_PROXY_PASSWORD") ?? "";
        var configured = server.Length > 0 || username.Length > 0 || password.Length > 0;
        var provider = Environment.GetEnvironmentVariable("LINKVERTISEBYPASS_PROXY_PROVIDER")?.Trim().ToLowerInvariant() switch
        {
            "iproyal" => ProxyProvider.IPRoyal,
            "custom" => ProxyProvider.Custom,
            _ => ProxyProvider.DataImpulse
        };
        return new ProxyOptions
        {
            Enabled = bool.TryParse(Environment.GetEnvironmentVariable("LINKVERTISEBYPASS_PROXY_ENABLED"), out var enabled) ? enabled : configured,
            Provider = provider,
            Server = server,
            Username = username,
            Password = password,
            Country = Environment.GetEnvironmentVariable("LINKVERTISEBYPASS_PROXY_COUNTRY")?.Trim().ToLowerInvariant() is { Length: > 0 } country ? country : "nl",
            SessionLifetime = Environment.GetEnvironmentVariable("LINKVERTISEBYPASS_PROXY_SESSION_LIFETIME")?.Trim().ToLowerInvariant() is { Length: > 0 } lifetime ? lifetime : "10m"
        };
    }

    private static string FirstEnvironment(params string[] names) => names.Select(Environment.GetEnvironmentVariable).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
}

public sealed record BypassOptions
{
    public ProxyOptions Proxy { get; init; } = ProxyOptions.FromEnvironment();
    public TimeSpan TaskDelay { get; init; } = TimeSpan.FromMilliseconds(50);
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(3);
    public bool AutoInstallBrowser { get; init; } = true;
    public static BypassOptions FromEnvironment() => new();
}

public sealed record BypassResponse
{
    [JsonPropertyName("sourceUrl")] public required string SourceUrl { get; init; }
    [JsonPropertyName("canonicalUrl")] public required string CanonicalUrl { get; init; }
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("value")] public required string Value { get; init; }
    [JsonPropertyName("verifiedHost"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public string? VerifiedHost { get; init; }
    [JsonPropertyName("proxyProvider"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public string? ProxyProvider { get; init; }
    [JsonPropertyName("proxyPort"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int ProxyPort { get; init; }
    [JsonPropertyName("proxyProbes")] public int ProxyProbes { get; init; }
    [JsonPropertyName("proxyAttempts")] public int ProxyAttempts { get; init; }
    [JsonIgnore] public TimeSpan Elapsed { get; init; }
    [JsonPropertyName("elapsedMilliseconds")] public long ElapsedMilliseconds => (long)Elapsed.TotalMilliseconds;
}
