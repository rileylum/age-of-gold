using System.Numerics;
using System.Text.Json;
using SoulsFormats;

namespace AgeOfGold.Scene;

internal static class SceneReader
{
    internal static byte[] Read(Install source)
    {
        var layout = MSB1.Read(source.Read("map/MapStudio/m10_02_00_00.msb"));
        var paths = Directory.GetFiles(source.Locate("map/m10_02_00_00"), "*.flver.dcx");
        var sourceModels = new List<(string Name, FLVER2 Model, int Repaired)>();
        foreach (var name in layout.Parts.MapPieces.Select(p => p.ModelName).Distinct().Order(StringComparer.Ordinal))
        {
            var candidates = paths.Where(p => Path.GetFileName(p).StartsWith(name + "A", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length != 1) throw new InvalidDataException($"Expected exactly one source model for {name}, found {candidates.Length}");
            var (model, repaired) = ModelReader.Read(source.Read(Path.GetRelativePath(source.Root, candidates[0])));
            sourceModels.Add((name, model, repaired));
        }
        var needed = sourceModels.SelectMany(m => m.Model.Materials).Select(m => Stem(m.MTD)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var definitions = new Dictionary<string, MTD>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in new[] { "mtd/Mtd.mtdbnd.dcx", "mtd/MtdPatch.mtdbnd.dcx" })
        {
            var binder = BND3.Read(source.Read(path));
            var local = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in binder.Files)
            {
                var name = Stem(file.Name);
                if (!needed.Contains(name)) continue;
                if (local.TryGetValue(name, out var previous) && !previous.AsSpan().SequenceEqual(file.Bytes))
                    throw new InvalidDataException($"Conflicting material definitions in {path}: {name}");
                local[name] = file.Bytes;
                definitions[name] = MTD.Read(file.Bytes);
            }
        }
        var models = new List<object>();
        foreach (var (name, model, repaired) in sourceModels)
        {
            var surfaces = new List<object>();
            for (int index = 0; index < model.Meshes.Count; index++)
            {
                var mesh = model.Meshes[index];
                if (mesh.MaterialIndex < 0 || mesh.MaterialIndex >= model.Materials.Count)
                    throw new InvalidDataException($"Invalid material index in {name}/{index}");
                var material = model.Materials[mesh.MaterialIndex];
                if (!definitions.TryGetValue(Stem(material.MTD), out var definition))
                    throw new InvalidDataException($"Missing material definition: {material.MTD}");
                var blend = definition.Params.Single(p => p.Name == "g_BlendMode");
                if (blend.Value is not int mode || mode is not (0 or 1 or 2 or 3 or 4 or 32))
                    throw new InvalidDataException($"Unclassified material blend mode: {material.MTD}");
                if (mesh.FaceSets.Any(f => f.Flags is not (FLVER2.FaceSet.FSFlags.None or FLVER2.FaceSet.FSFlags.LodLevel1 or FLVER2.FaceSet.FSFlags.LodLevel2)))
                    throw new InvalidDataException($"Unclassified face-set role in {name}/{index}");
                var faces = mesh.FaceSets.Single(f => f.Flags == FLVER2.FaceSet.FSFlags.None);
                var indices = faces.Triangulate(mesh.Vertices.Count < 65536);
                if (indices.Count % 3 != 0 || indices.Any(i => i < 0 || i >= mesh.Vertices.Count))
                    throw new InvalidDataException($"Invalid triangles in {name}/{index}");
                bool hasSecondaryTangent = model.BufferLayouts[mesh.VertexBuffers.Single().LayoutIndex]
                    .Any(member => member.Semantic == FLVER.LayoutSemantic.Bitangent);
                var vertices = mesh.Vertices.Select(vertex =>
                {
                    var geometry = FirelinkRigidGeometry.Bake(model, mesh, vertex);
                    return new
                    {
                        position = Vector(geometry.Position), normal = Vector(geometry.Normal),
                        uvs = vertex.UVs.Select(Vector).ToArray(),
                        tangents = vertex.Tangents.Select(t => Vector(FirelinkRigidGeometry.BakeTangent(model, mesh, vertex, t))).ToArray(),
                        secondaryTangent = hasSecondaryTangent ? Vector(FirelinkRigidGeometry.BakeTangent(model, mesh, vertex, vertex.Bitangent)) : null,
                        colours = vertex.Colors.Select(c => new[] { c.R, c.G, c.B, c.A }).ToArray(),
                    };
                }).ToArray();
                surfaces.Add(new
                {
                    index, indices, cullBackfaces = faces.CullBackfaces, vertices,
                    material = new
                    {
                        name = material.Name, definition = Stem(material.MTD), shader = definition.ShaderPath, blendMode = mode,
                        parameters = new SortedDictionary<string, object>(definition.Params.ToDictionary(p => p.Name, p => p.Value), StringComparer.Ordinal),
                        textures = material.Textures.Select(t => new { input = t.ParamName, texture = t.Path == "" ? "" : Stem(t.Path), scale = new[] { t.TilingScale.X, t.TilingScale.Y } }).ToArray(),
                        coordinates = definition.Textures.Select(t => new { input = t.Type, channel = t.UVNumber - 1 }).ToArray(),
                    },
                });
            }
            models.Add(new { name, repairedLayouts = repaired, surfaces });
        }
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = 1, models,
            placements = layout.Parts.MapPieces.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new
            {
                name = p.Name, model = p.ModelName, position = Vector(p.Position), rotation = Vector(p.Rotation), scale = Vector(p.Scale),
                lighting = p.LightID, playerLight = p.LanternID, disablePointLights = p.DisablePointLightEffect,
                drawGroups = p.DrawGroups,
            }).ToArray(),
        });
    }

    private static string Stem(string path) => Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
    private static float[] Vector(Vector3 v) => [v.X, v.Y, v.Z];
    private static float[] Vector(Vector4 v) => [v.X, v.Y, v.Z, v.W];
}
