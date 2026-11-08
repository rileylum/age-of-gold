using System.Security.Cryptography;
using System.Text.Json;
using AgeOfGold;
using AgeOfGold.Collision;
using SoulsFormats;

internal static class SourceCollisionCheck
{
    internal static void Run()
    {
        if (Environment.GetEnvironmentVariable("CI") is not null)
            throw new InvalidOperationException("Install checks must never run in CI");
        var source = new Install(Required("AOG_DARK_SOULS_REMASTERED"), "DarkSoulsRemastered.exe");
        _ = new Install(Required("AOG_ELDEN_RING"), "eldenring.exe");
        using var reference = JsonDocument.Parse(File.ReadAllBytes(Required("AOG_COLLISION_REFERENCE")));
        int count = 0;
        foreach (string detail in new[] { "h", "l" })
        {
            string stem = $"map/m10_02_00_00/{detail}10_02_00_00";
            var binder = BXF3.Read(source.Read(stem + ".hkxbhd"), source.Read(stem + ".hkxbdt"));
            foreach (var file in binder.Files)
            {
                string name = file.Name.Replace('\\', '/').Split('/')[^1];
                var raw = DCX.Decompress(file.Bytes);
                var expected = reference.RootElement.GetProperty(name);
                if (Convert.ToHexString(SHA256.HashData(file.Bytes)).ToLowerInvariant() != expected.GetProperty("sha256").GetString())
                    throw new Exception("Private comparison reference belongs to different source bytes");
                using var actual = JsonDocument.Parse(JsonSerializer.Serialize(SourceCollisionReader.Read(raw),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
                Compare(actual.RootElement, expected.GetProperty("collision"), name);
                count++;
            }
        }
        if (count != reference.RootElement.EnumerateObject().Count()) throw new Exception("Uncompared reference entries");
        Console.WriteLine($"PASS: all {count} source collision parts match the independent private reader");
    }

    private static void Compare(JsonElement actual, JsonElement expected, string path)
    {
        if (actual.ValueKind != expected.ValueKind) throw new Exception($"Collision value kind differs at {path}");
        if (actual.ValueKind == JsonValueKind.Object)
        {
            if (!actual.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(expected.EnumerateObject().Select(p => p.Name).Order()))
                throw new Exception($"Collision fields differ at {path}");
            foreach (var property in expected.EnumerateObject()) Compare(actual.GetProperty(property.Name), property.Value, path + "." + property.Name);
        }
        else if (actual.ValueKind == JsonValueKind.Array)
        {
            if (actual.GetArrayLength() != expected.GetArrayLength()) throw new Exception($"Collision array length differs at {path}");
            for (int i = 0; i < actual.GetArrayLength(); i++) Compare(actual[i], expected[i], path + $"[{i}]");
        }
        else if (actual.ValueKind == JsonValueKind.Number)
        {
            if (actual.GetSingle() != expected.GetSingle()) throw new Exception($"Collision number differs at {path}");
        }
        else if (actual.ToString() != expected.ToString()) throw new Exception($"Collision value differs at {path}");
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new ArgumentException($"Set {name}");
}
