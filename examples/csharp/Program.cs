using System.Text.Json;
using LinkvertiseBypass;

var result = await LinkvertiseClient.BypassAsync(args[0]);
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
