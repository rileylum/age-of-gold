using System.Numerics;
using System.Text;
using AgeOfGold.Characters;
using AgeOfGold.Scene;
using SoulsFormats;

internal static class SourceFaceCheck
{
    // A shaping file: a header naming how many vertices and modes it has, then for each mode a scale and three whole
    // numbers a vertex.
    private static byte[] Modes(int vertices, int asymmetric, params (float Scale, short[] Moves)[] modes)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write("FREGM002"u8); writer.Write(vertices); writer.Write(modes.Length - asymmetric); writer.Write(asymmetric); writer.Write(7); writer.Write(new byte[40]);
        foreach (var (scale, moves) in modes) { writer.Write(scale); foreach (short move in moves) writer.Write(move); }
        return stream.ToArray();
    }

    // The map from a model's vertices to shaping vertices: for each mesh the shaping mesh it follows, by name, and a
    // number a vertex.
    private static byte[] Map(params (string Follows, ushort[] Vertices)[] meshes)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write("F2TR"u8); foreach (ushort word in new ushort[] { 0, 1, 0, 16, (ushort)meshes.Length, 12 }) writer.Write(word);
        int data = 16 + 12 * meshes.Length, names = data + meshes.Sum(m => 2 * m.Vertices.Length);
        foreach (var (follows, vertices) in meshes) { writer.Write(names); writer.Write(data); writer.Write(vertices.Length); data += 2 * vertices.Length; names += 2 * follows.Length + 2; }
        foreach (var (_, vertices) in meshes) foreach (ushort vertex in vertices) writer.Write(vertex);
        foreach (var (follows, _) in meshes) { writer.Write(Encoding.Unicode.GetBytes(follows)); writer.Write((ushort)0); }
        return stream.ToArray();
    }

    // A recolouring file: a picture's size and how many modes, then for each mode a scale and the picture's red, green
    // and blue as signed bytes, rows from the top.
    private static byte[] Colouring(int width, int height, params (float Scale, sbyte[] Red, sbyte[] Green, sbyte[] Blue)[] modes)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write("FREGT003"u8); writer.Write(height); writer.Write(width); writer.Write(modes.Length); writer.Write(0); writer.Write(81); writer.Write(new byte[36]);
        foreach (var mode in modes) { writer.Write(mode.Scale); foreach (var plane in new[] { mode.Red, mode.Green, mode.Blue }) foreach (sbyte value in plane) writer.Write(value); }
        return stream.ToArray();
    }

    // A picture as the source keeps its base skin: three bytes a texel, blue first, rows from the bottom, each row
    // filled out to a multiple of four bytes.
    private static byte[] Picture(int width, int height, Func<int, int, (byte R, byte G, byte B)> texel)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        int row = (width * 3 + 3) / 4 * 4;
        writer.Write("BM"u8); writer.Write(54 + row * height); writer.Write(0); writer.Write(54);
        writer.Write(40); writer.Write(width); writer.Write(height); writer.Write((short)1); writer.Write((short)24); writer.Write(0); writer.Write(row * height); writer.Write(new byte[16]);
        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = 0; x < width; x++) { var (r, g, b) = texel(x, y); writer.Write(b); writer.Write(g); writer.Write(r); }
            writer.Write(new byte[row - width * 3]);
        }
        return stream.ToArray();
    }

    internal static void Run()
    {
        if (SourceFace.Weight(0) != -10 || SourceFace.Weight(255) != 10 || MathF.Abs(SourceFace.Weight(51) + 6) > 1e-5f) throw new Exception("A face row's byte weighs otherwise than ten below nothing to ten above");

        // A face front of two meshes: a triangle lying flat that follows three vertices of the face's shaping mesh, and one
        // vertex that follows the eyes'. The model's depth runs the other way from the shaping data's.
        FLVER2 Front(bool wound = true)
        {
            var model = new FLVER2();
            FLVER.Vertex At(float x, float y, float z) => new() { Positions = [new(x, y, z)], Normals = [new(0, 0, -1)], Tangents = [new(0, 1, 0, 1)], UVs = [new(0, 0, 0), new(0, 0, 0)], Colors = [new(1f, 1f, 1f, 1f)] };
            model.Meshes.Add(new() { Vertices = [At(0, 0, 0), At(1, 0, 0), At(0, 1, 0), At(1, 1, 0)], FaceSets = [new() { Indices = wound ? [0, 2, 1, 1, 2, 3] : [0, 1, 2, 1, 3, 2] }, new() { Flags = FLVER2.FaceSet.FSFlags.LodLevel1, Indices = [0, 2, 1] }] });
            model.Meshes.Add(new() { Vertices = [At(5, 5, 5)], FaceSets = [new() { Indices = [] }] });
            return model;
        }
        // The face's first mode lifts shaping vertex 2 (and 3 with it) forward by a thousandth a step; its second moves
        // vertex 0 sideways; a third, lopsided, is not weighed by a face row. The eyes' one mode moves their vertex up.
        var files = new Dictionary<string, byte[]>
        {
            ["face.flver2tri"] = Map(("face.tribe", [0, 1, 2, 3]), ("eye.tri", [0])),
            ["face.egm"] = Modes(4, 1, (0.001f, [0, 0, 0, 0, 0, 0, 0, 0, 100, 0, 0, 100]), (0.01f, [10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]), (1f, [9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9])),
            ["eye.egm"] = Modes(1, 0, (0.5f, [0, 2, 0]), (0.5f, [0, 0, 0])),
        };
        byte[] Lookup(string name) => files.TryGetValue(name, out var found) ? found : throw new InvalidDataException("no file " + name);
        // Bytes 255 and 0 weigh the first two modes by ten and by ten the other way.
        var front = Front();
        SourceFace.Shape(front, Lookup, [255, 0]);
        var bent = front.Meshes[0].Vertices;
        // Vertex 2 goes forward by 10 × 0.001 × 100 = 1, which in the model is depth less one; vertex 0 goes 10 × 0.01 × 10 = 1 to the other side.
        if (Vector3.Distance(bent[2].Position, new(0, 1, -1)) > 1e-5f || Vector3.Distance(bent[3].Position, new(1, 1, -1)) > 1e-5f || Vector3.Distance(bent[0].Position, new(-1, 0, 0)) > 1e-5f || bent[1].Position != new Vector3(1, 0, 0))
            throw new Exception("A face front is not bent by its modes: " + string.Join(" ", bent.Select(v => v.Position)));
        if (Vector3.Distance(front.Meshes[1].Vertices[0].Position, new(5, 15, 5)) > 1e-5f) throw new Exception("The eyes do not follow their own modes");
        // The sheet now comes forward as it rises, so a normal that faced straight out leans down; it stays of length one,
        // and the tangent is left as the model has it.
        var leaning = bent[3].Normal;
        if (MathF.Abs(leaning.Length() - 1) > 1e-4f || !(leaning.Y < -0.3f) || !(leaning.Z < -0.3f) || MathF.Abs(leaning.X) > 0.2f || bent[3].Tangents[0] != new Vector4(0, 1, 0, 1))
            throw new Exception("A bent face's normal does not turn with its surface: " + leaning);
        // A model that winds its triangles the other way about its normals turns them the same.
        var other = Front(false); SourceFace.Shape(other, Lookup, [255, 0]);
        if (Vector3.Distance(other.Meshes[0].Vertices[3].Normal, leaning) > 1e-4f) throw new Exception("A face wound the other way turns its normals otherwise: " + other.Meshes[0].Vertices[3].Normal);
        // Every weight in the middle leaves the model as it is, to within the half step a byte cannot say.
        var still = Front(); SourceFace.Shape(still, Lookup, [128, 127]);
        if (Vector3.Distance(still.Meshes[0].Vertices[2].Position, new(0, 1, 0)) > 0.005f || Vector3.Dot(still.Meshes[0].Vertices[2].Normal, new(0, 0, -1)) < 0.999f) throw new Exception("Middle weights bend a face");
        foreach (var bad in new Action[]
        {
            () => SourceFace.Shape(Front(), Lookup, [1]),
            () => SourceFace.Shape(Front(), name => name == "face.flver2tri" ? Map(("face.tri", [0, 1, 2]), ("eye.tri", [0])) : Lookup(name), [1, 2]),
            () => SourceFace.Shape(Front(), name => name == "face.flver2tri" ? Map(("face.tri", [0, 1, 2, 4]), ("eye.tri", [0])) : Lookup(name), [1, 2]),
            () => SourceFace.Shape(Front(), name => name == "face.flver2tri" ? Map(("face.tri", [0, 1, 2, 3])) : Lookup(name), [1, 2]),
            () => SourceFace.Shape(Front(), name => name == "face.egm" ? [.. "FREGM001"u8, .. Lookup(name).AsSpan(8)] : Lookup(name), [1, 2]),
            () => SourceFace.Shape(Front(), name => name == "face.egm" ? Lookup(name)[..^2] : Lookup(name), [1, 2]),
        })
        {
            try { bad(); throw new Exception("Unusable shaping data was accepted"); }
            catch (InvalidDataException) { }
        }
        Console.WriteLine("PASS: a face front is bent by the modes its face row weighs, each mesh following its own shaping mesh, and its normals turn with it; other layouts fail");

        // Skin: a base picture two texels by two and two modes. The first mode adds to red at the top right; the second
        // takes from green everywhere.
        var basePicture = Picture(2, 2, (x, y) => ((byte)(100 + 10 * x), (byte)(50 + 10 * y), 200));
        var plain = SourceFace.Picture(basePicture);
        if (plain.Width != 2 || plain.Height != 2 || !plain.Rgba.AsSpan(4, 4).SequenceEqual(new byte[] { 110, 50, 200, 255 }) || !plain.Rgba.AsSpan(8, 4).SequenceEqual(new byte[] { 100, 60, 200, 255 })) throw new Exception("The base skin picture is read wrongly");
        var colouring = Colouring(2, 2, (0.5f, [0, 8, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0]), (0.25f, [0, 0, 0, 0], [4, 4, 4, 4], [0, 0, 0, 100]));
        var skin = SourceFace.Skin(plain, colouring, [255, 0]);
        // Top right: red 110 + 10 × 0.5 × 8 = 150; green 50 − 10 × 0.25 × 4 = 40. Bottom right: blue 200 − 250 stops at nothing.
        if (!skin.Rgba.AsSpan(4, 4).SequenceEqual(new byte[] { 150, 40, 200, 255 }) || !skin.Rgba.AsSpan(12, 4).SequenceEqual(new byte[] { 110, 50, 0, 255 })) throw new Exception("Skin is not the base picture and its weighed modes: " + string.Join(",", skin.Rgba));
        // The rest of the body is shifted by what the skin's top right texel moved from the base's.
        var shift = SourceFace.Shift(skin, plain);
        if (!shift.SequenceEqual(new[] { 40, -10, 0 })) throw new Exception("The body's shift is not the top right texel's change: " + string.Join(",", shift));
        var shifted = SourceFace.Shifted(new(1, 2, [230, 5, 7, 99, 10, 20, 30, 255]), shift);
        if (!shifted.Rgba.SequenceEqual(new byte[] { 255, 0, 7, 99, 50, 10, 30, 255 })) throw new Exception("A shifted picture is not each colour moved and kept in range: " + string.Join(",", shifted.Rgba));
        // An eye is tinted where its picture's fourth value marks the iris, and wholly covered after.
        var eye = SourceFace.Tinted(new(2, 1, [200, 100, 50, 255, 200, 100, 50, 0]), [51, 255, 0]);
        if (!eye.Rgba.SequenceEqual(new byte[] { 40, 100, 0, 255, 200, 100, 50, 255 })) throw new Exception("An eye is not tinted by its iris: " + string.Join(",", eye.Rgba));
        foreach (var bad in new Action[]
        {
            () => SourceFace.Skin(plain, colouring, [1]),
            () => SourceFace.Skin(new(4, 4, new byte[64]), colouring, [1, 2]),
            () => SourceFace.Skin(plain, colouring[..^1], [1, 2]),
            () => SourceFace.Picture(basePicture[..^1]),
            () => SourceFace.Shift(skin, new(4, 4, new byte[64])),
        })
        {
            try { bad(); throw new Exception("Unusable colouring data was accepted"); }
            catch (InvalidDataException) { }
        }
        Console.WriteLine("PASS: a person's skin is the base picture and the modes its face row weighs; the body's shift, a shifted picture and a tinted eye follow; other layouts fail");

        // The face's own colour: over a detail picture laid out by the model's second coordinates, the skin, found by
        // the first coordinates, shows through by each vertex's fourth colour value. One triangle covers the detail's
        // left and top; the skin is one colour.
        var blending = new FLVER2();
        FLVER.Vertex Corner(float u, float v, float through) => new() { Positions = [Vector3.Zero], Normals = [Vector3.UnitZ], UVs = [new(0.5f, 0.5f, 0), new(u, v, 0)], Colors = [new(through, 1f, 1f, 1f)] };
        // The library keeps a colour's fourth value first.
        blending.Meshes.Add(new() { Vertices = [Corner(0, 0, 0.5f), Corner(1, 0, 0.5f), Corner(0, 1, 0.5f)], FaceSets = [new() { Indices = [0, 1, 2] }] });
        var detail = new BlockTexture.Image(4, 4, Enumerable.Range(0, 16).SelectMany(_ => new byte[] { 100, 100, 100, 255 }).ToArray());
        var one = new BlockTexture.Image(1, 1, [200, 0, 50, 255]);
        var blended = SourceFace.Blended(blending, [0], one, detail, [20, 0, -200]);
        // Inside the triangle: halfway from the skin (200, 0, 50) to the shifted detail (120, 100, -100), kept in range
        // only then. Outside: the shifted detail.
        if (!blended.Rgba.AsSpan(0, 4).SequenceEqual(new byte[] { 160, 50, 0, 255 }) || !blended.Rgba.AsSpan(15 * 4, 4).SequenceEqual(new byte[] { 120, 100, 0, 255 }) || blended.Width != 4 || blended.Height != 4)
            throw new Exception("A face's colour is not skin through detail: " + string.Join(",", blended.Rgba.Take(4)) + " / " + string.Join(",", blended.Rgba.Skip(60)));
        // A vertex that lets all of the detail show leaves the detail alone.
        foreach (var vertex in blending.Meshes[0].Vertices) vertex.Colors[0] = new(1f, 1f, 1f, 1f);
        if (!SourceFace.Blended(blending, [0], one, detail, [0, 0, 0]).Rgba.SequenceEqual(detail.Rgba)) throw new Exception("A face wholly of detail is changed");
        Console.WriteLine("PASS: a face's colour is its skin showing through its detail picture by each vertex's say, in the detail's layout");
    }
}
