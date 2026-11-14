using System.Numerics;
using SoulsFormats;

namespace AgeOfGold.Characters;

/// <summary>Writes a source character's meshes into a host character's model, carried by the host's own skeleton.</summary>
internal static class HostCharacterModel
{
    /// <param name="Mesh">The source mesh to carry.</param>
    /// <param name="Exemplar">The host mesh whose vertex layout and material it takes.</param>
    /// <param name="Textures">Texture paths by the exemplar material's input name; an input not named keeps the host material's own.</param>
    internal sealed record Part(int Mesh, int Exemplar, IReadOnlyDictionary<string, string> Textures);

    /// <summary>One source character's body.</summary>
    /// <param name="PairedBones">Source bones carried by a host bone of another name.</param>
    /// <param name="Mark">The display mark its meshes carry, so only rows that show the mark draw it; null draws it always.</param>
    /// <param name="Riders">Source bones that ride another source bone: attachments with no host counterpart.</param>
    /// <param name="Scale">The size ratio of the whole body, when this model is one part of it; null measures it from this model.</param>
    internal sealed record Body(FLVER2 Source, IReadOnlyDictionary<string, string>? PairedBones, IReadOnlyList<Part> Parts, int? Mark,
        IReadOnlyDictionary<string, string>? Riders = null, float? Scale = null);

    /// <summary>The host skeleton's size over a source skeleton's, measured across the bones they share by name.</summary>
    internal static float SizeRatio(byte[] hostModel, FLVER2 source, IReadOnlyDictionary<string, string>? pairedBones = null) =>
        SkeletonFit.Match(Bones(source), Bones(FLVER2.Read(hostModel)), pairedBones).Scale;

    private static SkeletonFit.Bone[] Bones(FLVER2 model)
    {
        var world = new Matrix4x4?[model.Nodes.Count];
        Matrix4x4 World(int node, int depth = 0)
        {
            if (node < 0) return Matrix4x4.Identity;
            if (node >= model.Nodes.Count || depth > model.Nodes.Count) throw new InvalidDataException("Model node hierarchy is invalid or cyclic");
            return world[node] ??= model.Nodes[node].ComputeLocalTransform() * World(model.Nodes[node].ParentIndex, depth + 1);
        }
        return model.Nodes.Select((node, i) => new SkeletonFit.Bone(node.Name, node.ParentIndex, World(i))).ToArray();
    }

    /// <summary>The highest display mark a host character row can show or hide in the field this build writes.</summary>
    internal const int HighestMark = 15;

    /// <summary>
    /// The host model with its meshes replaced by the source's. The host's nodes and attachment points stay, so its
    /// animations, hit points and effects still apply; each source vertex moves from its source bones' rest places to the
    /// host bones that carry them and is weighted to those.
    /// </summary>
    /// <remarks>Several bodies may share the model: each is drawn for the rows that show its mark.</remarks>
    internal static byte[] Write(byte[] hostModel, IReadOnlyList<Body> bodies) => Write(FLVER2.Read(hostModel), bodies, false);

    /// <summary>
    /// The source's body whole, in the host's model format: the source's own nodes and attachment points, so its own
    /// skeleton, animations and hit points apply and nothing is re-posed or resized. Only the meshes' vertex layouts and
    /// materials are a host model's.
    /// </summary>
    /// <param name="hostModel">A host character model whose meshes the parts name as exemplars.</param>
    /// <param name="bones">The names of the source skeleton's bones; the model's other nodes carry meshes or attachment points.</param>
    internal static byte[] WriteWhole(byte[] hostModel, FLVER2 source, IReadOnlyList<Part> parts, IReadOnlyCollection<string> bones)
    {
        var host = FLVER2.Read(hostModel);
        Stand(host, source, bones, source.Meshes.Select(m => m.NodeIndex).ToHashSet());
        return Write(host, [new(source, null, parts, null)], true);
    }

    // Gives a host model a source model's nodes and attachment points in place of its own.
    private static void Stand(FLVER2 host, FLVER2 source, IReadOnlyCollection<string> bones, HashSet<int> meshNodes)
    {
        if (bones.Except(source.Nodes.Select(n => n.Name)).FirstOrDefault() is { } absent) throw new InvalidDataException("The source model has no node for its skeleton's bone " + absent);
        var owners = source.Dummies.SelectMany(d => new[] { (int)d.AttachBoneIndex, (int)d.ParentBoneIndex }).Where(i => i >= 0).ToHashSet();
        if (owners.Any(i => i >= source.Nodes.Count)) throw new InvalidDataException("A source attachment point names a missing node");
        host.Nodes = source.Nodes.Select((node, i) => new FLVER.Node
        {
            Name = node.Name, ParentIndex = node.ParentIndex, FirstChildIndex = node.FirstChildIndex, NextSiblingIndex = node.NextSiblingIndex, PreviousSiblingIndex = node.PreviousSiblingIndex,
            Translation = node.Translation, Rotation = node.Rotation, Scale = node.Scale, BoundingBoxMin = node.BoundingBoxMin, BoundingBoxMax = node.BoundingBoxMax,
            // The host is told what each node is for; the source's format does not say.
            Flags = bones.Contains(node.Name) ? FLVER.Node.NodeFlags.Bone | (owners.Contains(i) ? FLVER.Node.NodeFlags.DummyOwner : 0)
                : meshNodes.Contains(i) ? FLVER.Node.NodeFlags.Mesh : owners.Contains(i) ? FLVER.Node.NodeFlags.DummyOwner : FLVER.Node.NodeFlags.Disabled,
        }).ToList();
        host.Dummies = source.Dummies.ToList();
    }

    // The host lists a model's node tree twice over; both lists hold every node in order.
    private static void ListNodes(FLVER2 host)
    {
        FLVER2.SkeletonSet.Bone Link(FLVER.Node node, int i) => new(i) { ParentIndex = node.ParentIndex, FirstChildIndex = node.FirstChildIndex, NextSiblingIndex = node.NextSiblingIndex, PreviousSiblingIndex = node.PreviousSiblingIndex };
        host.Skeletons = new FLVER2.SkeletonSet { BaseSkeleton = host.Nodes.Select(Link).ToList(), AllSkeletons = host.Nodes.Select(Link).ToList() };
    }

    /// <summary>
    /// A source body that comes as several models sharing one skeleton — a human's armour pieces and what it holds — whole
    /// on that skeleton's nodes. Each piece's vertices follow their bones from where the piece rests them to where the
    /// skeleton does.
    /// </summary>
    /// <param name="skeleton">The source model that has the skeleton's nodes and attachment points.</param>
    /// <param name="pieces">The piece models, unmarked and at the skeleton's own size.</param>
    internal static byte[] WritePieces(byte[] hostModel, FLVER2 skeleton, IReadOnlyList<Body> pieces, IReadOnlyCollection<string> bones)
    {
        if (pieces.Any(p => p.Mark is not null || p.Scale != 1)) throw new InvalidDataException("A whole body's pieces are unmarked and at their skeleton's own size");
        var host = FLVER2.Read(hostModel);
        Stand(host, skeleton, bones, []);
        // The pieces' own mesh nodes are not the skeleton's; one node after the skeleton's carries every mesh.
        if (host.Nodes.FindLastIndex(n => n.ParentIndex < 0) is >= 0 and var last) host.Nodes[last].NextSiblingIndex = (short)host.Nodes.Count;
        host.Nodes.Add(new FLVER.Node { Name = "aog_meshes", ParentIndex = -1, FirstChildIndex = -1, NextSiblingIndex = -1, PreviousSiblingIndex = (short)host.Nodes.FindLastIndex(n => n.ParentIndex < 0), Scale = Vector3.One, Flags = FLVER.Node.NodeFlags.Mesh });
        return Write(host, pieces, false, host.Nodes.Count - 1);
    }

    private static byte[] Write(FLVER2 host, IReadOnlyList<Body> bodies, bool whole, int? meshNode = null)
    {
        if (whole || meshNode is not null) ListNodes(host);
        if (host.Header.Version != 0x2001a || host.Header.BigEndian) throw new InvalidDataException("Unsupported host character model revision");
        if (bodies.Count == 0 || bodies.Any(b => b.Parts.Count == 0)) throw new InvalidDataException("A character model needs at least one mesh for each body");
        // A body may come as several models (armour pieces, a face, what it holds) under one mark; an unmarked model is
        // always drawn, so it cannot share the character with marked ones.
        if (bodies.Any(b => b.Mark is < 0 or > HighestMark) || bodies.Any(b => b.Mark is null) && bodies.Any(b => b.Mark is not null))
            throw new InvalidDataException("Bodies sharing a host character need display marks the host can show");
        var hostBones = Bones(host);

        var layouts = new List<FLVER2.BufferLayout>(); var layoutOf = new Dictionary<int, int>();
        var lists = new List<FLVER2.GXList>(); var listOf = new Dictionary<int, int>();
        var meshes = new List<FLVER2.Mesh>(); var materials = new List<FLVER2.Material>();
        var all = new List<Vector3>();
        // What each bone carries, in the bone's own space at rest.
        var extents = new SortedDictionary<int, (Vector3 Low, Vector3 High)>(); var fromRest = new Dictionary<int, Matrix4x4>();
        foreach (var body in bodies)
        {
        var source = body.Source;
        // A whole body stays on its own nodes, where it is; a carried one is fitted to the host's.
        var fit = whole ? new SkeletonFit.Fit(Enumerable.Range(0, source.Nodes.Count).ToArray(), Enumerable.Repeat(Matrix4x4.Identity, source.Nodes.Count).ToArray(), 1)
            : SkeletonFit.Match(Bones(source), hostBones, body.PairedBones, body.Riders, body.Scale);
        foreach (var part in body.Parts)
        {
            if (part.Mesh < 0 || part.Mesh >= source.Meshes.Count || part.Exemplar < 0 || part.Exemplar >= host.Meshes.Count) throw new InvalidDataException("A character part names a missing mesh");
            var from = source.Meshes[part.Mesh]; var exemplar = host.Meshes[part.Exemplar];
            if (!exemplar.UseBoneWeights || exemplar.BoneIndices.Count != 0 || exemplar.VertexBuffers.Count != 1 || exemplar.Vertices.Count == 0)
                throw new InvalidDataException("Host exemplar mesh is not skinned to the model's own bones in one buffer");
            int hostLayout = exemplar.VertexBuffers[0].LayoutIndex; var layout = host.BufferLayouts[hostLayout];
            int Count(FLVER.LayoutSemantic semantic) => layout.Count(m => m.Semantic == semantic);
            var bonesMember = layout.SingleOrDefault(m => m.Semantic == FLVER.LayoutSemantic.BoneIndices);
            if (Count(FLVER.LayoutSemantic.Position) != 1 || Count(FLVER.LayoutSemantic.Normal) != 1 || Count(FLVER.LayoutSemantic.Tangent) < 1 || bonesMember is null
                || Count(FLVER.LayoutSemantic.BoneWeights) != 1 || Count(FLVER.LayoutSemantic.UV) < 1 || layout.Any(m => m.Semantic == FLVER.LayoutSemantic.Bitangent))
                throw new InvalidDataException("Unsupported host character vertex layout");
            // A four-number coordinate member holds two coordinate pairs.
            int coordinates = layout.Where(m => m.Semantic == FLVER.LayoutSemantic.UV).Sum(m => m.Type is FLVER.LayoutType.Short4 or FLVER.LayoutType.Float4 ? 2 : 1);
            int highestBone = bonesMember.Type == FLVER.LayoutType.UByte4 ? byte.MaxValue : ushort.MaxValue;
            if (!layoutOf.TryGetValue(hostLayout, out int layoutIndex)) { layoutOf[hostLayout] = layoutIndex = layouts.Count; layouts.Add(layout); }

            var hostMaterial = host.Materials[exemplar.MaterialIndex];
            if (part.Textures.Keys.Except(hostMaterial.Textures.Select(t => t.ParamName)).FirstOrDefault() is { } missing)
                throw new InvalidDataException("Host exemplar material has no texture input named " + missing);
            if (!listOf.TryGetValue(hostMaterial.GXIndex, out int listIndex) && hostMaterial.GXIndex >= 0) { listOf[hostMaterial.GXIndex] = listIndex = lists.Count; lists.Add(host.GXLists[hostMaterial.GXIndex]); }
            materials.Add(new FLVER2.Material
            {
                // A name that opens "#NN#" is drawn only for rows that show mark NN; without one the mesh is always drawn.
                Name = (body.Mark is { } mark ? $"#{mark:D2}#" : "") + $"aog_{materials.Count}", MTD = hostMaterial.MTD, GXIndex = hostMaterial.GXIndex < 0 ? -1 : listIndex, Index = materials.Count,
                Textures = hostMaterial.Textures.Select(t => new FLVER2.Texture { ParamName = t.ParamName, Path = part.Textures.GetValueOrDefault(t.ParamName, t.Path), TilingScale = t.TilingScale }).ToList(),
            });

            const FLVER2.FaceSet.FSFlags known = FLVER2.FaceSet.FSFlags.LodLevel1 | FLVER2.FaceSet.FSFlags.LodLevel2 | FLVER2.FaceSet.FSFlags.MotionBlur;
            if (from.FaceSets.Any(f => (f.Flags & ~known) != 0)) throw new InvalidDataException("Unclassified face-set role in a source character mesh");
            var faces = from.FaceSets.Single(f => f.Flags == FLVER2.FaceSet.FSFlags.None);
            var indices = faces.Triangulate(from.Vertices.Count < 65536);
            if (indices.Count == 0 || indices.Count % 3 != 0 || indices.Any(i => i < 0 || i >= from.Vertices.Count)) throw new InvalidDataException("Invalid triangles in a source character mesh");
            int normalW = exemplar.Vertices[0].NormalW;
            bool slotted = from.VertexBuffers.SelectMany(b => source.BufferLayouts[b.LayoutIndex]).Any(m => m.Semantic == FLVER.LayoutSemantic.BoneIndices);
            var vertices = from.Vertices.Select(vertex =>
            {
                // The source bones a vertex follows, through the mesh's own bone list.
                var follows = new List<(int Node, float Weight)>();
                int Node(int slot) => slot >= 0 && slot < from.BoneIndices.Count && from.BoneIndices[slot] >= 0 && from.BoneIndices[slot] < source.Nodes.Count
                    ? from.BoneIndices[slot] : throw new InvalidDataException("Source character vertex references an absent bone");
                // A rigid mesh follows one bone: the one its vertex names in the mesh's list (in its bone slot when the layout
                // has one, else beside its normal), or, with no list, the mesh's own node.
                if (!from.UseBoneWeights)
                    follows.Add((from.BoneIndices.Count > 0 ? Node(slotted ? vertex.BoneIndices[0] : vertex.NormalW) : from.NodeIndex >= 0 && from.NodeIndex < source.Nodes.Count ? from.NodeIndex
                        : throw new InvalidDataException("Source character mesh names an absent node"), 1));
                else for (int i = 0; i < 4; i++) if (vertex.BoneWeights[i] != 0) follows.Add((Node(vertex.BoneIndices[i]), vertex.BoneWeights[i]));
                float total = follows.Sum(f => f.Weight);
                if (follows.Count == 0 || MathF.Abs(total - 1) > 0.002f) throw new InvalidDataException("Source character skin weights do not sum to one");
                var move = new Matrix4x4(); var carried = new SortedDictionary<int, float>();
                foreach (var (node, weight) in follows)
                {
                    int bone = fit.Host[node];
                    if (bone < 0) throw new InvalidDataException("No host bone carries the source bone " + source.Nodes[node].Name);
                    if (bone > highestBone) throw new InvalidDataException("The host layout cannot name the bone " + host.Nodes[bone].Name);
                    move += fit.Move[node] * (weight / total); carried[bone] = carried.GetValueOrDefault(bone) + weight / total;
                }
                Vector3 Direction(Vector3 v) { var turned = Vector3.TransformNormal(v, move); return turned.LengthSquared() == 0 ? turned : Vector3.Normalize(turned); }
                // The four heaviest host bones, their weights filling a byte exactly.
                var kept = carried.OrderByDescending(c => c.Value).ThenBy(c => c.Key).Take(4).ToArray();
                float keptTotal = kept.Sum(c => c.Value);
                var bytes = kept.Select(c => (int)MathF.Round(c.Value / keptTotal * 255)).ToArray();
                bytes[0] += 255 - bytes.Sum();
                var position = Vector3.Transform(vertex.Position, move); var normal = Direction(vertex.Normal);
                var tangent = vertex.Tangents.Count > 0 ? vertex.Tangents[0] : new Vector4(1, 0, 0, 1);
                var across = Direction(new(tangent.X, tangent.Y, tangent.Z));
                var written = new FLVER.Vertex
                {
                    Positions = [position], Normals = [normal], NormalWs = [normalW],
                    Tangents = Enumerable.Repeat(new Vector4(across, tangent.W < 0 ? -1 : 1), Count(FLVER.LayoutSemantic.Tangent)).ToList(),
                    Colors = Enumerable.Repeat(new FLVER.VertexColor(1f, 1f, 1f, 1f), Count(FLVER.LayoutSemantic.VertexColor)).ToList(),
                    UVs = Enumerable.Repeat(vertex.UVs.Count > 0 ? vertex.UVs[0] : Vector3.Zero, coordinates).ToList(),
                };
                for (int i = 0; i < kept.Length; i++) { written.BoneIndices[i] = kept[i].Key; written.BoneWeights[i] = bytes[i] / 255f; }
                foreach (var (bone, _) in kept)
                {
                    if (!fromRest.TryGetValue(bone, out var toBone)) fromRest[bone] = toBone = Matrix4x4.Invert(hostBones[bone].World, out var inverse) ? inverse : throw new InvalidDataException("A bone's rest placement cannot be undone: " + host.Nodes[bone].Name);
                    var within = Vector3.Transform(position, toBone);
                    extents[bone] = extents.TryGetValue(bone, out var extent) ? (Vector3.Min(extent.Low, within), Vector3.Max(extent.High, within)) : (within, within);
                }
                all.Add(position);
                return written;
            }).ToList();
            var low = vertices.Select(v => v.Position).Aggregate(Vector3.Min); var high = vertices.Select(v => v.Position).Aggregate(Vector3.Max);
            meshes.Add(new FLVER2.Mesh
            {
                UseBoneWeights = true, NodeIndex = whole ? from.NodeIndex : meshNode ?? exemplar.NodeIndex, MaterialIndex = materials.Count - 1, VertexBuffers = [new(layoutIndex)], Vertices = vertices,
                // The host picks a face set by distance; each level draws the source's full detail.
                FaceSets = new[] { FLVER2.FaceSet.FSFlags.None, FLVER2.FaceSet.FSFlags.LodLevel1, FLVER2.FaceSet.FSFlags.LodLevel2 }
                    .Select(level => new FLVER2.FaceSet { Flags = level, CullBackfaces = faces.CullBackfaces, Indices = indices.ToList() }).ToList(),
                // This host revision stores extent, reserved zero and centre in its mesh bounds fields.
                BoundingBox = new() { Min = (high - low) / 2 + new Vector3(0.001f), Max = Vector3.Zero, Unk = (high + low) / 2 },
            });
        }
        }
        host.Meshes = meshes; host.Materials = materials; host.BufferLayouts = layouts; host.GXLists = lists;
        host.Header.BoundingBoxMin = all.Aggregate(Vector3.Min); host.Header.BoundingBoxMax = all.Aggregate(Vector3.Max);
        // A skeleton model that comes without meshes states no extents. Each bone states the extent of what follows it,
        // and a node that carries nothing an empty one; the host draws nothing for a body whose bones state none.
        if (meshNode is not null)
            for (int i = 0; i < host.Nodes.Count; i++)
                (host.Nodes[i].BoundingBoxMin, host.Nodes[i].BoundingBoxMax) = extents.TryGetValue(i, out var extent) ? extent : (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        return host.Write();
    }
}
