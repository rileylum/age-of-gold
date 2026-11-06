using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using HKLib.Serialization.hk2018.Binary;
using SoulsFormats;

// Spike: source terrain collision and visual models above the host's starting map.
static class FirelinkDemo
{
    record Mesh(List<Vector3> Vertices, List<(int A, int B, int C)> Triangles, int ZeroAreaFaces);

    /// <summary>Check source transform composition and winding with an authored triangle.</summary>
    public static void CheckTransform()
    {
        using var fixture = JsonDocument.Parse("""
            {"role":"solid","position":[10,20,30],"rotation":[0,0,0],"scale":[1,1,1],"l":{
            "body_transform":[0,0,-1,0,0,1,0,0,1,0,0,0,5,6,7,1],"meshes":[{
            "rotation":[0,0,0,1],"translation":[0,0,0,0],"scale":[1,1,1,0],
            "flip_alternate_triangles":0,"extrusion":[0,0,0,0],
            "vertices":[[0,0,0,0],[2,0,0,0],[0,0,3,0]],"faces":[[0,1,2,0],[0,0,1,0]]}]}}
            """);
        var mesh = ReadMesh(fixture.RootElement, "l", new Vector3(100, 200, 300));
        Vector3[] expected = [new(115, 226, 337), new(115, 226, 335), new(118, 226, 337)];
        if (!mesh.Vertices.SequenceEqual(expected)) throw new InvalidDataException("Source transform composition changed positions");
        if (!mesh.Triangles.SequenceEqual(new[] { (0, 2, 1) }))
            throw new InvalidDataException("Source adapter must reverse winding and exclude zero-area faces");
        if (GroundBelow([mesh], new Vector3(116, 227, 336.5f)) != 226)
            throw new InvalidDataException("Transformed triangle does not support its expected surface point");
        Console.WriteLine("PASS: authored source transform, winding and zero-area face check");
    }

    /// <summary>Write and read back placed source terrain, diagnostic surfaces and a supported player spawn.</summary>
    public static void Run(string geometryPath, string output, Func<string, byte[]> read)
    {
        var hostFingerprints = new SortedDictionary<string, string>();
        byte[] get(string path)
        {
            var bytes = read(path);
            hostFingerprints[path] = Convert.ToHexString(SHA256.HashData(bytes));
            return bytes;
        }
        using var document = JsonDocument.Parse(File.ReadAllBytes(geometryPath));
        var source = document.RootElement;
        var parts = source.GetProperty("parts").EnumerateArray().ToArray();
        if (parts.Length == 0) throw new InvalidDataException("No source terrain placements");
        var layout = MSBE.Read(get("map/mapstudio/m10_01_00_00.msb.dcx"));
        var player = layout.Parts.Players.Single(p => p.Name == "c0000_0000");
        var sourceSpawn = V(source.GetProperty("spawn").GetProperty("position"));
        // Separate the diagnostic terrain from the existing Chapel while retaining its loading setup.
        var offset = player.Position + new Vector3(0, 200, 0) - sourceSpawn;
        var low = parts.Select(p => ReadMesh(p, "l", offset)).ToArray();
        var high = parts.Select(p => ReadMesh(p, "h", offset)).ToArray();
        var floor = GroundBelow(low, sourceSpawn + offset + new Vector3(0, 2, 0));
        if (Math.Abs(floor - (sourceSpawn.Y + offset.Y)) > 3)
            throw new InvalidDataException("Source respawn is not close to ordinary solid terrain");
        player.Position = new Vector3(sourceSpawn.X + offset.X, floor + 0.3f, sourceSpawn.Z + offset.Z);
        player.Rotation = V(source.GetProperty("spawn").GetProperty("rotation"));
        var originalEvents = get("event/m10_01_00_00.emevd.dcx");
        var events = EMEVD.Read(originalEvents);
        FirelinkArrival.Apply(events, layout, player);

        var mapDir = Path.Combine(output, "mod", "map", "m10", "m10_01_00_00");
        Directory.CreateDirectory(mapDir);
        var collisionTemplate = layout.Parts.Collisions.Single(p => p.ModelName == "h020100");
        if (collisionTemplate.HitFilterID != MSBE.Part.Collision.HitFilterType.Standard)
            throw new InvalidDataException("Host template must have a Standard hit filter");
        var collisionModel = layout.Models.Collisions.Single(p => p.Name == collisionTemplate.ModelName);
        string[] ids = Enumerable.Range(800000, parts.Length).Select(i => i.ToString("D6")).ToArray();
        foreach (var id in ids)
            if (layout.Models.Collisions.Any(m => m.Name == "h" + id) || layout.Models.MapPieces.Any(m => m.Name == "m" + id))
                throw new InvalidDataException("Diagnostic model ID is already occupied");

        for (int i = 0; i < parts.Length; i++)
        {
            var collision = (MSBE.Part.Collision)collisionTemplate.DeepCopy();
            SetPlacement(collision, "h" + ids[i]);
            Enable(collision.Unk1);
            layout.Parts.Collisions.Add(collision);
            var model = (MSBE.Model.Collision)collisionModel.DeepCopy();
            model.Name = collision.ModelName; model.SibPath = "";
            layout.Models.Collisions.Add(model);
        }

        int collisionChecks = 0;
        foreach (var detail in new[] { "l", "h" })
        {
            var stem = $"map/m10/m10_01_00_00/{detail}10_01_00_00";
            var binder = BXF4.Read(get(stem + ".hkxbhd"), get(stem + ".hkxbdt"));
            var reader = new HavokBinarySerializer();
            reader.LoadCompendium(new MemoryStream(DCX.Decompress(binder.Files.Single(f => f.Name.EndsWith(".compendium.dcx")).Bytes)));
            var template = binder.Files.Single(f => f.Name.EndsWith("_001400.hkx.dcx"));
            var templateRaw = DCX.Decompress(template.Bytes, out var compression);
            var writer = new HavokBinarySerializer();
            // Preserve the standalone-file setup already checked in play with the synthetic cage.
            foreach (var file in binder.Files.Where(f => !f.Name.EndsWith(".compendium.dcx")))
            {
                var raw = DCX.Decompress(file.Bytes, out var fileCompression);
                using var stream = new MemoryStream();
                writer.Write(reader.Read(new MemoryStream(raw)), stream);
                file.Bytes = DCX.Compress(stream.ToArray(), fileCompression);
            }
            var meshes = detail == "l" ? low : high;
            var checkedBytes = new Dictionary<string, byte[]>();
            int fileId = binder.Files.Max(f => f.ID) + 1;
            for (int i = 0; i < parts.Length; i++)
            {
                var root = reader.Read(new MemoryStream(templateRaw));
                Build.ReplaceGeometry(root, meshes[i].Vertices, meshes[i].Triangles, detail + ids[i]);
                using var stream = new MemoryStream(); writer.Write(root, stream);
                var raw = stream.ToArray();
                GeometryCheck.CheckMesh(new HavokBinarySerializer().Read(new MemoryStream(raw)), meshes[i].Vertices, meshes[i].Triangles);
                string name = template.Name.Replace("_001400.hkx.dcx", $"_{ids[i]}.hkx.dcx");
                binder.Files.Add(new BinderFile(template.Flags, fileId++, name, DCX.Compress(raw, compression)));
                checkedBytes.Add(name, raw);
            }
            string headerPath = Path.Combine(mapDir, $"{detail}10_01_00_00.hkxbhd");
            string dataPath = Path.Combine(mapDir, $"{detail}10_01_00_00.hkxbdt");
            binder.Write(headerPath, dataPath);
            var readback = BXF4.Read(headerPath, dataPath);
            foreach (var (name, expected) in checkedBytes)
            {
                if (!DCX.Decompress(readback.Files.Single(f => f.Name == name).Bytes).AsSpan().SequenceEqual(expected))
                    throw new InvalidDataException("Collision binder changed checked mesh bytes");
                collisionChecks++;
            }
            Console.WriteLine($"PASS: {detail} collision set, {meshes.Length} placed parts, {meshes.Sum(m => m.Triangles.Count)} triangles");
        }

        var visualBytes = get("map/m10/m10_01_00_00/m10_01_00_00_000000.mapbnd.dcx");
        FirelinkVisuals.CheckFixture(visualBytes);
        var visuals = FirelinkVisuals.Build(Environment.GetEnvironmentVariable("AOG_DARK_SOULS_REMASTERED")!,
            get, visualBytes, layout, offset, output, mapDir);
        var layoutDir = Path.Combine(output, "mod", "map", "mapstudio");
        Directory.CreateDirectory(layoutDir);
        var layoutPath = Path.Combine(layoutDir, "m10_01_00_00.msb.dcx");
        layout.Write(layoutPath);
        var readLayout = MSBE.Read(layoutPath);
        FirelinkVisuals.VerifyPlacements(visuals, readLayout);
        foreach (var id in ids)
        {
            var collision = readLayout.Parts.Collisions.Single(p => p.ModelName == "h" + id);
            if (collision.Position != Vector3.Zero || collision.Rotation != Vector3.Zero || collision.Scale != Vector3.One
                || collision.HitFilterID != MSBE.Part.Collision.HitFilterType.Standard)
                throw new InvalidDataException("Written terrain placement changed");
        }
        var writtenPlayer = readLayout.Parts.Players.Single(p => p.Name == player.Name);
        if (writtenPlayer.Position != player.Position || writtenPlayer.Rotation != player.Rotation
            || Math.Abs(writtenPlayer.Position.Y - GroundBelow(low, writtenPlayer.Position) - 0.3f) > 0.001f)
            throw new InvalidDataException("Written player spawn is not supported");
        var eventDir = Path.Combine(output, "mod", "event");
        Directory.CreateDirectory(eventDir);
        var eventPath = Path.Combine(eventDir, "m10_01_00_00.emevd.dcx");
        events.Write(eventPath);
        FirelinkArrival.Verify(originalEvents, File.ReadAllBytes(eventPath), readLayout, writtenPlayer);
        Console.WriteLine("PASS: opening arrival and relocated respawn read back; pre-existing event data preserved");
        File.WriteAllText(Path.Combine(output, "age-of-gold-firelink.me3"), """
            profileVersion = "v1"
            savefile = "AgeOfGoldSpike.sl2"
            start_online = false
            [[supports]]
            game = "eldenring"
            [[packages]]
            path = 'mod/'
            """);
        var report = new
        {
            placements = parts.Length, collision_parts_checked = collisionChecks, visible_parts_checked = visuals.placements,
            spawn_supported = true, opening_arrival_checked = true, source_fingerprints = source.GetProperty("fingerprints"),
            source_visuals = visuals,
            host_fingerprints = hostFingerprints,
            tool_sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(FirelinkDemo).Assembly.Location))),
            excluded_roles = source.GetProperty("excluded_roles"),
            material_translation = "Source visual geometry and colour textures with basic host shading; see source_visuals.material_limits",
            source_materials = parts.Select(p => new
            {
                name = p.GetProperty("name").GetString(),
                low = p.GetProperty("l").GetProperty("meshes").EnumerateArray().Select(m => m.GetProperty("material").Clone()).ToArray(),
                high = p.GetProperty("h").GetProperty("meshes").EnumerateArray().Select(m => m.GetProperty("material").Clone()).ToArray(),
            }).ToArray(),
            low_zero_area_faces = low.Sum(m => m.ZeroAreaFaces), high_zero_area_faces = high.Sum(m => m.ZeroAreaFaces),
            source_winding_reversed = true,
            geometry_sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(geometryPath))),
        };
        File.WriteAllText(Path.Combine(output, "check.json"), JsonSerializer.Serialize(report));
        Console.WriteLine("PASS: map placements and supported spawn read back");
    }

    static void SetPlacement(MSBE.Part part, string model)
    {
        part.Name = model + "_0000"; part.ModelName = model; part.SibPath = "";
        part.Position = Vector3.Zero; part.Rotation = Vector3.Zero; part.Scale = Vector3.One;
        part.EntityID = 0; Array.Fill(part.EntityGroupIDs, 0u);
    }

    static void Enable(MSBE.Part.UnkStruct1 groups)
    {
        Array.Fill(groups.DisplayGroups, uint.MaxValue);
        Array.Fill(groups.DrawGroups, uint.MaxValue);
        Array.Fill(groups.CollisionMask, uint.MaxValue);
    }

    static Vector3 V(JsonElement value)
    {
        var result = new Vector3(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
        if (!float.IsFinite(result.X) || !float.IsFinite(result.Y) || !float.IsFinite(result.Z))
            throw new InvalidDataException("Nonfinite source vector");
        return result;
    }

    static Mesh ReadMesh(JsonElement part, string detail, Vector3 offset)
    {
        // lazy: inspected solid placements only translate; add Euler composition when a rotated placement is needed.
        if (V(part.GetProperty("rotation")) != Vector3.Zero || V(part.GetProperty("scale")) != Vector3.One)
            throw new InvalidDataException("This diagnostic requires unrotated, unit-scale source placements");
        if (part.GetProperty("role").GetString() != "solid") throw new InvalidDataException("Expected ordinary solid terrain");
        var position = V(part.GetProperty("position")) + offset;
        var data = part.GetProperty(detail);
        var t = data.GetProperty("body_transform").EnumerateArray().Select(v => v.GetSingle()).ToArray();
        if (t.Length != 16 || t.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Invalid source body transform");
        // Havok stores basis columns consecutively; System.Numerics uses row-vector multiplication.
        var body = new Matrix4x4(t[0],t[1],t[2],t[3],t[4],t[5],t[6],t[7],t[8],t[9],t[10],t[11],t[12],t[13],t[14],t[15]);
        var vertices = new List<Vector3>(); var triangles = new List<(int, int, int)>();
        int zeroAreaFaces = 0;
        foreach (var mesh in data.GetProperty("meshes").EnumerateArray())
        {
            if (mesh.GetProperty("flip_alternate_triangles").GetInt32() != 0 || V(mesh.GetProperty("extrusion")) != Vector3.Zero)
                throw new InvalidDataException("Source winding flags or extrusion require translation");
            var q = mesh.GetProperty("rotation");
            var rotation = new Quaternion(q[0].GetSingle(), q[1].GetSingle(), q[2].GetSingle(), q[3].GetSingle());
            var scale = V(mesh.GetProperty("scale")); var translation = V(mesh.GetProperty("translation"));
            int first = vertices.Count;
            foreach (var vertex in mesh.GetProperty("vertices").EnumerateArray())
                vertices.Add(Vector3.Transform(Vector3.Transform(V(vertex) * scale, rotation) + translation, body) + position);
            foreach (var face in mesh.GetProperty("faces").EnumerateArray())
            {
                int a = face[0].GetInt32(), b = face[1].GetInt32(), c = face[2].GetInt32();
                if (a < 0 || b < 0 || c < 0 || a >= vertices.Count - first || b >= vertices.Count - first || c >= vertices.Count - first)
                    throw new InvalidDataException("Source triangle index is outside its submesh");
                a += first; b += first; c += first;
                if (Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).LengthSquared() == 0)
                {
                    zeroAreaFaces++;
                    continue;
                }
                // Source ground is clockwise; our checked host writer takes counter-clockwise faces.
                triangles.Add((a, c, b));
            }
        }
        return new(vertices, triangles, zeroAreaFaces);
    }

    static float GroundBelow(IEnumerable<Mesh> meshes, Vector3 point)
    {
        float ground = float.NegativeInfinity;
        foreach (var mesh in meshes)
            foreach (var (ia, ib, ic) in mesh.Triangles)
            {
                var a = mesh.Vertices[ia]; var b = mesh.Vertices[ib]; var c = mesh.Vertices[ic];
                var n = Vector3.Cross(b - a, c - a);
                if (n.Y <= 0) continue;
                float u = ((point.X - a.X) * (c.Z - a.Z) - (point.Z - a.Z) * (c.X - a.X)) / -n.Y;
                float v = ((b.X - a.X) * (point.Z - a.Z) - (b.Z - a.Z) * (point.X - a.X)) / -n.Y;
                if (u < 0 || v < 0 || u + v > 1) continue;
                float y = a.Y + u * (b.Y - a.Y) + v * (c.Y - a.Y);
                if (y <= point.Y) ground = Math.Max(ground, y);
            }
        if (!float.IsFinite(ground)) throw new InvalidDataException("No upward-facing terrain beneath spawn");
        return ground;
    }

    internal static void CheckVisibleBounds(FLVER2.Mesh mesh)
    {
        // In the checked host version, Min/Max/Unk store half-extents, Z-X-Y Euler angles and centre.
        var box = mesh.BoundingBox;
        if (box is null || new[] { box.Min, box.Max, box.Unk }.Any(v =>
            !float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z))
            || box.Min.X < 0 || box.Min.Y < 0 || box.Min.Z < 0)
            throw new InvalidDataException("Visible mesh culling box requires finite, nonnegative half-extents");
        var rotation = Matrix4x4.CreateRotationZ(box.Max.Z) * Matrix4x4.CreateRotationX(box.Max.X)
            * Matrix4x4.CreateRotationY(box.Max.Y);
        var inverse = Matrix4x4.Transpose(rotation);
        foreach (var vertex in mesh.Vertices)
        {
            var local = Vector3.Abs(Vector3.TransformNormal(vertex.Position - box.Unk, inverse));
            if (local.X > box.Min.X + 0.001f || local.Y > box.Min.Y + 0.001f || local.Z > box.Min.Z + 0.001f)
                throw new InvalidDataException("Visible mesh culling box does not contain its vertices");
        }
    }
}
