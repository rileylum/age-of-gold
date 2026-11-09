using System.Buffers.Binary;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using AgeOfGold;
using AgeOfGold.Params;
using AgeOfGold.Shaders;

internal static class SourceRendererSettingsCheck
{
    internal static void Installs(string output, bool environment = false)
    {
        if (Environment.GetEnvironmentVariable("CI") is not null) throw new InvalidOperationException("Install checks must never run in CI");
        output = Install.Canonical(output);
        if (!Install.Within(output, Install.Canonical(Path.GetTempPath())) || Directory.Exists(output) || File.Exists(output))
            throw new ArgumentException("Use a fresh output directory under system temp");
        var source = new Install(Required("AOG_DARK_SOULS_REMASTERED"), "DarkSoulsRemastered.exe");
        var host = new Install(Required("AOG_ELDEN_RING"), "eldenring.exe");
        if (Install.Within(output, source.Root) || Install.Within(output, host.Root)) throw new ArgumentException("Output enters an install");
        var first = SourceRendererSettings.Read(source);
        Require(first == SourceRendererSettings.Read(source), "Repeated installed settings differ");
        SourceEnvironmentSettings? environmentSettings = environment ? SourceEnvironmentSettings.Read(source, Required("AOG_LLVM_MINGW")) : null;
        if (environment)
            Require(environmentSettings == SourceEnvironmentSettings.Read(source, Required("AOG_LLVM_MINGW")), "Repeated environment settings differ");
        var lighting = SourceLightingReader.Read(source);
        var groups = new[] { lighting.Scenery, lighting.Characters };
        var rows = groups.Select(group => group.Select(row => new
        {
            row.Id,
            values = SourceLightingParameters.Values(row, first).ToDictionary(p => p.Key.Field, p => (float[])p.Value),
        }).ToArray()).ToArray();
        if (Environment.GetEnvironmentVariable("AOG_SOURCE_LIGHTING_REFERENCE") is { } reference)
        {
            using var oracle = JsonDocument.Parse(File.ReadAllBytes(reference));
            var row = rows[0].Single(r => r.Id == oracle.RootElement.GetProperty("sceneryRow").GetInt32());
            var expected = oracle.RootElement.GetProperty("values");
            Require(expected.EnumerateObject().Count() == row.values.Count, "Incomplete ambient reference");
            foreach (var field in expected.EnumerateObject())
            {
                var values = field.Value.EnumerateArray().Select(v => v.GetSingle()).ToArray();
                Require(row.values.TryGetValue(field.Name, out var observed) && values.Length == observed.Length, "Unknown ambient reference field");
                for (var i = 0; i < values.Length; i++)
                    Require(float.IsFinite(values[i]) && MathF.Abs(observed![i] - values[i]) <= 0.000001f * MathF.Max(1, MathF.Abs(values[i])),
                        "Captured source lighting differs: " + field.Name);
            }
            Require(first.NormalStrength == oracle.RootElement.GetProperty("normalStrength").GetSingle(), "Captured normal strength differs");
            if (environmentSettings is not null)
                Require(environmentSettings.Strength == oracle.RootElement.GetProperty("environmentStrength").GetSingle(), "Captured environment gain differs");
        }
        foreach (var install in new[] { source, host })
            foreach (var input in install.Fingerprints)
            {
                using var stream = File.OpenRead(install.Locate(input.Key));
                Require(Convert.ToHexString(SHA256.HashData(stream)) == input.Value, "Install changed");
            }
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, "renderer.json"), JsonSerializer.SerializeToUtf8Bytes(first,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        File.WriteAllBytes(Path.Combine(output, "ambient.json"), JsonSerializer.SerializeToUtf8Bytes(rows));
        if (environmentSettings is not null)
            File.WriteAllBytes(Path.Combine(output, "environment.json"), JsonSerializer.SerializeToUtf8Bytes(environmentSettings));
        File.WriteAllText(Path.Combine(output, "check.json"), JsonSerializer.Serialize(new
        {
            repeatIdentical = true, unchangedInputs = true, source = source.Fingerprints, host = host.Fingerprints,
            sceneryRows = rows[0].Length, characterRows = rows[1].Length,
            independentCapture = Environment.GetEnvironmentVariable("AOG_SOURCE_LIGHTING_REFERENCE") is not null,
        }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine("PASS: installed renderer settings and ambient parameters, repeat, capture when supplied and unchanged installs");
    }

    internal static void Run(string root)
    {
        var path = Path.Combine(root, "authored-renderer");
        Directory.CreateDirectory(path);
        SourceRendererSettings Read(byte[] bytes)
        {
            File.WriteAllBytes(Path.Combine(path, "DarkSoulsRemastered.exe"), bytes);
            return SourceRendererSettings.Read(new Install(path, "DarkSoulsRemastered.exe"));
        }
        var expected = new SourceRendererSettings(0.625f, 0.875f);
        Require(Read(Image()) == expected, "named defaults did not come from the executable");
        Require(Read(Image(0x180000000)) == expected, "relocation changed defaults");
        var changed = Image(); Float(changed, 0x9c0, 2.75f);
        Require(Read(changed).HemisphereStrength == 2.75f, "changed installed value was ignored");
        var moved = Image(); Array.Copy(moved, 0x800, moved, 0x8e0, 112); Array.Clear(moved, 0x800, 112);
        Require(Read(moved) == expected, "record order changed defaults");
        void Reject(byte[] bytes, string reason)
        {
            try { Read(bytes); }
            catch (InvalidDataException e) when (e.Message.Contains(reason, StringComparison.Ordinal)) { return; }
            throw new Exception("Accepted invalid renderer setting: " + reason);
        }
        var version = Image(); U32(version, 0xc90, 9u << 16);
        Reject(version, "version");
        var missing = Image(); missing[0x800] = (byte)'x';
        Reject(missing, "missing or ambiguous");
        var duplicate = Image(); Array.Copy(duplicate, 0x800, duplicate, 0x8e0, 112);
        Reject(duplicate, "missing or ambiguous");
        var type = Image(); U32(type, 0x864, 1);
        Reject(type, "record");
        var padding = Image(); padding[0x860] = 1;
        Reject(padding, "record");
        var code = Image(); U64(code, 0x868, 0x140001000);
        Reject(code, "storage");
        var outside = Image(); U64(outside, 0x868, 0x140006000);
        Reject(outside, "storage");
        var unaligned = Image(); U64(unaligned, 0x868, 0x1400031c1);
        Reject(unaligned, "storage");
        foreach (float value in new[] { -1f, float.NaN, float.PositiveInfinity })
        {
            var invalid = Image(); Float(invalid, 0x9c0, value);
            Reject(invalid, "value");
        }
        Console.WriteLine("PASS: named source renderer defaults, changed inputs, relocation and rejected metadata/storage/values");
    }

    private static byte[] Image(ulong imageBase = 0x140000000)
    {
        var bytes = HostBindingCheck.Image(imageBase);
        U32(bytes, 0xc90, (1u << 16) | 3); U32(bytes, 0xc94, 1u << 16);
        void Setting(int at, string name, uint address)
        {
            Encoding.Unicode.GetBytes(name + "\0").CopyTo(bytes, at);
            U32(bytes, at + 100, 2); U64(bytes, at + 104, imageBase + address);
        }
        Setting(0x800, "m_dbgAmbientStrength", 0x31c0);
        Setting(0x870, "m_dbgNormalStrength", 0x31c4);
        Float(bytes, 0x9c0, 0.625f); Float(bytes, 0x9c4, 0.875f);
        return bytes;
    }

    private static void Float(byte[] b, int at, float value) => U32(b, at, BitConverter.SingleToUInt32Bits(value));
    private static void U32(byte[] b, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), value);
    private static void U64(byte[] b, int at, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(at), value);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new ArgumentException("Set " + name);
}
