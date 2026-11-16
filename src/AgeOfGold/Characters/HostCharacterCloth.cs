using System.Numerics;
using AgeOfGold.Collision;

namespace AgeOfGold.Characters;

/// <summary>
/// The source moves a cloak, a skirt's flaps or a ponytail by chains of rigid bodies that hang from bodies following
/// the animation; each body carries a bone, and the model rests those bones spread out for the chains to drop.
/// lazy: a build simulates nothing. It hangs each chain once, where it would come to rest, so the cloth droops but
/// does not swing; the host's own cloth simulation is the way to move it, once it is known what makes the host run a
/// cloth file for a character of a build's.
/// </summary>
internal static class HostCharacterCloth
{
    /// <summary>A chain of bones hanging from one that follows the animation, from the top down.</summary>
    internal sealed record Chain(string Anchor, string[] Bones);

    // A chain within this of straight down is left as the model rests it; one further out is brought to rest this far
    // from straight down, on the side it leans to.
    // lazy: two angles for every chain; the source's joints each have their own limits, which are not read.
    private const float Within = 30 * MathF.PI / 180, RestsAt = 20 * MathF.PI / 180;

    /// <summary>The bodies a source cloth file names: those that move of themselves, and those that follow the animation.</summary>
    internal static (string[] Moving, string[] Still) Bodies(byte[] sourceCloth)
    {
        var moving = new SortedSet<string>(StringComparer.Ordinal); var still = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var variant in new SourceTagfile(sourceCloth).Root.Field("namedVariants").Elements())
        {
            var value = variant.Field("variant");
            if (value.TypeName != "hkpPhysicsData") continue;
            foreach (var system in value.Field("systems").Elements())
                foreach (var body in system.Field("rigidBodies").Elements())
                {
                    string name = body.Field("name").Text();
                    // A body with no inverse mass follows the animation.
                    bool moves = body.Field("motion").Field("inertiaAndMassInv").Floats()[3] > 0;
                    if (!(moves ? moving : still).Add(name) || (moves ? still : moving).Contains(name)) throw new InvalidDataException("Source cloth file names a body twice: " + name);
                }
        }
        return ([.. moving], [.. still]);
    }

    /// <summary>The chains among a cloth file's bodies: each run of moving bodies, by the bones they are named for, under a still one.</summary>
    internal static Chain[] Chains(IReadOnlyCollection<string> moving, IReadOnlyCollection<string> still, IReadOnlyList<(string Name, int Parent)> bones)
    {
        int Index(string name) => Enumerable.Range(0, bones.Count).Where(i => bones[i].Name == name).ToArray() is [var one] ? one : throw new InvalidDataException("Source cloth body has not one bone of its name: " + name);
        var chains = new List<Chain>();
        foreach (string top in moving.Order(StringComparer.Ordinal))
        {
            int parent = bones[Index(top)].Parent;
            if (parent >= 0 && moving.Contains(bones[parent].Name)) continue;
            if (parent < 0 || !still.Contains(bones[parent].Name)) throw new InvalidDataException("Source cloth chain hangs from a bone with no still body: " + top);
            var run = new List<string> { top };
            while (true)
            {
                var below = moving.Where(m => bones[Index(m)].Parent == Index(run[^1])).ToArray();
                if (below.Length == 0) break;
                // lazy: a chain that forks is not laid out; none of the characters built has one.
                run.Add(below.Length == 1 ? below[0] : throw new InvalidDataException("Source cloth chain forks at " + run[^1]));
            }
            chains.Add(new(bones[parent].Name, [.. run]));
        }
        if (chains.Sum(c => c.Bones.Length) != moving.Count) throw new InvalidDataException("Source cloth bodies do not all lie in chains");
        return [.. chains];
    }

    /// <summary>
    /// For each bone of a chain that rests far from hanging, the turn about the chain's top that brings the chain to
    /// rest: what follows the bone is turned with it.
    /// </summary>
    /// <param name="joints">Where each bone rests, in the model's space, the Y axis up.</param>
    internal static Dictionary<string, Matrix4x4> Hung(IReadOnlyList<Chain> chains, IReadOnlyDictionary<string, Vector3> joints)
    {
        var turns = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
        foreach (var chain in chains)
        {
            // lazy: a chain of one bone has no length to judge by and stays as the model rests it.
            if (chain.Bones.Length < 2) continue;
            Vector3 top = joints[chain.Bones[0]], run = joints[chain.Bones[^1]] - top;
            if (run.Length() < 1e-4f) throw new InvalidDataException("Cloth chain has no length: " + chain.Bones[0]);
            var along = Vector3.Normalize(run);
            float from = MathF.Acos(Math.Clamp(Vector3.Dot(along, -Vector3.UnitY), -1, 1));
            if (from <= Within) continue;
            // A chain pointing straight up has no side to fall to; it falls forward.
            var axis = Vector3.Cross(along, -Vector3.UnitY);
            axis = axis.Length() < 1e-4f ? Vector3.UnitX : Vector3.Normalize(axis);
            var turn = Matrix4x4.CreateTranslation(-top) * Matrix4x4.CreateFromAxisAngle(axis, from - RestsAt) * Matrix4x4.CreateTranslation(top);
            foreach (string bone in chain.Bones) if (!turns.TryAdd(bone, turn)) throw new InvalidDataException("A bone lies in two cloth chains: " + bone);
        }
        return turns;
    }
}
