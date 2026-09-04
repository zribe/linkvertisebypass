using System.Text.Json;
using LinkvertiseBypass;

var values = args.ToList();
if (values.Remove("--install-browser")) { Environment.ExitCode = LinkvertiseClient.InstallBrowser(); return; }
string? Take(string name) { var index = values.IndexOf(name); if (index < 0 || index + 1 >= values.Count) return null; var value = values[index + 1]; values.RemoveRange(index, 2); return value; }
var proxy = Take("--proxy")?.ToLowerInvariant();
var country = Take("--country")?.ToLowerInvariant();
var timeout = Take("--timeout");
var url = values.FirstOrDefault();
if (string.IsNullOrWhiteSpace(url)) { Console.Write("Linkvertise URL: "); url = Console.ReadLine()?.Trim(); }
if (string.IsNullOrWhiteSpace(url)) { Console.Error.WriteLine("a Linkvertise URL is required"); Environment.ExitCode = 1; return; }
var options = BypassOptions.FromEnvironment();
if (proxy is not null)
{
    options.Proxy.Enabled = proxy != "off";
    if (options.Proxy.Enabled) options.Proxy.Provider = proxy switch { "dataimpulse" => ProxyProvider.DataImpulse, "iproyal" => ProxyProvider.IPRoyal, "custom" => ProxyProvider.Custom, _ => throw new ArgumentException("invalid proxy provider") };
}
if (country is not null) options.Proxy.Country = country;
if (double.TryParse(timeout, out var seconds) && seconds > 0) options = options with { Timeout = TimeSpan.FromSeconds(seconds) };
try { Console.WriteLine(JsonSerializer.Serialize(await LinkvertiseClient.BypassAsync(url, options), new JsonSerializerOptions { WriteIndented = true })); }
catch (Exception error) { Console.Error.WriteLine(error.Message); Environment.ExitCode = 1; }
