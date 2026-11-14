using System.Numerics;
using AgeOfGold.Collision;
using HKLib.hk2018;
using Value = AgeOfGold.Collision.SourceTagfile.Value;

namespace AgeOfGold.Animation;

/// <summary>
/// Writes a source character's skeleton and animations as host files. The two games hold the same things under the same
/// names; the compressed track data is laid out alike in both, so it is carried over byte for byte.
/// </summary>
internal static class HostAnimationWriter
{
    /// <summary>A source skeleton as the host's object, under another name if one is given.</summary>
    internal static hkaSkeleton Skeleton(Value skeleton, string? name = null)
    {
        foreach (string empty in new[] { "referenceFloats", "floatSlots", "localFrames", "partitions" })
            if (skeleton.Field(empty).Elements().Length != 0) throw new InvalidDataException("Unsupported source skeleton: it has " + empty);
        var result = new hkaSkeleton
        {
            m_name = name ?? skeleton.Field("name").Text(),
            m_parentIndices = skeleton.Field("parentIndices").Elements().Select(v => checked((short)v.Integer())).ToList(),
            m_bones = skeleton.Field("bones").Elements().Select(b => new hkaBone { m_name = b.Field("name").Text(), m_lockTranslation = b.Field("lockTranslation").Integer() != 0 }).ToList(),
            m_referencePose = skeleton.Field("referencePose").Elements().Select(Placement).ToList(),
        };
        if (result.m_bones.Count == 0 || result.m_parentIndices.Count != result.m_bones.Count || result.m_referencePose.Count != result.m_bones.Count)
            throw new InvalidDataException("Source skeleton tables disagree");
        return result;
    }

    /// <summary>A placement as both games store one: where, turned how, and scaled how.</summary>
    internal static hkQsTransform Placement(Value placement)
    {
        float[] t = Four(placement.Field("translation")), r = Four(placement.Field("rotation")), s = Four(placement.Field("scale"));
        return new() { m_translation = new(t[0], t[1], t[2], t[3]), m_rotation = new(r[0], r[1], r[2], r[3]), m_scale = new(s[0], s[1], s[2], s[3]) };
    }

    private static float[] Four(Value value) => value.Floats() is { Length: 4 } four ? four : throw new InvalidDataException("Expected four numbers");

    /// <summary>The source's skeleton file as a host skeleton file whose skeleton carries the host character's name.</summary>
    internal static byte[] WriteSkeleton(byte[] sourceFile, string name, HostTagfile types)
    {
        var container = Container(sourceFile);
        var skeletons = container.Field("skeletons").Elements();
        if (skeletons.Length != 1 || container.Field("animations").Elements().Length != 0) throw new InvalidDataException("Expected a source file holding one skeleton");
        var written = new hkaAnimationContainer();
        written.m_skeletons.Add(Skeleton(skeletons[0], name));
        return types.Write(Root(written));
    }

    /// <summary>A source animation file as a host animation file bound to the named skeleton.</summary>
    internal static byte[] WriteAnimation(byte[] sourceFile, string skeletonName, HostTagfile types)
    {
        var container = Container(sourceFile);
        var animations = container.Field("animations").Elements(); var bindings = container.Field("bindings").Elements();
        if (animations.Length != 1 || bindings.Length != 1 || container.Field("skeletons").Elements().Length != 0) throw new InvalidDataException("Expected a source file holding one animation and its binding");
        var from = animations[0];
        if (from.TypeName != "hkaSplineCompressedAnimation") throw new InvalidDataException("Unsupported source animation compression: " + from.TypeName);
        // The host's own animations carry no annotation tracks; the source's only repeat each track's bone name.
        if (from.Field("annotationTracks").Elements().Any(t => t.Field("annotations").Elements().Length != 0)) throw new InvalidDataException("Unsupported source animation: it carries annotations");
        int Whole(string field) => checked((int)from.Field(field).Integer());
        float Real(Value value) => value.Floats() is { Length: 1 } one ? one[0] : throw new InvalidDataException("Expected one number");
        List<uint> Offsets(string field) => from.Field(field).Elements().Select(v => checked((uint)v.Integer())).ToList();
        var animation = new hkaSplineCompressedAnimation
        {
            m_type = hkaAnimation.AnimationType.HK_SPLINE_COMPRESSED_ANIMATION,
            m_duration = Real(from.Field("duration")), m_numberOfTransformTracks = Whole("numberOfTransformTracks"), m_numberOfFloatTracks = Whole("numberOfFloatTracks"),
            m_numFrames = Whole("numFrames"), m_numBlocks = Whole("numBlocks"), m_maxFramesPerBlock = Whole("maxFramesPerBlock"), m_maskAndQuantizationSize = Whole("maskAndQuantizationSize"),
            m_blockDuration = Real(from.Field("blockDuration")), m_blockInverseDuration = Real(from.Field("blockInverseDuration")), m_frameDuration = Real(from.Field("frameDuration")),
            m_blockOffsets = Offsets("blockOffsets"), m_floatBlockOffsets = Offsets("floatBlockOffsets"), m_transformOffsets = Offsets("transformOffsets"), m_floatOffsets = Offsets("floatOffsets"),
            m_data = from.Field("data").Bytes().ToList(), m_endian = Whole("endian"),
        };
        if (Whole("type") != (int)animation.m_type || animation.m_endian != 0 || animation.m_numberOfTransformTracks <= 0 || animation.m_numberOfFloatTracks != 0 || animation.m_data.Count == 0)
            throw new InvalidDataException("Unsupported source animation layout");
        var motion = from.Field("extractedMotion");
        if (!motion.IsNull)
        {
            if (motion.TypeName != "hkaDefaultAnimatedReferenceFrame") throw new InvalidDataException("Unsupported source root motion: " + motion.TypeName);
            Vector4 Vector(Value value) { var f = Four(value); return new(f[0], f[1], f[2], f[3]); }
            animation.m_extractedMotion = new hkaDefaultAnimatedReferenceFrame
            {
                m_up = Vector(motion.Field("up")), m_forward = Vector(motion.Field("forward")), m_duration = Real(motion.Field("duration")),
                m_referenceFrameSamples = motion.Field("referenceFrameSamples").Elements().Select(Vector).ToList(),
            };
        }
        var bound = bindings[0];
        if (bound.Field("floatTrackToFloatSlotIndices").Elements().Length != 0 || bound.Field("partitionIndices").Elements().Length != 0) throw new InvalidDataException("Unsupported source animation binding");
        var tracks = bound.Field("transformTrackToBoneIndices").Elements().Select(v => checked((short)v.Integer())).ToList();
        // A source binding without a track table drives the bones in order; the host is told so outright.
        if (tracks.Count == 0) tracks = Enumerable.Range(0, animation.m_numberOfTransformTracks).Select(i => (short)i).ToList();
        if (tracks.Count != animation.m_numberOfTransformTracks) throw new InvalidDataException("Source animation tracks do not match its binding");
        var written = new hkaAnimationContainer();
        written.m_animations.Add(animation);
        written.m_bindings.Add(new hkaAnimationBinding
        {
            m_originalSkeletonName = skeletonName, m_animation = animation, m_transformTrackToBoneIndices = tracks,
            m_blendHint = (hkaAnimationBinding.BlendHint)bound.Field("blendHint").Integer(),
        });
        return types.Write(Root(written));
    }

    private static Value Container(byte[] sourceFile)
    {
        var root = new SourceTagfile(sourceFile).Root;
        var variants = root.Field("namedVariants").Elements();
        if (root.TypeName != "hkRootLevelContainer" || variants.Length != 1 || variants[0].Field("variant").TypeName != "hkaAnimationContainer")
            throw new InvalidDataException("Expected one source animation container");
        return variants[0].Field("variant");
    }

    private static hkRootLevelContainer Root(hkaAnimationContainer container)
    {
        var root = new hkRootLevelContainer();
        root.m_namedVariants.Add(new() { m_name = "Merged Animation Container", m_className = "hkaAnimationContainer", m_variant = container });
        return root;
    }
}
