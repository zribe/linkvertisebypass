using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace LinkvertiseBypass;

internal sealed record ProxyConnection(ProxyProvider Provider, Uri Server, string Username, string Password, string Country, string Lifetime, string Identity)
{
    public int Port => Server.Port;
    public Microsoft.Playwright.Proxy PlaywrightProxy() => new() { Server = Server.ToString(), Username = Username.Length > 0 ? Username : null, Password = Username.Length > 0 ? Password : null };
    public IWebProxy WebProxy()
    {
        var value = new System.Net.WebProxy(Server);
        if (Username.Length > 0) value.Credentials = new NetworkCredential(Username, Password);
        return value;
    }
}

internal sealed class ProxyPool
{
    private static readonly Regex Country = new("^[a-z]{2}$", RegexOptions.Compiled);
    private static readonly Regex Lifetime = new("^[1-9][0-9]*[smhd]$", RegexOptions.Compiled);
    private static readonly Regex DataCountry = new("__cr\\.[A-Za-z]{2}(?:,[A-Za-z]{2})*", RegexOptions.Compiled);
    private static readonly Regex RoyalOptions = new("_(?:country-[A-Za-z]{2}|session-[A-Za-z0-9]{8}|lifetime-[1-9][0-9]*[smhd]|forcerandom-1)", RegexOptions.Compiled);
    private readonly ProxyConnection _base;
    private readonly HashSet<string> _used = [];

    private ProxyPool(ProxyConnection value) => _base = value;
    public bool Rotatable => _base.Provider == ProxyProvider.IPRoyal || _base.Provider == ProxyProvider.DataImpulse && _base.Port is >= 10000 and <= 20000;

    public static ProxyPool? Create(ProxyOptions options)
    {
        if (!options.Enabled) return null;
        var server = options.Server.Trim();
        server = options.Provider switch
        {
            ProxyProvider.DataImpulse when server.Length == 0 => "http://gw.dataimpulse.com:10000",
            ProxyProvider.IPRoyal when server.Length == 0 => "http://geo.iproyal.com:12321",
            ProxyProvider.Custom when server.Length == 0 => throw new ArgumentException("custom proxy server is required"),
            _ => server
        };
        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new ArgumentException("invalid proxy server");
        var username = options.Username;
        var password = options.Password;
        if (uri.UserInfo.Length > 0)
        {
            var credentials = uri.UserInfo.Split(':', 2);
            if (username.Length == 0) username = Uri.UnescapeDataString(credentials[0]);
            if (password.Length == 0) password = Uri.UnescapeDataString(credentials.ElementAtOrDefault(1) ?? "");
            uri = new UriBuilder(uri) { UserName = "", Password = "" }.Uri;
        }
        var country = options.Country.Trim().ToLowerInvariant();
        var lifetime = options.SessionLifetime.Trim().ToLowerInvariant();
        if (country.Length > 0 && !Country.IsMatch(country)) throw new ArgumentException("proxy country must be a two-letter code");
        if (!Lifetime.IsMatch(lifetime)) throw new ArgumentException("IPRoyal session lifetime must look like 10m, 2h, or 30s");
        if (options.Provider == ProxyProvider.DataImpulse && country.Length > 0)
        {
            var tag = "__cr." + country;
            username = DataCountry.IsMatch(username) ? DataCountry.Replace(username, tag) : username + tag;
        }
        return new ProxyPool(new ProxyConnection(options.Provider, uri, username, password, country, lifetime, ""));
    }

    public IReadOnlyList<ProxyConnection> Next(int count)
    {
        var result = new List<ProxyConnection>(count);
        while (result.Count < count)
        {
            ProxyConnection candidate;
            if (_base.Provider == ProxyProvider.DataImpulse)
            {
                var builder = new UriBuilder(_base.Server);
                if (_used.Count > 0) builder.Port = RandomNumberGenerator.GetInt32(10000, 20001);
                candidate = _base with { Server = builder.Uri, Identity = builder.Uri.ToString() };
            }
            else if (_base.Provider == ProxyProvider.IPRoyal)
            {
                var session = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
                var password = RoyalOptions.Replace(_base.Password, "");
                if (_base.Country.Length > 0) password += "_country-" + _base.Country;
                password += $"_session-{session}_lifetime-{_base.Lifetime}";
                candidate = _base with { Password = password, Identity = session };
            }
            else
            {
                candidate = _base with { Identity = _base.Server.ToString() };
            }
            if (_used.Add(candidate.Identity)) result.Add(candidate);
        }
        return result;
    }
}
