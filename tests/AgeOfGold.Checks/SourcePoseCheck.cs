using System.Buffers.Binary;
using System.Numerics;
using AgeOfGold.Animation;

internal static class SourcePoseCheck
{
    internal static void Run()
    {
        // Three authored tracks, written here byte by byte: fixed values; curves with fixed parts; and nothing at all.
        var first = new Quaternion(0.1f, -0.2f, 0.3f, 0.9273618f); var second = Quaternion.Normalize(new Quaternion(0.8f, 0.3f, -0.2f, 0.1f));
        var block = new List<byte>();
        // Masks: packing kinds (translation bits 0-1, rotation bits 2-5, scale bits 6-7), then translation, rotation and scale parts.
        block.AddRange([1 << 2, 0b0000_0111, 0x0F, 0]);                      // fixed x, y, z; fixed rotation in five bytes; rest scale
        block.AddRange([1 | 2 << 2, 0b0010_0001, 0xF0, 0b0000_0001]);    // fixed x, curved y (16-bit); curved rotation in six bytes; fixed scale x
        block.AddRange([0, 0, 0, 0]);
        block.Add(0b1010_1010);                                           // one float track's mask, then padding to four bytes
        while (block.Count % 4 != 0) block.Add(0);
        int transformOffset = block.Count;
        void Float(float value) { var bytes = new byte[4]; BinaryPrimitives.WriteSingleLittleEndian(bytes, value); block.AddRange(bytes); }
        void Align(int to) { while (block.Count % to != 0) block.Add(0xCD); }
        // Track one: three fixed translations, then the rotation packed as three 12-bit components beside the largest's place and sign.
        Float(1.5f); Float(-2.25f); Float(0.125f); Align(4);
        block.AddRange(Pack40(first)); Align(4);
        Align(4);
        // Track two: a curve header (two control points, degree one, four knots), bounds for the curved axis, fixed x, then points.
        block.AddRange([1, 0, 1, 0, 0, 1, 1]); Align(4);
        Float(0.75f);            // fixed x
        Float(2); Float(6);      // y between 2 and 6
        block.AddRange([0x00, 0x40, 0xFF, 0xFF]);   // first point a quarter of the way: 3; second at the top
        Align(4);
        block.AddRange([1, 0, 1, 0, 0, 1, 1]); Align(2);
        block.AddRange(Pack48(second)); block.AddRange(Pack48(first)); Align(4);
        Float(1.75f); Align(4);  // fixed scale x; y and z rest at one
        var decoded = SourcePoseReader.DecodeFirstFrame(block.ToArray(), 3, 1, transformOffset);
        void Near(Vector3 actual, Vector3 expected, string what) { if (Vector3.Distance(actual, expected) > 2e-4f) throw new Exception($"Pose check failed: {what}: {actual} != {expected}"); }
        void Turned(Quaternion actual, Quaternion expected, string what)
        {
            // A rotation and its negation are the same turn; packing costs about a thousandth.
            if (MathF.Abs(Quaternion.Dot(actual, expected)) < 0.999995f || MathF.Abs(actual.Length() - 1) > 1e-3f) throw new Exception($"Pose check failed: {what}: {actual} != {expected}");
        }
        Near(decoded[0].Translation, new(1.5f, -2.25f, 0.125f), "fixed translation"); Turned(decoded[0].Rotation, first, "fixed rotation"); Near(decoded[0].Scale, Vector3.One, "rest scale");
        Near(decoded[1].Translation, new(0.75f, 3, 0), "curved translation starts at its first control point");
        Turned(decoded[1].Rotation, second, "curved rotation starts at its first control point"); Near(decoded[1].Scale, new(1.75f, 1, 1), "partly fixed scale");
        if (decoded[2] != new SourcePoseReader.Placement(Vector3.Zero, Quaternion.Identity, Vector3.One)) throw new Exception("An empty track is not at rest");
        // The decoder must consume exactly the authored bytes, or a later track would start in the wrong place.
        var shifted = block.ToArray(); shifted[4] = 2 << 2;   // claim the six-byte packing for a five-byte value
        if (SourcePoseReader.DecodeFirstFrame(shifted, 3, 1, transformOffset)[1].Translation == decoded[1].Translation) throw new Exception("Track sizes do not affect later tracks");
        Reject(() => SourcePoseReader.DecodeFirstFrame(block.ToArray(), 3, 1, transformOffset + 4));
        var polar = block.ToArray(); polar[0] = 0;
        Reject(() => SourcePoseReader.DecodeFirstFrame(polar, 3, 1, transformOffset));
        // A placement scales, then turns, then moves.
        var matrix = new SourcePoseReader.Placement(new(1, 2, 3), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2), new(2, 1, 1)).Matrix;
        Near(Vector3.Transform(new Vector3(1, 0, 0), matrix), new(1, 2, 1), "placement order");
        Console.WriteLine("PASS: first-frame poses decode fixed, curved and absent tracks in both rotation packings; unsupported layouts fail");
    }

    /// <summary>Against the source install: a character's first idle frame keeps its skeleton's bone lengths and unit rotations.</summary>
    internal static void Installs()
    {
        string source = Environment.GetEnvironmentVariable("AOG_DARK_SOULS_REMASTERED") ?? throw new Exception("Set AOG_DARK_SOULS_REMASTERED");
        foreach (string character in new[] { "c2750", "c2540", "c2900" })
        {
            var binder = SoulsFormats.BND3.Read(Path.Combine(source, "chr", character + ".anibnd.dcx"));
            byte[] File(string leaf) => binder.Files.Single(f => f.Name.EndsWith("\\" + leaf, StringComparison.OrdinalIgnoreCase)).Bytes;
            var skeleton = SourcePoseReader.ReadSkeleton(File("Skeleton.hkx"));
            string idle = binder.Files.Select(f => f.Name.Split('\\')[^1]).Where(n => n.StartsWith('a') && n.EndsWith(".hkx", StringComparison.Ordinal)).Order(StringComparer.Ordinal).First();
            var pose = SourcePoseReader.ReadFirstFrame(File(idle), skeleton);
            int kept = 0, moved = 0;
            for (int i = 0; i < skeleton.Length; i++)
            {
                if (MathF.Abs(pose[i].Rotation.Length() - 1) > 2e-3f) throw new Exception($"{character} {skeleton[i].Name}: rotation is not unit length");
                // Limbs do not stretch: a bone keeps its distance from its parent unless the animation deliberately slides it.
                float rest = skeleton[i].Rest.Translation.Length(), now = pose[i].Translation.Length();
                if (MathF.Abs(rest - now) <= 0.01f * MathF.Max(0.05f, rest)) kept++; else moved++;
            }
            if (kept < skeleton.Length * 0.85) throw new Exception($"{character}: only {kept} of {skeleton.Length} bones keep their length in {idle}");
            Console.WriteLine($"{character}: {skeleton.Length} bones, {idle}, {kept} keep their length, {moved} slide");
        }
        Console.WriteLine("PASS: source skeletons and first idle frames read from the install with bone lengths kept");
    }

    // Three 12-bit components (each (value / sqrt(1/2)) scaled to ±2047 about 2047), two bits for the largest's place, one for its sign.
    private static byte[] Pack40(Quaternion q)
    {
        var (small, largest, negative) = Split(q);
        ulong bits = 0;
        for (int i = 0; i < 3; i++) bits |= (ulong)(uint)(MathF.Round(small[i] / MathF.Sqrt(0.5f) * 2047) + 2047) << 12 * i;
        bits |= (ulong)largest << 36; if (negative) bits |= 1UL << 38;
        return Enumerable.Range(0, 5).Select(i => (byte)(bits >> 8 * i)).ToArray();
    }

    // Three 15-bit components; the top bits of the first two words hold the largest's place, the third's holds its sign.
    private static byte[] Pack48(Quaternion q)
    {
        var (small, largest, negative) = Split(q);
        var words = small.Select(v => (ushort)(MathF.Round(v / MathF.Sqrt(0.5f) * 16383) + 16383)).ToArray();
        if ((largest & 1) != 0) words[0] |= 0x8000;
        if ((largest & 2) != 0) words[1] |= 0x8000;
        if (negative) words[2] |= 0x8000;
        return words.SelectMany(w => new[] { (byte)w, (byte)(w >> 8) }).ToArray();
    }

    private static (float[] Small, int Largest, bool Negative) Split(Quaternion q)
    {
        float[] all = [q.X, q.Y, q.Z, q.W];
        int largest = Array.IndexOf(all, all.MaxBy(MathF.Abs));
        return (all.Where((_, i) => i != largest).ToArray(), largest, all[largest] < 0);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Expected pose rejection");
    }
}
