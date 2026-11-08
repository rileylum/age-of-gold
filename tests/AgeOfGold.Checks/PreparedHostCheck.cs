using AgeOfGold;
using SoulsFormats;

internal static class PreparedHostCheck
{
    internal static void Run(string preparation)
    {
        if (Environment.GetEnvironmentVariable("CI") is not null)
            throw new InvalidOperationException("Install checks must never run in CI");
        _ = new Install(Required("AOG_DARK_SOULS_REMASTERED"), "DarkSoulsRemastered.exe");
        var host = new Install(Required("AOG_ELDEN_RING"), "eldenring.exe");
        Oodle.SearchPaths.Add(host.Root);
        byte[] Input(string relative) => File.ReadAllBytes(Path.Combine(preparation, "host", relative));

        var map = MSBE.Read(Input("map/mapstudio/m10_01_00_00.msb.dcx"));
        if (map.Parts.MapPieces.Count == 0 || map.Parts.Collisions.Count == 0 || map.Models.MapPieces.Count == 0)
            throw new Exception("Prepared host map has no placement templates");
        var materials = BND4.Read(Input("material/allmaterial.matbinbnd.dcx"));
        if (materials.Files.Count == 0 || materials.Files.Any(f => f.Bytes.Length == 0 || !f.Name.EndsWith(".matbin")))
            throw new Exception("Prepared host material binder has missing material definitions");
        foreach (var file in materials.Files) _ = MATBIN.Read(file.Bytes);
        var models = BND4.Read(Input("map/m10/m10_01_00_00/m10_01_00_00_000000.mapbnd.dcx"));
        var meshes = models.Files.Where(f => f.Name.EndsWith(".flver")).Select(f => FLVER2.Read(f.Bytes)).ToArray();
        if (meshes.Length == 0 || meshes.Any(m => m.Meshes.Count == 0 || m.Meshes.All(mesh => mesh.Vertices.Count == 0)))
            throw new Exception("Prepared host model binder has no geometry templates");
        Console.WriteLine($"PASS: prepared host map has {map.Parts.MapPieces.Count} map pieces; "
            + $"{materials.Files.Count} material definitions and {meshes.Length} models parse successfully");
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new ArgumentException($"Set {name}");
}
