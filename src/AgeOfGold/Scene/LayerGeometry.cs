using System.Numerics;
using System.Text.Json;

namespace AgeOfGold.Scene;

internal static class LayerGeometry
{
    internal sealed record Vertex(Vector3 Position, Vector3 Normal, Vector2 Primary, Vector2 Secondary,
        Vector4 Tangent, Vector4 SecondaryTangent, Vector4 Bitangent, Vector4 SecondaryBitangent, Vector4 Colour);
    internal sealed record Surface(Vertex[] Vertices, int[] Indices, bool CullBackfaces);
    internal static Surface Read(JsonElement surface)
    {
        var material = surface.GetProperty("material");
        bool single = LayerMaterial.Single(material);
        foreach (var (name, channel) in single ? new[] { ("g_Diffuse", 0), ("g_Bumpmap", 0) }
            : new[] { ("g_Diffuse", 0), ("g_Bumpmap", 0), ("g_Diffuse_2", 1), ("g_Bumpmap_2", 1) })
        {
            if (material.GetProperty("coordinates").EnumerateArray().Single(c => c.GetProperty("input").GetString() == name)
                .GetProperty("channel").GetInt32() != channel)
                throw new InvalidDataException("Unsupported independent-layer coordinate binding");
        }
        foreach (var texture in material.GetProperty("textures").EnumerateArray())
            if (!texture.GetProperty("scale").EnumerateArray().Select(v => v.GetSingle()).SequenceEqual(new[] { 1f, 1f }))
                throw new InvalidDataException("Unsupported independent-layer texture scale");
        var vertices = surface.GetProperty("vertices").EnumerateArray().Select(v =>
        {
            if (v.GetProperty("uvs").GetArrayLength() != (single ? 1 : 2) || v.GetProperty("tangents").GetArrayLength() != 1
                || v.GetProperty("colours").GetArrayLength() != 1 || !single && v.GetProperty("secondaryTangent").ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Independent layer vertex lacks its mappings, frames or colour");
            var position = V3(v.GetProperty("position")); var normal = V3(v.GetProperty("normal"));
            // A one-layer surface repeats its mapping and frame for the host's second layer.
            var uv = V3(v.GetProperty("uvs")[0]); var secondary = single ? uv : V3(v.GetProperty("uvs")[1]);
            var tangent = V4(v.GetProperty("tangents")[0]); var other = single ? tangent : V4(v.GetProperty("secondaryTangent"));
            var colour = V4(v.GetProperty("colours")[0]);
            if (uv.Z != 0 || secondary.Z != 0 || Math.Abs(tangent.W) != 1 || Math.Abs(other.W) != 1 || normal.LengthSquared() == 0)
                throw new InvalidDataException("Unsupported independent-layer coordinate or tangent encoding");
            Vector4 Bitangent(Vector4 t) => new(Vector3.Cross(normal, new(t.X, t.Y, t.Z)) * t.W, 0);
            return new Vertex(position, normal, new(uv.X, uv.Y), new(secondary.X, secondary.Y), tangent, other,
                Bitangent(tangent), Bitangent(other), colour);
        }).ToArray();
        var indices = surface.GetProperty("indices").EnumerateArray().Select(i => i.GetInt32()).ToArray();
        if (indices.Length == 0 || indices.Length % 3 != 0 || indices.Any(i => i < 0 || i >= vertices.Length))
            throw new InvalidDataException("Invalid independent-layer triangle indices");
        return new(vertices, indices, surface.GetProperty("cullBackfaces").GetBoolean());
    }

    internal static Surface Pack(Surface surface)
    {
        var vertices = new List<Vertex>(); var indices = new List<int>();
        var shared = new Dictionary<Vertex, int>();
        for (var i = 0; i < surface.Indices.Length; i += 3)
        {
            var a = surface.Vertices[surface.Indices[i]]; var b = surface.Vertices[surface.Indices[i + 1]]; var c = surface.Vertices[surface.Indices[i + 2]];
            foreach (var triangle in FirelinkUvPacking.Fit(a.Primary, b.Primary, c.Primary))
                foreach (var corner in new[] { triangle.A, triangle.B, triangle.C })
                {
                    var w = corner.Barycentric;
                    var v = new Vertex(a.Position * w.X + b.Position * w.Y + c.Position * w.Z,
                        a.Normal * w.X + b.Normal * w.Y + c.Normal * w.Z, corner.UV,
                        a.Secondary * w.X + b.Secondary * w.Y + c.Secondary * w.Z,
                        a.Tangent * w.X + b.Tangent * w.Y + c.Tangent * w.Z,
                        a.SecondaryTangent * w.X + b.SecondaryTangent * w.Y + c.SecondaryTangent * w.Z,
                        a.Bitangent * w.X + b.Bitangent * w.Y + c.Bitangent * w.Z,
                        a.SecondaryBitangent * w.X + b.SecondaryBitangent * w.Y + c.SecondaryBitangent * w.Z,
                        a.Colour * w.X + b.Colour * w.Y + c.Colour * w.Z);
                    if (!shared.TryGetValue(v, out var index))
                    {
                        index = vertices.Count;
                        vertices.Add(v); shared.Add(v, index);
                    }
                    indices.Add(index);
                }
        }
        return new(vertices.ToArray(), indices.ToArray(), surface.CullBackfaces);
    }

    private static float[] Vector(JsonElement value, int size)
    {
        var result = value.EnumerateArray().Select(v => v.GetSingle()).ToArray();
        if (result.Length != size || result.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Invalid source vertex vector");
        return result;
    }
    private static Vector3 V3(JsonElement value) { var v = Vector(value, 3); return new(v[0], v[1], v[2]); }
    private static Vector4 V4(JsonElement value) { var v = Vector(value, 4); return new(v[0], v[1], v[2], v[3]); }
}
