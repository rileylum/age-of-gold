using System.Text;
using AgeOfGold.Scene;
using SoulsFormats;

namespace AgeOfGold.Effects;

/// <summary>Adds a translated effect, its textures and its texture list to the host's effect bundle.</summary>
internal static class EffectBundle
{
    /// <param name="Textures">The host texture identity given to each source texture.</param>
    /// <param name="Template">The installed effect whose scaffolding the new effect is built on.</param>
    internal sealed record Added(byte[] Bundle, SortedDictionary<int, int> Textures, string Template);

    /// <param name="firstTexture">The first of the consecutive host texture identities reserved for this effect.</param>
    /// <param name="sourceTexture">Reads a source texture's image by its identity.</param>
    internal static Added Add(byte[] bundle, int effect, int firstTexture, IReadOnlyList<BillboardCluster> clusters, Func<int, byte[]> sourceTexture)
    {
        var binder = BND4.Read(bundle);
        static string Leaf(string name) => name.Replace('\\', '/').Split('/')[^1];
        BinderFile[] Members(string extension) => binder.Files.Where(f => f.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)).OrderBy(f => f.ID).ToArray();
        var effects = Members(".fxr"); var images = Members(".tpf"); var lists = Members(".ffxreslist");
        // The first installed effect with the writer's shape; installed effects the format reader cannot follow are not candidates.
        var template = effects.FirstOrDefault(f =>
        {
            try { return HostEffectWriter.IsTemplate(FXR3.Read(f.Bytes)); }
            catch (Exception e) when (e is InvalidDataException or EndOfStreamException or ArgumentException or InvalidOperationException) { return false; }
        }) ?? throw new InvalidDataException("Host install lacks a one-emitter billboard effect to build on");
        var templateList = lists.SingleOrDefault(f => Leaf(f.Name).Equals(Path.ChangeExtension(Leaf(template.Name), ".ffxreslist"), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Host effect template has no texture list");
        if (images.Length == 0) throw new InvalidDataException("Host effect bundle holds no textures");

        var textures = new SortedDictionary<int, int>();
        foreach (int source in clusters.Select(c => c.Texture).Distinct().Order()) textures.Add(source, firstTexture + textures.Count);
        string effectName = $"f{effect:D9}";
        string[] names = [effectName + ".fxr", effectName + ".ffxreslist", .. textures.Values.Select(id => $"s{id}_a.tpf")];
        if (binder.Files.Any(f => names.Contains(Leaf(f.Name), StringComparer.OrdinalIgnoreCase))) throw new InvalidDataException("Effect identity is occupied in the host install");
        string Beside(BinderFile sibling, string leaf) => sibling.Name[..^Leaf(sibling.Name).Length] + leaf;
        // New members follow the last of their kind; a list keeps the host's fixed distance from its effect.
        int effectMember = effects[^1].ID + 1, imageMember = images[^1].ID + 1, listMember = templateList.ID - template.ID + effectMember;
        var added = new List<BinderFile>
        {
            new(template.Flags, effectMember, Beside(template, names[0]), HostEffectWriter.Write(template.Bytes, effect, clusters, textures)),
            new(templateList.Flags, listMember, Beside(templateList, names[1]), Encoding.ASCII.GetBytes(string.Concat(textures.Values.Select(id => $"s{id}_a.tif\r\n")))),
        };
        var writer = new TextureWriter(images[0].Bytes);
        // lazy: images keep the source's own encoding, as in the accepted playtest; the host's colour response differs until its particle stage is translated.
        foreach (var (source, id) in textures)
            added.Add(new(images[0].Flags, imageMember++, Beside(images[0], $"s{id}_a.tpf"), writer.Write($"s{id}_a", sourceTexture(source), false)));
        if (added.Any(a => binder.Files.Any(f => f.ID == a.ID))) throw new InvalidDataException("Effect bundle member identity is occupied");
        binder.Files.AddRange(added);
        binder.Files.Sort((a, b) => a.ID.CompareTo(b.ID));
        return new(binder.Write(), textures, Leaf(template.Name));
    }
}
