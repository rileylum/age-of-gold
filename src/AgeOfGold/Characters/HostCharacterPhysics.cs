using System.Collections;
using System.Numerics;
using System.Reflection;
using AgeOfGold.Animation;
using AgeOfGold.Collision;
using HKLib.hk2018;
using Value = AgeOfGold.Collision.SourceTagfile.Value;

namespace AgeOfGold.Characters;

/// <summary>
/// Writes a source character's physics file — the bodies that take hits and fall when it dies — as the host's. The
/// source keeps the older kind of rigid body and the host the newer; skeletons, the mappers between them and the joints'
/// limits are the same in both.
/// </summary>
internal static class HostCharacterPhysics
{
    /// <param name="Kept">Joint values the source states in a form this reader cannot decode, left as the host exemplar's.</param>
    internal sealed record Written(byte[] File, int Bodies, int Joints, int RagdollBones, int AnimationBones, string[] Kept);

    /// <summary>
    /// A capsule as the host stores one: a thin box about its axis, a hundredth of the radius thick, which the host
    /// rounds out by the rest of the radius. Corners are numbered by end (odd at <paramref name="a"/>) and by side.
    /// </summary>
    /// <returns>Eight corners and six planes (a normal and its offset), in the host's order.</returns>
    internal static (Vector4[] Corners, Vector4[] Planes) CapsuleBox(Vector3 a, Vector3 b, float radius)
    {
        if (!(radius > 0) || !float.IsFinite(radius)) throw new InvalidDataException("A capsule needs a radius");
        var along = b - a; along = along.Length() < 1e-6f ? Vector3.UnitX : Vector3.Normalize(along);
        // Any direction across the axis will do; the world axis furthest from it gives a steady one.
        var least = MathF.Abs(along.X) <= MathF.Abs(along.Y) && MathF.Abs(along.X) <= MathF.Abs(along.Z) ? Vector3.UnitX : MathF.Abs(along.Y) <= MathF.Abs(along.Z) ? Vector3.UnitY : Vector3.UnitZ;
        var side = Vector3.Normalize(least - Vector3.Dot(least, along) * along); var other = Vector3.Cross(along, side);
        float thin = radius * 0.01f;
        var corners = new Vector4[8];
        for (int i = 0; i < 8; i++)
        {
            var corner = ((i & 1) != 0 ? a - along * thin : b + along * thin) + ((i & 2) != 0 ? -side : side) * thin + ((i & 4) != 0 ? -other : other) * thin;
            // A corner's fourth number is one half whose lowest bits carry the corner's own number.
            corners[i] = new(corner, BitConverter.Int32BitsToSingle(0x3f000000 | i));
        }
        static Vector4 Plane(Vector3 normal, Vector3 on) => new(normal, -Vector3.Dot(normal, on));
        return (corners, [Plane(along, b + along * thin), Plane(-along, a - along * thin), Plane(side, a + side * thin), Plane(-side, a - side * thin), Plane(other, a + other * thin), Plane(-other, a - other * thin)]);
    }

    /// <param name="exemplar">A host physics file that uses every kind of object the source's needs.</param>
    internal static Written Write(byte[] sourceFile, byte[] exemplar)
    {
        var types = HostTagfile.Read(exemplar, out var root);
        var variants = new SourceTagfile(sourceFile).Root.Field("namedVariants").Elements().Select(v => v.Field("variant")).ToArray();
        Value One(string type) => variants.Count(v => v.TypeName == type) == 1 ? variants.Single(v => v.TypeName == type) : throw new InvalidDataException("Expected one " + type + " in a source physics file");
        var instance = One("hkaRagdollInstance");
        var sourceMappers = variants.Where(v => v.TypeName == "hkaSkeletonMapper").ToArray();
        var skeletons = One("hkaAnimationContainer").Field("skeletons").Elements().Select(s => HostAnimationWriter.Skeleton(s)).ToList();
        string ragdollName = instance.Field("skeleton").Field("name").Text();
        if (skeletons.Count != 2 || skeletons.Count(s => s.m_name == ragdollName) != 1) throw new InvalidDataException("Expected the animation skeleton and the ragdoll's in a source physics file");
        var ragdollSkeleton = skeletons.Single(s => s.m_name == ragdollName); var animationSkeleton = skeletons.Single(s => s != ragdollSkeleton);

        var hostContainer = root.m_namedVariants.Select(v => v.m_variant).OfType<hkaAnimationContainer>().Single();
        var data = root.m_namedVariants.Select(v => v.m_variant).OfType<hknpRagdollData>().Distinct().Single();
        var hostMappers = root.m_namedVariants.Select(v => v.m_variant).OfType<hkaSkeletonMapper>().ToArray();
        if (hostContainer.m_skeletons.Count != 2 || hostMappers.Length != 2 || sourceMappers.Length != 2 || data.m_bodyCinfos.Count == 0 || data.m_bodyCinfos[0].m_shape is not hknpCapsuleShape)
            throw new InvalidDataException("Unsupported host physics exemplar");
        // The host lists the two skeletons in the order of its own file.
        var hostRagdoll = data.m_skeleton;
        hostContainer.m_skeletons = hostContainer.m_skeletons[0] == hostRagdoll ? [ragdollSkeleton, animationSkeleton] : [animationSkeleton, ragdollSkeleton];

        var bodies = instance.Field("rigidBodies").Elements();
        var bodyExemplar = data.m_bodyCinfos[0]; var material = data.m_materials[0]; var motion = data.m_motionProperties[0];
        var jointExemplars = data.m_constraintCinfos.GroupBy(c => c.m_constraintData!.GetType().Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var names = bodies.Select(b => b.Field("name").Text()).ToList();
        if (names.Count == 0 || names.Distinct(StringComparer.Ordinal).Count() != names.Count) throw new InvalidDataException("Source ragdoll bodies need distinct names");
        data.m_bodyCinfos = []; data.m_materials = []; data.m_motionProperties = [];
        for (int i = 0; i < bodies.Length; i++)
        {
            // lazy: each body is a clone of one host capsule body; its mass spread and air drag stay the exemplar's, which only
            // shape how it falls. Give each its own when a fall looks wrong. Source bodies of another shape need those shapes built.
            var body = (hknpPhysicsSystemData.bodyCinfoWithAttachment)Clone(bodyExemplar);
            var shape = bodies[i].Field("collidable").Field("shape");
            if (shape.TypeName != "hkpCapsuleShape") throw new InvalidDataException("Unsupported source body shape: " + shape.TypeName);
            float[] a = shape.Field("vertexA").Floats(), b = shape.Field("vertexB").Floats(); float radius = shape.Field("radius").Floats()[0];
            var capsule = (hknpCapsuleShape)body.m_shape!;
            var (corners, planes) = CapsuleBox(new(a[0], a[1], a[2]), new(b[0], b[1], b[2]), radius);
            if (capsule.m_vertices.Count != corners.Length || capsule.m_planes.Count != planes.Length) throw new InvalidDataException("Host capsule exemplar is not a box of eight corners");
            capsule.m_a = new(a[0], a[1], a[2], radius); capsule.m_b = new(b[0], b[1], b[2], 0);
            // The box is rounded by all but its own thickness; the host lets bodies sink a third of the radius into each other.
            capsule.m_convexRadius = radius * 0.99f; capsule.m_maxAllowedPenetration = radius * 0.33f;
            capsule.m_vertices = corners.ToList(); capsule.m_planes = planes.ToList();
            var state = bodies[i].Field("motion");
            var placed = state.Field("motionState").Field("transform").Floats();
            if (placed.Length != 16) throw new InvalidDataException("Unsupported source body placement");
            // Three columns of the body's turn, then where it is.
            body.m_orientation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new(placed[0], placed[1], placed[2], 0, placed[4], placed[5], placed[6], 0, placed[8], placed[9], placed[10], 0, 0, 0, 0, 1)));
            body.m_position = new(placed[12], placed[13], placed[14], 1);
            float inverseMass = state.Field("inertiaAndMassInv").Floats()[3];
            if (!(inverseMass > 0)) throw new InvalidDataException("A source ragdoll body has no mass");
            body.m_mass = 1 / inverseMass; body.m_name = names[i];
            uint filter = checked((uint)bodies[i].Field("collidable").Field("broadPhaseHandle").Field("collisionFilterInfo").Integer());
            // The source's layer and the neighbours a body ignores keep their bits; above them the host marks a ragdoll body.
            body.m_collisionFilterInfo = filter & 0x7fff | 0x8000;
            body.m_materialId = (ushort)i; body.m_motionPropertiesId = (ushort)i;
            data.m_bodyCinfos.Add(body); data.m_materials.Add((hknpMaterial)Clone(material)); data.m_motionProperties.Add((hknpMotionProperties)Clone(motion));
        }
        var kept = new SortedSet<string>(StringComparer.Ordinal);
        data.m_constraintCinfos = instance.Field("constraints").Elements().Select(from =>
        {
            var kind = from.Field("data");
            if (!jointExemplars.TryGetValue(kind.TypeName, out var jointExemplar)) throw new InvalidDataException("The host exemplar has no joint like the source's " + kind.TypeName);
            var joint = (hknpConstraintCinfo)Clone(jointExemplar);
            Overlay(joint.m_constraintData!, kind, kind.TypeName, kept);
            var ends = from.Field("entities").Elements().Select(e => names.IndexOf(e.Field("name").Text())).ToArray();
            if (ends.Length != 2 || ends.Any(e => e < 0) || ends[0] == ends[1]) throw new InvalidDataException("A source joint does not join two of the ragdoll's bodies");
            joint.m_bodyA = new() { m_serialAndIndex = (uint)ends[0] }; joint.m_bodyB = new() { m_serialAndIndex = (uint)ends[1] };
            joint.m_name = from.Field("name").Text();
            return joint;
        }).ToList();
        data.m_skeleton = ragdollSkeleton;
        data.m_boneToBodyMap = instance.Field("boneToRigidBodyMap").Elements().Select(v => checked((int)v.Integer())).ToList();
        if (data.m_boneToBodyMap.Count != ragdollSkeleton.m_bones.Count || data.m_boneToBodyMap.Any(body => body < -1 || body >= names.Count)) throw new InvalidDataException("Source ragdoll bones do not map onto its bodies");

        hkaSkeleton Named(Value skeleton) => skeleton.Field("name").Text() == ragdollName ? ragdollSkeleton : animationSkeleton;
        foreach (var host in hostMappers)
        {
            // Each host mapper is filled from the source's that maps the same way round.
            bool fromRagdoll = host.m_mapping.m_skeletonA == hostRagdoll;
            var from = sourceMappers.Single(m => (m.Field("mapping").Field("skeletonA").Field("name").Text() == ragdollName) == fromRagdoll).Field("mapping");
            foreach (string empty in new[] { "partitionMap", "simpleMappingPartitionRanges", "chainMappingPartitionRanges" })
                if (from.Field(empty).Elements().Length != 0) throw new InvalidDataException("Unsupported source skeleton mapper: it has " + empty);
            short Bone(Value value) => checked((short)value.Integer());
            var to = host.m_mapping;
            to.m_skeletonA = Named(from.Field("skeletonA")); to.m_skeletonB = Named(from.Field("skeletonB"));
            if (to.m_skeletonA == to.m_skeletonB) throw new InvalidDataException("A source skeleton mapper maps a skeleton onto itself");
            to.m_simpleMappings = from.Field("simpleMappings").Elements().Select(m => new hkaSkeletonMapperData.SimpleMapping
                { m_boneA = Bone(m.Field("boneA")), m_boneB = Bone(m.Field("boneB")), m_aFromBTransform = HostAnimationWriter.Placement(m.Field("aFromBTransform")) }).ToList();
            to.m_chainMappings = from.Field("chainMappings").Elements().Select(m => new hkaSkeletonMapperData.ChainMapping
            {
                m_startBoneA = Bone(m.Field("startBoneA")), m_endBoneA = Bone(m.Field("endBoneA")), m_startBoneB = Bone(m.Field("startBoneB")), m_endBoneB = Bone(m.Field("endBoneB")),
                m_startAFromBTransform = HostAnimationWriter.Placement(m.Field("startAFromBTransform")), m_endAFromBTransform = HostAnimationWriter.Placement(m.Field("endAFromBTransform")),
            }).ToList();
            to.m_unmappedBones = from.Field("unmappedBones").Elements().Select(Bone).ToList();
            to.m_extractedMotionMapping = HostAnimationWriter.Placement(from.Field("extractedMotionMapping"));
            to.m_keepUnmappedLocal = from.Field("keepUnmappedLocal").Integer() != 0;
            to.m_partitionMap = []; to.m_simpleMappingPartitionRanges = []; to.m_chainMappingPartitionRanges = [];
        }
        return new(types.Write(root), data.m_bodyCinfos.Count, data.m_constraintCinfos.Count, ragdollSkeleton.m_bones.Count, animationSkeleton.m_bones.Count, kept.ToArray());
    }

    // What the source states, laid over a host object of the same class. A value the reader cannot decode (the two
    // releases pack a few small numbers differently) keeps the host exemplar's.
    private static void Overlay(object target, Value source, string path, SortedSet<string> kept)
    {
        var stated = source.FieldNames;
        foreach (var field in target.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            string name = field.Name.StartsWith("m_", StringComparison.Ordinal) ? field.Name[2..] : field.Name;
            if (!stated.Contains(name)) { kept.Add($"{path}.{name}"); continue; }
            var type = field.FieldType;
            try
            {
                var value = source.Field(name);
                if (type == typeof(float)) field.SetValue(target, value.Floats()[0]);
                else if (type == typeof(bool)) field.SetValue(target, value.Integer() != 0);
                else if (type.IsPrimitive) field.SetValue(target, Convert.ChangeType(value.Integer(), type));
                else if (type == typeof(Vector4)) { var f = value.Floats(); field.SetValue(target, new Vector4(f[0], f[1], f[2], f[3])); }
                else if (type == typeof(Matrix4x4)) { var f = value.Floats(); field.SetValue(target, new Matrix4x4(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11], f[12], f[13], f[14], f[15])); }
                else if (type == typeof(HKLib.ValueTypes.Matrix3x3))
                {
                    // Three columns of four numbers; the fourth of each is padding.
                    var f = value.Floats(); object matrix = field.GetValue(target)!; string[] members = ["M11", "M12", "M13", "M21", "M22", "M23", "M31", "M32", "M33"];
                    for (int i = 0; i < 9; i++) type.GetProperty(members[i])!.SetValue(matrix, f[i / 3 * 4 + i % 3]);
                    field.SetValue(target, matrix);
                }
                else if (typeof(IHavokObject).IsAssignableFrom(type) && field.GetValue(target) is { } inner && !value.IsNull) Overlay(inner, value, $"{path}.{name}", kept);
                else kept.Add($"{path}.{name}");
            }
            catch (InvalidDataException) { kept.Add($"{path}.{name}"); }
        }
    }

    // A copy of a host object and everything it holds.
    private static object Clone(object? value, Dictionary<object, object>? seen = null)
    {
        if (value is null) return null!;
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is string || type.IsValueType && type.Namespace is "System.Numerics" or "HKLib.ValueTypes") return value;
        seen ??= new(ReferenceEqualityComparer.Instance);
        if (seen.TryGetValue(value, out var done)) return done;
        if (value is Array array)
        {
            var copy = Array.CreateInstance(type.GetElementType()!, array.Length); seen[value] = copy;
            for (int i = 0; i < array.Length; i++) copy.SetValue(Clone(array.GetValue(i), seen), i);
            return copy;
        }
        if (value is IList list)
        {
            var copy = (IList)Activator.CreateInstance(type)!; seen[value] = copy;
            foreach (var item in list) copy.Add(Clone(item, seen));
            return copy;
        }
        var clone = Activator.CreateInstance(type)!; seen[value] = clone;
        for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
            foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                field.SetValue(clone, Clone(field.GetValue(value), seen));
        return clone;
    }
}
