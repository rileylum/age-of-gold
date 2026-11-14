using System.Numerics;
using AgeOfGold.Characters;

internal static class SkeletonFitCheck
{
    internal static void Run()
    {
        static SkeletonFit.Bone At(string name, int parent, float x, float y, float z, float turn = 0) =>
            new(name, parent, Matrix4x4.CreateRotationZ(turn) * Matrix4x4.CreateTranslation(x, y, z));
        // A small source skeleton, standing straight: a leg, a twist helper, a finger the host names differently, and a loose node.
        SkeletonFit.Bone[] source =
        [
            At("Pelvis", -1, 0, 1, 0), At("L Thigh", 0, 0.1f, 1, 0), At("L Calf", 1, 0.1f, 0.5f, 0), At("LCalfTwist", 1, 0.1f, 0.5f, 0),
            At("L Finger0", 2, 0.1f, 0.4f, 0), At("model", -1, 0, 0, 0),
        ];
        // The host skeleton is a fifth larger, lists its bones in another order and holds its calf turned a quarter turn.
        SkeletonFit.Bone[] host =
        [
            At("Root", -1, 0, 0, 0), At("Pelvis", 0, 0, 1.2f, 0), At("L_Calf", 3, 0.12f, 0.6f, 0, MathF.PI / 2), At("L_Thigh", 1, 0.12f, 1.2f, 0),
            At("L_Finger0[omit]", 2, 0.12f, 0.5f, 0), At("L_CalfTwist[omit]", 3, 0.12f, 0.6f, 0),
            // Helper nodes no source bone pairs with may share a name.
            At("helper_Leg", 0, 0, 0, 0), At("helper_leg", 0, 0, 0, 0),
        ];
        var fit = SkeletonFit.Match(source, host, new Dictionary<string, string> { ["LCalfTwist"] = "L_Calf" });
        // Bones pair by name, ignoring spacing; the twist helper is paired as told; the finger and the loose node are not paired.
        if (!fit.Host.SequenceEqual([1, 3, 2, 2, 2, -1])) throw new Exception("Bones are paired wrongly: " + string.Join(",", fit.Host));
        // The size ratio is the host's bone lengths over the source's, across paired bones: (0.12 + 0.6) / (0.1 + 0.5).
        if (MathF.Abs(fit.Scale - 1.2f) > 1e-5f) throw new Exception("Size ratio is " + fit.Scale);
        static void Near(Vector3 actual, Vector3 expected, string what) { if ((actual - expected).Length() > 1e-5f) throw new Exception($"{what}: {actual}, expected {expected}"); }
        // A point at a source bone lands on the host bone; a point beside it keeps its place around the bone, grown by the
        // size ratio and turned with the host bone.
        Near(Vector3.Transform(new Vector3(0.1f, 1, 0), fit.Move[1]), new(0.12f, 1.2f, 0), "thigh origin");
        Near(Vector3.Transform(new Vector3(0.15f, 0.9f, 0), fit.Move[1]), new(0.18f, 1.08f, 0), "point beside the thigh");
        Near(Vector3.Transform(new Vector3(0.1f, 0.5f, 0), fit.Move[2]), new(0.12f, 0.6f, 0), "calf origin");
        // The host calf is turned a quarter turn about z: what lay 0.1 below the source calf lies 0.12 to the calf's turned "below".
        Near(Vector3.Transform(new Vector3(0.1f, 0.4f, 0), fit.Move[2]), new(0.12f + 0.12f, 0.6f, 0), "point below the turned calf");
        // An unpaired bone follows the nearest paired bone above it, rigidly.
        if (fit.Move[4] != fit.Move[2]) throw new Exception("An unpaired bone does not follow its parent");
        // Directions turn without growing.
        Near(Vector3.TransformNormal(Vector3.UnitY, SkeletonFit.Turn(fit.Move[2])), new(-1, 0, 0), "direction on the turned calf");
        // An attachment with no bone above it rides a named source bone; a part of a body takes the whole body's size ratio.
        var part = SkeletonFit.Match(source, host, new Dictionary<string, string> { ["LCalfTwist"] = "L_Calf" }, new Dictionary<string, string> { ["model"] = "L Thigh" }, 2);
        if (part.Host[5] != 3 || part.Scale != 2) throw new Exception("A rider is not carried by the bone it rides, or the given size ratio is not used");
        Near(Vector3.Transform(new Vector3(0.15f, 0.9f, 0), part.Move[5]), new(0.12f + 0.1f, 1.2f - 0.2f, 0), "point on a rider");
        // A lone bone with nothing to measure fits only with a given size ratio.
        if (SkeletonFit.Match([At("blade", -1, 0, 0, 0)], host, new Dictionary<string, string> { ["blade"] = "L_Thigh" }, null, 1.2f).Host[0] != 3) throw new Exception("A single paired bone does not fit");
        foreach (var bad in new Action[]
        {
            () => SkeletonFit.Match([At("blade", -1, 0, 0, 0)], host, new Dictionary<string, string> { ["blade"] = "L_Thigh" }),
            () => SkeletonFit.Match(source, host, null, new Dictionary<string, string> { ["model"] = "Missing" }),
            () => SkeletonFit.Match(source, host, null, null, 0),
            () => SkeletonFit.Match(source, host, new Dictionary<string, string> { ["LCalfTwist"] = "Missing" }),
            () => SkeletonFit.Match(source, [.. host, At("Pelvis", 0, 0, 1, 0)]),
            () => SkeletonFit.Match([At("Other", -1, 0, 0, 0)], host),
        })
        {
            try { bad(); throw new Exception("An unusable skeleton pairing was accepted"); }
            catch (InvalidDataException) { }
        }
        Console.WriteLine("PASS: a source skeleton fits a host skeleton by bone name, with its size ratio, turned rest poses and followers");
    }
}
