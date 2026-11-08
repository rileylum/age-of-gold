using AgeOfGold;
using AgeOfGold.Archives;
using HKLib.Serialization.hk2018.Binary;
using SoulsFormats;

internal static class HostCollisionCheck
{
    internal static void Run(string output)
    {
        if (Environment.GetEnvironmentVariable("CI") is not null)
            throw new InvalidOperationException("Install checks must never run in CI");
        _ = new Install(Required("AOG_DARK_SOULS_REMASTERED"), "DarkSoulsRemastered.exe");
        var host = new Install(Required("AOG_ELDEN_RING"), "eldenring.exe");
        Oodle.SearchPaths.Add(host.Root);
        var archive = new HostArchive(host);
        foreach (string detail in new[] { "h", "l" })
        {
            string stem = $"map/m10/m10_01_00_00/{detail}10_01_00_00";
            var binder = BXF4.Read(archive.Read(stem + ".hkxbhd"), archive.Read(stem + ".hkxbdt"));
            var reader = new HavokBinarySerializer();
            reader.LoadCompendium(new MemoryStream(DCX.Decompress(
                binder.Files.Single(f => f.Name.EndsWith(".compendium.dcx", StringComparison.Ordinal)).Bytes)));
            var file = binder.Files.Single(f => f.Name.EndsWith("_001400.hkx.dcx", StringComparison.Ordinal));
            var raw = DCX.Decompress(file.Bytes);
            var root = reader.Read(new MemoryStream(raw));
            Console.WriteLine($"Read {detail} collision with {reader.TypeRegistry.Types.Count()} install-derived types");
            using var first = new MemoryStream();
            reader.Write(root, first);
            using var second = new MemoryStream();
            var check = new HavokBinarySerializer();
            check.Write(check.Read(new MemoryStream(first.ToArray())), second);
            if (!first.ToArray().SequenceEqual(second.ToArray()))
                throw new Exception("Collision round trip changed serialized bytes");
            File.WriteAllBytes(Path.Combine(output, detail + "-collision.hkx"), first.ToArray());
            GeometryCheck.Run(() => reader.Read(new MemoryStream(raw)), Path.Combine(output, detail),
                () => new HavokBinarySerializer(reader.TypeRegistry));
        }
        Console.WriteLine("PASS: both host collision templates read and round trip without bundled metadata");
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new ArgumentException($"Set {name}");
}
