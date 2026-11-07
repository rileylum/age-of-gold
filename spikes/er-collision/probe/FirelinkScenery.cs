using System.Security.Cryptography;
using System.Text.Json;
using SoulsFormats;

static class FirelinkScenery
{
    /// <summary>Disable retained host scenery without changing translated scenery or map services.</summary>
    public static byte[] Isolate(byte[] native, byte[] input)
    {
        var original = MSBE.Read(native);
        var map = MSBE.Read(input);
        var targets = map.Parts.GetEntries().ToDictionary(p => p.Name);
        var previous = new Dictionary<string, MSBE.Part.GameEditionDisableType>(StringComparer.Ordinal);
        foreach (var part in original.Parts.GetEntries().Where(p => p is MSBE.Part.MapPiece or MSBE.Part.Asset))
        {
            if (!targets.TryGetValue(part.Name, out var target) || target.GetType() != part.GetType()
                || target.ModelName != part.ModelName)
                throw new InvalidDataException("Retained scenery identity differs from the host install");
            previous.Add(target.Name, target.GameEditionDisable);
            target.GameEditionDisable = MSBE.Part.GameEditionDisableType.DisableInRelease;
        }
        if (previous.Count == 0) throw new InvalidDataException("No retained host scenery found");
        var result = map.Write();
        var readback = MSBE.Read(result);
        foreach (var part in readback.Parts.GetEntries().Where(p => previous.ContainsKey(p.Name)))
        {
            if (part.GameEditionDisable != MSBE.Part.GameEditionDisableType.DisableInRelease)
                throw new InvalidDataException("Host scenery disable flag did not round trip");
            part.GameEditionDisable = previous[part.Name];
        }
        if (!readback.Write(new DCX.NoCompressionInfo()).AsSpan().SequenceEqual(DCX.Is(input) ? DCX.Decompress(input) : input))
            throw new InvalidDataException("Scenery isolation changed unrelated map bytes");
        return result;
    }

    /// <summary>Write a checked build with retained host scenery disabled using the installed map's identities.</summary>
    public static void Build(string source, byte[] native, string input, string output)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Scenery output must be fresh");
        const string layout = "mod/map/mapstudio/m10_01_00_00.msb.dcx";
        const string sourceMap = "map/MapStudio/m10_02_00_00.msb";
        string hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(input, "check.json")));
        if (manifest.RootElement.GetProperty("host_fingerprints").GetProperty(layout[4..]).GetString() != hash(native)
            || manifest.RootElement.GetProperty("source_visuals").GetProperty("source_fingerprints").GetProperty(sourceMap).GetString()
                != hash(File.ReadAllBytes(Path.Combine(source, sourceMap))))
            throw new InvalidDataException("Installs differ from the checked input build");
        var candidate = Isolate(native, File.ReadAllBytes(Path.Combine(input, layout)));
        var fingerprints = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(input, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(input, file).Replace('\\', '/');
            var bytes = File.ReadAllBytes(file); fingerprints.Add(relative, hash(bytes));
            var target = Path.Combine(output, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, relative == layout ? candidate : bytes);
        }
        foreach (var (relative, fingerprint) in fingerprints)
            if (hash(File.ReadAllBytes(Path.Combine(output, relative))) != (relative == layout ? hash(candidate) : fingerprint))
                throw new InvalidDataException($"Written file differs: {relative}");
        var original = MSBE.Read(native);
        File.WriteAllText(Path.Combine(output, "scenery-check.json"), JsonSerializer.Serialize(new {
            purpose = "Disable retained host scenery while preserving translated scenery and map services",
            disabled_map_pieces = original.Parts.MapPieces.Count, disabled_assets = original.Parts.Assets.Count,
            input_fingerprints = fingerprints, native_layout_sha256 = hash(native),
            tool_sha256 = hash(File.ReadAllBytes(typeof(FirelinkScenery).Assembly.Location)),
            unchanged_input_files = fingerprints.Count - 1,
            limits = "Host effects, characters, connections and collision retained; source visibility groups unchanged",
            inherited_reports = "Earlier reports describe preceding diagnostic stages",
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: retained host scenery disabled; translated scenery and unrelated files preserved");
    }
}
