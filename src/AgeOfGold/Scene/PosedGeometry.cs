using System.Numerics;
using AgeOfGold.Animation;
using SoulsFormats;

namespace AgeOfGold.Scene;

/// <summary>Moves a skinned source model's vertices from the pose it was modelled in to a pose of its skeleton.</summary>
internal static class PosedGeometry
{
    /// <summary>For each model node, the transform from its modelled placement to its posed one.</summary>
    internal static Matrix4x4[] Skin(FLVER2 model, SourcePoseReader.Bone[] skeleton, SourcePoseReader.Placement[] pose)
    {
        if (pose.Length != skeleton.Length) throw new InvalidDataException("Pose does not match its skeleton");
        var posedBones = new Matrix4x4[skeleton.Length];
        for (int i = 0; i < skeleton.Length; i++)
            posedBones[i] = skeleton[i].Parent < 0 ? pose[i].Matrix : pose[i].Matrix * posedBones[skeleton[i].Parent];
        var bones = skeleton.Select((bone, index) => (bone.Name, index)).ToDictionary(p => p.Name, p => p.index, StringComparer.Ordinal);
        var modelled = new Matrix4x4?[model.Nodes.Count]; var posed = new Matrix4x4?[model.Nodes.Count];
        Matrix4x4 Modelled(int node, int depth = 0)
        {
            if (node < 0) return Matrix4x4.Identity;
            if (node >= model.Nodes.Count || depth > model.Nodes.Count) throw new InvalidDataException("Model node hierarchy is invalid or cyclic");
            return modelled[node] ??= model.Nodes[node].ComputeLocalTransform() * Modelled(model.Nodes[node].ParentIndex, depth + 1);
        }
        // A node the skeleton does not animate keeps its own placement and follows its parent.
        Matrix4x4 Posed(int node, int depth = 0)
        {
            if (node < 0) return Matrix4x4.Identity;
            if (node >= model.Nodes.Count || depth > model.Nodes.Count) throw new InvalidDataException("Model node hierarchy is invalid or cyclic");
            return posed[node] ??= bones.TryGetValue(model.Nodes[node].Name, out int bone) ? posedBones[bone]
                : model.Nodes[node].ComputeLocalTransform() * Posed(model.Nodes[node].ParentIndex, depth + 1);
        }
        return Enumerable.Range(0, model.Nodes.Count).Select(node =>
            Matrix4x4.Invert(Modelled(node), out var inverse) ? inverse * Posed(node) : throw new InvalidDataException("Model node placement is singular")).ToArray();
    }

    /// <summary>The weighted blend of the node transforms a vertex follows.</summary>
    internal static Matrix4x4 Blend(Matrix4x4[] skin, FLVER2.Mesh mesh, FLVER.Vertex vertex)
    {
        int Node(int slot) => slot >= 0 && slot < mesh.BoneIndices.Count && mesh.BoneIndices[slot] >= 0 && mesh.BoneIndices[slot] < skin.Length
            ? mesh.BoneIndices[slot] : throw new InvalidDataException("Skinned vertex references an absent node");
        // A mesh without weights binds each vertex wholly to one node.
        if (!mesh.UseBoneWeights) return skin[Node(vertex.NormalW)];
        var blend = new Matrix4x4(); float total = 0;
        for (int i = 0; i < 4; i++)
        {
            float weight = vertex.BoneWeights[i];
            if (weight == 0) continue;
            blend += skin[Node(vertex.BoneIndices[i])] * weight; total += weight;
        }
        if (MathF.Abs(total - 1) > 0.002f) throw new InvalidDataException("Skin weights do not sum to one");
        return blend;
    }
}
