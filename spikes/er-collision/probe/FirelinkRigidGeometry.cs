using System.Numerics;
using SoulsFormats;

// Static source scenery can still use a palette of rigid node transforms.
static class FirelinkRigidGeometry
{
    /// <summary>Bake a static vertex's palette-selected transform into its position and normal.</summary>
    public static (Vector3 Position, Vector3 Normal) Bake(FLVER2 model, FLVER2.Mesh mesh, FLVER.Vertex vertex)
    {
        if (mesh.UseBoneWeights || Enumerable.Range(0, 4).Any(i => vertex.BoneWeights[i] != 0))
            throw new InvalidDataException("Rigid scenery cannot contain weighted vertices");
        int slot = vertex.BoneIndices[0];
        if (slot < 0 || slot >= mesh.BoneIndices.Count)
            throw new InvalidDataException("Rigid vertex references an absent mesh palette slot");
        int node = mesh.BoneIndices[slot];
        var visited = new HashSet<int>();
        var transform = Matrix4x4.Identity;
        while (node != -1)
        {
            if (node < 0 || node >= model.Nodes.Count || !visited.Add(node))
                throw new InvalidDataException("Rigid node hierarchy is invalid or cyclic");
            transform *= model.Nodes[node].ComputeLocalTransform();
            node = model.Nodes[node].ParentIndex;
        }
        if (transform.GetDeterminant() < 0)
            throw new InvalidDataException("Mirrored rigid scenery requires a triangle-facing translation");
        if (!Matrix4x4.Invert(transform, out var inverse))
            throw new InvalidDataException("Rigid node transform is singular");
        var position = Vector3.Transform(vertex.Position, transform);
        var normal = Vector3.TransformNormal(vertex.Normal, Matrix4x4.Transpose(inverse));
        if (!Finite(position) || !Finite(normal)) throw new InvalidDataException("Rigid transform produced nonfinite geometry");
        return (position, normal.LengthSquared() == 0 ? normal : Vector3.Normalize(normal));
    }

    static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>Check palette selection, parent composition and nonuniform normal scaling with authored geometry.</summary>
    public static void Check()
    {
        var model = new FLVER2 { Nodes = [
            new FLVER.Node { Translation = new(0, 10, 0), Scale = Vector3.One, ParentIndex = -1 },
            new FLVER.Node { Translation = new(5, 0, 0), Rotation = new(0, MathF.PI / 2, 0),
                Scale = new(2, 1, 3), ParentIndex = 0 },
        ] };
        var mesh = new FLVER2.Mesh { BoneIndices = [0, 1], NodeIndex = 0 };
        var vertex = new FLVER.Vertex { Positions = [new(1, 2, 3)], Normals = [new(1, 1, 0)] };
        vertex.BoneIndices[0] = 1;
        var result = Bake(model, mesh, vertex);
        if (Vector3.Distance(result.Position, new(14, 12, -2)) > 0.00001f
            || Vector3.Distance(result.Normal, Vector3.Normalize(new Vector3(0, 1, -0.5f))) > 0.00001f)
            throw new InvalidDataException("Rigid geometry must follow the vertex palette, compose parents and inverse-scale normals");
        model.Nodes[0].ParentIndex = 1;
        Reject(() => Bake(model, mesh, vertex));
        model.Nodes[0].ParentIndex = -1;
        vertex.BoneIndices[0] = 2;
        Reject(() => Bake(model, mesh, vertex));
        vertex.BoneIndices[0] = 1;
        vertex.BoneWeights[0] = 1;
        Reject(() => Bake(model, mesh, vertex));
        Console.WriteLine("PASS: authored rigid scenery follows palette and parent transforms; normals account for nonuniform scale");
    }

    static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidDataException("Invalid rigid geometry was accepted");
    }
}
