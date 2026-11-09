using System.Numerics;
using System.Text.Json;
using SoulsFormats;

namespace AgeOfGold.Collision;

internal static class SourceCollisionReader
{
    internal sealed record Body(string HavokVersion, float[] BodyTransform, Mesh[] Meshes);
    internal sealed record Mesh(float[][] Vertices, int[][] Faces, int IndexBits, int Material,
        float[] Translation, float[] Rotation, float[] Scale, int FlipAlternateTriangles, float[] Extrusion);
    internal sealed record Placement(string Name, string High, string Low, string Role, bool Disabled,
        float[] Position, float[] Rotation, float[] Scale, uint[] DisplayGroups);
    internal sealed record Spawn(float[] Position, float[] Rotation);
    internal sealed record Scene(int Format, SortedDictionary<string, Body> Models, Placement[] Placements, Spawn Spawn);

    internal static byte[] ReadFirelink(Install source) => JsonSerializer.SerializeToUtf8Bytes(ReadScene(source),
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

    internal static Scene ReadScene(Install source)
    {
        var models = new SortedDictionary<string, Body>(StringComparer.Ordinal);
        foreach (string detail in new[] { "h", "l" })
        {
            string stem = $"map/m10_02_00_00/{detail}10_02_00_00";
            var binder = BXF3.Read(source.Read(stem + ".hkxbhd"), source.Read(stem + ".hkxbdt"));
            foreach (var file in binder.Files)
            {
                string name = file.Name.Replace('\\', '/').Split('/')[^1];
                if (!name.StartsWith(detail, StringComparison.Ordinal) || !name.EndsWith("A10.hkx.dcx", StringComparison.Ordinal))
                    throw new InvalidDataException($"Unexpected source collision filename: {name}");
                models.Add(name, Read(DCX.Decompress(file.Bytes)));
            }
        }
        var layout = MSB1.Read(source.Read("map/MapStudio/m10_02_00_00.msb"));
        var placements = layout.Parts.Collisions.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p =>
        {
            var model = layout.Models.Collisions.Single(m => m.Name == p.ModelName);
            var names = new[] { "h", "l" }.Select(d => d + model.Name[1..] + "A10.hkx.dcx").ToArray();
            if (names.Any(n => !models.ContainsKey(n))) throw new InvalidDataException($"Missing source collision model for {p.Name}");
            return new Placement(p.Name, names[0], names[1], FirelinkSource.Role(p.HitFilterID),
                p.DisableStart, Vector(p.Position), Vector(p.Rotation), Vector(p.Scale), p.DispGroups);
        }).ToArray();
        var spawnEvent = layout.Events.SpawnPoints.Single();
        var spawn = layout.Regions.GetEntries().Single(r => r.Name == spawnEvent.SpawnPointName);
        return new Scene(1, models, placements, new Spawn(Vector(spawn.Position), Vector(spawn.Rotation)));
    }

    internal static Body Read(byte[] raw)
    {
        var root = RequireType(new SourceTagfile(raw).Root, "hkRootLevelContainer");
        var physics = RequireType(One(root.Field("namedVariants")).Field("variant"), "hkpPhysicsData");
        var system = RequireType(One(physics.Field("systems")), "hkpPhysicsSystem");
        var body = RequireType(One(system.Field("rigidBodies")), "hkpRigidBody");
        var mopp = RequireType(body.Field("collidable").Field("shape"), "hkpMoppBvTreeShape");
        var shape = RequireType(mopp.Field("child").Field("childShape"), "CustomParamStorageExtendedMeshShape");
        if (shape.Field("shapesSubparts").Elements().Length != 0 || shape.Field("shapestorage").Elements().Length != 0)
            throw new InvalidDataException("Source collision contains non-triangle shapes");
        var storage = shape.Field("meshstorage").Elements();
        var materials = shape.Field("materialArray").Elements();
        var subparts = shape.Field("trianglesSubparts").Elements();
        if (storage.Length == 0 || storage.Length != materials.Length || storage.Length != subparts.Length)
            throw new InvalidDataException("Source collision submesh and material counts differ");
        var meshes = new Mesh[storage.Length];
        for (int i = 0; i < storage.Length; i++)
        {
            var vertices = storage[i].Field("vertices").Elements().Select(v => Vector(v, 4)).ToArray();
            var streams = new[] { 8, 16, 32 }.Select(bits => (Bits: bits,
                Values: storage[i].Field("indices" + bits).Elements().Select(v => checked((int)v.Integer())).ToArray()))
                .Where(s => s.Values.Length != 0).ToArray();
            if (streams.Length != 1 || streams[0].Values.Length % 4 != 0)
                throw new InvalidDataException("Source collision needs one four-corner index stream");
            var faces = streams[0].Values.Chunk(4).ToArray();
            var part = subparts[i];
            if (vertices.Length != part.Field("numVertices").Integer() || faces.Length != part.Field("numTriangleShapes").Integer()
                || faces.Any(f => f.Take(3).Any(v => v < 0 || v >= vertices.Length)))
                throw new InvalidDataException("Source collision geometry counts or indices are invalid");
            var transform = part.Field("transform");
            meshes[i] = new Mesh(vertices, faces, streams[0].Bits,
                checked((int)materials[i].Field("materialNameData").Integer()),
                Vector(transform.Field("translation"), 4), Vector(transform.Field("rotation"), 4), Vector(transform.Field("scale"), 4),
                checked((int)part.Field("flipAlternateTriangles").Integer()), Vector(part.Field("extrusion"), 4));
        }
        return new Body("20150100", Vector(body.Field("motion").Field("motionState").Field("transform"), 16), meshes);
    }

    private static SourceTagfile.Value One(SourceTagfile.Value array)
    {
        var values = array.Elements();
        return values.Length == 1 ? values[0] : throw new InvalidDataException("Expected one source collision physics object");
    }

    private static SourceTagfile.Value RequireType(SourceTagfile.Value value, string name) => value.TypeName == name
        ? value : throw new InvalidDataException($"Unsupported source collision type: {value.TypeName}; expected {name}");

    private static float[] Vector(SourceTagfile.Value value, int size)
    {
        var result = value.Floats();
        return result.Length == size ? result : throw new InvalidDataException("Source collision vector has an unexpected size");
    }

    private static float[] Vector(Vector3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new InvalidDataException("Non-finite collision placement");
        return [value.X, value.Y, value.Z];
    }
}
