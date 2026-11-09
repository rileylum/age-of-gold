using System.Text.Json;

namespace AgeOfGold.Scene;

internal sealed record LayerMaterial(LayerMaterial.Layer First, LayerMaterial.Layer Second)
{
    internal sealed record Layer(string Colour, string Normal, string Packed);

    internal static LayerMaterial Read(JsonElement material)
    {
        // lazy: the checked opaque ground family; other families need their own coordinate and texture roles.
        if (material.GetProperty("definition").GetString() != "M[DB][M]" || material.GetProperty("blendMode").GetInt32() != 0)
            throw new InvalidDataException("Unsupported layered material family");
        string[] inputs = ["g_Diffuse", "g_Bumpmap", "g_Specular", "g_Diffuse_2", "g_Bumpmap_2", "g_Specular_2", "g_DetailBumpmap"];
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
        return new(new(names["g_Diffuse"], names["g_Bumpmap"], names["g_Specular"]),
            new(names["g_Diffuse_2"], names["g_Bumpmap_2"], names["g_Specular_2"]));
    }
}
