using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using AgeOfGold.Scene;
using SoulsFormats;

namespace AgeOfGold.Characters;

/// <summary>
/// The source gives every person one face front and makes it the person's own as the game runs: a face row's bytes
/// weigh fifty ways of bending the model and fifty of recolouring its skin. A build does the same once, as it is made.
/// Checked against a frame of the source game: the bent vertices agree to a tenth of a millimetre, the skin to a few
/// levels of 255 (how the source rounds its sums is not known), and the body's shift exactly.
/// </summary>
internal static class SourceFace
{
    private const int Header = 64;

    /// <summary>The weight a face row's byte gives one way of bending or recolouring: ten below nothing to ten above.</summary>
    internal static float Weight(byte value) => value * 20f / 255 - 10;

    /// <summary>
    /// Bends the face front as a face row shapes it. Each mesh follows a shaping mesh the map file names; a mode moves
    /// each shaping vertex by a scale times three whole numbers, with depth running the other way from the model's.
    /// </summary>
    /// <param name="file">The files the source keeps its face shaping in, by name: the map from the model's vertices, and each shaping mesh's modes.</param>
    /// <param name="shape">The face row's shaping bytes, one for each mode that bends both sides alike; the lopsided modes are left at nothing.</param>
    // lazy: a normal is turned by the change in its surface's own normal; the source's recomputed normals, which
    // differ from these by some thirteen degrees in a frame of the game, are not reproduced.
    internal static void Shape(FLVER2 front, Func<string, byte[]> file, byte[] shape)
    {
        byte[] map = file("face.flver2tri");
        int Word(int at) => at >= 0 && at + 2 <= map.Length ? BinaryPrimitives.ReadUInt16LittleEndian(map.AsSpan(at)) : throw new InvalidDataException("Face map is shorter than it says");
        int Whole(int at) => at >= 0 && at + 4 <= map.Length ? BinaryPrimitives.ReadInt32LittleEndian(map.AsSpan(at)) : throw new InvalidDataException("Face map is shorter than it says");
        if (map.Length < 16 || !map.AsSpan(0, 4).SequenceEqual("F2TR"u8) || Word(10) != 16 || Word(14) != 12 || Word(12) != front.Meshes.Count)
            throw new InvalidDataException("Face map is not one record for each mesh of the face front");
        var follows = new (string Shaping, int[] Vertices)[front.Meshes.Count];
        for (int mesh = 0; mesh < follows.Length; mesh++)
        {
            int name = Whole(16 + 12 * mesh), data = Whole(20 + 12 * mesh), count = Whole(24 + 12 * mesh), end = name;
            if (count != front.Meshes[mesh].Vertices.Count) throw new InvalidDataException("Face map does not count a mesh's vertices");
            while (Word(end) != 0) end += 2;
            string named = Encoding.Unicode.GetString(map, name, end - name);
            // A shaping mesh's modes are kept beside it under its own name; some names carry letters after the mesh's.
            int stem = named.IndexOf(".tri", StringComparison.Ordinal);
            if (stem <= 0) throw new InvalidDataException("Face map names no shaping mesh: " + named);
            follows[mesh] = (named[..stem], Enumerable.Range(0, count).Select(i => Word(data + 2 * i)).ToArray());
        }
        foreach (var group in Enumerable.Range(0, follows.Length).GroupBy(mesh => follows[mesh].Shaping, StringComparer.Ordinal))
        {
            byte[] modes = file(group.Key + ".egm");
            if (modes.Length < Header || !modes.AsSpan(0, 8).SequenceEqual("FREGM002"u8)) throw new InvalidDataException("Unsupported face shaping file: " + group.Key);
            int vertices = BinaryPrimitives.ReadInt32LittleEndian(modes.AsSpan(8)), alike = BinaryPrimitives.ReadInt32LittleEndian(modes.AsSpan(12)), lopsided = BinaryPrimitives.ReadInt32LittleEndian(modes.AsSpan(16));
            if (vertices <= 0 || alike < 0 || lopsided < 0 || modes.Length != Header + (long)(alike + lopsided) * (4 + 6 * vertices)) throw new InvalidDataException("Face shaping file is not the length its counts give: " + group.Key);
            if (shape.Length != alike) throw new InvalidDataException("A face row does not weigh each of a shaping mesh's modes: " + group.Key);
            var moved = new Vector3[vertices];
            for (int mode = 0; mode < alike; mode++)
            {
                int at = Header + mode * (4 + 6 * vertices);
                float weight = Weight(shape[mode]) * BinaryPrimitives.ReadSingleLittleEndian(modes.AsSpan(at));
                for (int v = 0; v < vertices; v++)
                    moved[v] += weight * new Vector3(BinaryPrimitives.ReadInt16LittleEndian(modes.AsSpan(at + 4 + 6 * v)), BinaryPrimitives.ReadInt16LittleEndian(modes.AsSpan(at + 6 + 6 * v)), -BinaryPrimitives.ReadInt16LittleEndian(modes.AsSpan(at + 8 + 6 * v)));
            }
            // The surface's own normal at each shaping vertex, before and after: the sum of its triangles' normals, each
            // weighing by its triangle's size, over every mesh that follows the shaping mesh.
            var before = new Vector3[vertices]; var after = new Vector3[vertices];
            foreach (int mesh in group)
            {
                var (_, to) = follows[mesh]; var body = front.Meshes[mesh];
                if (to.Any(v => v >= vertices)) throw new InvalidDataException("Face map names a shaping vertex the mesh lacks: " + group.Key);
                var whole = body.FaceSets.FirstOrDefault(f => f.Flags == FLVER2.FaceSet.FSFlags.None) ?? throw new InvalidDataException("A face mesh has no whole set of triangles");
                var corners = whole.Triangulate(body.Vertices.Count < ushort.MaxValue);
                for (int t = 0; t + 2 < corners.Count; t += 3)
                {
                    int a = corners[t], b = corners[t + 1], c = corners[t + 2];
                    Vector3 pa = body.Vertices[a].Position, pb = body.Vertices[b].Position, pc = body.Vertices[c].Position;
                    var flat = Vector3.Cross(pb - pa, pc - pa); var bent = Vector3.Cross(pb + moved[to[b]] - pa - moved[to[a]], pc + moved[to[c]] - pa - moved[to[a]]);
                    foreach (int corner in new[] { a, b, c }) { before[to[corner]] += flat; after[to[corner]] += bent; }
                }
            }
            foreach (int mesh in group)
            {
                var (_, to) = follows[mesh];
                for (int v = 0; v < to.Length; v++)
                {
                    var vertex = front.Meshes[mesh].Vertices[v];
                    vertex.Position += moved[to[v]];
                    // A vertex no triangle uses keeps its normal.
                    if (before[to[v]].LengthSquared() == 0 || after[to[v]].LengthSquared() == 0) continue;
                    // A model may wind its triangles either way about its normals; the surface's normal is taken on the stored normal's side.
                    Vector3 flat = Vector3.Normalize(before[to[v]]), bent = Vector3.Normalize(after[to[v]]);
                    var turned = vertex.Normal + (Vector3.Dot(flat, vertex.Normal) < 0 ? flat - bent : bent - flat);
                    if (turned.LengthSquared() > 1e-8f) vertex.Normal = Vector3.Normalize(turned);
                }
            }
        }
    }

    /// <summary>The source's base skin picture: three bytes a texel, rows from the bottom.</summary>
    internal static BlockTexture.Image Picture(byte[] picture)
    {
        int Whole(int at) => BinaryPrimitives.ReadInt32LittleEndian(picture.AsSpan(at));
        if (picture.Length < 54 || picture[0] != 'B' || picture[1] != 'M' || BinaryPrimitives.ReadInt16LittleEndian(picture.AsSpan(28)) != 24 || Whole(30) != 0 || Whole(18) <= 0 || Whole(22) <= 0)
            throw new InvalidDataException("The base skin picture is not three plain bytes a texel, rows from the bottom");
        int width = Whole(18), height = Whole(22), data = Whole(10), row = (width * 3 + 3) / 4 * 4;
        if (data < 54 || (long)data + (long)row * height > picture.Length) throw new InvalidDataException("The base skin picture is shorter than its size");
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int from = data + (height - 1 - y) * row + x * 3, to = (y * width + x) * 4;
                rgba[to] = picture[from + 2]; rgba[to + 1] = picture[from + 1]; rgba[to + 2] = picture[from]; rgba[to + 3] = 255;
            }
        return new(width, height, rgba);
    }

    /// <summary>
    /// A person's skin: the base picture and, for each mode a face row's byte weighs, a scale times a signed byte for
    /// each texel's red, green and blue.
    /// </summary>
    internal static BlockTexture.Image Skin(BlockTexture.Image basePicture, byte[] colouring, byte[] skin)
    {
        if (colouring.Length < Header || !colouring.AsSpan(0, 8).SequenceEqual("FREGT003"u8)) throw new InvalidDataException("Unsupported skin colouring file");
        int height = BinaryPrimitives.ReadInt32LittleEndian(colouring.AsSpan(8)), width = BinaryPrimitives.ReadInt32LittleEndian(colouring.AsSpan(12));
        int alike = BinaryPrimitives.ReadInt32LittleEndian(colouring.AsSpan(16)), lopsided = BinaryPrimitives.ReadInt32LittleEndian(colouring.AsSpan(20));
        if (width <= 0 || height <= 0 || alike < 0 || lopsided < 0 || colouring.Length != Header + (long)(alike + lopsided) * (4 + 3L * width * height)) throw new InvalidDataException("Skin colouring file is not the length its counts give");
        if (width != basePicture.Width || height != basePicture.Height) throw new InvalidDataException("Skin colouring is for a picture of another size than the base skin's");
        if (skin.Length != alike) throw new InvalidDataException("A face row does not weigh each of the skin's modes");
        int texels = width * height;
        var sum = new float[texels * 3];
        for (int i = 0; i < texels; i++) for (int c = 0; c < 3; c++) sum[i * 3 + c] = basePicture.Rgba[i * 4 + c];
        for (int mode = 0; mode < alike; mode++)
        {
            int at = Header + mode * (4 + 3 * texels);
            float weight = Weight(skin[mode]) * BinaryPrimitives.ReadSingleLittleEndian(colouring.AsSpan(at));
            // A mode keeps its red for every texel, then its green, then its blue.
            for (int c = 0; c < 3; c++) for (int i = 0; i < texels; i++) sum[i * 3 + c] += weight * (sbyte)colouring[at + 4 + c * texels + i];
        }
        var rgba = new byte[texels * 4];
        for (int i = 0; i < texels; i++) { for (int c = 0; c < 3; c++) rgba[i * 4 + c] = Level(sum[i * 3 + c]); rgba[i * 4 + 3] = 255; }
        return new(width, height, rgba);
    }

    private static byte Level(float value) => (byte)Math.Clamp(MathF.Round(value), 0, 255);

    /// <summary>
    /// What the source adds to the colour of the rest of a person's skin (the head behind the face, the body) so that it
    /// meets the face: how far the skin's top right texel has moved from the base picture's, in levels of 255.
    /// </summary>
    internal static int[] Shift(BlockTexture.Image skin, BlockTexture.Image basePicture)
    {
        if (skin.Width != basePicture.Width || skin.Height != basePicture.Height) throw new InvalidDataException("A skin and the base picture differ in size");
        int at = (skin.Width - 1) * 4;
        return [skin.Rgba[at] - basePicture.Rgba[at], skin.Rgba[at + 1] - basePicture.Rgba[at + 1], skin.Rgba[at + 2] - basePicture.Rgba[at + 2]];
    }

    /// <summary>A colour picture with each texel's red, green and blue moved by a shift and kept in range.</summary>
    internal static BlockTexture.Image Shifted(BlockTexture.Image colour, int[] shift)
    {
        var rgba = colour.Rgba.ToArray();
        for (int i = 0; i < rgba.Length; i += 4) for (int c = 0; c < 3; c++) rgba[i + c] = (byte)Math.Clamp(rgba[i + c] + shift[c], 0, 255);
        return colour with { Rgba = rgba };
    }

    /// <summary>An eye's picture with its iris, which the picture's fourth value marks, multiplied by the person's eye colour.</summary>
    internal static BlockTexture.Image Tinted(BlockTexture.Image eye, byte[] colour)
    {
        var rgba = eye.Rgba.ToArray();
        for (int i = 0; i < rgba.Length; i += 4)
        {
            float iris = rgba[i + 3] / 255f;
            for (int c = 0; c < 3; c++) rgba[i + c] = Level(rgba[i + c] * (1 + iris * (colour[c] / 255f - 1)));
            rgba[i + 3] = 255;
        }
        return eye with { Rgba = rgba };
    }

    /// <summary>
    /// The face's colour as one picture in the layout of its detail picture. The source draws the face from two
    /// pictures: the person's skin, found by a vertex's first coordinates, and a detail picture every face shares, found
    /// by its second and shifted like the rest of the body; a vertex's fourth colour value says how much of the detail
    /// shows.
    /// </summary>
    /// <param name="meshes">The face front's meshes that are drawn this way.</param>
    // lazy: the skin is laid into the detail's layout once, so it is as sharp as the detail's texels there and no sharper.
    internal static BlockTexture.Image Blended(FLVER2 front, IEnumerable<int> meshes, BlockTexture.Image skin, BlockTexture.Image detail, int[] shift)
    {
        int width = detail.Width, height = detail.Height;
        var rgba = Shifted(detail, shift).Rgba;
        // A texel whose centre lies in a triangle is that triangle's; one within a texel of a triangle takes it unless
        // another holds its centre, so the picture's smoothing near an edge of the layout reads no bare detail.
        var held = new byte[width * height];
        Vector3 SkinAt(Vector2 at)
        {
            // The coordinates repeat, and the picture is read smoothly between its texels' centres.
            float x = (at.X - MathF.Floor(at.X)) * skin.Width - 0.5f, y = (at.Y - MathF.Floor(at.Y)) * skin.Height - 0.5f;
            int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y); float fx = x - x0, fy = y - y0;
            Vector3 Texel(int tx, int ty) { int i = ((ty % skin.Height + skin.Height) % skin.Height * skin.Width + (tx % skin.Width + skin.Width) % skin.Width) * 4; return new(skin.Rgba[i], skin.Rgba[i + 1], skin.Rgba[i + 2]); }
            return Vector3.Lerp(Vector3.Lerp(Texel(x0, y0), Texel(x0 + 1, y0), fx), Vector3.Lerp(Texel(x0, y0 + 1), Texel(x0 + 1, y0 + 1), fx), fy);
        }
        foreach (int mesh in meshes)
        {
            var body = front.Meshes[mesh];
            var whole = body.FaceSets.FirstOrDefault(f => f.Flags == FLVER2.FaceSet.FSFlags.None) ?? throw new InvalidDataException("A face mesh has no whole set of triangles");
            if (body.Vertices.Any(v => v.UVs.Count < 2 || v.Colors.Count < 1 || v.Colors[0].R != 1 || v.Colors[0].G != 1 || v.Colors[0].B != 1))
                throw new InvalidDataException("A face mesh lacks two sets of coordinates or a colour, or tints its surface");
            var corners = whole.Triangulate(body.Vertices.Count < ushort.MaxValue);
            for (int t = 0; t + 2 < corners.Count; t += 3)
            {
                var v = new[] { body.Vertices[corners[t]], body.Vertices[corners[t + 1]], body.Vertices[corners[t + 2]] };
                var p = v.Select(c => new Vector2(c.UVs[1].X * width, c.UVs[1].Y * height)).ToArray();
                float area = (p[1].X - p[0].X) * (p[2].Y - p[0].Y) - (p[2].X - p[0].X) * (p[1].Y - p[0].Y);
                if (MathF.Abs(area) < 1e-6f) continue;
                int left = Math.Max(0, (int)MathF.Floor(Math.Min(p[0].X, Math.Min(p[1].X, p[2].X)) - 1)), right = Math.Min(width - 1, (int)MathF.Ceiling(Math.Max(p[0].X, Math.Max(p[1].X, p[2].X)) + 1));
                int top = Math.Max(0, (int)MathF.Floor(Math.Min(p[0].Y, Math.Min(p[1].Y, p[2].Y)) - 1)), bottom = Math.Min(height - 1, (int)MathF.Ceiling(Math.Max(p[0].Y, Math.Max(p[1].Y, p[2].Y)) + 1));
                for (int y = top; y <= bottom; y++)
                    for (int x = left; x <= right; x++)
                    {
                        var at = new Vector2(x + 0.5f, y + 0.5f);
                        // The texel's place among the triangle's corners, and how far outside the triangle it lies, in texels.
                        float w1 = ((at.X - p[0].X) * (p[2].Y - p[0].Y) - (p[2].X - p[0].X) * (at.Y - p[0].Y)) / area, w2 = ((p[1].X - p[0].X) * (at.Y - p[0].Y) - (at.X - p[0].X) * (p[1].Y - p[0].Y)) / area, w0 = 1 - w1 - w2;
                        bool inside = w0 >= 0 && w1 >= 0 && w2 >= 0;
                        if (!inside)
                        {
                            float away = MathF.Min(Away(at, p[0], p[1]), MathF.Min(Away(at, p[1], p[2]), Away(at, p[2], p[0])));
                            if (away > 1 || held[y * width + x] != 0) continue;
                            w0 = Math.Clamp(w0, 0, 1); w1 = Math.Clamp(w1, 0, 1); w2 = Math.Clamp(w2, 0, 1);
                            float sum = w0 + w1 + w2; w0 /= sum; w1 /= sum; w2 /= sum;
                        }
                        else if (held[y * width + x] == 2) continue;
                        held[y * width + x] = (byte)(inside ? 2 : 1);
                        var first = w0 * new Vector2(v[0].UVs[0].X, v[0].UVs[0].Y) + w1 * new Vector2(v[1].UVs[0].X, v[1].UVs[0].Y) + w2 * new Vector2(v[2].UVs[0].X, v[2].UVs[0].Y);
                        float through = w0 * v[0].Colors[0].A + w1 * v[1].Colors[0].A + w2 * v[2].Colors[0].A;
                        var own = SkinAt(first);
                        int i = (y * width + x) * 4;
                        // The shifted detail is kept in range only after the two are mixed, as the source mixes them.
                        for (int c = 0; c < 3; c++) rgba[i + c] = Level(own[c] + through * (detail.Rgba[i + c] + shift[c] - own[c]));
                    }
            }
        }
        return new(width, height, rgba);
    }

    // How far a point lies from a segment.
    private static float Away(Vector2 at, Vector2 from, Vector2 to)
    {
        var along = to - from; float length = along.LengthSquared();
        float t = length == 0 ? 0 : Math.Clamp(Vector2.Dot(at - from, along) / length, 0, 1);
        return Vector2.Distance(at, from + t * along);
    }
}
