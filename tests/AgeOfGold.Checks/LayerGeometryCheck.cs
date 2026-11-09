using System.Numerics;
using System.Text.Json;
using AgeOfGold.Scene;

internal static class LayerGeometryCheck
{
    internal static void Run()
    {
        var source = new LayerGeometry.Surface([
            Vertex(new(0, 0, 0), new(-20, 0), new(3, 5), new(0, 0, 0, 1)),
            Vertex(new(4, 0, 0), new(40, 0), new(11, 5), new(1, 0, 0, 0)),
            Vertex(new(0, 0, 4), new(-20, 60), new(3, 17), new(0, 1, 0, 0.5f)),
        ], [0, 1, 2], false);
        var result = LayerGeometry.Pack(source);
        Require(result.Indices.Length > 3 && !result.CullBackfaces, "Wide source triangle was not split");
        var area = 0f;
        for (var i = 0; i < result.Indices.Length; i += 3)
        {
            var triangle = result.Indices.Skip(i).Take(3).Select(j => result.Vertices[j]).ToArray();
            var a = triangle[0].Position; var b = triangle[1].Position; var c = triangle[2].Position;
            var signed = Vector3.Cross(b - a, c - a).Y;
            Require(signed < 0, "Source winding changed"); area -= signed / 2;
            Vector2? shift = null;
            foreach (var v in triangle)
            {
                var x = v.Position.X / 4; var z = v.Position.Z / 4;
                var expected = new Vector2(-20 + 60 * x, 60 * z);
                var offset = expected - v.Primary;
                Require(offset.X == MathF.Round(offset.X) && offset.Y == MathF.Round(offset.Y)
                    && (shift is null || offset == shift) && Math.Abs(v.Primary.X) < 16 && Math.Abs(v.Primary.Y) < 16,
                    "Primary repeating interpolation changed");
                shift = offset;
                Require(v.Secondary == new Vector2(3 + 8 * x, 5 + 12 * z), "Secondary coordinates were wrapped or rescaled");
                Require(v.Colour == new Vector4(x, z, 0, 1 - x - z / 2), "Colour/alpha interpolation changed");
                Require(v.Normal == Vector3.UnitY && v.Tangent == new Vector4(1, 0, 0, -1)
                    && v.SecondaryTangent == new Vector4(0, 0, 1, 1)
                    && v.Bitangent == new Vector4(0, 0, 1, 0) && v.SecondaryBitangent == new Vector4(1, 0, 0, 0),
                    "Independent tangent frames changed");
            }
        }
        Require(area == 8, "Source triangle coverage changed");
        var second = LayerGeometry.Pack(source);
        Require(result.Vertices.SequenceEqual(second.Vertices) && result.Indices.SequenceEqual(second.Indices), "Packing is nondeterministic");
        JsonElement Input(int secondChannel) => JsonSerializer.SerializeToElement(new
        {
            material = new { definition = "M[DB][M]", blendMode = 0,
                coordinates = new[] { ("g_Diffuse", 0), ("g_Bumpmap", 0), ("g_Diffuse_2", secondChannel), ("g_Bumpmap_2", 1) }
                    .Select(v => new { input = v.Item1, channel = v.Item2 }), textures = Array.Empty<object>() },
            indices = source.Indices, cullBackfaces = source.CullBackfaces,
            vertices = source.Vertices.Select(v => new { position = new[] { v.Position.X, v.Position.Y, v.Position.Z },
                normal = new[] { v.Normal.X, v.Normal.Y, v.Normal.Z },
                uvs = new[] { new[] { v.Primary.X, v.Primary.Y, 0 }, new[] { v.Secondary.X, v.Secondary.Y, 0 } },
                tangents = new[] { new[] { v.Tangent.X, v.Tangent.Y, v.Tangent.Z, v.Tangent.W } },
                secondaryTangent = new[] { v.SecondaryTangent.X, v.SecondaryTangent.Y, v.SecondaryTangent.Z, v.SecondaryTangent.W },
                colours = new[] { new[] { v.Colour.X, v.Colour.Y, v.Colour.Z, v.Colour.W } } })
        });
        Require(LayerGeometry.Read(Input(1)).Vertices.SequenceEqual(source.Vertices), "Source layer frame or mapping translation differs");
        bool rejected = false;
        try { LayerGeometry.Read(Input(0)); }
        catch (InvalidDataException e) when (e.Message.Contains("coordinate binding", StringComparison.Ordinal)) { rejected = true; }
        Require(rejected, "Changed source coordinate binding accepted");
        Console.WriteLine("PASS: split geometry retains both texture mappings, signed frames, colours, alpha, area and winding");
    }

    private static LayerGeometry.Vertex Vertex(Vector3 position, Vector2 primary, Vector2 secondary, Vector4 colour) =>
        new(position, Vector3.UnitY, primary, secondary, new(1, 0, 0, -1), new(0, 0, 1, 1), new(0, 0, 1, 0), new(1, 0, 0, 0), colour);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
