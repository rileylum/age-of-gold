using System.Numerics;
using HKLib.hk2018;
using HKLib.hk2018.hkcdStaticMeshTree;
using Aabb4 = HKLib.hk2018.hkcdCompressedAabbCodecs.Aabb4BytesCodec;
using Aabb5 = HKLib.hk2018.hkcdCompressedAabbCodecs.Aabb5BytesCodec;

// Spike: replaces a real collision part's geometry with our own triangles, keeping its physics settings.
static class Build
{
    const float Margin = 0.015f;

    /// <summary>Replace template geometry with counter-clockwise triangles in right-handed, Y-up space.</summary>
    public static void ReplaceGeometry(IHavokObject root, IReadOnlyList<Vector3> vertices, IReadOnlyList<(int A, int B, int C)> triangles, string bodyName)
    {
        int n = triangles.Count;
        if (n < 1) throw new ArgumentOutOfRangeException(nameof(triangles), "a mesh needs triangles");
        // lazy: one shared-vertex page; add paging or packed vertices when a part exceeds 65,536 vertices.
        if (vertices.Count is < 1 or > 65536) throw new ArgumentOutOfRangeException(nameof(vertices), "spike handles 1–65,536 vertices per part");
        foreach (var v in vertices)
            if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z))
                throw new ArgumentException("mesh positions must be finite", nameof(vertices));
        dynamic scene = ((hkRootLevelContainer)root).m_namedVariants[0].m_variant!;
        dynamic body = scene.m_systemDatas[0].m_bodyCinfos[0];
        body.m_name = bodyName;
        dynamic shape = body.m_shape;
        var data = (hknpCompressedMeshShapeData)shape.m_data;
        var tree = data.m_meshTree;

        var lo = vertices.Aggregate(Vector3.Min); var hi = vertices.Aggregate(Vector3.Max);
        var domain = new hkAabb { m_min = new Vector4(lo - new Vector3(Margin), 1 - Margin), m_max = new Vector4(hi + new Vector3(Margin), 1 + Margin) };
        var extent = domain.m_max - domain.m_min;
        if (!float.IsFinite(extent.X) || !float.IsFinite(extent.Y) || !float.IsFinite(extent.Z)
            || extent.X <= 0 || extent.Y <= 0 || extent.Z <= 0)
            throw new ArgumentException("mesh extent is not representable", nameof(vertices));

        tree.m_packedVertices.Clear();
        tree.m_sharedVertices.Clear();
        tree.m_sharedVertices.AddRange(vertices.Select(v => PackShared(v, domain)));
        tree.m_sharedVerticesIndex.Clear();
        tree.m_primitives.Clear();
        tree.m_primitiveDataRuns.Clear();
        tree.m_sections.Clear();
        var primitiveKeys = new uint[n];
        for (int first = 0; first < n;)
        {
            int sectionIndex = tree.m_sections.Count;
            if (sectionIndex == 32768) throw new ArgumentOutOfRangeException(nameof(triangles), "top tree has at most 32,768 section leaves");
            var section = new Section
            {
                m_firstSharedVertexIndex = (uint)tree.m_sharedVerticesIndex.Count,
                m_firstPrimitiveIndex = (uint)first,
                m_firstDataRunIndex = (uint)tree.m_primitiveDataRuns.Count,
                m_numDataRuns = 1,
                m_domain = Copy(domain),
            };
            var local = new Dictionary<int, byte>();
            int end = first;
            while (end < n && end - first < 127)
            {
                var (a, b, c) = triangles[end];
                int[] corners = [a, b, c];
                foreach (int index in corners)
                    if ((uint)index >= vertices.Count) throw new ArgumentException($"triangle {end} has an invalid vertex index", nameof(triangles));
                if (local.Count + corners.Distinct().Count(i => !local.ContainsKey(i)) > 256) break;
                foreach (int index in corners)
                    if (!local.ContainsKey(index))
                    {
                        local.Add(index, checked((byte)local.Count));
                        tree.m_sharedVerticesIndex.Add(checked((ushort)index));
                    }
                var primitive = new Primitive();
                primitive.m_indices[0] = local[a]; primitive.m_indices[1] = local[b];
                primitive.m_indices[2] = primitive.m_indices[3] = local[c];
                tree.m_primitives.Add(primitive);
                primitiveKeys[end] = (uint)((sectionIndex << 8) | ((end - first) << 1));
                end++;
            }
            section.m_numPrimitives = checked((byte)(end - first));
            AddSectionNodes(section.m_nodes, 0, end - first);
            tree.m_sections.Add(section);
            tree.m_primitiveDataRuns.Add(new hkcdDefaultStaticMeshTree.PrimitiveDataRun
                { m_value = 0xFFFF, m_index = 0, m_count = section.m_numPrimitives });
            first = end;
        }
        uint maxKey = primitiveKeys[^1];
        int bits = Math.Max(1, BitsFor(checked((int)maxKey + 1)));
        tree.m_numPrimitiveKeys = n;
        tree.m_bitsPerKey = bits;
        tree.m_maxKeyValue = maxKey;

        tree.m_nodes.Clear();
        AddTopNodes(tree.m_nodes, tree.m_sections, 0, tree.m_sections.Count);
        tree.m_domain = Copy(domain);

        // Node zero is the sentinel; node one remains an inner root even for a small mesh.
        var simd = data.m_simdTree.m_nodes;
        simd.Clear();
        simd.Add(EmptyNode(false));
        // Include quantisation error as well as the collision margin in each SIMD box.
        var padding = new Vector3(Margin) + new Vector3(extent.X / 2097151f, extent.Y / 2097151f, extent.Z / 4194303f);
        var boxes = triangles.Select(t => (
            Lo: Vector3.Min(Vector3.Min(vertices[t.A], vertices[t.B]), vertices[t.C]) - padding,
            Hi: Vector3.Max(Vector3.Max(vertices[t.A], vertices[t.B]), vertices[t.C]) + padding)).ToArray();
        AddSimdNodes(simd, boxes, primitiveKeys, 0, n, true);
        data.m_simdTree.m_isCompact = true;
        data.m_hasSimdTree = true;
        data.m_connectivity.m_headers.Clear(); data.m_connectivity.m_localLinks.Clear(); data.m_connectivity.m_globalLinks.Clear();
        shape.m_externShapes.Clear();

        shape.m_numShapeKeyBits = (byte)bits;
        List<uint> keys = shape.m_triangleIndexToShapeKey;
        keys.Clear();
        uint low = (1u << (32 - bits)) - 1;
        foreach (uint key in primitiveKeys) keys.Add((key << (32 - bits)) | low);
        dynamic interior = shape.m_triangleIsInterior;
        int bitCount = checked((int)maxKey + 1);
        interior.m_storage.m_numBits = bitCount;
        interior.m_storage.m_words.Clear();
        for (int i = 0; i < (bitCount + 31) / 32; i++) interior.m_storage.m_words.Add(0u);

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

    // A leaf stores its primitive index doubled; an inner node stores its right-child distance with bit zero set.
    // lazy: scalar trees keep their parent's box; tighten m_xyz when whole-map play exposes traversal cost.
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

    static void AddTopNodes(List<Aabb5> nodes, List<Section> sections, int first, int count)
    {
        int at = nodes.Count;
        var node = new Aabb5();
        nodes.Add(node);
        if (count == 1)
        {
            node.m_hiData = checked((byte)(first >> 8));
            node.m_loData = (byte)(first & 255);
            sections[first].m_leafIndex = checked((ushort)at);
            return;
        }
        int left = count / 2;
        node.m_hiData = (byte)(128 | (left >> 8));
        node.m_loData = (byte)(left & 255);
        AddTopNodes(nodes, sections, first, left);
        AddTopNodes(nodes, sections, first + left, count - left);
    }

    static hkcdSimdTree.Node EmptyNode(bool leaf)
    {
        var node = new hkcdSimdTree.Node { m_isLeaf = leaf };
        for (int i = 0; i < 4; i++) SetSlot(node, i, new Vector3(float.MaxValue), new Vector3(-float.MaxValue));
        return node;
    }

    static (Vector3 Lo, Vector3 Hi) AddSimdNodes(List<hkcdSimdTree.Node> nodes,
        (Vector3 Lo, Vector3 Hi)[] boxes, uint[] keys, int first, int count, bool root = false)
    {
        var node = EmptyNode(count <= 4 && !root);
        nodes.Add(node);
        var lo = new Vector3(float.MaxValue); var hi = new Vector3(-float.MaxValue);
        int step = node.m_isLeaf ? 1 : (count + 3) / 4;
        for (int at = first, slot = 0; at < first + count; at += step, slot++)
        {
            node.m_data[slot] = node.m_isLeaf ? keys[at] : (uint)nodes.Count;
            var box = node.m_isLeaf ? boxes[at]
                : AddSimdNodes(nodes, boxes, keys, at, Math.Min(step, first + count - at));
            SetSlot(node, slot, box.Lo, box.Hi);
            lo = Vector3.Min(lo, box.Lo); hi = Vector3.Max(hi, box.Hi);
        }
        return (lo, hi);
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
