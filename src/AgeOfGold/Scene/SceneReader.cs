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

    // The patch binder overrides the base one. Within a binder a name can appear twice with different contents (the remaster
    // added definitions beside older ones of the same name); the first entry is used.
    // lazy: first-entry-wins is inferred from how resource registries usually treat a repeated name, not observed in the source game.
    private MTD Definition(string name)
    {
        if (definitions.TryGetValue(name, out var known)) return known;
        byte[]? chosen = null;
        foreach (var (path, files) in definitionBinders)
        {
            var entries = files[name].ToArray();
            if (entries.Length != 0) chosen = entries[0];
        }
        if (chosen is null) throw new InvalidDataException($"Missing material definition: {name}");
        return definitions[name] = MTD.Read(chosen);
    }

    /// <summary>
    /// What a definition multiplies a surface's colour map by, as factors on linear light: where the surface is not
    /// metal, and where it is. The source multiplies by the colour times its power before its display curve.
    /// </summary>
    internal (float[] Plain, float[] Metal) SurfaceColours(string definitionPath)
    {
        var definition = Definition(Stem(definitionPath));
        // The remastered source reads metal and roughness from the specular map unless a definition says otherwise.
        if (definition.Params.SingleOrDefault(p => p.Name == "g_MaterialWorkflow") is { Value: not 0 }) throw new InvalidDataException("Unclassified material workflow: " + definitionPath);
        float[] Colour(string name)
        {
            if (definition.Params.SingleOrDefault(p => p.Name == name)?.Value is not float[] { Length: 3 } colour || definition.Params.SingleOrDefault(p => p.Name == name + "Power")?.Value is not float power
                || colour.Append(power).Any(v => !float.IsFinite(v) || v < 0))
                throw new InvalidDataException($"Material definition lacks a colour and its power, {name}: {definitionPath}");
            return colour.Select(v => MathF.Pow(v * power, 2.2f)).ToArray();
        }
        return (Colour("g_DiffuseMapColor"), Colour("g_SpecularMapColor"));
    }

    /// <summary>An object's model in the same shape as a map piece's; its textures come from <see cref="ReadObjectTextures"/>.</summary>
    internal byte[] ReadObject(string name) => JsonSerializer.SerializeToUtf8Bytes(Model(name, ObjectFile(name, ".flver")));

    /// <summary>The textures an object holds itself; an object that holds none wears textures of the map's packages.</summary>
    internal (string Name, byte[] Bytes)[] ReadObjectTextures(string name) => ObjectFiles(name, ".tpf").SelectMany(TextureReader.ReadPackage).ToArray();

    /// <summary>A character's model at the first frame of one of its animations, in the same shape as a map piece's.</summary>
    internal byte[] ReadCharacter(string name, string animation)
    {
        byte[] Member(string binder, string leaf) => BND3.Read(source.Read($"chr/{Install.Relative(name)}.{binder}.dcx")).Files
            .SingleOrDefault(f => f.Name.Replace('\\', '/').EndsWith("/" + leaf, StringComparison.OrdinalIgnoreCase))?.Bytes
            ?? throw new InvalidDataException($"Source character {name} has no {leaf}");
        var skeleton = Animation.SourcePoseReader.ReadSkeleton(Member("anibnd", "Skeleton.hkx"));
        var pose = Animation.SourcePoseReader.ReadFirstFrame(Member("anibnd", animation + ".hkx"), skeleton);
        return JsonSerializer.SerializeToUtf8Bytes(Model(name, Member("chrbnd", name + ".flver"), model => PosedGeometry.Skin(model, skeleton, pose)));
    }

    /// <summary>
    /// A character's textures: those held in its binder, those in the package beside it, and the body textures all
    /// characters share. Some characters hold only textures, which the models of their family wear.
    /// </summary>
    internal (string Name, byte[] Bytes)[] ReadCharacterTextures(string name)
    {
        var binder = BND3.Read(source.Read($"chr/{Install.Relative(name)}.chrbnd.dcx"));
        var held = binder.Files.Where(f => f.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase)).SelectMany(f => TextureReader.ReadPackage(f.Bytes)).ToList();
        var header = binder.Files.Where(f => f.Name.EndsWith(".chrtpfbhd", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (header.Length > 1) throw new InvalidDataException($"Source character {name} has more than one texture package");
        if (header.Length == 1) held.AddRange(BXF3.Read(header[0].Bytes, source.Read($"chr/{Install.Relative(name)}.chrtpfbdt")).Files.SelectMany(f => TextureReader.ReadPackage(f.Bytes)));
        if (held.Count == 0) throw new InvalidDataException($"Source character {name} holds no textures");
        return held.Concat(TextureReader.ReadPackage(source.Read("parts/Common_Body.tpf"))).ToArray();
    }

    /// <summary>Character placements in the shape of <see cref="ReadPlacements"/>.</summary>
    internal byte[] ReadCharacterPlacements() => JsonSerializer.SerializeToUtf8Bytes(Placements(layout.Parts.Enemies));

    /// <summary>Where effects attach to an object, in the object's own space.</summary>
    internal Vector3 ReadObjectAttachment(string name)
    {
        var (model, _) = ModelReader.Read(ObjectFile(name, ".flver"));
        // lazy: one attachment point on an unturned node; several points need the reference each effect asks for.
        if (model.Dummies.Count != 1 || model.Dummies[0].ParentBoneIndex < 0 || model.Dummies[0].ParentBoneIndex >= model.Nodes.Count)
            throw new InvalidDataException($"Source object {name} does not have exactly one attachment point");
        var node = model.Nodes[model.Dummies[0].ParentBoneIndex];
        if (node.ParentIndex != -1 || node.Rotation != Vector3.Zero) throw new InvalidDataException($"Source object {name} attaches effects to a turned or nested node");
        return model.Dummies[0].Position * node.Scale + node.Translation;
    }

    // An object keeps its model and textures together in one binder.
    private byte[][] ObjectFiles(string name, string extension) => BND3.Read(source.Read($"obj/{Install.Relative(name)}.objbnd.dcx")).Files
        .Where(f => f.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)).Select(f => f.Bytes).ToArray();

    // The member named after the object itself: a breakable object also holds a model of its pieces under a longer name.
    private byte[] ObjectFile(string name, string extension)
    {
        var files = BND3.Read(source.Read($"obj/{Install.Relative(name)}.objbnd.dcx")).Files
            .Where(f => f.Name.Replace('\\', '/').Split('/')[^1].Equals(name + extension, StringComparison.OrdinalIgnoreCase)).ToArray();
        return files.Length == 1 ? files[0].Bytes : throw new InvalidDataException($"Source object {name} does not have one {extension} member of its own name");
    }

    private object Model(string name)
    {
        var candidates = paths.Where(p => Path.GetFileName(p).StartsWith(name + "A", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length != 1) throw new InvalidDataException($"Expected exactly one source model for {name}, found {candidates.Length}");
        return Model(name, source.Read(Path.GetRelativePath(source.Root, candidates[0])));
    }

    private object Model(string name, byte[] bytes, Func<FLVER2, Matrix4x4[]>? pose = null)
    {
        var (model, repaired) = ModelReader.Read(bytes);
        var skin = pose?.Invoke(model);
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
            // Besides the full-detail set a mesh may carry lower-detail sets and, on characters, copies drawn for motion blur.
            const FLVER2.FaceSet.FSFlags known = FLVER2.FaceSet.FSFlags.LodLevel1 | FLVER2.FaceSet.FSFlags.LodLevel2 | FLVER2.FaceSet.FSFlags.MotionBlur;
            if (mesh.FaceSets.Any(f => (f.Flags & ~known) != 0))
                throw new InvalidDataException($"Unclassified face-set role in {name}/{index}");
            var faces = mesh.FaceSets.Single(f => f.Flags == FLVER2.FaceSet.FSFlags.None);
            var indices = faces.Triangulate(mesh.Vertices.Count < 65536);
            if (indices.Count % 3 != 0 || indices.Any(i => i < 0 || i >= mesh.Vertices.Count))
                throw new InvalidDataException($"Invalid triangles in {name}/{index}");
            bool hasSecondaryTangent = model.BufferLayouts[mesh.VertexBuffers.Single().LayoutIndex]
                .Any(member => member.Semantic == FLVER.LayoutSemantic.Bitangent);
            var vertices = mesh.Vertices.Select(vertex =>
            {
                // Scenery vertices follow one fixed node; a posed character's follow the blend of their nodes' posed transforms.
                var blend = skin is null ? default : PosedGeometry.Blend(skin, mesh, vertex);
                Vector3 Direction(Vector3 v) { var turned = Vector3.TransformNormal(v, blend); return turned.LengthSquared() == 0 ? turned : Vector3.Normalize(turned); }
                var geometry = skin is null ? FirelinkRigidGeometry.Bake(model, mesh, vertex) : (Position: Vector3.Transform(vertex.Position, blend), Normal: Direction(vertex.Normal));
                Vector4 Tangent(Vector4 t)
                {
                    if (skin is null) return FirelinkRigidGeometry.BakeTangent(model, mesh, vertex, t);
                    var across = Direction(new(t.X, t.Y, t.Z));
                    across -= geometry.Normal * Vector3.Dot(across, geometry.Normal);
                    // A skinned model can carry a degenerate tangent; any direction across the normal then completes the frame.
                    if (across.LengthSquared() < 1e-12f) across = Vector3.Cross(geometry.Normal, MathF.Abs(geometry.Normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX);
                    return new(Vector3.Normalize(across), t.W < 0 ? -1 : 1);
                }
                return new
                {
                    position = Vector(geometry.Position), normal = Vector(geometry.Normal),
                    uvs = vertex.UVs.Select(Vector).ToArray(),
                    tangents = vertex.Tangents.Select(t => Vector(Tangent(t))).ToArray(),
                    secondaryTangent = hasSecondaryTangent ? Vector(Tangent(vertex.Bitangent)) : null,
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
