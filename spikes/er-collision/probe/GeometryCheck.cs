using System.Numerics;
using HKLib.hk2018;
using HKLib.Serialization.hk2018.Binary;

// Manual install check: geometry is written from scratch, physics settings come from the host install.
static class GeometryCheck
{
    /// <summary>Check synthetic geometry through serialized host-format readback.</summary>
    public static void Run(Func<IHavokObject> template, string output, Func<HavokBinarySerializer>? writerFactory = null)
    {
        output = Path.GetFullPath(output);
        if (!output.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("geometry check output must be under the system temp directory");
        Directory.CreateDirectory(output);
        writerFactory ??= () => new HavokBinarySerializer();
        Check(template, writerFactory, output, 1);
        Check(template, writerFactory, output, 17);
        Check(template, writerFactory, output, 86, reuseLast: true);
        Check(template, writerFactory, output, 86);
        Check(template, writerFactory, output, 128, true);
        Check(template, writerFactory, output, 21840);
    }

    static void Check(Func<IHavokObject> template, Func<HavokBinarySerializer> writerFactory, string output, int count, bool reuseVertices = false, bool reuseLast = false)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<(int A, int B, int C)>();
        for (int i = 0; i < count; i++)
        {
            // Disjoint sloped triangles expose dropped primitives and wrapping vertex indices.
            float x = (i % 100) * 2, z = (i / 100) * 2;
            vertices.AddRange([new(x, 0, z), new(x, 0, z + 1), new(x + 1, 0.25f, z)]);
            triangles.Add((3 * i, 3 * i + 1, 3 * i + 2));
        }
        if (reuseVertices)
        {
            vertices = vertices.Take(3).ToList();
            triangles = Enumerable.Repeat((0, 1, 2), count).ToList();
        }
        // With 85 disjoint faces plus one new vertex, the final corner occupies local slot 255.
        if (reuseLast) triangles[^1] = (0, 1, vertices.Count - 1);
        var root = template();
        Build.ReplaceGeometry(root, vertices, triangles, "synthetic-mesh");
        var writer = writerFactory();
        using var bytes = new MemoryStream();
        writer.Write(root, bytes);
        var path = Path.Combine(output, $"mesh-{count}{(reuseLast ? "-256-slots" : "")}.hkx");
        using (var file = new FileStream(path, FileMode.CreateNew)) file.Write(bytes.ToArray());
        var readback = new HavokBinarySerializer().Read(path);
        int sections = CheckMesh(readback, vertices, triangles);
        if (reuseLast) Require(sections == 1, "256 local vertex slots should fit one section");
        var repeated = template();
        Build.ReplaceGeometry(repeated, vertices, triangles, "synthetic-mesh");
        using var second = new MemoryStream();
        writerFactory().Write(repeated, second);
        Require(bytes.ToArray().AsSpan().SequenceEqual(second.ToArray()), "repeated geometry build changed bytes");
        Console.WriteLine($"PASS: {count} triangles read back, {sections} sections; {path}");
    }

    /// <summary>Check written geometry and its lookup trees against the supplied mesh.</summary>
    public static int CheckMesh(IHavokObject readback, IReadOnlyList<Vector3> vertices,
        IReadOnlyList<(int A, int B, int C)> triangles)
    {
        int count = triangles.Count;
        var scene = (hknpPhysicsSceneData)((hkRootLevelContainer)readback).m_namedVariants[0].m_variant!;
        var shape = (fsnpCustomParamCompressedMeshShape)scene.m_systemDatas[0]!.m_bodyCinfos[0].m_shape!;
        var data = (hknpCompressedMeshShapeData)shape.m_data!;
        var tree = data.m_meshTree;
        Require(tree.m_primitives.Count == count, "triangle count changed");
        Require(tree.m_numPrimitiveKeys == count, "primitive-key count changed");
        Require(shape.m_triangleIndexToShapeKey.Count == count, "shape-key count changed");
        Require(shape.m_numShapeKeyBits == tree.m_bitsPerKey, "shape and tree key widths disagree");
        var boxes = new Dictionary<uint, (Vector3 Lo, Vector3 Hi)>();
        int triangle = 0;
        for (int sectionIndex = 0; sectionIndex < tree.m_sections.Count; sectionIndex++)
        {
            var section = tree.m_sections[sectionIndex];
            Require(section.m_numPrimitives is >= 1 and <= 127, "section primitive limit exceeded");
            Require(section.m_numPackedVertices == 0, "check expects shared vertices");
            Require(section.m_page == 0, "check expects one shared-vertex page");
            var run = tree.m_primitiveDataRuns[(int)section.m_firstDataRunIndex];
            Require(section.m_numDataRuns == 1 && run.m_index == 0 && run.m_count == section.m_numPrimitives,
                "primitive data run does not cover the section");
            for (int p = 0; p < section.m_numPrimitives; p++, triangle++)
            {
                uint key = (uint)((sectionIndex << 8) | (p << 1));
                int unused = 32 - tree.m_bitsPerKey;
                Require(shape.m_triangleIndexToShapeKey[triangle] == ((key << unused) | (uint)((1L << unused) - 1)),
                    "triangle maps to the wrong shape key");
                var indices = tree.m_primitives[(int)section.m_firstPrimitiveIndex + p].m_indices;
                Require(indices[2] == indices[3], "triangle became a quad");
                var triangleLo = new Vector3(float.MaxValue);
                var triangleHi = new Vector3(-float.MaxValue);
                for (int corner = 0; corner < 3; corner++)
                {
                    int shared = tree.m_sharedVerticesIndex[(int)section.m_firstSharedVertexIndex + indices[corner]];
                    ulong packed = tree.m_sharedVertices[shared];
                    var lo = tree.m_domain.m_min;
                    var extent = tree.m_domain.m_max - lo;
                    var actual = new Vector3(
                        lo.X + (packed & 0x1FFFFF) / 2097151f * extent.X,
                        lo.Y + ((packed >> 21) & 0x1FFFFF) / 2097151f * extent.Y,
                        lo.Z + (packed >> 42) / 4194303f * extent.Z);
                    var face = triangles[triangle];
                    int expectedIndex = corner == 0 ? face.A : corner == 1 ? face.B : face.C;
                    Require(Vector3.Distance(actual, vertices[expectedIndex]) < 0.001f,
                        $"triangle {triangle}, corner {corner}: position changed");
                    triangleLo = Vector3.Min(triangleLo, actual);
                    triangleHi = Vector3.Max(triangleHi, actual);
                }
                boxes.Add(key, (triangleLo, triangleHi));
            }
        }
        Require(triangle == count, "sections do not cover all triangles");
        uint maxKey = boxes.Keys.Max();
        Require(tree.m_maxKeyValue == maxKey && tree.m_bitsPerKey == BitOperations.Log2(maxKey | 1) + 1,
            "maximum key or key width is wrong");
        Require(shape.m_triangleIsInterior.m_storage.m_numBits == maxKey + 1, "interior bit field has the wrong key range");
        Require(shape.m_triangleIsInterior.m_storage.m_words.Count == (maxKey + 32) / 32
            && shape.m_triangleIsInterior.m_storage.m_words.All(w => w == 0), "interior bits contain stale data");
        var param = shape.m_pParam!;
        Require(param.m_triangleDataArray.Count == count && param.m_primitiveDataArray.Count == 1,
            "triangle parameters contain stale rows");
        Require(param.m_primitiveDataArray[0].m_vertexData.Count == vertices.Count * param.m_vertexDataStride,
            "vertex parameters do not cover the mesh");
        for (int i = 0; i < count; i++)
        {
            var item = param.m_triangleDataArray[i];
            var (a, b, c) = triangles[i];
            Require(item.m_primitiveDataIndex == 0 && item.m_triangleDataIndex == i
                && item.m_vertexIndexA == c && item.m_vertexIndexB == b && item.m_vertexIndexC == a,
                "triangle parameters address the wrong input triangle");
        }
        CheckTrees(readback);
        CheckSimd(data, boxes);
        return tree.m_sections.Count;
    }

    /// <summary>Check top-level and section-tree reachability and leaf identities.</summary>
    public static void CheckTrees(IHavokObject root)
    {
        var scene = (hknpPhysicsSceneData)((hkRootLevelContainer)root).m_namedVariants[0].m_variant!;
        var shape = (fsnpCustomParamCompressedMeshShape)scene.m_systemDatas[0]!.m_bodyCinfos[0].m_shape!;
        var tree = ((hknpCompressedMeshShapeData)shape.m_data!).m_meshTree;
        var visited = new HashSet<int>();
        var sections = new HashSet<int>();
        void VisitTop(int at)
        {
            Require(at >= 0 && at < tree.m_nodes.Count && visited.Add(at), "invalid top-level node");
            var node = tree.m_nodes[at];
            int payload = ((node.m_hiData & 127) << 8) | node.m_loData;
            if ((node.m_hiData & 128) != 0)
            {
                VisitTop(at + 1);
                VisitTop(at + payload * 2);
            }
            else
            {
                Require(payload < tree.m_sections.Count && sections.Add(payload), "invalid top-level leaf");
                Require(tree.m_sections[payload].m_leafIndex == at, "section leaf index disagrees with top tree");
            }
        }
        VisitTop(0);
        Require(visited.Count == tree.m_nodes.Count && sections.Count == tree.m_sections.Count, "incomplete top tree");
        foreach (var section in tree.m_sections)
        {
            visited.Clear();
            var primitives = new HashSet<int>();
            void VisitSection(int at)
            {
                Require(at >= 0 && at < section.m_nodes.Count && visited.Add(at), "invalid section node");
                int payload = section.m_nodes[at].m_data;
                if ((payload & 1) != 0)
                {
                    VisitSection(at + 1);
                    VisitSection(at + (payload & ~1));
                }
                else Require(payload / 2 < section.m_numPrimitives && primitives.Add(payload / 2), "invalid primitive leaf");
            }
            VisitSection(0);
            Require(visited.Count == section.m_nodes.Count && primitives.Count == section.m_numPrimitives, "incomplete section tree");
        }
    }

    static void CheckSimd(hknpCompressedMeshShapeData data, Dictionary<uint, (Vector3 Lo, Vector3 Hi)> expected)
    {
        Require(data.m_hasSimdTree, "SIMD tree missing");
        var nodes = data.m_simdTree.m_nodes;
        var seen = new HashSet<int>();
        var found = new HashSet<uint>();
        (Vector3 Lo, Vector3 Hi) Visit(int index)
        {
            Require(index > 0 && index < nodes.Count && seen.Add(index), "invalid or repeated SIMD node");
            var node = nodes[index];
            var lo = new Vector3(float.MaxValue);
            var hi = new Vector3(-float.MaxValue);
            for (int slot = 0; slot < 4; slot++)
            {
                var laneLo = new Vector3(node.m_lx[slot], node.m_ly[slot], node.m_lz[slot]);
                var laneHi = new Vector3(node.m_hx[slot], node.m_hy[slot], node.m_hz[slot]);
                if (laneLo.X > laneHi.X || laneLo.Y > laneHi.Y || laneLo.Z > laneHi.Z) continue;
                (Vector3 Lo, Vector3 Hi) content;
                if (node.m_isLeaf)
                {
                    Require(found.Add(node.m_data[slot]), "primitive appears twice in SIMD tree");
                    content = expected[node.m_data[slot]];
                }
                else content = Visit(checked((int)node.m_data[slot]));
                for (int axis = 0; axis < 3; axis++)
                    Require(laneLo[axis] <= content.Lo[axis] && laneHi[axis] >= content.Hi[axis],
                        "SIMD box excludes its content");
                lo = Vector3.Min(lo, laneLo); hi = Vector3.Max(hi, laneHi);
            }
            return (lo, hi);
        }
        Visit(1);
        Require(found.SetEquals(expected.Keys), "SIMD tree does not cover every primitive");
        Require(seen.Count == nodes.Count - 1, "unreachable SIMD nodes");
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
