using System.Text.Json;
using AgeOfGold.Scene;
using SoulsFormats;

internal static class LayerMaterialCheck
{
    internal static void Run()
    {
        string[] labels = ["authored_BlendEdge", "authored_AlbedoMap_0", "authored_NormalMap_0", "authored_NormalMap_1", "authored_AlbedoMap_1", "authored_Packed_0", "authored_Packed_1"];
        string[] groups = ["", "left", "left", "right", "right", "left", "right"];
        byte[] types = [0, 1, 4, 4, 1, 14, 14];
        var metadata = ShaderMetadataCheck.Fixture(5, "unused", labels, groups, types);
        var donorMetadata = ShaderMetadataCheck.Fixture(7, "unused", labels, groups, types);
        byte[] Definition(int count)
        {
            var value = MATBIN.Read(MaterialWriterCheck.Fixture(count, "unused"));
            for (int i = 0; i < count; i++) value.Samplers[i].Type = labels[i];
            return value.Write();
        }
        var material = LayerMaterial.Read(Input());
        var paths = new[] { "colour-a", "normal-a", "packed-a", "colour-b", "normal-b", "packed-b" }
            .ToDictionary(n => n, n => "AUTHORED_" + n + ".tif");
        var bound = MaterialWriter.BindLayers(Definition(5), metadata, Definition(7), donorMetadata, material, paths, "AUTHORED_env.tif");
        string[] expected = ["AUTHORED_env.tif", paths["colour-a"], paths["normal-a"], paths["normal-b"], paths["colour-b"], paths["packed-a"], paths["packed-b"]];
        if (!bound.Textures.Select(t => t.Path).SequenceEqual(expected)) throw new Exception("Layer roles were reordered or collapsed");
        Reject(() => LayerMaterial.Read(Input(secondary: 0)));
        Reject(() => LayerMaterial.Read(Input(detail: "unexpected-detail")));
        Reject(() => LayerMaterial.Read(Input(scale: 2)));
        paths.Remove("packed-b");
        Reject(() => MaterialWriter.BindLayers(Definition(5), metadata, Definition(7), donorMetadata, material, paths, "AUTHORED_env.tif"));
        groups[6] = "left";
        var ambiguous = ShaderMetadataCheck.Fixture(7, "unused", labels, groups, types);
        Reject(() => MaterialWriter.BindLayers(Definition(5), metadata, Definition(7), ambiguous, material, paths, "AUTHORED_env.tif"));
        // A one-layer source surface fills both host layers with the same inputs; a two-layer shader never does so silently.
        JsonElement Single(string shader, string detail = "", string extra = "g_DetailBumpmap", int blend = 0)
        {
            var fields = new[] { ("g_Diffuse", "colour-a"), ("g_Bumpmap", "normal-a"), ("g_Specular", "packed-a"), (extra, detail) };
            return JsonSerializer.SerializeToElement(new { definition = "authored", shader, blendMode = blend,
                textures = fields.Select(f => new { input = f.Item1, texture = f.Item2, scale = new[] { 1f, 1f } }),
                coordinates = fields.Select(f => new { input = f.Item1, channel = 0 }) });
        }
        var single = LayerMaterial.Read(Single(@"N:\authored\FRPG_Phn_ColDifSpcBmp.spx"));
        if (single.First != single.Second || single.First != new LayerMaterial.Layer("colour-a", "normal-a", "packed-a") || !LayerMaterial.Single(Single(@"N:\authored\FRPG_Phn_ColDifSpcBmp.spx"))
            || LayerMaterial.Single(Input()))
            throw new Exception("One-layer source surface was not duplicated across both host layers");
        Reject(() => LayerMaterial.Read(Single(@"N:\authored\FRPG_Phn_ColDifSpcBmp.spx", detail: "unexpected-detail")));
        Reject(() => LayerMaterial.Read(Single(@"N:\authored\FRPG_Phn_ColDifSpcBmp.spx", extra: "g_Lightmap")));
        Reject(() => LayerMaterial.Read(Single(@"N:\authored\FRPG_Other.spx")));
        // Alpha-tested and blended one-layer surfaces keep their alpha as a cutout; two-layer shaders have no such variant.
        const string phong = @"N:\authored\FRPG_Phn_ColDifSpcBmp.spx";
        if (LayerMaterial.Cutout(Single(phong)) || !LayerMaterial.Cutout(Single(phong, blend: 1)) || !LayerMaterial.Cutout(Single(phong, blend: 2))
            || LayerMaterial.Read(Single(phong, blend: 1)) != single || LayerMaterial.Layers(Single(phong, blend: 4)) != 0
            || LayerMaterial.Layers(Single(@"N:\authored\FRPG_Phn_ColDifSpcBmpMul.spx", blend: 1)) != 0)
            throw new Exception("Cutout classification of one-layer surfaces differs");
        // Foliage and ivy shade as one lit layer; their sway is a separate translation.
        foreach (string plant in new[] { @"N:\authored\FRPG_Foliage_ColDifSpcBmp.spx", @"N:\authored\FRPG_Ivy_ColDifSpcBmp.spx" })
            if (LayerMaterial.Read(Single(plant, blend: 1)) != single || LayerMaterial.Layers(Single(plant)) != 1 || LayerMaterial.Layers(Single(plant, blend: 2)) != 0)
                throw new Exception("Foliage is not classified as one lit layer");
        Console.WriteLine("PASS: ground colour, normal, packed and environment roles bind by layer and semantic; unsupported mappings fail");
    }

    private static JsonElement Input(int secondary = 1, string detail = "", float scale = 1)
    {
        var fields = new[] { ("g_Diffuse", "colour-a", 0), ("g_Bumpmap", "normal-a", 0), ("g_Specular", "packed-a", 0),
            ("g_Diffuse_2", "colour-b", secondary), ("g_Bumpmap_2", "normal-b", secondary), ("g_Specular_2", "packed-b", secondary), ("g_DetailBumpmap", detail, 0) };
        return JsonSerializer.SerializeToElement(new { definition = "M[DB][M]", shader = @"N:\authored\FRPG_Phn_ColDifSpcBmpMul.spx", blendMode = 0,
            textures = fields.Select(f => new { input = f.Item1, texture = f.Item2, scale = new[] { scale, scale } }),
            coordinates = fields.Select(f => new { input = f.Item1, channel = f.Item3 }) });
    }

    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("Unsupported ground texture roles accepted");
    }
}
