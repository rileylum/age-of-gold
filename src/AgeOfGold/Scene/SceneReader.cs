using System.Numerics;
using System.Text.Json;
using SoulsFormats;

namespace AgeOfGold.Scene;

/// <summary>Reads the source map's scenery one model at a time, so a build never holds every model at once.</summary>
internal sealed class SceneReader
{
    private readonly Install source;
    private readonly MSB1 layout;
    private readonly string[] paths;
    private readonly List<(string Path, ILookup<string, byte[]> Files)> definitionBinders = [];
    private readonly Dictionary<string, MTD> definitions = new(StringComparer.OrdinalIgnoreCase);

    internal string[] Models { get; }

    internal SceneReader(Install source)
    {
        this.source = source;
        layout = MSB1.Read(source.Read("map/MapStudio/m10_02_00_00.msb"));
        paths = Directory.GetFiles(source.Locate("map/m10_02_00_00"), "*.flver.dcx");
        Models = layout.Parts.MapPieces.Select(p => p.ModelName).Distinct().Order(StringComparer.Ordinal).ToArray();
        foreach (var path in new[] { "mtd/Mtd.mtdbnd.dcx", "mtd/MtdPatch.mtdbnd.dcx" })
            definitionBinders.Add((path, BND3.Read(source.Read(path)).Files.ToLookup(f => Stem(f.Name), f => f.Bytes, StringComparer.OrdinalIgnoreCase)));
    }

    internal static byte[] Read(Install source)
    {
        var reader = new SceneReader(source);
        var models = reader.Models.Select(reader.Model).ToArray();
        return JsonSerializer.SerializeToUtf8Bytes(new { format = 1, models, placements = reader.Placements() });
    }

    internal byte[] ReadModel(string name) => JsonSerializer.SerializeToUtf8Bytes(Model(name));

    internal byte[] ReadPlacements() => JsonSerializer.SerializeToUtf8Bytes(Placements());

    /// <summary>Object placements in the shape of <see cref="ReadPlacements"/>.</summary>
    internal byte[] ReadObjectPlacements() => JsonSerializer.SerializeToUtf8Bytes(Placements(layout.Parts.Objects));

    private object Placements() => Placements(layout.Parts.MapPieces);

    private static object Placements(IEnumerable<MSB1.Part> parts) => parts.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new
            {
                name = p.Name, model = p.ModelName, position = Vector(p.Position), rotation = Vector(p.Rotation), scale = Vector(p.Scale),
                lighting = p.LightID, fog = p.FogID, scattering = p.ScatterID, playerLight = p.LanternID, disablePointLights = p.DisablePointLightEffect,
                drawGroups = p.DrawGroups,
            }).ToArray();

    // The patch binder overrides the base one; two different entries for one name inside a binder are ambiguous.
    private MTD Definition(string name)
    {
        if (definitions.TryGetValue(name, out var known)) return known;
        byte[]? chosen = null;
        foreach (var (path, files) in definitionBinders)
        {
            var entries = files[name].ToArray();
            if (entries.Skip(1).Any(e => !e.AsSpan().SequenceEqual(entries[0])))
                throw new InvalidDataException($"Conflicting material definitions in {path}: {name}");
            if (entries.Length != 0) chosen = entries[0];
        }
        if (chosen is null) throw new InvalidDataException($"Missing material definition: {name}");
        return definitions[name] = MTD.Read(chosen);
    }

    /// <summary>An object's model in the same shape as a map piece's; its textures come from <see cref="ReadObjectTextures"/>.</summary>
    internal byte[] ReadObject(string name) => JsonSerializer.SerializeToUtf8Bytes(Model(name, ObjectFile(name, ".flver").Single()));

    internal (string Name, byte[] Bytes)[] ReadObjectTextures(string name) => ObjectFile(name, ".tpf").SelectMany(TextureReader.ReadPackage).ToArray();

    // An object keeps its model and textures together in one binder.
    private byte[][] ObjectFile(string name, string extension)
    {
        var files = BND3.Read(source.Read($"obj/{Install.Relative(name)}.objbnd.dcx")).Files.Where(f => f.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)).ToArray();
        return files.Length != 0 ? files.Select(f => f.Bytes).ToArray() : throw new InvalidDataException($"Source object {name} has no {extension} member");
    }

    private object Model(string name)
    {
        var candidates = paths.Where(p => Path.GetFileName(p).StartsWith(name + "A", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length != 1) throw new InvalidDataException($"Expected exactly one source model for {name}, found {candidates.Length}");
        return Model(name, source.Read(Path.GetRelativePath(source.Root, candidates[0])));
    }

    private object Model(string name, byte[] bytes)
    {
        var (model, repaired) = ModelReader.Read(bytes);
        var surfaces = new List<object>();
        for (int index = 0; index < model.Meshes.Count; index++)
        {
            var mesh = model.Meshes[index];
            if (mesh.MaterialIndex < 0 || mesh.MaterialIndex >= model.Materials.Count)
                throw new InvalidDataException($"Invalid material index in {name}/{index}");
            var material = model.Materials[mesh.MaterialIndex];
            var definition = Definition(Stem(material.MTD));
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
            return new { name, repairedLayouts = repaired, surfaces };
    }

    private static string Stem(string path) => Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
    private static float[] Vector(Vector3 v) => [v.X, v.Y, v.Z];
    private static float[] Vector(Vector4 v) => [v.X, v.Y, v.Z, v.W];
}
