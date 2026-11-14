using System.Numerics;
using AgeOfGold;
using AgeOfGold.Navigation;
using SoulsFormats;

internal static class SourceNavigationCheck
{
    internal static void Run(string root)
    {
        var source = Path.Combine(root, "authored-navigation");
        Directory.CreateDirectory(Path.Combine(source, "map/MapStudio")); Directory.CreateDirectory(Path.Combine(source, "map/m10_02_00_00"));
        File.WriteAllText(Path.Combine(source, "DarkSoulsRemastered.exe"), "authored executable");
        var layout = new MSB1();
        foreach (string model in new[] { "n0001B2", "n0000B2" })
        {
            layout.Models.Navmeshes.Add(new() { Name = model });
            // A rotation of two millionths of a degree is authoring noise, as the installed placements carry.
            layout.Parts.Navmeshes.Add(new() { Name = model + "_0000", ModelName = model, Rotation = new(0, -2e-6f, 0) });
        }
        File.WriteAllBytes(Path.Combine(source, "map/MapStudio/m10_02_00_00.msb"), layout.Write());
        static NVM Mesh(Vector3[] vertices, params (int A, int B, int C, NVM.TriangleFlags Flags)[] triangles)
        {
            var mesh = new NVM { BigEndian = false, Vertices = [.. vertices], Triangles = [], RootBox = new NVM.Box { TriangleIndices = [] }, Entities = [] };
            foreach (var (a, b, c, flags) in triangles) mesh.Triangles.Add(new NVM.Triangle { VertexIndex1 = a, VertexIndex2 = b, VertexIndex3 = c, EdgeIndex1 = -1, EdgeIndex2 = -1, EdgeIndex3 = -1, Flags = flags });
            return mesh;
        }
        // Two meshes meeting along the edge from (1,0,0) to (1,0,1). The first holds an upward triangle, a wall and a triangle
        // standing on edge; the second holds a downward-wound triangle across the shared edge and a copy lying on the first's.
        var first = Mesh([new(0, 0, 0), new(1, 0, 1), new(1, 0, 0), new(0, 2, 0), new(0, 0, 1)],
            (0, 1, 2, NVM.TriangleFlags.NONE), (0, 2, 3, NVM.TriangleFlags.WALL), (0, 3, 2, NVM.TriangleFlags.EDGE));
        var second = Mesh([new(1, 0, 0), new(1, 0, 1), new(2, 0.5f, 0), new(0, 0, 0)],
            (0, 1, 2, NVM.TriangleFlags.LARGE_SPACE), (3, 1, 0, NVM.TriangleFlags.NONE));
        var binder = new BND3();
        binder.Files.Add(new(Binder.FileFlags.Flag1, 0, @"N:\authored\n0000B2A10.nvm", first.Write()));
        binder.Files.Add(new(Binder.FileFlags.Flag1, 1, @"N:\authored\n0001B2A10.nvm", second.Write()));
        binder.Files.Add(new(Binder.FileFlags.Flag1, 2, @"N:\authored\n0009B2A10.nvm", first.Write()));
        File.WriteAllBytes(Path.Combine(source, "map/m10_02_00_00/m10_02_00_00.nvmbnd.dcx"), binder.Write());

        var read = SourceNavigationReader.Read(new Install(source, "DarkSoulsRemastered.exe"));
        // Meshes are read in name order; unplaced files are not read. Shared positions weld, so the second mesh's triangle
        // pairs with the first's along the shared edge, running the opposite way.
        if (read.Triangles.Length != 3) throw new Exception($"Expected three walkable triangles, read {read.Triangles.Length}");
        Vector3[] Corners((int A, int B, int C) t) => [read.Vertices[t.A], read.Vertices[t.B], read.Vertices[t.C]];
        if (read.Triangles.Any(t => Vector3.Cross(Corners(t)[1] - Corners(t)[0], Corners(t)[2] - Corners(t)[0]).Y <= 0)) throw new Exception("A triangle is not wound upward");
        var (one, two, copy) = (read.Triangles[0], read.Triangles[1], read.Triangles[2]);
        if (!Corners(one).SequenceEqual(new Vector3[] { new(0, 0, 0), new(1, 0, 1), new(1, 0, 0) })) throw new Exception("The first triangle changed");
        var shared = new[] { (one.B, one.C) };
        var edgesOfTwo = new[] { (two.A, two.B), (two.B, two.C), (two.C, two.A) };
        if (!edgesOfTwo.Contains((one.C, one.B)) || read.Vertices.Count(v => v == new Vector3(1, 0, 0)) != 2) throw new Exception("Meshes are not joined along their shared edge");
        // The copy would run the same way along the first triangle's edges, so it keeps its own vertices.
        if (new[] { copy.A, copy.B, copy.C }.Intersect(new[] { one.A, one.B, one.C, two.A, two.B, two.C }).Any()) throw new Exception("An overlapping triangle shares vertices");
        // Every triangle remembers the placed mesh it came from.
        if (!read.Parts.SequenceEqual(["n0000B2_0000", "n0001B2_0000", "n0001B2_0000"])) throw new Exception("Triangles do not name their source meshes: " + string.Join(", ", read.Parts));
        if (read.Dropped.Count != 3 || read.Dropped["closed: WALL"] != 1 || read.Dropped["on edge"] != 1 || read.Dropped["joined to nothing: overlapping edge"] != 1)
            throw new Exception("Dropped triangles are not accounted for: " + string.Join(", ", read.Dropped));
        layout.Parts.Navmeshes[0].Position = new(1, 0, 0);
        File.WriteAllBytes(Path.Combine(source, "map/MapStudio/m10_02_00_00.msb"), layout.Write());
        try { SourceNavigationReader.Read(new Install(source, "DarkSoulsRemastered.exe")); throw new Exception("A moved navigation mesh was accepted"); }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS: source navigation triangles weld across meshes, wind upward, and account for every triangle left out");
    }
}
