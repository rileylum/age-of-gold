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
        Write("map/m10_02_00_00/m0000A10.flver.dcx", model.Write());
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
        static void Require(bool condition, string field)
        {
            if (!condition) throw new Exception("Source scene lost " + field);
        }
    }
}
