using System.Numerics;
using AgeOfGold.Characters;

/// <summary>Chains of cloth bones found among a cloth file's bodies, and hung where they would come to rest.</summary>
internal static class HostCharacterClothCheck
{
    internal static void Run()
    {
        // An authored skeleton: a spine with a cloak of three bones, a skirt flap of two and a tail of one; and a hip with nothing.
        (string Name, int Parent)[] bones = [("Root", -1), ("Spine", 0), ("Cloak1", 1), ("Cloak2", 2), ("Cloak3", 3), ("Flap1", 1), ("Flap2", 5), ("Tail", 1), ("Hip", 0)];
        string[] moving = ["Cloak2", "Flap1", "Cloak1", "Tail", "Cloak3", "Flap2"], still = ["Spine", "Root"];
        var chains = HostCharacterCloth.Chains(moving, still, bones);
        // Each run of moving bodies under a still one, from the top down, in the order of their top bones' names.
        if (chains.Length != 3 || chains.Any(c => c.Anchor != "Spine") || !chains[0].Bones.SequenceEqual(["Cloak1", "Cloak2", "Cloak3"]) || !chains[1].Bones.SequenceEqual(["Flap1", "Flap2"]) || !chains[2].Bones.SequenceEqual(["Tail"]))
            throw new Exception("Cloth chains differ: " + string.Join(" | ", chains.Select(c => c.Anchor + ":" + string.Join(",", c.Bones))));
        foreach (var bad in new Action[]
        {
            // A chain under a bone with no still body; a chain that forks; a body with no bone.
            () => HostCharacterCloth.Chains(moving, ["Root"], bones),
            () => HostCharacterCloth.Chains([.. moving, "Hip"], still, [.. bones[..8], ("Hip", 2)]),
            () => HostCharacterCloth.Chains([.. moving, "Absent"], still, bones),
        })
        {
            try { bad(); throw new Exception("An unusable set of cloth bodies was accepted"); } catch (InvalidDataException) { }
        }

        // The cloak rests straight out sideways; the flap already hangs, leaning a little; the tail is one bone.
        var joints = new Dictionary<string, Vector3>
        {
            ["Cloak1"] = new(0.1f, 1.4f, 0.05f), ["Cloak2"] = new(0.3f, 1.4f, 0.05f), ["Cloak3"] = new(0.5f, 1.4f, 0.05f),
            ["Flap1"] = new(0, 0.9f, 0.1f), ["Flap2"] = new(0.03f, 0.75f, 0.12f), ["Tail"] = new(0, 1.7f, -0.1f),
        };
        var hung = HostCharacterCloth.Hung(chains, joints);
        if (!hung.Keys.Order(StringComparer.Ordinal).SequenceEqual(["Cloak1", "Cloak2", "Cloak3"])) throw new Exception("The wrong bones were hung: " + string.Join(",", hung.Keys));
        // The cloak turns about its top until it is twenty degrees from straight down, on the side it leant to: its top
        // stays, and its last joint, four tenths of a metre along, ends below and a little outward.
        var top = Vector3.Transform(joints["Cloak1"], hung["Cloak1"]); var end = Vector3.Transform(joints["Cloak3"], hung["Cloak3"]);
        float lean = 20 * MathF.PI / 180;
        var expected = joints["Cloak1"] + 0.4f * new Vector3(MathF.Sin(lean), -MathF.Cos(lean), 0);
        if (Vector3.Distance(top, joints["Cloak1"]) > 1e-5f || Vector3.Distance(end, expected) > 1e-5f) throw new Exception($"The cloak hangs at {end}, not {expected}");
        if (hung["Cloak1"] != hung["Cloak2"] || hung["Cloak2"] != hung["Cloak3"]) throw new Exception("A chain's bones were not turned together");
        // A chain resting straight up falls forward rather than nowhere.
        var up = HostCharacterCloth.Hung([new("Spine", ["A", "B"])], new Dictionary<string, Vector3> { ["A"] = new(0, 1, 0), ["B"] = new(0, 1.5f, 0) });
        var fallen = Vector3.Transform(new Vector3(0, 1.5f, 0), up["B"]);
        if (!(fallen.Y < 0.6f)) throw new Exception("A chain resting straight up did not fall: " + fallen);
        Console.WriteLine("PASS: cloth chains are the runs of moving bodies under still ones, and a chain resting far from hanging is turned about its top to rest twenty degrees from straight down");
    }
}
