using System.Buffers.Binary;
using System.Numerics;
using AgeOfGold.Collision;

namespace AgeOfGold.Navigation;

/// <summary>Writes a host navigation mesh of triangles into an installed, non-empty host mesh file used as a template.</summary>
internal static class HostNavigationMesh
{
    private const string Release = "20180100";
    // An ordinary walkable edge, as the installed meshes flag theirs.
    private const byte OrdinaryEdge = 4;

    /// <summary>Whether an installed mesh file holds a mesh with faces and the tree its queries use.</summary>
    internal static bool IsTemplate(byte[] file)
    {
        var variants = new SourceTagfile(file, Release).Root.Field("namedVariants").Elements();
        return variants.Length == 2 && variants[0].Field("variant").TypeName == "hkaiNavMesh" && variants[1].Field("variant").TypeName == "hkaiStaticTreeNavMeshQueryMediator"
            && variants[0].Field("variant").Field("faces").Item != 0;
    }

    /// <param name="triangles">Vertex indices of each face, wound so its upper side faces up.</param>
    internal static byte[] Write(byte[] template, IReadOnlyList<Vector3> vertices, IReadOnlyList<(int A, int B, int C)> triangles)
    {
        if (!IsTemplate(template)) throw new InvalidDataException("Host navigation template is not a mesh with faces and a query tree");
        if (vertices.Count == 0 || triangles.Count == 0) throw new InvalidDataException("A navigation mesh needs vertices and faces");
        var edited = template.ToArray();
        var file = new SourceTagfile(edited, Release);
        var variants = file.Root.Field("namedVariants").Elements();
        var mesh = variants[0].Field("variant"); var tree = variants[1].Field("variant").Field("tree").Field("treePtr").Field("tree");
        var arrays = new Dictionary<int, (byte[], int)>();
        void Replace(SourceTagfile.Value array, int size, int count, Action<Span<byte>, int> write)
        {
            // The packed layouts written here are the installed files' own; another layout stops the build.
            if (array.Item == 0 || array.ElementSize != size) throw new InvalidDataException("Host navigation template stores an array in an unexpected form");
            var content = new byte[size * count];
            for (int i = 0; i < count; i++) write(content.AsSpan(i * size, size), i);
            arrays.Add(array.Item, (content, count));
        }

        // Each directed edge finds its twin running the other way along the same two vertices.
        var starts = new Dictionary<(int, int), int>();
        for (int face = 0; face < triangles.Count; face++)
        {
            var (a, b, c) = triangles[face];
            if (new[] { a, b, c }.Any(v => v < 0 || v >= vertices.Count) || a == b || b == c || c == a) throw new InvalidDataException("Invalid navigation triangle");
            // Every installed face is wound so this normal points up; the host walks on that side.
            if (!(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).Y > 0))
                throw new InvalidDataException($"Navigation triangle {face} is wound downward or stands on edge");
            foreach (var (edge, from, to) in new[] { (0, a, b), (1, b, c), (2, c, a) })
                if (!starts.TryAdd((from, to), face * 3 + edge)) throw new InvalidDataException("Two navigation faces run the same way along one edge");
        }
        Replace(mesh.Field("faces"), 16, triangles.Count, (face, i) =>
        {
            // First edge, no user edges, three edges, no cluster.
            BinaryPrimitives.WriteInt32LittleEndian(face, i * 3); BinaryPrimitives.WriteInt32LittleEndian(face[4..], -1);
            BinaryPrimitives.WriteInt16LittleEndian(face[8..], 3); BinaryPrimitives.WriteInt16LittleEndian(face[12..], -1);
        });
        Replace(mesh.Field("edges"), 20, triangles.Count * 3, (edge, i) =>
        {
            var (a, b, c) = triangles[i / 3];
            var (from, to) = (i % 3) switch { 0 => (a, b), 1 => (b, c), _ => (c, a) };
            BinaryPrimitives.WriteInt32LittleEndian(edge, from); BinaryPrimitives.WriteInt32LittleEndian(edge[4..], to);
            bool joined = starts.TryGetValue((to, from), out int twin);
            BinaryPrimitives.WriteUInt32LittleEndian(edge[8..], joined ? (uint)twin : uint.MaxValue);
            BinaryPrimitives.WriteUInt32LittleEndian(edge[12..], joined ? (uint)(twin / 3) : uint.MaxValue);
            edge[16] = OrdinaryEdge;
        });
        Replace(mesh.Field("vertices"), 16, vertices.Count, (vertex, i) =>
        {
            BinaryPrimitives.WriteSingleLittleEndian(vertex, vertices[i].X); BinaryPrimitives.WriteSingleLittleEndian(vertex[4..], vertices[i].Y);
            BinaryPrimitives.WriteSingleLittleEndian(vertex[8..], vertices[i].Z); BinaryPrimitives.WriteSingleLittleEndian(vertex[12..], 1);
        });
        // lazy: no per-face or per-edge attributes; the source's own face flags are not translated.
        Replace(mesh.Field("faceData"), 4, triangles.Count, (_, _) => { });
        Replace(mesh.Field("edgeData"), 4, triangles.Count * 3, (_, _) => { });
        var bounds = BoundingTree.Build(triangles.Select(t =>
            (Vector3.Min(vertices[t.A], Vector3.Min(vertices[t.B], vertices[t.C])), Vector3.Max(vertices[t.A], Vector3.Max(vertices[t.B], vertices[t.C])))).ToArray());
        Replace(tree.Field("nodes"), 6, bounds.Nodes.Length / 6, (node, i) => bounds.Nodes.AsSpan(i * 6, 6).CopyTo(node));

        void Corner(SourceTagfile.Value box, string field, Vector3 value)
        {
            int at = box.Field(field).Position;
            BinaryPrimitives.WriteSingleLittleEndian(edited.AsSpan(at), value.X); BinaryPrimitives.WriteSingleLittleEndian(edited.AsSpan(at + 4), value.Y);
            BinaryPrimitives.WriteSingleLittleEndian(edited.AsSpan(at + 8), value.Z);
        }
        foreach (var box in new[] { mesh.Field("aabb"), tree.Field("domain") }) { Corner(box, "min", bounds.Min); Corner(box, "max", bounds.Max); }
        // The template's precomputed clearances describe its own faces; without them the host works clearances out itself,
        // as it does for its installed meshes that carry none.
        var clearance = mesh.Field("clearanceCacheSeedingDataSet");
        if (clearance.Kind != "Record" && !clearance.IsNull) throw new InvalidDataException("Host navigation template lacks its clearance reference");
        BinaryPrimitives.WriteUInt64LittleEndian(edited.AsSpan(clearance.Position), 0);
        return new SourceTagfile(edited, Release).Rewrite(arrays);
    }
}
