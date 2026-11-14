using System.Numerics;
using System.Text.Json;
using AgeOfGold;
using AgeOfGold.Scene;
using SoulsFormats;

internal static class SceneCheck
{
    internal static void Run(string root)
    {
        var source = Path.Combine(root, "authored-scene");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "DarkSoulsRemastered.exe"), "authored executable");
        var layout = new MSB1();
        layout.Models.MapPieces.Add(new() { Name = "m0000" });
        layout.Parts.MapPieces.Add(new() { Name = "authored-placement", ModelName = "m0000", Position = new(5, 6, 7),
            LightID = 13, LanternID = 7, DisablePointLightEffect = 1 });
        new uint[] { 1, 4, 16, 64 }.CopyTo(layout.Parts.MapPieces[0].DrawGroups, 0);
        layout.Models.Objects.Add(new() { Name = "o9999" });
        layout.Parts.Objects.Add(new() { Name = "authored-object", ModelName = "o9999", Position = new(8, 9, 10), Rotation = new(0, 45, 0), LightID = 14, FogID = 15, ScatterID = 16 });
        Write("map/MapStudio/m10_02_00_00.msb", layout.Write());
        var definition = new MTD
        {
            ShaderPath = "authored-shader.fx",
            Params = [new() { Name = "g_BlendMode", Value = 0 }, new() { Name = "authored", Type = MTD.ParamType.Float, Value = 0.25f }],
            Textures = [new() { Type = "g_Diffuse", UVNumber = 1 }, new() { Type = "g_Bumpmap", UVNumber = 2 }],
        };
        var binder = new BND3 { Files = [new BinderFile(Binder.FileFlags.Flag1, 0, "authored.mtd", definition.Write())] };
        binder.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 1, "unreferenced.mtd", [1]));
        binder.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 2, "unreferenced.mtd", [2]));
        Write("mtd/Mtd.mtdbnd.dcx", binder.Write());
        definition.Params[1].Value = 0.75f;
        binder.Files[0].Bytes = definition.Write();
        // A binder can hold a name twice with different contents; the first entry is the one used.
        definition.Params[1].Value = 0.9f;
        binder.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 3, "authored.mtd", definition.Write()));
        Write("mtd/MtdPatch.mtdbnd.dcx", binder.Write());
        var model = new FLVER2();
        model.Header.Version = 0x2000C;
        model.Nodes.Add(new() { Name = "authored-node", Translation = new(1, 2, 3), Scale = Vector3.One, ParentIndex = -1 });
        model.Materials.Add(new()
        {
            Name = "authored-material", MTD = "authored.mtd", GXIndex = -1,
            Textures = [new() { ParamName = "g_Diffuse", Path = "authored-colour.tga" }, new() { ParamName = "g_Bumpmap", Path = "authored-normal.tga" }],
        });
        model.BufferLayouts.Add([
            new(FLVER.LayoutType.Float3, FLVER.LayoutSemantic.Position),
            new(FLVER.LayoutType.UByte4, FLVER.LayoutSemantic.BoneIndices),
            new(FLVER.LayoutType.Float3, FLVER.LayoutSemantic.Normal),
            new(FLVER.LayoutType.Float4, FLVER.LayoutSemantic.Tangent),
            new(FLVER.LayoutType.Float4, FLVER.LayoutSemantic.VertexColor),
            new(FLVER.LayoutType.Float2, FLVER.LayoutSemantic.UV),
            new(FLVER.LayoutType.Float2, FLVER.LayoutSemantic.UV, 1),
        ]);
        var mesh = new FLVER2.Mesh
        {
            MaterialIndex = 0, NodeIndex = 0, BoneIndices = [0], VertexBuffers = [new(0)],
            FaceSets = [new() { Indices = [0, 1, 2], CullBackfaces = true }],
        };
        foreach (var position in new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitZ })
            mesh.Vertices.Add(new()
            {
                Positions = [position], Normals = [Vector3.UnitY],
                UVs = [new(2, 3, 0), new(8, 9, 0)], Tangents = [new(1, 0, 0, -1)],
                Colors = [new(0.25f, 0.5f, 0.75f, 1f)],
            });
        model.Meshes.Add(mesh);
        // An attachment point for effects, held by the node it follows.
        model.Dummies.Add(new() { ReferenceID = 100, Position = new(0.5f, 0.25f, 0.125f), ParentBoneIndex = 0, AttachBoneIndex = -1, Forward = Vector3.UnitZ, Upward = Vector3.UnitY });
        Write("map/m10_02_00_00/m0000A10.flver.dcx", model.Write());
        // An object's model and textures sit together in its own binder; the same reader shapes its surfaces.
        Write("obj/o9999.objbnd.dcx", new BND3 { Files = [new BinderFile(Binder.FileFlags.Flag1, 0, @"N:\authored\o9999.flver", model.Write()),
            new BinderFile(Binder.FileFlags.Flag1, 1, @"N:\authored\o9999.tpf", AuthoredPackage("authored-colour"))] }.Write());
        var objects = new SceneReader(new Install(source, "DarkSoulsRemastered.exe"));
        using (var placed = JsonDocument.Parse(objects.ReadObject("o9999")))
        using (var piece = JsonDocument.Parse(objects.ReadModel("m0000")))
            Require(placed.RootElement.GetProperty("name").GetString() == "o9999"
                && placed.RootElement.GetProperty("surfaces").GetRawText() == piece.RootElement.GetProperty("surfaces").GetRawText(), "object model surfaces");
        Require(objects.ReadObjectTextures("o9999").Select(t => t.Name).SequenceEqual(new[] { "authored-colour" }), "object textures");
        if (objects.ReadObjectAttachment("o9999") != new Vector3(1.5f, 2.25f, 3.125f)) throw new Exception("Object attachment point is not carried through its node");
        // A breakable object keeps a second model of its pieces beside its whole one, and may hold no textures of its own:
        // the whole model is the one read, and its textures are then looked up with the map's.
        var pieces = FLVER2.Read(model.Write()); pieces.Meshes.Clear();
        Write("obj/o9998.objbnd.dcx", new BND3 { Files = [new BinderFile(Binder.FileFlags.Flag1, 0, @"N:\authored\o9998.flver", model.Write()),
            new BinderFile(Binder.FileFlags.Flag1, 1, @"N:\authored\o9998_1.flver", pieces.Write())] }.Write());
        using (var whole = JsonDocument.Parse(objects.ReadObject("o9998")))
        using (var piece = JsonDocument.Parse(objects.ReadModel("m0000")))
            Require(whole.RootElement.GetProperty("surfaces").GetRawText() == piece.RootElement.GetProperty("surfaces").GetRawText() && objects.ReadObjectTextures("o9998").Length == 0, "breakable object's whole model");
        // Objects are placed like map pieces and select the same drawing rows.
        using (var placedObjects = JsonDocument.Parse(objects.ReadObjectPlacements()))
        {
            var only = placedObjects.RootElement.EnumerateArray().Single();
            Require(only.GetProperty("name").GetString() == "authored-object" && only.GetProperty("model").GetString() == "o9999"
                && only.GetProperty("position")[2].GetSingle() == 10 && only.GetProperty("rotation")[1].GetSingle() == 45
                && only.GetProperty("lighting").GetInt32() == 14 && only.GetProperty("fog").GetInt32() == 15 && only.GetProperty("scattering").GetInt32() == 16, "object placements");
        }
        var first = SceneReader.Read(new Install(source, "DarkSoulsRemastered.exe"));
        var second = SceneReader.Read(new Install(source, "DarkSoulsRemastered.exe"));
        if (!first.SequenceEqual(second)) throw new Exception("Repeated scene reads differ");
        using var scene = JsonDocument.Parse(first);
        var rootElement = scene.RootElement;
        var surface = rootElement.GetProperty("models")[0].GetProperty("surfaces")[0];
        var vertex = surface.GetProperty("vertices")[0];
        Require(vertex.GetProperty("position")[0].GetSingle() == 1 && vertex.GetProperty("position")[2].GetSingle() == 3, "rigid geometry");
        Require(vertex.GetProperty("uvs")[1][0].GetSingle() == 8, "independent UV coordinates");
        Require(vertex.GetProperty("tangents")[0][3].GetSingle() == -1, "tangent handedness");
        Require(vertex.GetProperty("colours")[0][3].GetSingle() == 0.25f, "vertex alpha");
        Require(surface.GetProperty("material").GetProperty("parameters").GetProperty("authored").GetSingle() == 0.75f, "patched material defaults");
        Require(rootElement.GetProperty("placements")[0].GetProperty("position")[0].GetSingle() == 5, "placement transform");
        var placement = rootElement.GetProperty("placements")[0];
        Require(placement.GetProperty("drawGroups").EnumerateArray().Select(v => v.GetUInt32()).SequenceEqual(new uint[] { 1, 4, 16, 64 }), "source visibility groups");
        Require(placement.GetProperty("lighting").GetInt32() == 13 && placement.GetProperty("playerLight").GetInt32() == 7
            && placement.GetProperty("disablePointLights").GetInt32() == 1, "placement lighting selections");
        Require(surface.GetProperty("indices").EnumerateArray().Select(i => i.GetInt32()).SequenceEqual(new[] { 0, 1, 2 }), "triangle winding");
        Console.WriteLine("PASS: source scene retains rigid transforms, independent UVs, tangent signs, alpha, materials and placements");

        void Write(string relative, byte[] bytes)
        {
            var path = Path.Combine(source, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        // A one-texture PC package holding a DDS header with no pixels, enough for name listing.
    static byte[] AuthoredPackage(string name)
    {
        var dds = new DDS { dwWidth = 4, dwHeight = 4, dwMipMapCount = 1, ddspf = new DDS.PIXELFORMAT { dwFourCC = "DXT1", dwFlags = DDS.DDPF.FOURCC } }.Write(new byte[8]);
        var label = System.Text.Encoding.ASCII.GetBytes(name + '\0');
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("TPF\0"u8); writer.Write(dds.Length); writer.Write(1); writer.Write(new byte[] { 0, 3, 0, 0 });
        writer.Write(36 + label.Length); writer.Write(dds.Length); writer.Write(new byte[] { 0, 0, 1, 0 }); writer.Write(36); writer.Write(0);
        writer.Write(label); writer.Write(dds);
        return stream.ToArray();
    }

    static void Require(bool condition, string field)
        {
            if (!condition) throw new Exception("Source scene lost " + field);
        }
    }
}
