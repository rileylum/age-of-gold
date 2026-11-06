using System.Numerics;
using HKLib.hk2018;
using HKLib.hk2018.hkcdStaticMeshTree;
using Aabb4 = HKLib.hk2018.hkcdCompressedAabbCodecs.Aabb4BytesCodec;
using Aabb5 = HKLib.hk2018.hkcdCompressedAabbCodecs.Aabb5BytesCodec;

// Spike: replaces a real collision part's geometry with our own triangles, keeping its physics settings.
static class Build
{
    const float Margin = 0.015f;

    // Triangles wind counter-clockwise seen from their front (right-handed, Y up).
    public static void ReplaceGeometry(IHavokObject root, IReadOnlyList<Vector3> vertices, IReadOnlyList<(int A, int B, int C)> triangles, string bodyName)
    {
        int n = triangles.Count;
        if (n is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(triangles), "spike handles 1–4 triangles: one SIMD leaf");
        dynamic scene = ((hkRootLevelContainer)root).m_namedVariants[0].m_variant!;
        dynamic body = scene.m_systemDatas[0].m_bodyCinfos[0];
        body.m_name = bodyName;
        dynamic shape = body.m_shape;
        var data = (hknpCompressedMeshShapeData)shape.m_data;
        var tree = data.m_meshTree;

        var lo = vertices.Aggregate(Vector3.Min); var hi = vertices.Aggregate(Vector3.Max);
        var domain = new hkAabb { m_min = new Vector4(lo - new Vector3(Margin), 1 - Margin), m_max = new Vector4(hi + new Vector3(Margin), 1 + Margin) };
        int bits = BitsFor(n) + 1;

        tree.m_packedVertices.Clear();
        tree.m_sharedVertices.Clear();
        tree.m_sharedVertices.AddRange(vertices.Select(v => PackShared(v, domain)));
        tree.m_sharedVerticesIndex.Clear();
        tree.m_sharedVerticesIndex.AddRange(Enumerable.Range(0, vertices.Count).Select(i => (ushort)i));
        tree.m_primitives.Clear();
        foreach (var (a, b, c) in triangles)
        {
            var p = new Primitive(); p.m_indices[0] = (byte)a; p.m_indices[1] = (byte)b; p.m_indices[2] = (byte)c; p.m_indices[3] = (byte)c;
            tree.m_primitives.Add(p);
        }
        tree.m_primitiveDataRuns.Clear();
        tree.m_primitiveDataRuns.Add(new hkcdDefaultStaticMeshTree.PrimitiveDataRun { m_value = 0xFFFF, m_index = 0, m_count = (byte)n });
        tree.m_numPrimitiveKeys = n;
        tree.m_bitsPerKey = bits;
        tree.m_maxKeyValue = (uint)((n - 1) << 1);

        var section = tree.m_sections[0];
        tree.m_sections.Clear();
        tree.m_sections.Add(section);
        section.m_firstPackedVertexIndex = 0; section.m_firstSharedVertexIndex = 0;
        section.m_firstPrimitiveIndex = 0; section.m_firstDataRunIndex = 0;
        section.m_numPackedVertices = 0; section.m_numPrimitives = (byte)n; section.m_numDataRuns = 1;
        section.m_leafIndex = 0;
        section.m_domain = Copy(domain);
        section.m_nodes.Clear();
        AddSectionNodes(section.m_nodes, 0, n);

        tree.m_nodes.Clear();
        tree.m_nodes.Add(new Aabb5());
        tree.m_domain = Copy(domain);

        // SIMD tree: an empty node 0, a root holding the whole mesh, and one leaf with a box per triangle.
        var simd = data.m_simdTree.m_nodes;
        simd.Clear();
        simd.Add(EmptyNode(false));
        var rootNode = EmptyNode(false);
        rootNode.m_data[0] = 2;
        SetSlot(rootNode, 0, lo, hi);
        simd.Add(rootNode);
        var leaf = EmptyNode(true);
        for (int i = 0; i < n; i++)
        {
            var (a, b, c) = triangles[i];
            leaf.m_data[i] = (uint)(i << 1);
            SetSlot(leaf, i, Vector3.Min(Vector3.Min(vertices[a], vertices[b]), vertices[c]), Vector3.Max(Vector3.Max(vertices[a], vertices[b]), vertices[c]));
        }
        simd.Add(leaf);
        data.m_simdTree.m_isCompact = true;
        data.m_hasSimdTree = true;
        data.m_connectivity.m_headers.Clear(); data.m_connectivity.m_localLinks.Clear(); data.m_connectivity.m_globalLinks.Clear();

        shape.m_numShapeKeyBits = (byte)bits;
        List<uint> keys = shape.m_triangleIndexToShapeKey;
        keys.Clear();
        uint low = (1u << (32 - bits)) - 1;
        for (int i = 0; i < n; i++) keys.Add(((uint)(i << 1) << (32 - bits)) | low);
        dynamic interior = shape.m_triangleIsInterior;
        interior.m_storage.m_numBits = 2 * n - 1;
        interior.m_storage.m_words.Clear();
        interior.m_storage.m_words.Add(0u);

        var param = (fsnpCustomMeshParameter)shape.m_pParam;
        param.m_triangleDataArray.Clear();
        for (int i = 0; i < n; i++)
        {
            var (a, b, c) = triangles[i];
            param.m_triangleDataArray.Add(new fsnpCustomMeshParameter.TriangleData
                { m_primitiveDataIndex = 0, m_triangleDataIndex = (uint)i, m_vertexIndexA = (uint)c, m_vertexIndexB = (uint)b, m_vertexIndexC = (uint)a });
        }
        var prim = param.m_primitiveDataArray[0];
        param.m_primitiveDataArray.Clear();
        param.m_primitiveDataArray.Add(prim);
        prim.m_vertexData.Clear();
        prim.m_vertexData.AddRange(Enumerable.Repeat((byte)255, vertices.Count));
    }

    static int BitsFor(int n) { int b = 0; while ((1 << b) < n) b++; return b; }

    // 21, 21 and 22 bits of x, y and z, each quantised across the domain.
    static ulong PackShared(Vector3 v, hkAabb d)
    {
        ulong Q(float x, float min, float max, int bits) => (ulong)Math.Round((x - min) / (max - min) * ((1 << bits) - 1));
        return Q(v.X, d.m_min.X, d.m_max.X, 21) | Q(v.Y, d.m_min.Y, d.m_max.Y, 21) << 21 | Q(v.Z, d.m_min.Z, d.m_max.Z, 22) << 42;
    }

    // Depth first; a leaf stores its primitive index doubled, an inner node half the distance to its right child, plus one.
    // Zero boxes mean "same as parent", which is loose but always encloses.
    static void AddSectionNodes(List<Aabb4> nodes, int first, int count)
    {
        var node = new Aabb4();
        nodes.Add(node);
        if (count == 1) { node.m_data = (byte)(first << 1); return; }
        int left = count / 2;
        node.m_data = (byte)((left << 1) | 1);
        AddSectionNodes(nodes, first, left);
        AddSectionNodes(nodes, first + left, count - left);
    }

    static hkcdSimdTree.Node EmptyNode(bool leaf)
    {
        var node = new hkcdSimdTree.Node { m_isLeaf = leaf };
        for (int i = 0; i < 4; i++) SetSlot(node, i, new Vector3(float.MaxValue), new Vector3(-float.MaxValue));
        return node;
    }

    static void SetSlot(hkcdSimdTree.Node node, int i, Vector3 lo, Vector3 hi)
    {
        node.m_lx = With(node.m_lx, i, lo.X); node.m_hx = With(node.m_hx, i, hi.X);
        node.m_ly = With(node.m_ly, i, lo.Y); node.m_hy = With(node.m_hy, i, hi.Y);
        node.m_lz = With(node.m_lz, i, lo.Z); node.m_hz = With(node.m_hz, i, hi.Z);
    }

    static Vector4 With(Vector4 v, int i, float x) { v[i] = x; return v; }
    static hkAabb Copy(hkAabb a) => new() { m_min = a.m_min, m_max = a.m_max };
}
