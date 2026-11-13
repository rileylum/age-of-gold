using System.Numerics;
using AgeOfGold;
using AgeOfGold.Archives;
using AgeOfGold.Collision;
using AgeOfGold.Navigation;
using SoulsFormats;

/// <summary>Against the host install: an authored mesh written into an installed template reads back as the host reads its own.</summary>
internal static class NavigationInstallCheck
{
    internal static void Run()
    {
        string hostPath = Environment.GetEnvironmentVariable("AOG_ELDEN_RING") ?? throw new Exception("Set AOG_ELDEN_RING");
        var host = new Install(hostPath, "eldenring.exe");
        Oodle.SearchPaths.Add(host.Root);
        var bundle = BND4.Read(new HostArchive(host).Read("map/m10/m10_01_00_00/m10_01_00_00.nvmhktbnd.dcx"));
        var template = bundle.Files.OrderBy(f => f.ID).First(f => HostNavigationMesh.IsTemplate(f.Bytes)).Bytes;
        // The installed mesh's own faces show which way round a face is wound when its upper side faces up.
        var (hostVertices, hostFaces) = Read(template);
        int upward = hostFaces.Count(f => Normal(hostVertices, f).Y > 0), downward = hostFaces.Count(f => Normal(hostVertices, f).Y < 0);
        Console.WriteLine($"Installed template: {hostFaces.Length} faces, {upward} wound with an upward normal, {downward} downward");
        if (downward != 0 || upward == 0) throw new Exception("Installed faces are not all wound with an upward normal");

        // A three-by-three grid of vertices on a slope, split into eight triangles wound the same way.
        var vertices = (from z in Enumerable.Range(0, 3) from x in Enumerable.Range(0, 3) select new Vector3(100 + x * 2, 50 + x * 0.5f + z * 0.25f, -30 + z * 3)).ToArray();
        var triangles = new List<(int, int, int)>();
        for (int z = 0; z < 2; z++)
            for (int x = 0; x < 2; x++)
            {
                int a = z * 3 + x, b = a + 1, c = a + 3, d = c + 1;
                triangles.Add((a, c, b)); triangles.Add((b, c, d));
            }
        if (triangles.Any(t => Normal(vertices, [t.Item1, t.Item2, t.Item3]).Y <= 0)) throw new Exception("Authored triangles are not wound upward");
        var written = HostNavigationMesh.Write(template, vertices, triangles);
        if (!written.SequenceEqual(HostNavigationMesh.Write(template, vertices, triangles))) throw new Exception("Navigation mesh writing does not repeat");
        var (readVertices, readFaces) = Read(written);
        if (!readVertices.SequenceEqual(vertices) || readFaces.Length != 8 || !readFaces.Select(f => (f[0], f[1], f[2])).SequenceEqual(triangles) || readFaces.Any(f => f.Length != 3))
            throw new Exception("Written vertices or faces differ");
        var file = new SourceTagfile(written, "20180100");
        var variants = file.Root.Field("namedVariants").Elements();
        var mesh = variants[0].Field("variant"); var mediator = variants[1].Field("variant");
        var edges = mesh.Field("edges").Elements().Select(e => e.Raw()).Select(b => (A: BitConverter.ToInt32(b, 0), B: BitConverter.ToInt32(b, 4), Twin: BitConverter.ToUInt32(b, 8), Face: BitConverter.ToUInt32(b, 12), Flags: b[16])).ToArray();
        int joined = 0;
        for (int i = 0; i < edges.Length; i++)
        {
            if (edges[i].Flags != 4) throw new Exception("Written edge is not flagged as an ordinary edge");
            if (edges[i].Twin == uint.MaxValue) { if (edges[i].Face != uint.MaxValue) throw new Exception("A border edge names a face"); continue; }
            var twin = edges[edges[i].Twin];
            if (twin.Twin != i || twin.A != edges[i].B || twin.B != edges[i].A || edges[i].Face != edges[i].Twin / 3) throw new Exception("Written edges are not paired both ways");
            joined++;
        }
        // Eight triangles in a two-by-two grid share eight inner edges: sixteen directed edges are paired, eight lie on the border.
        if (joined != 16 || edges.Length != 24) throw new Exception($"Expected sixteen paired edges of twenty-four, found {joined} of {edges.Length}");
        if (!mesh.Field("clearanceCacheSeedingDataSet").IsNull || mesh.Field("faceData").Elements().Length != 8 || mesh.Field("edgeData").Elements().Length != 24
            || variants[0].Field("name").Text() != "hkaiNavMesh" || mediator.TypeName != "hkaiStaticTreeNavMeshQueryMediator")
            throw new Exception("Written mesh keeps the template's clearances or lost its attributes or names");
        var low = mesh.Field("aabb").Field("min").Floats(); var high = mesh.Field("aabb").Field("max").Floats();
        if (new Vector3(low[0], low[1], low[2]) != vertices.Aggregate(Vector3.Min) || new Vector3(high[0], high[1], high[2]) != vertices.Aggregate(Vector3.Max)) throw new Exception("Written bounds differ");
        // The query tree reaches every face through boxes that contain it.
        var tree = mediator.Field("tree").Field("treePtr").Field("tree");
        var nodes = tree.Field("nodes").Elements().SelectMany(n => n.Raw()).ToArray();
        var domainLow = tree.Field("domain").Field("min").Floats(); var domainHigh = tree.Field("domain").Field("max").Floats();
        var boxes = triangles.Select(t => (Vector3.Min(vertices[t.Item1], Vector3.Min(vertices[t.Item2], vertices[t.Item3])), Vector3.Max(vertices[t.Item1], Vector3.Max(vertices[t.Item2], vertices[t.Item3])))).ToArray();
        NavigationTreeCheck.Reaches(nodes, new(domainLow[0], domainLow[1], domainLow[2]), new(domainHigh[0], domainHigh[1], domainHigh[2]), boxes);
        if (mediator.Field("navMesh").Field("faces").Elements().Length != 8) throw new Exception("The query tree's mesh reference no longer reaches the mesh");
        foreach (var bad in new Action[] { () => HostNavigationMesh.Write(template, vertices, [(0, 1, 99)]), () => HostNavigationMesh.Write(template, vertices, [(0, 3, 1), (0, 3, 1)]), () => HostNavigationMesh.Write(template, vertices, [(0, 1, 3)]),
            () => HostNavigationMesh.Write(bundle.Files.First(f => !HostNavigationMesh.IsTemplate(f.Bytes)).Bytes, vertices, triangles) })
        {
            try { bad(); }
            catch (InvalidDataException) { continue; }
            throw new Exception("An invalid navigation mesh or template was accepted");
        }
        Console.WriteLine("PASS: an authored mesh written into an installed host template reads back with its faces, paired edges, bounds and query tree");
    }

    // A mesh file's vertices and each face's vertex ring.
    private static (Vector3[] Vertices, int[][] Faces) Read(byte[] bytes)
    {
        var mesh = new SourceTagfile(bytes, "20180100").Root.Field("namedVariants").Elements()[0].Field("variant");
        var vertices = mesh.Field("vertices").Elements().Select(v => v.Floats()).Select(f => new Vector3(f[0], f[1], f[2])).ToArray();
        var edges = mesh.Field("edges").Elements().Select(e => BitConverter.ToInt32(e.Raw(), 0)).ToArray();
        var faces = mesh.Field("faces").Elements().Select(f => f.Raw()).Select(b => Enumerable.Range(BitConverter.ToInt32(b, 0), BitConverter.ToInt16(b, 8)).Select(e => edges[e]).ToArray()).ToArray();
        return (vertices, faces);
    }

    // The sum of the ring's triangle-fan cross products: its direction is the side the ring is wound anticlockwise from.
    private static Vector3 Normal(Vector3[] vertices, int[] ring)
    {
        var sum = Vector3.Zero;
        for (int i = 1; i + 1 < ring.Length; i++) sum += Vector3.Cross(vertices[ring[i]] - vertices[ring[0]], vertices[ring[i + 1]] - vertices[ring[0]]);
        return sum;
    }
}
