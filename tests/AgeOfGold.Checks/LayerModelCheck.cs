using System.Numerics;
using AgeOfGold.Scene;
using SoulsFormats;

internal static class LayerModelCheck
{
    internal static void Run()
    {
        var template = new FLVER2 { Skeletons = new() }; template.Header.Version = 0x2001a;
        template.Nodes.Add(new() { Name = "authored", Scale = Vector3.One });
        template.GXLists.Add(new());
        template.Materials.Add(new() { Name = "authored-layer", MTD = "authored.mtd", GXIndex = 0, Index = 0 });
        template.BufferLayouts.Add([
            new(FLVER.LayoutType.Float3, FLVER.LayoutSemantic.Position),
            new(FLVER.LayoutType.UByte4Norm, FLVER.LayoutSemantic.Normal),
            new(FLVER.LayoutType.UByte4Norm, FLVER.LayoutSemantic.Tangent),
            new(FLVER.LayoutType.UByte4Norm, FLVER.LayoutSemantic.VertexColor),
            new(FLVER.LayoutType.UByte4Norm, FLVER.LayoutSemantic.VertexColor, 1),
            new(FLVER.LayoutType.Short2, FLVER.LayoutSemantic.UV),
            new(FLVER.LayoutType.Short4, FLVER.LayoutSemantic.UV, 1),
        ]);
        var positions = new[] { new Vector3(-3, 2, 5), new Vector3(1, 2, 5), new Vector3(-3, 2, 9) };
        var values = positions.Select(p => new LayerGeometry.Vertex(p, Vector3.UnitY, new(1, 2), new(52, -37),
            new(1, 0, 0, -1), new(0, 0, 1, 1), new(0, 0, 1, 0), new(1, 0, 0, 0), new(0.1f, 0.2f, 0.3f, 0.4f))).ToArray();
        template.Meshes.Add(new()
        {
            NodeIndex = 0, MaterialIndex = 0, VertexBuffers = [new(0)], FaceSets = [new() { Indices = [0, 1, 2] }],
            Vertices = positions.Select(p => new FLVER.Vertex { Positions = [p], Normals = [Vector3.UnitY], NormalWs = [0], UVs = [Vector3.Zero, Vector3.Zero, Vector3.Zero],
                Tangents = [new(1, 0, 0, 1)], Colors = [new(1f, 1f, 1f, 1f), new(1f, 1f, 1f, 1f)] }).ToList(),
        });
        var bytes = template.Write();
        var surface = new LayerGeometry.Surface(values, [0, 1, 2], true);
        var result = LayerModelWriter.Write(bytes, bytes, 0, [surface]);
        Check(FLVER2.Read(result), [surface]);
        if (!result.SequenceEqual(LayerModelWriter.Write(bytes, bytes, 0, [surface])) || !bytes.SequenceEqual(template.Write()))
            throw new Exception("Layered model write changed inputs or repeat bytes");
        Console.WriteLine("PASS: layered host format preserves full secondary coordinates, four frame vectors, float colour and bounds");
    }

    internal static void Check(FLVER2 model, LayerGeometry.Surface[] expected)
    {
        if (model.Meshes.Count != expected.Length) throw new Exception("Surface count changed");
        for (var m = 0; m < expected.Length; m++)
        {
            var mesh = model.Meshes[m]; var source = expected[m];
            if (mesh.UseBoneWeights || mesh.NodeIndex != 0 || mesh.Vertices.Count != source.Vertices.Length
                || mesh.FaceSets.Count != 1 || mesh.FaceSets[0].CullBackfaces != source.CullBackfaces
                || !mesh.FaceSets[0].Indices.SequenceEqual(source.Indices)) throw new Exception("Surface topology changed");
            for (var i = 0; i < source.Vertices.Length; i++)
            {
                var v = mesh.Vertices[i]; var s = source.Vertices[i];
                var colour = v.Colors[0];
                if (v.Position != s.Position || Vector3.Distance(v.Normal, s.Normal) > 0.02f
                    || v.UVs.Count != 3 || Vector2.Distance(new(v.UVs[0].X, v.UVs[0].Y), s.Primary) > 1f / 2048
                    || v.UVs[1] != new Vector3(s.Secondary, 0) || v.UVs[2] != Vector3.Zero || v.Tangents.Count != 4
                    || Vector4.Distance(v.Tangents[0], s.Tangent) > 0.02f || Vector4.Distance(v.Tangents[1], s.SecondaryTangent) > 0.02f
                    || v.Tangents[2] != s.Bitangent || v.Tangents[3] != s.SecondaryBitangent
                    || new Vector4(colour.R, colour.G, colour.B, colour.A) != s.Colour)
                    throw new Exception("Layered vertex attribute changed");
                var box = mesh.BoundingBox ?? throw new Exception("Missing visibility bounds");
                var distance = Vector3.Abs(v.Position - box.Unk);
                if (box.Max != Vector3.Zero || distance.X > box.Min.X || distance.Y > box.Min.Y || distance.Z > box.Min.Z)
                    throw new Exception("Visibility bounds exclude a vertex");
            }
        }
    }
}
