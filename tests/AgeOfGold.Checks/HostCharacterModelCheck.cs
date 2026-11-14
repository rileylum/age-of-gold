using System.Numerics;
using AgeOfGold.Characters;
using SoulsFormats;

internal static class HostCharacterModelCheck
{
    internal static void Run()
    {
        // An authored host character: a pelvis, a leg of two bones and a mesh node; one skinned mesh in the host's layout.
        var host = new FLVER2 { Skeletons = new() }; host.Header.Version = 0x2001a;
        host.Nodes.Add(new() { Name = "Pelvis", ParentIndex = -1, Translation = new(0, 1.2f, 0), Scale = Vector3.One });
        host.Nodes.Add(new() { Name = "L_Thigh", ParentIndex = 0, Translation = new(0.12f, 0, 0), Scale = Vector3.One });
        host.Nodes.Add(new() { Name = "L_Calf", ParentIndex = 1, Translation = new(0, -0.6f, 0), Scale = Vector3.One });
        host.Nodes.Add(new() { Name = "body", ParentIndex = -1, Scale = Vector3.One });
        host.Dummies.Add(new() { Position = new(0.3f, 1, 0), ReferenceID = 200, ParentBoneIndex = 2, AttachBoneIndex = 2 });
        host.GXLists.Add(new());
        host.Materials.Add(new()
        {
            Name = "#03#body", MTD = @"N:\exemplar\chr\C[c9000]_Body.matxml", GXIndex = 0, Index = 0,
            Textures = [new() { ParamName = "X_AlbedoMap", Path = "", TilingScale = Vector2.One }, new() { ParamName = "X_NormalMap", Path = "", TilingScale = Vector2.One },
                new() { ParamName = "X_DetailMap", Path = "", TilingScale = Vector2.One }],
        });
        host.BufferLayouts.Add([
            new(FLVER.LayoutType.Float3, FLVER.LayoutSemantic.Position), new(FLVER.LayoutType.UByte4, FLVER.LayoutSemantic.Normal),
            new(FLVER.LayoutType.UByte4, FLVER.LayoutSemantic.Tangent), new(FLVER.LayoutType.UByte4, FLVER.LayoutSemantic.BoneIndices),
            new(FLVER.LayoutType.UByte4Norm, FLVER.LayoutSemantic.BoneWeights), new(FLVER.LayoutType.UByte4Norm, FLVER.LayoutSemantic.VertexColor, 1),
            new(FLVER.LayoutType.Short4, FLVER.LayoutSemantic.UV),
        ]);
        FLVER.Vertex HostVertex(Vector3 p) => new()
        {
            Positions = [p], Normals = [Vector3.UnitZ], NormalWs = [127], Tangents = [new(1, 0, 0, 1)], Colors = [new(1f, 1f, 1f, 1f)],
            UVs = [Vector3.Zero, Vector3.Zero], BoneIndices = new FLVER.VertexBoneIndices { [0] = 1 }, BoneWeights = new FLVER.VertexBoneWeights { [0] = 1 },
        };
        host.Meshes.Add(new()
        {
            UseBoneWeights = true, NodeIndex = 3, MaterialIndex = 0, VertexBuffers = [new(0)], FaceSets = [new() { Indices = [0, 1, 2] }],
            Vertices = [HostVertex(new(0, 1, 0)), HostVertex(new(1, 1, 0)), HostVertex(new(0, 2, 0))],
        });
        var hostBytes = host.Write();

        // An authored source character, a fifth smaller, with a finger the host does not carry and a loose node.
        var source = new FLVER2 { Skeletons = new() }; source.Header.Version = 0x2000c;
        source.Nodes.Add(new() { Name = "Pelvis", ParentIndex = -1, Translation = new(0, 1, 0), Scale = Vector3.One });
        source.Nodes.Add(new() { Name = "L Thigh", ParentIndex = 0, Translation = new(0.1f, 0, 0), Scale = Vector3.One });
        source.Nodes.Add(new() { Name = "L Calf", ParentIndex = 1, Translation = new(0, -0.5f, 0), Scale = Vector3.One });
        source.Nodes.Add(new() { Name = "L Finger0", ParentIndex = 2, Translation = new(0, -0.1f, 0), Scale = Vector3.One });
        source.Nodes.Add(new() { Name = "loose", ParentIndex = -1, Scale = Vector3.One });
        // The source's layout: weighted vertices name their bones by slot in the mesh's bone list.
        source.BufferLayouts.Add([
            new(FLVER.LayoutType.Float3, FLVER.LayoutSemantic.Position), new(FLVER.LayoutType.UByte4, FLVER.LayoutSemantic.BoneIndices),
            new(FLVER.LayoutType.Short4Norm, FLVER.LayoutSemantic.BoneWeights), new(FLVER.LayoutType.UByte4Norm, FLVER.LayoutSemantic.Normal),
            new(FLVER.LayoutType.UByte4Norm, FLVER.LayoutSemantic.Tangent), new(FLVER.LayoutType.UByte4Norm, FLVER.LayoutSemantic.VertexColor), new(FLVER.LayoutType.Short2, FLVER.LayoutSemantic.UV),
        ]);
        FLVER.Vertex SourceVertex(Vector3 p, Vector2 uv, params (int Slot, float Weight)[] bones)
        {
            var vertex = new FLVER.Vertex { Positions = [p], Normals = [Vector3.UnitZ], NormalWs = [0], Tangents = [new(1, 0, 0, -1)], Colors = [new(1f, 1f, 1f, 1f)], UVs = [new(uv, 0)] };
            for (int i = 0; i < bones.Length; i++) { vertex.BoneIndices[i] = bones[i].Slot; vertex.BoneWeights[i] = bones[i].Weight; }
            return vertex;
        }
        // The mesh lists the bones it uses: slots 0, 1, 2 and 3 are the thigh, the calf, the finger and the loose node.
        var body = new FLVER2.Mesh
        {
            UseBoneWeights = true, NodeIndex = 4, MaterialIndex = 0, BoneIndices = [1, 2, 3, 4], VertexBuffers = [new(0)],
            FaceSets = [new() { TriangleStrip = true, CullBackfaces = true, Indices = [0, 1, 2, 3] }],
            Vertices =
            [
                SourceVertex(new(0.1f, 1, 0), new(0.25f, 0.5f), (0, 1)),
                SourceVertex(new(0.1f, 0.5f, 0), new(0.5f, 0.5f), (0, 0.5f), (1, 0.5f)),
                SourceVertex(new(0.1f, 0.4f, 0), new(0.75f, 0.5f), (1, 0.4f), (2, 0.6f)),
                SourceVertex(new(0.2f, 0.4f, 0), new(1, 1), (1, 1)),
            ],
        };
        source.Meshes.Add(body);

        var textures = new Dictionary<string, string> { ["X_AlbedoMap"] = "AUTHORED_colour.tif", ["X_NormalMap"] = "AUTHORED_normal.tif" };
        var written = HostCharacterModel.Write(hostBytes, [new(source, null, [new(0, 0, textures)], null)]);
        if (!written.SequenceEqual(HostCharacterModel.Write(hostBytes, [new(source, null, [new(0, 0, textures)], null)])) || !hostBytes.SequenceEqual(host.Write())) throw new Exception("Character model write changed its inputs or does not repeat");
        var model = FLVER2.Read(written);
        // The host's skeleton and attachment points are kept, so its animations and hit points still apply.
        if (model.Nodes.Count != 4 || !model.Nodes.Select(n => (n.Name, n.ParentIndex, n.Translation)).SequenceEqual(host.Nodes.Select(n => (n.Name, n.ParentIndex, n.Translation)))
            || model.Dummies.Count != 1 || model.Dummies[0].ReferenceID != 200 || model.Dummies[0].AttachBoneIndex != 2) throw new Exception("The host skeleton or its attachment points changed");
        if (model.Meshes.Count != 1 || model.Materials.Count != 1) throw new Exception("The model does not hold exactly the source's meshes");
        var mesh = model.Meshes[0]; var material = model.Materials[mesh.MaterialIndex];
        // The source mesh takes the host exemplar's material without its display mask, and the named inputs take new textures.
        if (material.MTD != host.Materials[0].MTD || material.Name.StartsWith('#') || material.Textures.Count != 3
            || !material.Textures.Select(t => (t.ParamName, t.Path)).SequenceEqual([("X_AlbedoMap", "AUTHORED_colour.tif"), ("X_NormalMap", "AUTHORED_normal.tif"), ("X_DetailMap", "")]))
            throw new Exception("The material is not the exemplar's with the named textures");
        if (!mesh.UseBoneWeights || mesh.BoneIndices.Count != 0 || mesh.NodeIndex != 3 || mesh.Vertices.Count != 4) throw new Exception("The mesh is not skinned to the host's own bones");
        // Every level of detail draws the same triangles, unwound from the source's strip.
        if (mesh.FaceSets.Count != 3 || mesh.FaceSets.Any(f => f.TriangleStrip || !f.CullBackfaces || !f.Indices.SequenceEqual(mesh.FaceSets[0].Indices)) || mesh.FaceSets[0].Indices.Count != 6
            || mesh.FaceSets[0].Flags != FLVER2.FaceSet.FSFlags.None || mesh.FaceSets[1].Flags != FLVER2.FaceSet.FSFlags.LodLevel1 || mesh.FaceSets[2].Flags != FLVER2.FaceSet.FSFlags.LodLevel2)
            throw new Exception("The mesh's triangles or levels of detail differ");
        static void Near(Vector3 actual, Vector3 expected, string what) { if ((actual - expected).Length() > 1e-4f) throw new Exception($"{what}: {actual}, expected {expected}"); }
        // Vertices move with the bones that carry them: bone origins land on the host's, grown by a fifth in between.
        Near(mesh.Vertices[0].Position, new(0.12f, 1.2f, 0), "thigh vertex");
        Near(mesh.Vertices[1].Position, new(0.12f, 0.6f, 0), "knee vertex");
        Near(mesh.Vertices[2].Position, new(0.12f, 0.48f, 0), "vertex below the knee");
        Near(mesh.Vertices[3].Position, new(0.24f, 0.48f, 0), "vertex beside the calf");
        Near(mesh.Vertices[0].Normal, Vector3.UnitZ, "normal");
        if (mesh.Vertices[0].Tangents[0] != new Vector4(1, 0, 0, -1)) throw new Exception("A tangent or its handedness changed");
        // Weights name host bones. The finger is carried by the calf, so its weight joins the calf's; weights fill a byte exactly.
        (int Bone, int Weight)[] Bones(FLVER.Vertex v) => Enumerable.Range(0, 4).Select(i => (v.BoneIndices[i], (int)MathF.Round(v.BoneWeights[i] * 255))).Where(b => b.Item2 > 0).ToArray();
        if (!Bones(mesh.Vertices[0]).SequenceEqual([(1, 255)]) || !Bones(mesh.Vertices[2]).SequenceEqual([(2, 255)]) || !Bones(mesh.Vertices[3]).SequenceEqual([(2, 255)]))
            throw new Exception("Single-bone weights differ");
        var knee = Bones(mesh.Vertices[1]);
        if (knee.Length != 2 || knee.Sum(b => b.Weight) != 255 || !knee.Select(b => b.Bone).Order().SequenceEqual([1, 2]) || Math.Abs(knee[0].Weight - knee[1].Weight) > 1) throw new Exception("Shared weights differ");
        if (mesh.Vertices.Select(v => new Vector2(v.UVs[0].X, v.UVs[0].Y)).Zip(body.Vertices, (a, b) => (a - new Vector2(b.UVs[0].X, b.UVs[0].Y)).Length()).Any(d => d > 1e-3f)) throw new Exception("Texture coordinates changed");

        // Two source bodies share the host character: each body's meshes carry its display mark, so a row shows one of them.
        var shared = FLVER2.Read(HostCharacterModel.Write(hostBytes, [new(source, null, [new(0, 0, textures)], 10), new(source, null, [new(0, 0, textures), new(0, 0, textures)], 11)]));
        if (shared.Meshes.Count != 3 || !shared.Meshes.Select(m => shared.Materials[m.MaterialIndex].Name[..4]).SequenceEqual(["#10#", "#11#", "#11#"])
            || shared.Materials.Select(m => m.Name).Distinct().Count() != 3 || shared.Nodes.Count != 4) throw new Exception("Bodies sharing a host character are not told apart by display mark");

        // A body may come as several models under one mark, each a part of it placed with the whole body's size ratio.
        float whole = HostCharacterModel.SizeRatio(hostBytes, source);
        var pieces = FLVER2.Read(HostCharacterModel.Write(hostBytes, [new(source, null, [new(0, 0, textures)], 14, null, whole), new(source, null, [new(0, 0, textures)], 14, null, 2)]));
        if (MathF.Abs(whole - 1.2f) > 1e-5f || pieces.Meshes.Count != 2 || pieces.Materials.Any(m => !m.Name.StartsWith("#14#", StringComparison.Ordinal))) throw new Exception("Parts of one body do not share its mark");
        Near(pieces.Meshes[0].Vertices[3].Position, new(0.24f, 0.48f, 0), "vertex of a part at the body's size");
        Near(pieces.Meshes[1].Vertices[3].Position, new(0.32f, 0.4f, 0), "vertex of a part at a given size");

        // A rigid source mesh with no bone list follows its own node: here the thigh, so its vertices move with the thigh.
        source.Meshes.Add(new FLVER2.Mesh
        {
            UseBoneWeights = false, NodeIndex = 1, MaterialIndex = 0, VertexBuffers = [new(0)], FaceSets = [new() { Indices = [0, 1, 2] }],
            Vertices = [SourceVertex(new(0.1f, 1, 0), default), SourceVertex(new(0.15f, 0.9f, 0), default), SourceVertex(new(0.1f, 0.9f, 0), default)],
        });
        var rigid = FLVER2.Read(HostCharacterModel.Write(hostBytes, [new(source, null, [new(1, 0, textures)], null)])).Meshes.Single();
        Near(rigid.Vertices[1].Position, new(0.18f, 1.08f, 0), "vertex of a rigid mesh");
        if (!Bones(rigid.Vertices[1]).SequenceEqual([(1, 255)])) throw new Exception("A rigid mesh is not weighted to the bone that carries its node");
        source.Meshes.RemoveAt(1);

        foreach (var bad in new Action[]
        {
            // A vertex that follows a node no host bone carries.
            () => { body.Vertices[3].BoneIndices[0] = 3; try { HostCharacterModel.Write(hostBytes, [new(source, null, [new(0, 0, textures)], null)]); } finally { body.Vertices[3].BoneIndices[0] = 1; } },
            () => HostCharacterModel.Write(hostBytes, [new(source, null, [new(0, 0, new Dictionary<string, string> { ["X_Missing"] = "AUTHORED.tif" })], null)]),
            () => HostCharacterModel.Write(hostBytes, [new(source, null, [new(1, 0, textures)], null)]),
            () => HostCharacterModel.Write(hostBytes, [new(source, null, [], null)]),
            () => HostCharacterModel.Write(hostBytes, []),
            // An unmarked model is always drawn, so it cannot share the character with marked ones; marks stay within what a row can show.
            () => HostCharacterModel.Write(hostBytes, [new(source, null, [new(0, 0, textures)], null), new(source, null, [new(0, 0, textures)], 4)]),
            () => HostCharacterModel.Write(hostBytes, [new(source, null, [new(0, 0, textures)], 16)]),
        })
        {
            try { bad(); throw new Exception("An unusable character model request was accepted"); }
            catch (InvalidDataException) { }
        }
        Console.WriteLine("PASS: a source character's meshes are carried on a host skeleton with host bones, weights, layout and material");

        // The same source built whole: its own nodes and attachment points, nothing moved or resized; only layout and material are the host's.
        source.Dummies.Add(new() { Position = new(0.1f, 0.4f, 0), ReferenceID = 11, ParentBoneIndex = 2, AttachBoneIndex = 3 });
        string[] bones = ["Pelvis", "L Thigh", "L Calf", "L Finger0"];
        var ownBytes = HostCharacterModel.WriteWhole(hostBytes, source, [new(0, 0, textures)], bones);
        var own = FLVER2.Read(ownBytes);
        if (!ownBytes.SequenceEqual(HostCharacterModel.WriteWhole(hostBytes, source, [new(0, 0, textures)], bones)) || !hostBytes.SequenceEqual(host.Write())) throw new Exception("Whole character model write changed its inputs or differs across runs");
        if (own.Header.Version != 0x2001a || !own.Nodes.Select(n => (n.Name, n.ParentIndex, n.Translation)).SequenceEqual(source.Nodes.Select(n => (n.Name, n.ParentIndex, n.Translation))))
            throw new Exception("A whole character does not keep the source's nodes");
        const FLVER.Node.NodeFlags Bone = FLVER.Node.NodeFlags.Bone, Owner = FLVER.Node.NodeFlags.DummyOwner;
        if (!own.Nodes.Select(n => n.Flags).SequenceEqual([Bone, Bone, Bone | Owner, Bone | Owner, FLVER.Node.NodeFlags.Mesh])) throw new Exception("A whole character's nodes are not told apart: " + string.Join(", ", own.Nodes.Select(n => n.Flags)));
        foreach (var set in new[] { own.Skeletons.BaseSkeleton, own.Skeletons.AllSkeletons })
            if (!set.Select(b => (b.NodeIndex, (int)b.ParentIndex)).SequenceEqual(source.Nodes.Select((n, i) => (i, (int)n.ParentIndex)))) throw new Exception("A whole character's node lists do not hold every node in order");
        if (own.Dummies.Count != 1 || own.Dummies[0].ReferenceID != 11 || own.Dummies[0].AttachBoneIndex != 3 || own.Dummies[0].ParentBoneIndex != 2) throw new Exception("A whole character does not keep the source's attachment points");
        var ownMesh = own.Meshes.Single();
        if (ownMesh.NodeIndex != 4 || !own.BufferLayouts[ownMesh.VertexBuffers.Single().LayoutIndex].Select(m => m.Semantic).SequenceEqual(host.BufferLayouts[0].Select(m => m.Semantic)) || own.Materials.Single().MTD != host.Materials[0].MTD
            || own.Materials.Single().Textures.Single(t => t.ParamName == "X_AlbedoMap").Path != "AUTHORED_colour.tif")
            throw new Exception("A whole character's mesh does not sit on its own node in the host's layout and material");
        for (int i = 0; i < 4; i++) Near(ownMesh.Vertices[i].Position, body.Vertices[i].Position, "whole vertex " + i);
        // Weights name the source's own nodes: the calf and the finger, as the source weighted them.
        var third = ownMesh.Vertices[2];
        var weights = Enumerable.Range(0, 4).Where(i => third.BoneWeights[i] > 0).ToDictionary(i => third.BoneIndices[i], i => third.BoneWeights[i]);
        if (weights.Count != 2 || MathF.Abs(weights[2] - 0.4f) > 0.01f || MathF.Abs(weights[3] - 0.6f) > 0.01f) throw new Exception("A whole character's vertex is not weighted to the source's own bones");
        try { HostCharacterModel.WriteWhole(hostBytes, source, [new(0, 0, textures)], ["Pelvis", "Tail"]); throw new Exception("A skeleton bone the model lacks was accepted"); }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS: a source character built whole keeps its own nodes, attachment points, positions and weights in the host's layout and material");

        // A body that comes as several models sharing one skeleton (a human's armour pieces): the skeleton model has the nodes
        // and attachment points and no mesh; each piece names the bones it uses in its own order and may rest them elsewhere.
        var frame = new FLVER2 { Skeletons = new() }; frame.Header.Version = 0x2000c;
        foreach (var node in source.Nodes) frame.Nodes.Add(new() { Name = node.Name, ParentIndex = node.ParentIndex, Translation = node.Translation, Scale = Vector3.One });
        frame.Dummies.Add(new() { Position = new(0.1f, 0.4f, 0), ReferenceID = 11, ParentBoneIndex = 2, AttachBoneIndex = 3 });
        var piece = new FLVER2 { Skeletons = new() }; piece.Header.Version = 0x2000c;
        piece.Nodes.Add(new() { Name = "piece", ParentIndex = -1, Scale = Vector3.One });
        // The piece rests its calf 0.2 to one side of where the skeleton does.
        piece.Nodes.Add(new() { Name = "L Calf", ParentIndex = -1, Translation = new(0.3f, 0.5f, 0), Scale = Vector3.One });
        piece.Nodes.Add(new() { Name = "L Thigh", ParentIndex = -1, Translation = new(0.1f, 1, 0), Scale = Vector3.One });
        piece.BufferLayouts.Add(source.BufferLayouts[0]);
        piece.Meshes.Add(new()
        {
            UseBoneWeights = true, NodeIndex = 0, MaterialIndex = 0, BoneIndices = [1, 2], VertexBuffers = [new(0)], FaceSets = [new() { Indices = [0, 1, 2] }],
            Vertices = [SourceVertex(new(0.3f, 0.5f, 0), new(0, 0), (0, 1)), SourceVertex(new(0.3f, 0.4f, 0), new(1, 0), (0, 1)), SourceVertex(new(0.1f, 0.9f, 0), new(0, 1), (1, 1))],
        });
        var piecesBytes = HostCharacterModel.WritePieces(hostBytes, frame, [new(piece, null, [new(0, 0, textures)], null, null, 1), new(piece, null, [new(0, 0, textures)], null, null, 1)], bones);
        var assembled = FLVER2.Read(piecesBytes);
        // The skeleton's nodes come first, as they are; one node more carries the meshes.
        if (assembled.Nodes.Count != frame.Nodes.Count + 1 || !assembled.Nodes.Take(frame.Nodes.Count).Select(n => (n.Name, n.ParentIndex)).SequenceEqual(frame.Nodes.Select(n => (n.Name, n.ParentIndex)))
            || assembled.Nodes[^1].Flags != FLVER.Node.NodeFlags.Mesh || assembled.Nodes[^1].ParentIndex != -1 || assembled.Meshes.Count != 2 || assembled.Meshes.Any(m => m.NodeIndex != frame.Nodes.Count)
            || assembled.Dummies.Count != 1 || assembled.Skeletons.BaseSkeleton.Count != assembled.Nodes.Count)
            throw new Exception("A body of pieces does not stand on its skeleton's nodes with one node for its meshes");
        // A vertex follows its bone to where the skeleton rests it, and is weighted to the skeleton's node of that name.
        Near(assembled.Meshes[0].Vertices[0].Position, new(0.1f, 0.5f, 0), "piece vertex on the calf"); Near(assembled.Meshes[0].Vertices[2].Position, new(0.1f, 0.9f, 0), "piece vertex on the thigh");
        if (assembled.Meshes[0].Vertices[0].BoneIndices[0] != 2 || assembled.Meshes[0].Vertices[2].BoneIndices[0] != 1) throw new Exception("A piece's vertex is not weighted to the skeleton's own node");
        // Each bone states the extent of what follows it, in its own space; the host draws nothing for a body whose bones state none.
        // The calf carries vertices at its own place and a tenth below; the thigh one a tenth below; the others nothing.
        var calf = assembled.Nodes[2]; var thigh = assembled.Nodes[1];
        Near(calf.BoundingBoxMin, new(0, -0.1f, 0), "calf extent"); Near(calf.BoundingBoxMax, Vector3.Zero, "calf extent");
        Near(thigh.BoundingBoxMin, new(0, -0.1f, 0), "thigh extent"); Near(thigh.BoundingBoxMax, new(0, -0.1f, 0), "thigh extent");
        if (new[] { 0, 3, 4, 5 }.Any(i => assembled.Nodes[i].BoundingBoxMin != new Vector3(float.MaxValue) || assembled.Nodes[i].BoundingBoxMax != new Vector3(float.MinValue))) throw new Exception("A bone that carries nothing states an extent");
        foreach (var bad in new Action[]
        {
            () => HostCharacterModel.WritePieces(hostBytes, frame, [new(piece, null, [new(0, 0, textures)], 3, null, 1)], bones),
            () => HostCharacterModel.WritePieces(hostBytes, frame, [new(piece, null, [new(0, 0, textures)], null)], bones),
            () => HostCharacterModel.WritePieces(hostBytes, frame, [], bones),
        })
        {
            try { bad(); throw new Exception("An unusable body of pieces was accepted"); }
            catch (InvalidDataException) { }
        }
        Console.WriteLine("PASS: a body of several pieces stands whole on the skeleton they share, each vertex following its bone to the skeleton's rest");
    }
}
