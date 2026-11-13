using System.Buffers.Binary;
using System.Numerics;
using AgeOfGold.Collision;

namespace AgeOfGold.Animation;

/// <summary>Reads a source character's skeleton and the pose at the first frame of one of its animations.</summary>
internal static class SourcePoseReader
{
    /// <summary>A bone's placement relative to its parent: scale, then rotation, then translation.</summary>
    internal readonly record struct Placement(Vector3 Translation, Quaternion Rotation, Vector3 Scale)
    {
        internal Matrix4x4 Matrix => Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Translation);
    }

    internal sealed record Bone(string Name, int Parent, Placement Rest);

    internal static Bone[] ReadSkeleton(byte[] file)
    {
        var skeletons = Container(file).Field("skeletons").Elements();
        if (skeletons.Length != 1) throw new InvalidDataException("Expected one source skeleton");
        var bones = skeletons[0].Field("bones").Elements();
        var parents = skeletons[0].Field("parentIndices").Elements();
        var rest = skeletons[0].Field("referencePose").Elements();
        if (bones.Length == 0 || parents.Length != bones.Length || rest.Length != bones.Length) throw new InvalidDataException("Source skeleton tables disagree");
        var result = new Bone[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            int parent = checked((int)parents[i].Integer());
            // Parents come first, so a pose can be composed in one pass.
            if (parent < -1 || parent >= i) throw new InvalidDataException("Source skeleton is not ordered parent first");
            var values = new[] { "translation", "rotation", "scale" }.Select(f => rest[i].Field(f).Floats()).ToArray();
            if (values.Any(v => v.Length != 4)) throw new InvalidDataException("Unsupported source bone placement");
            result[i] = new(bones[i].Field("name").Text(), parent, new(new(values[0][0], values[0][1], values[0][2]),
                new(values[1][0], values[1][1], values[1][2], values[1][3]), new(values[2][0], values[2][1], values[2][2])));
        }
        if (result.Select(b => b.Name).Distinct(StringComparer.Ordinal).Count() != result.Length) throw new InvalidDataException("Source skeleton repeats a bone name");
        return result;
    }

    /// <summary>Each bone's placement at the animation's first frame; bones the animation does not move keep their rest placement.</summary>
    internal static Placement[] ReadFirstFrame(byte[] file, Bone[] skeleton)
    {
        var container = Container(file);
        var animations = container.Field("animations").Elements(); var bindings = container.Field("bindings").Elements();
        if (animations.Length != 1 || bindings.Length != 1) throw new InvalidDataException("Expected one source animation and binding");
        var animation = animations[0];
        if (animation.TypeName != "hkaSplineCompressedAnimation") throw new InvalidDataException("Unsupported source animation compression: " + animation.TypeName);
        int tracks = checked((int)animation.Field("numberOfTransformTracks").Integer()), floats = checked((int)animation.Field("numberOfFloatTracks").Integer());
        var blocks = animation.Field("blockOffsets").Elements();
        if (blocks.Length == 0 || animation.Field("endian").Integer() != 0) throw new InvalidDataException("Unsupported source animation block layout");
        var data = animation.Field("data").Bytes();
        int block = checked((int)blocks[0].Integer());
        // The tracks' values follow the masks, whose size the animation states.
        var decoded = DecodeFirstFrame(data.AsSpan(block), tracks, floats, checked((int)animation.Field("maskAndQuantizationSize").Integer()));
        var targets = bindings[0].Field("transformTrackToBoneIndices").Elements().Select(v => checked((int)v.Integer())).ToArray();
        // An animation without a track table drives the bones in order.
        if (targets.Length == 0) targets = Enumerable.Range(0, tracks).ToArray();
        if (targets.Length != tracks || targets.Any(t => t < 0 || t >= skeleton.Length) || targets.Distinct().Count() != tracks)
            throw new InvalidDataException("Source animation tracks do not map onto its skeleton");
        var pose = skeleton.Select(b => b.Rest).ToArray();
        for (int track = 0; track < tracks; track++) pose[targets[track]] = decoded[track];
        return pose;
    }

    private static SourceTagfile.Value Container(byte[] file)
    {
        var root = new SourceTagfile(file).Root;
        var variants = root.Field("namedVariants").Elements();
        if (root.TypeName != "hkRootLevelContainer" || variants.Length != 1 || variants[0].Field("variant").TypeName != "hkaAnimationContainer")
            throw new InvalidDataException("Expected one source animation container");
        return variants[0].Field("variant");
    }

    /// <summary>
    /// Decodes the first block of spline-compressed transform tracks at its first key. A track's translation, rotation and
    /// scale are each absent (rest value), one fixed value, or a clamped curve whose first control point is its first key.
    /// </summary>
    /// <param name="transformOffset">Where the tracks' values begin, after the per-track masks.</param>
    internal static Placement[] DecodeFirstFrame(ReadOnlySpan<byte> block, int tracks, int floatTracks, int transformOffset)
    {
        if (tracks < 0 || floatTracks < 0 || transformOffset != (tracks * 4 + floatTracks + 3 & ~3) || transformOffset > block.Length)
            throw new InvalidDataException("Unsupported source animation mask layout");
        var result = new Placement[tracks];
        int at = transformOffset;
        static void Align(ref int position, int to) => position = position + to - 1 & -to;
        for (int track = 0; track < tracks; track++)
        {
            // Four mask bytes: packed quantization kinds, then which translation, rotation and scale parts are fixed or curved.
            byte kinds = block[track * 4], translation = block[track * 4 + 1], rotation = block[track * 4 + 2], scale = block[track * 4 + 3];
            var position = Vector(block, ref at, translation, kinds & 3, 0);
            Align(ref at, 4);
            var turn = Quaternion.Identity;
            int packing = kinds >> 2 & 15;
            if ((rotation & 0xF0) != 0)
            {
                Curve(block, ref at, out int points);
                Align(ref at, RotationAlignment(packing));
                turn = Rotation(block, ref at, packing, out int size);
                // Only the first control point is needed; the rest are stepped over.
                at += size * (points - 1);
            }
            else if ((rotation & 0x0F) != 0)
            {
                Align(ref at, RotationAlignment(packing));
                turn = Rotation(block, ref at, packing, out _);
            }
            Align(ref at, 4);
            var size3 = Vector(block, ref at, scale, kinds >> 6 & 3, 1);
            Align(ref at, 4);
            result[track] = new(position, turn, size3);
        }
        return result;
    }

    // A curve header: the count of control points less one, the degree, then one knot byte per point and per degree step.
    private static void Curve(ReadOnlySpan<byte> block, ref int at, out int points)
    {
        int last = BinaryPrimitives.ReadUInt16LittleEndian(block[at..]); int degree = block[at + 2];
        if (degree is < 1 or > 3) throw new InvalidDataException("Unsupported source animation curve degree");
        at += 3 + last + degree + 2;
        points = last + 1;
    }

    private static Vector3 Vector(ReadOnlySpan<byte> block, ref int at, byte parts, int quantization, float rest)
    {
        if ((parts & 0x88) != 0 || quantization > 1) throw new InvalidDataException("Unsupported source animation vector track");
        bool curved = (parts & 0x70) != 0;
        int points = 1;
        if (curved) { Curve(block, ref at, out points); at = at + 3 & ~3; }
        var low = new float[3]; var high = new float[3];
        for (int axis = 0; axis < 3; axis++)
        {
            if ((parts & 0x10 << axis) != 0) { low[axis] = Single(block, ref at); high[axis] = Single(block, ref at); }
            else low[axis] = high[axis] = (parts & 1 << axis) != 0 ? Single(block, ref at) : rest;
        }
        var value = new float[3];
        int width = quantization == 0 ? 1 : 2, curvedAxes = Enumerable.Range(0, 3).Count(axis => (parts & 0x10 << axis) != 0);
        for (int axis = 0; axis < 3; axis++)
        {
            if ((parts & 0x10 << axis) == 0) { value[axis] = low[axis]; continue; }
            // The first control point of each curved axis, as a fraction between the axis's bounds.
            float fraction = width == 1 ? block[at] / 255f : BinaryPrimitives.ReadUInt16LittleEndian(block[at..]) / 65535f;
            at += width;
            value[axis] = low[axis] + (high[axis] - low[axis]) * fraction;
        }
        at += (points - 1) * curvedAxes * width;
        return new(value[0], value[1], value[2]);
    }

    private static float Single(ReadOnlySpan<byte> block, ref int at)
    {
        float value = BinaryPrimitives.ReadSingleLittleEndian(block[at..]);
        at += 4;
        return float.IsFinite(value) ? value : throw new InvalidDataException("Non-finite source animation value");
    }

    // Rotation packings by kind: 1 is three 12-bit components in five bytes, 2 is three 15-bit components in six bytes.
    // lazy: the two packings the Firelink characters use; the 32-bit polar, 24-bit, 16-bit and unpacked kinds stop the read.
    private static int RotationAlignment(int packing) => packing switch { 1 => 1, 2 => 2, _ => throw new InvalidDataException("Unsupported source animation rotation packing " + packing) };

    private static Quaternion Rotation(ReadOnlySpan<byte> block, ref int at, int packing, out int size)
    {
        // Both packings store the three smaller components and rebuild the largest from the unit length.
        Span<float> small = stackalloc float[3];
        int largest; bool negative;
        if (packing == 1)
        {
            ulong bits = 0;
            for (int i = 0; i < 5; i++) bits |= (ulong)block[at + i] << 8 * i;
            for (int i = 0; i < 3; i++) small[i] = ((int)(bits >> 12 * i & 4095) - 2047) * (MathF.Sqrt(0.5f) / 2047);
            largest = (int)(bits >> 36 & 3); negative = (bits >> 38 & 1) != 0;
            size = 5;
        }
        else
        {
            Span<ushort> words = stackalloc ushort[3];
            for (int i = 0; i < 3; i++) words[i] = BinaryPrimitives.ReadUInt16LittleEndian(block[(at + 2 * i)..]);
            for (int i = 0; i < 3; i++) small[i] = ((words[i] & 0x7FFF) - 16383) * (MathF.Sqrt(0.5f) / 16383);
            largest = words[1] >> 14 & 2 | words[0] >> 15 & 1; negative = words[2] >> 15 != 0;
            size = 6;
        }
        at += size;
        float rebuilt = MathF.Sqrt(MathF.Max(0, 1 - small[0] * small[0] - small[1] * small[1] - small[2] * small[2]));
        if (negative) rebuilt = -rebuilt;
        Span<float> all = stackalloc float[4];
        for (int i = 0, next = 0; i < 4; i++) all[i] = i == largest ? rebuilt : small[next++];
        return new(all[0], all[1], all[2], all[3]);
    }
}
