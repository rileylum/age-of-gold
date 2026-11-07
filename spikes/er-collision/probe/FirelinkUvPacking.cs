using System.Numerics;

// Repeating texture coordinates may shift by whole tiles without changing sampled colour.
static class FirelinkUvPacking
{
    public record Corner(Vector3 Barycentric, Vector2 UV);
    public record Triangle(Corner A, Corner B, Corner C);

    /// <summary>Fit a repeating triangle into signed packed UVs without changing its texture scale.</summary>
    public static IEnumerable<Triangle> Fit(Vector2 a, Vector2 b, Vector2 c)
    {
        if (new[] { a, b, c }.Any(v => !float.IsFinite(v.X) || !float.IsFinite(v.Y)))
            throw new InvalidDataException("Texture coordinates must be finite");
        // lazy: cap subdivision growth until a source triangle wider than 256 tiles is encountered.
        var span = Vector2.Max(a, Vector2.Max(b, c)) - Vector2.Min(a, Vector2.Min(b, c));
        if (span.X > 256 || span.Y > 256)
            throw new InvalidDataException("Source triangle exceeds the checked subdivision range");
        return Split(new(Vector3.UnitX, a), new(Vector3.UnitY, b), new(Vector3.UnitZ, c));
    }

    static IEnumerable<Triangle> Split(Corner a, Corner b, Corner c)
    {
        var lo = Vector2.Min(a.UV, Vector2.Min(b.UV, c.UV));
        var hi = Vector2.Max(a.UV, Vector2.Max(b.UV, c.UV));
        if (hi.X - lo.X > 30 || hi.Y - lo.Y > 30)
        {
            Corner Mid(Corner p, Corner q) => new((p.Barycentric + q.Barycentric) / 2, (p.UV + q.UV) / 2);
            var ab = Mid(a, b); var bc = Mid(b, c); var ca = Mid(c, a);
            foreach (var triangle in new[] { (a, ab, ca), (ab, b, bc), (ca, bc, c), (ab, bc, ca) })
                foreach (var child in Split(triangle.Item1, triangle.Item2, triangle.Item3)) yield return child;
            yield break;
        }
        float Shift(float min, float max) => min >= -15.999f && max <= 15.999f ? 0 : MathF.Round((min + max) / 2);
        var offset = new Vector2(Shift(lo.X, hi.X), Shift(lo.Y, hi.Y));
        yield return new(a with { UV = a.UV - offset }, b with { UV = b.UV - offset }, c with { UV = c.UV - offset });
    }

    /// <summary>Check texture interpolation, area and winding with authored wide and negative coordinates.</summary>
    public static void Check()
    {
        foreach (var source in new[] {
            new[] { new Vector2(-20, 0), new Vector2(40, 0), new Vector2(-20, 60) },
            new[] { new Vector2(20, -30), new Vector2(20.5f, -30), new Vector2(20, -29.5f) },
            new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1) },
        })
        {
            float area = 0;
            foreach (var triangle in Fit(source[0], source[1], source[2]))
            {
                var corners = new[] { triangle.A, triangle.B, triangle.C };
                Vector2? shift = null;
                foreach (var corner in corners)
                {
                    var w = corner.Barycentric;
                    if (w.X < 0 || w.Y < 0 || w.Z < 0 || w.X + w.Y + w.Z != 1)
                        throw new InvalidDataException("Packed triangle left its source triangle");
                    var expected = source[0] * w.X + source[1] * w.Y + source[2] * w.Z;
                    var offset = expected - corner.UV;
                    if (Math.Abs(corner.UV.X) > 15.999f || Math.Abs(corner.UV.Y) > 15.999f)
                        throw new InvalidDataException("UV does not fit signed packed coordinates");
                    if (offset.X != MathF.Round(offset.X) || offset.Y != MathF.Round(offset.Y)
                        || shift.HasValue && shift.Value != offset)
                        throw new InvalidDataException("Packed triangle changed repeated texture interpolation");
                    shift = offset;
                }
                var p = triangle.A.Barycentric; var q = triangle.B.Barycentric; var r = triangle.C.Barycentric;
                var signed = (q.Y-p.Y)*(r.Z-p.Z)-(q.Z-p.Z)*(r.Y-p.Y);
                if (signed <= 0) throw new InvalidDataException("Packed triangle winding changed");
                area += signed;
            }
            if (area != 1) throw new InvalidDataException("Packed triangles do not cover their source area");
        }
        Console.WriteLine("PASS: repeated UV scale and interpolation survive packing; triangle area and winding preserved");
    }
}
