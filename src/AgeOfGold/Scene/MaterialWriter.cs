using System.Numerics;
using AgeOfGold.Shaders;
using SoulsFormats;

namespace AgeOfGold.Scene;

internal static class MaterialWriter
{
    internal sealed record Bound(byte[] Definition, byte[] Metadata, FLVER2.Texture[] Textures);

    internal static Bound BindLayers(byte[] definition, byte[] metadata, byte[] donorDefinition, byte[] donorMetadata,
        LayerMaterial material, IReadOnlyDictionary<string, string> names, string environment)
    {
        var before = ShaderMetadata.Read(metadata); var extra = ShaderMetadata.Read(donorMetadata);
        if (before.Textures.Count != 5 || extra.Textures.Count != 7)
            throw new InvalidDataException("Unsupported layered host texture layout");
        METAPARAM.Texture One(IEnumerable<METAPARAM.Texture> textures)
        {
            var matches = textures.ToArray();
            return matches.Length == 1 ? matches[0] : throw new InvalidDataException("Ambiguous layered host texture semantic");
        }
        var paths = new Dictionary<int, string>();
        var groups = new HashSet<string>(StringComparer.Ordinal);
        for (int layer = 0; layer < 2; layer++)
        {
            var source = layer == 0 ? material.First : material.Second;
            var colour = One(before.Textures.Where(t => t.Type == METAPARAM.TextureType.Albedo
                && t.Name.EndsWith("AlbedoMap_" + layer, StringComparison.Ordinal)));
            if (string.IsNullOrWhiteSpace(colour.UvGroupName) || !groups.Add(colour.UvGroupName))
                throw new InvalidDataException("Layered host coordinates are not independent");
            var normal = One(before.Textures.Where(t => t.Type == METAPARAM.TextureType.Normal && t.UvGroupName == colour.UvGroupName));
            var packed = One(extra.Textures.Where(t => t.Type == METAPARAM.TextureType.Metallic
                && t.UvGroupName == colour.UvGroupName && t.Slot >= before.Textures.Count));
            foreach (var (target, name) in new[] { (colour, source.Colour), (normal, source.Normal), (packed, source.Packed) })
            {
                if (!names.TryGetValue(name, out var path)) throw new InvalidDataException($"Unbuilt layered texture: {name}");
                paths.Add(target.Slot, path);
            }
        }
        var cube = One(before.Textures.Where(t => t.Type == METAPARAM.TextureType.Other && t.Name.EndsWith("BlendEdge", StringComparison.Ordinal)));
        paths.Add(cube.Slot, environment);
        return BindTextures(definition, metadata, donorDefinition, donorMetadata, paths);
    }

    /// <summary>The texture registers the layered ground pixel program reads, by role.</summary>
    internal static PixelShader.LayerSlots LayerSlots(byte[] metadata)
    {
        var textures = ShaderMetadata.Read(metadata).Textures;
        METAPARAM.Texture One(IEnumerable<METAPARAM.Texture> matches)
        {
            var found = matches.ToArray();
            return found.Length == 1 ? found[0] : throw new InvalidDataException("Ambiguous layered host texture semantic");
        }
        var colours = Enumerable.Range(0, 2).Select(layer => One(textures.Where(t => t.Type == METAPARAM.TextureType.Albedo
            && t.Name.EndsWith("AlbedoMap_" + layer, StringComparison.Ordinal)))).ToArray();
        var normals = colours.Select(c => One(textures.Where(t => t.Type == METAPARAM.TextureType.Normal && t.UvGroupName == c.UvGroupName))).ToArray();
        var cube = One(textures.Where(t => t.Type == METAPARAM.TextureType.Other && t.Name.EndsWith("BlendEdge", StringComparison.Ordinal)));
        return new(cube.Slot, colours.Select(t => (int)t.Slot).ToArray(), normals.Select(t => (int)t.Slot).ToArray());
    }

    internal static Bound BindTextures(byte[] definition, byte[] metadata, byte[] donorDefinition, byte[] donorMetadata,
        IReadOnlyDictionary<int, string> textures)
    {
        var material = MATBIN.Read(definition); var original = ShaderMetadata.Read(metadata);
        var donor = MATBIN.Read(donorDefinition); var extra = ShaderMetadata.Read(donorMetadata);
        CheckSamplers(material, original); CheckSamplers(donor, extra);
        if (textures.Count < original.Textures.Count || textures.Count > 32
            || !textures.Keys.Order().SequenceEqual(Enumerable.Range(0, textures.Count))
            || textures.Values.Any(t => string.IsNullOrWhiteSpace(t) || t.Any(char.IsControl)))
            throw new InvalidDataException("Material requires one named texture for every declared slot");
        var added = Enumerable.Range(original.Textures.Count, textures.Count - original.Textures.Count).ToArray();
        var rebuilt = added.Length == 0 ? (byte[])metadata.Clone() : ShaderMetadata.AppendTextures(metadata, donorMetadata, added);
        var layout = ShaderMetadata.Read(rebuilt);
        foreach (int slot in added)
        {
            var name = layout.Textures.Single(t => t.Slot == slot).Name;
            material.Samplers.Add(donor.Samplers.Single(s => s.Type == name));
        }
        var bindings = layout.Textures.OrderBy(t => t.Slot).Select(t => new FLVER2.Texture
            { ParamName = t.Name, Path = textures[t.Slot], TilingScale = Vector2.One }).ToArray();
        return new(material.Write(), rebuilt, bindings);
    }

    private static void CheckSamplers(MATBIN material, METAPARAM metadata)
    {
        if (!material.Samplers.Select(s => s.Type).Order(StringComparer.Ordinal)
            .SequenceEqual(metadata.Textures.Select(t => t.Name).Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Material and shader texture declarations disagree");
    }
}
