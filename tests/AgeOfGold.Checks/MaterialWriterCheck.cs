using System.Numerics;
using System.Text.Json;
using AgeOfGold.Scene;
using SoulsFormats;

internal static class MaterialWriterCheck
{
    internal static void Run()
    {
        var definition = Fixture(2, "base"); var donor = Fixture(4, "extra");
        var metadata = ShaderMetadataCheck.Fixture(2, "base"); var donorMetadata = ShaderMetadataCheck.Fixture(4, "extra");
        var paths = new Dictionary<int, string> { [3] = "AUTHORED_SECOND.tif", [0] = "AUTHORED_COLOUR.tif", [2] = "AUTHORED_FIRST.tif", [1] = "AUTHORED_NORMAL.tif" };
        var first = MaterialWriter.BindTextures(definition, metadata, donor, donorMetadata, paths);
        var read = MATBIN.Read(first.Definition);
        if (!read.Samplers.Select(s => s.Type).SequenceEqual(new[] { "base_0", "base_1", "extra_2", "extra_3" })
            || !read.Samplers.Select(s => s.Key).SequenceEqual(new uint[] { 41, 42, 43, 44 })
            || read.Params.Single().Value is not float[] colour || !colour.SequenceEqual(new[] { 0.25f, 0.5f, 0.75f }))
            throw new Exception("Material samplers or existing parameters differ");
        if (!first.Textures.Select(t => t.ParamName).SequenceEqual(read.Samplers.Select(s => s.Type))
            || !first.Textures.Select(t => t.Path).SequenceEqual(paths.OrderBy(p => p.Key).Select(p => p.Value))
            || first.Textures.Any(t => t.TilingScale != Vector2.One)) throw new Exception("Model/material texture bindings disagree");
        read.Samplers.RemoveRange(2, 2);
        if (!read.Write().SequenceEqual(definition)) throw new Exception("Unrelated material fields changed");
        var second = MaterialWriter.BindTextures(definition, metadata, donor, donorMetadata, paths);
        if (!first.Definition.SequenceEqual(second.Definition) || !first.Metadata.SequenceEqual(second.Metadata)
            || JsonSerializer.Serialize(first.Textures) != JsonSerializer.Serialize(second.Textures)
            || !definition.SequenceEqual(Fixture(2, "base"))) throw new Exception("Material binding does not repeat or changed its input");
        var wrong = MATBIN.Read(donor); wrong.Samplers[2].Type = "unrelated";
        Reject(() => MaterialWriter.BindTextures(definition, metadata, wrong.Write(), donorMetadata, paths));
        wrong = MATBIN.Read(definition); wrong.Samplers.Add(wrong.Samplers[0]);
        Reject(() => MaterialWriter.BindTextures(wrong.Write(), metadata, donor, donorMetadata, paths));
        paths.Remove(1);
        Reject(() => MaterialWriter.BindTextures(definition, metadata, donor, donorMetadata, paths));
        paths[1] = "";
        Reject(() => MaterialWriter.BindTextures(definition, metadata, donor, donorMetadata, paths));
        Console.WriteLine("PASS: material, shader and model texture bindings agree, retain template parameters and repeat; ambiguous/missing inputs fail");
    }

    /// <summary>A character's cut-out material definition: an installed plain one with a coverage threshold.</summary>
    internal static void Cutout()
    {
        byte[] plain = new MATBIN
        {
            SourcePath = @"N:\authored\C[AMSN].matxml", ShaderPath = @"N:\authored\C[AMSN].spx", Key = 7,
            Params = [new() { Name = "g_BlendMode", Key = 1, Type = MATBIN.ParamType.Int, Value = 0 }, new() { Name = "g_AlphaRef", Key = 2, Type = MATBIN.ParamType.Int2, Value = new[] { 0, 0 } }],
            Samplers = [new() { Type = "C_AMSN__snp_Texture2D_2_AlbedoMap_0", Path = "", Key = 3 }],
        }.Write();
        var cut = MATBIN.Read(AgeOfGold.Characters.HostCharacterMaterial.Cutout(plain, 128));
        if (!((int[])cut.Params.Single(p => p.Name == "g_AlphaRef").Value).SequenceEqual([128, 0]) || (int)cut.Params.Single(p => p.Name == "g_BlendMode").Value != 0 || cut.ShaderPath != @"N:\authored\C[AMSN].spx" || cut.Samplers.Single().Path != "")
            throw new Exception("A cut-out definition is not its plain one with the threshold set");
        if (!MATBIN.Read(plain).Write().AsSpan().SequenceEqual(plain)) throw new Exception("Making a cut-out definition changed its input");
        foreach (var bad in new Action[]
        {
            () => AgeOfGold.Characters.HostCharacterMaterial.Cutout(plain, 0),
            () => AgeOfGold.Characters.HostCharacterMaterial.Cutout(plain, 256),
            () => AgeOfGold.Characters.HostCharacterMaterial.Cutout(Fixture(1, "authored"), 128),
        })
            Reject(bad);
        // A definition of the build's own: the host's plain one drawn by the build's family, carrying the source definition's
        // two colours and naming the textures the build's program adds.
        byte[] coloured = new MATBIN
        {
            SourcePath = @"N:\authored\C[AMSN].matxml", ShaderPath = @"N:\authored\C[AMSN].spx", Key = 7,
            Params = [new() { Name = "g_AlphaRef", Key = 2, Type = MATBIN.ParamType.Int2, Value = new[] { 0, 0 } }, new() { Name = "g_DiffuseMapColor", Key = 4, Type = MATBIN.ParamType.Float3, Value = new[] { 1f, 1f, 1f } },
                new() { Name = "C_AMSN__snp_0_color_0", Key = 5, Type = MATBIN.ParamType.Float5, Value = new[] { 1f, 1f, 1f, 0.5f, 0.25f } },
                new() { Name = "C_AMSN__snp_0_float_0", Key = 6, Type = MATBIN.ParamType.Float, Value = 0f }],
            Samplers = [new() { Type = "C_AMSN__snp_Texture2D_2_AlbedoMap_0", Path = "", Key = 3 }],
        }.Write();
        byte[] donor = new MATBIN { SourcePath = "donor.matxml", ShaderPath = "donor.spx", Key = 9, Params = [],
            Samplers = [new() { Type = "donor_0", Path = "", Key = 21 }, new() { Type = "donor_1", Path = "", Key = 22 }, new() { Type = "donor_2", Path = "", Key = 23 }] }.Write();
        float[] dull = [0.6f, 0.55f, 0.5f], shiny = [0.7f, 0.8f, 0.9f];
        foreach (int? threshold in new int?[] { null, 128 })
        {
            // The definition's first number says which way the source reads the specular map: nought the newer, one the older.
            if ((float)MATBIN.Read(AgeOfGold.Characters.HostCharacterMaterial.Own(coloured, donor, ["donor_2"], "C[AMSN]", "aog_character", dull, shiny, threshold, true)).Params.Single(p => p.Name == "C_AMSN__snp_0_float_0").Value != 1)
                throw new Exception("A character definition does not say its specular map is read the older way");
            var own = MATBIN.Read(AgeOfGold.Characters.HostCharacterMaterial.Own(coloured, donor, ["donor_2", "donor_0"], "C[AMSN]", "aog_character", dull, shiny, threshold, false));
            if (own.SourcePath != @"N:\authored\aog_character.matxml" || own.ShaderPath != @"N:\authored\aog_character.spx"
                || !((float[])own.Params.Single(p => p.Name == "g_DiffuseMapColor").Value).SequenceEqual(dull)
                || !((float[])own.Params.Single(p => p.Name == "C_AMSN__snp_0_color_0").Value).SequenceEqual(new[] { 0.7f, 0.8f, 0.9f, 0.5f, 0.25f })
                || !((int[])own.Params.Single(p => p.Name == "g_AlphaRef").Value).SequenceEqual([threshold ?? 0, 0]) || (float)own.Params.Single(p => p.Name == "C_AMSN__snp_0_float_0").Value != 0
                || !own.Samplers.Select(x => (x.Type, x.Key)).SequenceEqual([("C_AMSN__snp_Texture2D_2_AlbedoMap_0", 3u), ("donor_2", 23u), ("donor_0", 21u)]))
                throw new Exception("A character definition of the build's own differs");
        }
        foreach (var bad in new Action[]
        {
            () => AgeOfGold.Characters.HostCharacterMaterial.Own(coloured, donor, ["donor_3"], "C[AMSN]", "aog_character", dull, shiny, null, false),
            () => AgeOfGold.Characters.HostCharacterMaterial.Own(coloured, donor, ["donor_0", "donor_0"], "C[AMSN]", "aog_character", dull, shiny, null, false),
            () => AgeOfGold.Characters.HostCharacterMaterial.Own(coloured, donor, ["donor_0"], "C[Other]", "aog_character", dull, shiny, null, false),
            () => AgeOfGold.Characters.HostCharacterMaterial.Own(coloured, donor, ["donor_0"], "C[AMSN]", "aog_character", [0.5f, 0.5f], shiny, null, false),
            () => AgeOfGold.Characters.HostCharacterMaterial.Own(plain, donor, ["donor_0"], "C[AMSN]", "aog_character", dull, shiny, null, false),
        })
            Reject(bad);
        Console.WriteLine("PASS: a character's material is an installed plain definition with a coverage threshold, or that drawn by the build's family with the source's two colours and added textures");
    }

    internal static byte[] Fixture(int count, string prefix) => new MATBIN
    {
        SourcePath = "authored.matxml", ShaderPath = "authored.spx", Key = 101,
        Params = [new() { Name = "authored-tint", Key = 103, Type = MATBIN.ParamType.Float3, Value = new[] { 0.25f, 0.5f, 0.75f } }],
        Samplers = Enumerable.Range(0, count).Select(i => new MATBIN.Sampler
            { Type = prefix + "_" + i, Path = "authored-default", Key = (uint)(41 + i) }).ToList(),
    }.Write();

    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("Unsupported material texture binding accepted");
    }
}
