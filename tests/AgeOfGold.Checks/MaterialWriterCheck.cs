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
            Params = [new() { Name = "g_BlendMode", Key = 1, Type = MATBIN.ParamType.Int, Value = 0 }, new() { Name = "g_AlphaRef", Key = 2, Type = MATBIN.ParamType.Int2, Value = new[] { 0, 0 } },
                new() { Name = "g_DiffuseMapColor", Key = 4, Type = MATBIN.ParamType.Float3, Value = new[] { 1f, 1f, 1f } }],
            Samplers = [new() { Type = "C_AMSN__snp_Texture2D_2_AlbedoMap_0", Path = "", Key = 3 }],
        }.Write();
        var cut = MATBIN.Read(AgeOfGold.Characters.HostCharacterMaterial.Cutout(plain, 128));
        if (!((int[])cut.Params.Single(p => p.Name == "g_AlphaRef").Value).SequenceEqual([128, 0]) || (int)cut.Params.Single(p => p.Name == "g_BlendMode").Value != 0 || cut.ShaderPath != @"N:\authored\C[AMSN].spx" || cut.Samplers.Single().Path != "")
            throw new Exception("A cut-out definition is not its plain one with the threshold set");
        if (!MATBIN.Read(plain).Write().AsSpan().SequenceEqual(plain)) throw new Exception("Making a cut-out definition changed its input");
        // The source multiplies a surface's colour map by a colour of its definition's; the host definition has the same setting.
        var tinted = MATBIN.Read(AgeOfGold.Characters.HostCharacterMaterial.Tinted(plain, [0.325f, 0.3f, 0.25f]));
        if (!((float[])tinted.Params.Single(p => p.Name == "g_DiffuseMapColor").Value).SequenceEqual([0.325f, 0.3f, 0.25f]) || !((int[])tinted.Params.Single(p => p.Name == "g_AlphaRef").Value).SequenceEqual([0, 0])
            || tinted.ShaderPath != @"N:\authored\C[AMSN].spx" || tinted.Samplers.Single().Path != "")
            throw new Exception("A tinted definition is not its plain one with the colour set");
        var both = MATBIN.Read(AgeOfGold.Characters.HostCharacterMaterial.Cutout(AgeOfGold.Characters.HostCharacterMaterial.Tinted(plain, [0.5f, 0.5f, 0.5f]), 128));
        if (!((float[])both.Params.Single(p => p.Name == "g_DiffuseMapColor").Value).SequenceEqual([0.5f, 0.5f, 0.5f]) || !((int[])both.Params.Single(p => p.Name == "g_AlphaRef").Value).SequenceEqual([128, 0]))
            throw new Exception("A definition does not take both a colour and a cut-out");
        foreach (var bad in new Action[]
        {
            () => AgeOfGold.Characters.HostCharacterMaterial.Tinted(plain, [0.5f, 0.5f]),
            () => AgeOfGold.Characters.HostCharacterMaterial.Tinted(plain, [0.5f, -0.1f, 0.5f]),
            () => AgeOfGold.Characters.HostCharacterMaterial.Tinted(plain, [0.5f, float.NaN, 0.5f]),
            () => AgeOfGold.Characters.HostCharacterMaterial.Tinted(Fixture(1, "authored"), [0.5f, 0.5f, 0.5f]),
        })
        {
            try { bad(); } catch (InvalidDataException) { continue; }
            throw new Exception("An invalid tint or a definition without a colour setting was accepted");
        }
        foreach (var bad in new Action[]
        {
            () => AgeOfGold.Characters.HostCharacterMaterial.Cutout(plain, 0),
            () => AgeOfGold.Characters.HostCharacterMaterial.Cutout(plain, 256),
            () => AgeOfGold.Characters.HostCharacterMaterial.Cutout(Fixture(1, "authored"), 128),
        })
            Reject(bad);
        Console.WriteLine("PASS: a character's cut-out material is an installed plain definition with a coverage threshold");
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
