using System.Text.Json;

namespace AgeOfGold.Scene;

internal sealed record LayerMaterial(LayerMaterial.Layer First, LayerMaterial.Layer Second)
{
    internal sealed record Layer(string Colour, string Normal, string Packed);

    /// <summary>The number of layers of an opaque lit source material, or zero when its family is not translated yet.</summary>
    internal static int Layers(JsonElement material)
    {
        // The source shader, not the definition's name, decides how its inputs are shaded.
        string shader = Path.GetFileNameWithoutExtension(material.GetProperty("shader").GetString()!.Replace('\\', '/'));
        // lazy: the opaque lit families only; cutout, blended, lightmapped and foliage shaders need their own translations.
        return (shader, material.GetProperty("blendMode").GetInt32()) switch
        {
            ("FRPG_Phn_ColDifSpcBmp", 0 or 1 or 2) => 1, ("FRPG_Phn_ColDifSpcBmpMul", 0) => 2,
            // lazy: foliage and ivy are lit as plain surfaces; their wind sway needs the vertex stage and their extra coordinate sets.
            ("FRPG_Foliage_ColDifSpcBmp" or "FRPG_Ivy_ColDifSpcBmp", 0 or 1) => 1,
            _ => 0,
        };
    }

    /// <summary>Whether a one-layer material's vertices carry wind coordinate sets after the texture mapping.</summary>
    internal static bool Sways(JsonElement material) => Path.GetFileNameWithoutExtension(material.GetProperty("shader").GetString()!.Replace('\\', '/'))
        is "FRPG_Foliage_ColDifSpcBmp" or "FRPG_Ivy_ColDifSpcBmp";

    /// <summary>Whether the surface's alpha removes pixels.</summary>
    // lazy: blended surfaces are cut out too, as in the accepted playtest, until forward blending is translated.
    internal static bool Cutout(JsonElement material) => material.GetProperty("blendMode").GetInt32() is 1 or 2;

    /// <summary>Whether an opaque source material has one layer; two-layer materials return false and others are rejected.</summary>
    internal static bool Single(JsonElement material) => Layers(material) switch
    {
        1 => true, 2 => false, _ => throw new InvalidDataException("Unsupported layered material family"),
    };

    internal static LayerMaterial Read(JsonElement material)
    {
        bool single = Single(material);
        string[] inputs = single ? ["g_Diffuse", "g_Bumpmap", "g_Specular"]
            : ["g_Diffuse", "g_Bumpmap", "g_Specular", "g_Diffuse_2", "g_Bumpmap_2", "g_Specular_2"];
        // Some definitions omit the unused detail input altogether.
        if (material.GetProperty("textures").EnumerateArray().Any(t => t.GetProperty("input").GetString() == "g_DetailBumpmap"))
            inputs = [.. inputs, "g_DetailBumpmap"];
        var textures = material.GetProperty("textures").EnumerateArray().ToArray();
        var coordinates = material.GetProperty("coordinates").EnumerateArray().ToArray();
        foreach (var records in new[] { textures, coordinates })
            if (!records.Select(t => t.GetProperty("input").GetString()).Order(StringComparer.Ordinal)
                .SequenceEqual(inputs.Order(StringComparer.Ordinal))) throw new InvalidDataException("Missing or ambiguous layered material inputs");
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            var texture = textures.Single(t => t.GetProperty("input").GetString() == input);
            var coordinate = coordinates.Single(t => t.GetProperty("input").GetString() == input);
            int channel = input.EndsWith("_2", StringComparison.Ordinal) ? 1 : 0;
            string name = texture.GetProperty("texture").GetString() ?? throw new InvalidDataException("Missing layered texture name");
            if (coordinate.GetProperty("channel").GetInt32() != channel
                || !texture.GetProperty("scale").EnumerateArray().Select(v => v.GetSingle()).SequenceEqual(new[] { 1f, 1f })
                || (input == "g_DetailBumpmap" ? name.Length != 0 : string.IsNullOrWhiteSpace(name)))
                throw new InvalidDataException($"Unsupported layered texture binding: {input}");
            names.Add(input, name);
        }
        var first = new Layer(names["g_Diffuse"], names["g_Bumpmap"], names["g_Specular"]);
        return new(first, single ? first : new(names["g_Diffuse_2"], names["g_Bumpmap_2"], names["g_Specular_2"]));
    }
}
