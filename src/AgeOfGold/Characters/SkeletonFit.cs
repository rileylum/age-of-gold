using System.Numerics;

namespace AgeOfGold.Characters;

/// <summary>
/// Fits a source character's skeleton onto a host skeleton whose bones carry the same names, so the source's body can be
/// carried by the host's animations. Both games rest a bone's own axes the same way; their rest poses and sizes differ.
/// </summary>
internal static class SkeletonFit
{
    /// <param name="World">The bone's rest placement in the model's space.</param>
    internal sealed record Bone(string Name, int Parent, Matrix4x4 World);

    /// <param name="Host">For each source bone, the host bone that carries it, or -1 when none above it is paired.</param>
    /// <param name="Move">For each source bone, the move from a point modelled around it to its place on the host skeleton.</param>
    /// <param name="Scale">The host skeleton's size over the source's.</param>
    internal sealed record Fit(int[] Host, Matrix4x4[] Move, float Scale);

    /// <param name="named">Source bones paired with a host bone of another name.</param>
    internal static Fit Match(IReadOnlyList<Bone> source, IReadOnlyList<Bone> host, IReadOnlyDictionary<string, string>? named = null)
    {
        // Names differ only in spacing and case between the games.
        static string Key(string name) => new string(name.Where(c => char.IsLetterOrDigit(c) || c is '[' or ']').ToArray()).ToLowerInvariant();
        // A host model may name helper nodes twice; that only matters for a name a source bone pairs with.
        var hostByKey = host.Select((bone, index) => (Key: Key(bone.Name), index)).GroupBy(b => b.Key, b => b.index, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        int One(string name, int[] found) => found.Length == 1 ? found[0] : throw new InvalidDataException("Host skeleton names a paired bone twice: " + name);
        var paired = new int[source.Count];
        for (int s = 0; s < source.Count; s++)
        {
            if (named is not null && named.TryGetValue(source[s].Name, out string? other))
                paired[s] = hostByKey.TryGetValue(Key(other), out var told) ? One(other, told) : throw new InvalidDataException("Host skeleton lacks the bone named for " + source[s].Name);
            else paired[s] = hostByKey.TryGetValue(Key(source[s].Name), out var found) ? One(source[s].Name, found) : -1;
        }
        // The size ratio: the length of every paired bone whose parent is paired too, host over source.
        float sourceLength = 0, hostLength = 0;
        for (int s = 0; s < source.Count; s++)
        {
            int parent = source[s].Parent;
            if (parent < 0 || paired[s] < 0 || paired[parent] < 0 || paired[s] == paired[parent]) continue;
            sourceLength += (source[s].World.Translation - source[parent].World.Translation).Length();
            hostLength += (host[paired[s]].World.Translation - host[paired[parent]].World.Translation).Length();
        }
        if (!(sourceLength > 0) || !(hostLength > 0)) throw new InvalidDataException("Skeletons share no paired bone lengths to compare");
        float scale = hostLength / sourceLength;
        var carrier = new int[source.Count]; var move = new Matrix4x4[source.Count];
        for (int s = 0; s < source.Count; s++)
        {
            // An unpaired bone is carried, rigidly, by the nearest paired bone above it.
            int from = s;
            for (int depth = 0; from >= 0 && paired[from] < 0; depth++)
            {
                if (depth > source.Count) throw new InvalidDataException("Source skeleton hierarchy is cyclic");
                from = source[from].Parent;
            }
            if (from < 0) { carrier[s] = -1; move[s] = Matrix4x4.Identity; continue; }
            if (!Matrix4x4.Invert(source[from].World, out var around)) throw new InvalidDataException("Source bone placement is singular: " + source[from].Name);
            carrier[s] = paired[from];
            // Into the source bone's own space, grown by the size ratio, then out through the host bone's rest placement.
            move[s] = around * Matrix4x4.CreateScale(scale) * host[paired[from]].World;
        }
        return new(carrier, move, scale);
    }

    /// <summary>A move without its growth or shift, for directions.</summary>
    internal static Matrix4x4 Turn(Matrix4x4 move)
    {
        if (!Matrix4x4.Decompose(move, out _, out var rotation, out _)) throw new InvalidDataException("A skeleton move cannot be separated into turn and growth");
        return Matrix4x4.CreateFromQuaternion(rotation);
    }
}
