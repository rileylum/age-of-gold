using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using SoulsFormats;

// Spike only: keep the host's room lighting below the translated outdoor scenery.
static class FirelinkEnvironment
{
    static string EnclosingVolume(MSBE map)
    {
        var boxes = map.Regions.EnvironmentMapEffectBoxes;
        if (boxes.Count < 2) throw new InvalidDataException("Expected outdoor and local environment volumes");
        foreach (var box in boxes)
            if (box.Shape is not MSB.Shape.Box s || !float.IsFinite(s.Width + s.Height + s.Depth)
                || s.Width <= 0 || s.Height <= 0 || s.Depth <= 0
                || !float.IsFinite(box.Position.X + box.Position.Y + box.Position.Z + box.Rotation.Y)
                || box.Rotation.X != 0 || box.Rotation.Z != 0)
                throw new InvalidDataException("Expected finite, upright environment boxes");
        Vector3[] corners(MSBE.Region.EnvironmentMapEffectBox box)
        {
            var s = (MSB.Shape.Box)box.Shape;
            var rotation = Matrix4x4.CreateRotationY(box.Rotation.Y * MathF.PI / 180);
            return (from x in new[] { -s.Width / 2, s.Width / 2 }
                    from y in new[] { 0f, s.Height }
                    from z in new[] { -s.Depth / 2, s.Depth / 2 }
                    select Vector3.Transform(new(x, y, z), rotation) + box.Position).ToArray();
        }
        bool contains(MSBE.Region.EnvironmentMapEffectBox box, Vector3 point)
        {
            var s = (MSB.Shape.Box)box.Shape;
            var p = Vector3.Transform(point - box.Position,
                Matrix4x4.CreateRotationY(-box.Rotation.Y * MathF.PI / 180));
            return Math.Abs(p.X) <= s.Width / 2 + .001f && Math.Abs(p.Z) <= s.Depth / 2 + .001f
                && p.Y >= -.001f && p.Y <= s.Height + .001f;
        }
        // lazy: one enclosing host outdoor volume; revisit when translating source environment data.
        var enclosing = boxes.Where(b => boxes.Where(other => other != b)
            .SelectMany(corners).All(p => contains(b, p))).ToArray();
        if (enclosing.Length != 1) throw new InvalidDataException("Expected one enclosing outdoor environment volume");
        return enclosing[0].Name;
    }

    /// <summary>Restore local environment volumes while retaining the relocated enclosing volume.</summary>
    public static byte[] RestoreRooms(byte[] native, byte[] input)
    {
        var original = MSBE.Read(native); var map = MSBE.Read(input);
        var outside = EnclosingVolume(original);
        var rooms = original.Regions.EnvironmentMapEffectBoxes.ToDictionary(b => b.Name);
        var targets = map.Regions.EnvironmentMapEffectBoxes;
        var positions = targets.ToDictionary(b => b.Name, b => b.Position);
        if (!targets.Select(b => b.Name).Order().SequenceEqual(rooms.Keys.Order()))
            throw new InvalidDataException("Environment volume identities differ from the host install");
        foreach (var box in targets.Where(b => b.Name != outside)) box.Position = rooms[box.Name].Position;
        var result = map.Write();
        var readback = MSBE.Read(result);
        foreach (var box in readback.Regions.EnvironmentMapEffectBoxes.Where(b => b.Name != outside))
        {
            if (box.Position != rooms[box.Name].Position) throw new InvalidDataException("Room position did not round trip");
            box.Position = positions[box.Name];
        }
        if (!readback.Write(new DCX.NoCompressionInfo()).AsSpan().SequenceEqual(DCX.Is(input) ? DCX.Decompress(input) : input))
            throw new InvalidDataException("Environment repair changed unrelated map bytes");
        return result;
    }

    /// <summary>Write a checked Firelink build with native room volumes restored to their host locations.</summary>
    public static void Build(string source, byte[] native, string input, string output)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Environment output must be fresh");
        const string layout = "mod/map/mapstudio/m10_01_00_00.msb.dcx";
        const string sourceMap = "map/MapStudio/m10_02_00_00.msb";
        string hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(input, "check.json")));
        if (manifest.RootElement.GetProperty("host_fingerprints").GetProperty(layout[4..]).GetString() != hash(native)
            || manifest.RootElement.GetProperty("source_visuals").GetProperty("source_fingerprints").GetProperty(sourceMap).GetString()
                != hash(File.ReadAllBytes(Path.Combine(source, sourceMap))))
            throw new InvalidDataException("Installs differ from the checked input build");
        var previous = File.ReadAllBytes(Path.Combine(input, layout));
        var candidate = RestoreRooms(native, previous);
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
        File.WriteAllText(Path.Combine(output, "environment-repair-check.json"), JsonSerializer.Serialize(new {
            purpose = "Restore native room volumes; retain relocated outdoor fill over Firelink",
            restored_volumes = MSBE.Read(native).Regions.EnvironmentMapEffectBoxes.Count - 1,
            input_fingerprints = fingerprints, native_layout_sha256 = hash(native),
            tool_sha256 = hash(File.ReadAllBytes(typeof(FirelinkEnvironment).Assembly.Location)),
            unchanged_input_files = fingerprints.Count - 1,
            limits = "Host outdoor environment reused; source sky, fog, baked lighting and material translation remain incomplete",
            inherited_reports = "Earlier reports describe preceding diagnostic stages",
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: native room locations restored; outdoor lighting and unrelated files preserved");
    }
}
