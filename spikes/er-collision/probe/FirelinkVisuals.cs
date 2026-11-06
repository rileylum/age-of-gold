using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using SoulsFormats;

// Spike only: source visual geometry and colour textures, with explicit diagnostic material limits.
static class FirelinkVisuals
{
    record Vertex(Vector3 Position, Vector3 Normal, Vector2 UV);
    record Surface(Vertex[] Vertices, int[] Indices, int Material, bool CullBackfaces);
    /// <summary>A source placement expressed in the host map's coordinates.</summary>
    public record Placement(string Name, string Model, Vector3 Position, Vector3 Rotation, Vector3 Scale);
    /// <summary>Checked visual output, input fingerprints and the limits of its diagnostic materials.</summary>
    public record Result(int models, int placements, int textures, int surfaces, bool geometry_checked,
        bool texture_payloads_checked, SortedDictionary<string, string> source_fingerprints,
        string[] material_limits, SortedDictionary<string, int> excluded_surfaces, int vertex_layouts_repaired,
        [property: JsonIgnore] Placement[] Expected);

    /// <summary>Write source visual models and colour textures with basic opaque and cutout host materials.</summary>
    public static Result Build(string source, Func<string, byte[]> host, byte[] templateBytes,
        MSBE layout, Vector3 offset, string output, string mapDir)
    {
        var fingerprints = new SortedDictionary<string, string>(StringComparer.Ordinal);
        byte[] read(string relative)
        {
            var bytes = File.ReadAllBytes(Path.Combine(source, relative));
            fingerprints[relative] = Convert.ToHexString(SHA256.HashData(bytes));
            return bytes;
        }
        var sourceLayout = MSB1.Read(read("map/MapStudio/m10_02_00_00.msb"));
        var definitions = new Dictionary<string, MTD>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in new[] { "mtd/Mtd.mtdbnd.dcx", "mtd/MtdPatch.mtdbnd.dcx" })
            foreach (var file in BND3.Read(read(path)).Files) definitions[Stem(file.Name)] = MTD.Read(file.Bytes);

        var templateBinder = BND4.Read(templateBytes);
        var nativeFile = templateBinder.Files.Single(f => f.Name.EndsWith(".flver"));
        var native = FLVER2.Read(nativeFile.Bytes);
        var hostDefinitions = BND4.Read(host("material/allmaterial.matbinbnd.dcx"));
        var basic = new Dictionary<bool, (string Path, string Sampler)>();
        foreach (bool cutout in new[] { false, true })
        {
            var file = hostDefinitions.Files.Single(f => f.Name.Replace('\\', '/')
                .EndsWith("/Map_Base/matxml/M[A]" + (cutout ? "_Edge" : "") + ".matbin", StringComparison.OrdinalIgnoreCase));
            var definition = MATBIN.Read(file.Bytes);
            var sampler = definition.Samplers.Single();
            if (!sampler.Type.Contains("AlbedoMap") || sampler.Path != "")
                throw new InvalidDataException("Basic host material must have one overridable colour sampler");
            int prefixEnd = native.Materials[0].MTD.IndexOf("\\mtd\\", StringComparison.OrdinalIgnoreCase);
            if (prefixEnd < 0) throw new InvalidDataException("Host material path has no material-definition root");
            var nativePrefix = native.Materials[0].MTD[..(prefixEnd + 5)];
            var relative = file.Name[(file.Name.IndexOf("\\matbin\\", StringComparison.OrdinalIgnoreCase) + 8)..];
            basic[cutout] = (nativePrefix + Path.ChangeExtension(relative, ".matxml"), sampler.Type);
        }
        var nativeDefinition = MATBIN.Read(hostDefinitions.Files.Single(f =>
            Stem(f.Name).Equals(Stem(native.Materials[0].MTD), StringComparison.OrdinalIgnoreCase)).Bytes);
        var nativeColour = nativeDefinition.Samplers.First(t => t.Type.Contains("AlbedoMap"));
        var nativeTextureName = Stem(nativeColour.Path);
        var nativeTextureStem = nativeTextureName[..nativeTextureName.LastIndexOf('_')].ToLowerInvariant();
        var textureTemplate = TPF.Read(host($"asset/aet/{nativeTextureStem[..6]}/{nativeTextureStem}.tpf.dcx"));

        var modelPaths = Directory.GetFiles(Path.Combine(source, "map/m10_02_00_00"), "*.flver.dcx");
        var models = new List<(string Source, string Id, Surface[] Surfaces, List<(string Texture, bool Cutout)> Materials)>();
        var exclusions = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var textureNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        int layoutRepairs = 0;
        foreach (var name in sourceLayout.Parts.MapPieces.Select(p => p.ModelName).Distinct().Order(StringComparer.Ordinal))
        {
            var path = modelPaths.Single(p => Path.GetFileName(p).StartsWith(name + "A", StringComparison.OrdinalIgnoreCase));
            var (model, repaired) = ReadSourceModel(read(Path.GetRelativePath(source, path).Replace('\\', '/')));
            layoutRepairs += repaired;
            var surfaces = new List<Surface>();
            var materials = new List<(string Texture, bool Cutout)>();
            foreach (var mesh in model.Meshes)
            {
                if (mesh.UseBoneWeights) throw new InvalidDataException("Source visual mesh requires skeletal translation");
                var material = model.Materials[mesh.MaterialIndex];
                var definition = definitions[Stem(material.MTD)];
                int blend = (int)definition.Params.Single(p => p.Name == "g_BlendMode").Value;
                string? excluded = blend switch {
                    0 or 1 => null, 2 => "alpha-blended", 3 => "water", 4 => "additive", 32 => "unlit-sky",
                    _ => throw new InvalidDataException($"Unclassified source blend mode {blend} in {material.MTD}"),
                };
                if (excluded is not null)
                {
                    exclusions[excluded] = exclusions.GetValueOrDefault(excluded) + 1;
                    continue;
                }
                var diffuse = material.Textures.Single(t => t.ParamName == "g_Diffuse");
                if (diffuse.Path == "") throw new InvalidDataException($"Source material has no explicit colour texture: {material.MTD}");
                int uv = definition.Textures.Single(t => t.Type == "g_Diffuse").UVNumber - 1;
                var vertices = mesh.Vertices.Select(v => {
                    if (uv < 0 || uv >= v.UVs.Count || !Finite(v.Position) || !Finite(v.Normal)
                        || !Finite(v.UVs[uv]) || v.Normal.LengthSquared() == 0)
                        throw new InvalidDataException($"Invalid visual vertex in {name}, {material.MTD}: colour UV {uv}/{v.UVs.Count}, normal length squared {v.Normal.LengthSquared()}");
                    return new Vertex(v.Position, Vector3.Normalize(v.Normal), new(v.UVs[uv].X, v.UVs[uv].Y));
                }).ToArray();
                var faces = mesh.FaceSets.Single(f => f.Flags == FLVER2.FaceSet.FSFlags.None);
                if (mesh.FaceSets.Any(f => f.Flags is not (FLVER2.FaceSet.FSFlags.None
                    or FLVER2.FaceSet.FSFlags.LodLevel1 or FLVER2.FaceSet.FSFlags.LodLevel2)))
                    throw new InvalidDataException("Unclassified source visual face-set role");
                var indices = faces.Triangulate(vertices.Length < 65536).ToArray();
                if (indices.Length == 0 || indices.Length % 3 != 0 || indices.Any(i => i < 0 || i >= vertices.Length))
                    throw new InvalidDataException("Source visual face set has invalid triangle indices");
                var key = (Stem(diffuse.Path), blend == 1);
                int materialIndex = materials.IndexOf(key);
                if (materialIndex < 0) { materialIndex = materials.Count; materials.Add(key); }
                textureNames.Add(key.Item1);
                surfaces.Add(new(vertices, indices, materialIndex, faces.CullBackfaces));
            }
            if (surfaces.Count > 0) models.Add((name, (810000 + models.Count).ToString("D6"), surfaces.ToArray(), materials));
        }
        var sourceTextures = ReadTextures(source, textureNames, fingerprints);
        // lazy: one three-digit texture namespace suffices for this map; allocate another when it exceeds 1,000 textures.
        if (textureNames.Count > 1000) throw new InvalidDataException("Diagnostic texture namespace is full");
        var texturePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int textureIndex = 0;
        foreach (var name in textureNames)
        {
            string stem = "aet999_" + (textureIndex++).ToString("D3");
            texturePaths[name] = stem.ToUpperInvariant() + "_a.tif";
            foreach (string suffix in new[] { "", "_l" })
            {
                string relative = $"asset/aet/aet999/{stem}{suffix}.tpf.dcx";
                try { host(relative); throw new InvalidDataException("Diagnostic texture ID already exists in the host install"); }
                catch (FileNotFoundException) { }
                var bytes = WriteTexture(sourceTextures[name], textureTemplate, stem.ToUpperInvariant() + "_a" + suffix);
                var destination = Path.Combine(output, "mod", relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, bytes);
                var written = TPF.Read(destination).Textures.Single();
                if (written.Name != Stem(texturePaths[name]) + suffix)
                    throw new InvalidDataException("Written texture name does not match its model binding");
                VerifyTexture(sourceTextures[name], written);
            }
        }
        var partTemplate = layout.Parts.MapPieces.First(p => p.ModelName == "m000000");
        var modelTemplate = layout.Models.MapPieces.Single(p => p.Name == partTemplate.ModelName);
        var placements = new List<Placement>();
        foreach (var model in models)
        {
            string id = "m" + model.Id;
            if (layout.Models.MapPieces.Any(m => m.Name == id)) throw new InvalidDataException("Diagnostic visual ID already occupied");
            var hostMaterials = model.Materials.Select((m, i) => new FLVER2.Material {
                Name = "aog_colour_" + i, MTD = basic[m.Cutout].Path, Index = i, GXIndex = native.Materials[0].GXIndex,
                Textures = [new FLVER2.Texture { ParamName = basic[m.Cutout].Sampler, Path = texturePaths[m.Texture] }],
            }).ToList();
            var bytes = WriteModel(FLVER2.Read(nativeFile.Bytes), model.Surfaces, hostMaterials);
            var binder = BND4.Read(templateBytes);
            var file = binder.Files.Single(f => f.Name.EndsWith(".flver"));
            file.Bytes = bytes; file.Name = file.Name.Replace("m10_01_00_00_000000", "m10_01_00_00_" + model.Id);
            binder.Files = [file];
            var path = Path.Combine(mapDir, $"m10_01_00_00_{model.Id}.mapbnd.dcx");
            binder.Write(path);
            var written = FLVER2.Read(BND4.Read(path).Files.Single().Bytes);
            VerifyModel(written, model.Surfaces);
            for (int i = 0; i < hostMaterials.Count; i++)
                if (written.Materials[i].MTD != hostMaterials[i].MTD
                    || written.Materials[i].GXIndex != native.Materials[0].GXIndex
                    || written.Materials[i].Textures.Single().Path != hostMaterials[i].Textures.Single().Path
                    || written.Materials[i].Textures.Single().ParamName != hostMaterials[i].Textures.Single().ParamName
                    || written.Materials[i].Textures.Single().TilingScale != hostMaterials[i].Textures.Single().TilingScale)
                    throw new InvalidDataException("Visual material binding or native GX settings reference changed in the written model");
            var mapModel = (MSBE.Model.MapPiece)modelTemplate.DeepCopy();
            mapModel.Name = id; mapModel.SibPath = "";
            layout.Models.MapPieces.Add(mapModel);
            int instance = 0;
            foreach (var sourcePart in sourceLayout.Parts.MapPieces.Where(p => p.ModelName == model.Source))
            {
                var part = (MSBE.Part.MapPiece)partTemplate.DeepCopy();
                part.Name = id + "_" + (instance++).ToString("D4"); part.ModelName = id; part.SibPath = "";
                part.Position = sourcePart.Position + offset; part.Rotation = sourcePart.Rotation; part.Scale = sourcePart.Scale;
                part.EntityID = 0; Array.Fill(part.EntityGroupIDs, 0u);
                Array.Fill(part.Unk1.DisplayGroups, uint.MaxValue); Array.Fill(part.Unk1.DrawGroups, uint.MaxValue);
                Array.Fill(part.Unk1.CollisionMask, uint.MaxValue);
                layout.Parts.MapPieces.Add(part);
                placements.Add(new(part.Name, id, part.Position, part.Rotation, part.Scale));
            }
        }
        Console.WriteLine($"PASS: {models.Count} source visual models, {placements.Count} placements, {textureNames.Count} colour textures read back");
        return new(models.Count, placements.Count, textureNames.Count, models.Sum(m => m.Surfaces.Length), true, true,
            fingerprints, ["Basic colour only; secondary layers, source vertex colours, normal/specular maps and baked lighting deferred",
                "Cutouts use the host threshold; source foliage motion and special material effects deferred",
                "Highest-detail source faces only; identical full mip chains in full and low-detail texture packages"],
            exclusions, layoutRepairs, placements.ToArray());
    }

    /// <summary>Verify that every translated visual placement retains its source transform and enabled draw groups.</summary>
    public static void VerifyPlacements(Result expected, MSBE written)
    {
        foreach (var value in expected.Expected)
        {
            var part = written.Parts.MapPieces.Single(p => p.Name == value.Name);
            if (part.ModelName != value.Model || part.Position != value.Position || part.Rotation != value.Rotation
                || part.Scale != value.Scale || part.Unk1.DrawGroups.Any(v => v != uint.MaxValue)
                || part.Unk1.DisplayGroups.Any(v => v != uint.MaxValue) || part.Unk1.CollisionMask.Any(v => v != uint.MaxValue))
                throw new InvalidDataException("Written source visual placement or visibility groups changed");
        }
    }

    static Dictionary<string, TPF.Texture> ReadTextures(string source, SortedSet<string> names,
        SortedDictionary<string, string> fingerprints)
    {
        var found = new Dictionary<string, TPF.Texture>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.GetFiles(Path.Combine(source, "map/m10"), "*.tpfbhd").Order(StringComparer.Ordinal))
        {
            var dataPath = Path.ChangeExtension(path, ".tpfbdt");
            using var binder = new BXF3Reader(path, dataPath);
            bool used = false;
            foreach (var file in binder.Files)
            {
                if (!names.Contains(Stem(Stem(file.Name)))) continue;
                used = true;
                var package = TPF.Read(binder.ReadFile(file));
                if (package.Platform != TPF.TPFPlatform.PC) throw new InvalidDataException("Source texture package must use PC format");
                foreach (var texture in package.Textures.Where(t => names.Contains(t.Name)))
                    found.Add(texture.Name, texture);
            }
            if (used)
                foreach (var input in new[] { path, dataPath })
                {
                    using var stream = File.OpenRead(input);
                    fingerprints[Path.GetRelativePath(source, input).Replace('\\', '/')] = Convert.ToHexString(SHA256.HashData(stream));
                }
        }
        if (!names.SetEquals(found.Keys)) throw new InvalidDataException("Source colour texture could not be resolved in the map archives");
        return found;
    }

    static byte[] WriteTexture(TPF.Texture source, TPF template, string name)
    {
        if (source.Type != TPF.TexType.Texture || source.Platform != TPF.TPFPlatform.PC)
            throw new InvalidDataException("Source colour texture must be a PC two-dimensional texture");
        var dds = new DDS(source.Bytes);
        var pixels = source.Bytes[dds.DataOffset..];
        var format = dds.GetDXGIFormat() switch {
            DDS.DXGI_FORMAT.BC1_UNORM or DDS.DXGI_FORMAT.BC1_UNORM_SRGB => DDS.DXGI_FORMAT.BC1_UNORM_SRGB,
            DDS.DXGI_FORMAT.BC7_UNORM or DDS.DXGI_FORMAT.BC7_UNORM_SRGB => DDS.DXGI_FORMAT.BC7_UNORM_SRGB,
            _ => throw new InvalidDataException("Unclassified source colour DDS format"),
        };
        dds.ddspf.dwFourCC = "DX10";
        dds.header10 = new DDS.HEADER_DXT10 { dxgiFormat = format };
        var texture = new TPF.Texture(name, (byte)(format == DDS.DXGI_FORMAT.BC1_UNORM_SRGB ? 0 : 112),
            0, dds.Write(pixels), TPF.TPFPlatform.PC);
        var package = new TPF { Platform = template.Platform, Encoding = template.Encoding,
            Flag2 = template.Flag2, Compression = template.Compression, Textures = [texture] };
        // lazy: keep the source mip chain in both packages until the first textured play check proves loading.
        return package.Write();
    }

    static void VerifyTexture(TPF.Texture source, TPF.Texture written)
    {
        var before = new DDS(source.Bytes); var after = new DDS(written.Bytes);
        if (before.dwWidth != after.dwWidth || before.dwHeight != after.dwHeight || before.dwMipMapCount != after.dwMipMapCount
            || !source.Bytes.AsSpan(before.DataOffset).SequenceEqual(written.Bytes.AsSpan(after.DataOffset))
            || after.GetDXGIFormat() is not (DDS.DXGI_FORMAT.BC1_UNORM_SRGB or DDS.DXGI_FORMAT.BC7_UNORM_SRGB))
            throw new InvalidDataException("Written colour texture changed source pixels, mipmaps, dimensions or colour space");
    }

    static string Stem(string path) => Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
    static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    /// <summary>Check visual geometry, material separation and untiled texture coordinates with authored surfaces.</summary>
    public static void CheckFixture(byte[] template)
    {
        CheckSourceLayouts();
        var native = FLVER2.Read(BND4.Read(template).Files.Single(f => f.Name.EndsWith(".flver")).Bytes);
        Surface[] surfaces = [
            new([
                new(new(1, 2, 3), Vector3.UnitY, new(-20, 0)),
                new(new(5, 2, 3), Vector3.UnitY, new(40, 0)),
                new(new(1, 2, 7), Vector3.UnitY, new(-20, 60)),
            ], [0, 1, 2], 0, true),
            new([
                new(new(-3, 4, 5), Vector3.UnitZ, new(0.25f, 0.75f)),
                new(new(-3, 8, 5), Vector3.UnitZ, new(0.5f, 0.75f)),
                new(new(1, 4, 5), Vector3.UnitZ, new(0.25f, 1)),
            ], [0, 1, 2], 1, false),
        ];
        var written = FLVER2.Read(WriteModel(native, surfaces, native.Materials.Take(2).ToList()));
        VerifyModel(written, surfaces);
        if (written.Materials.SelectMany(m => m.Textures).Any(t => t.TilingScale != new Vector2(4, 4)))
            throw new InvalidDataException("Authored wide UVs require fourfold sampler scale with native vertex packing");
        Console.WriteLine("PASS: authored visual geometry, material separation, winding and wide UV coordinates");
    }

    static void CheckSourceLayouts()
    {
        FLVER2.BufferLayout plain = [
            new(FLVER.LayoutType.Float3, FLVER.LayoutSemantic.Position),
            new(FLVER.LayoutType.UByte4, FLVER.LayoutSemantic.BoneIndices),
            new(FLVER.LayoutType.UByte4Norm, FLVER.LayoutSemantic.Normal),
            new(FLVER.LayoutType.UByte4Norm, FLVER.LayoutSemantic.VertexColor),
            new(FLVER.LayoutType.Short2, FLVER.LayoutSemantic.UV),
        ];
        var tangent = RepairSourceLayout(plain, 32);
        var bitangent = RepairSourceLayout(tangent, 36);
        var removed = RepairSourceLayout(tangent, 28);
        if (plain.Count != 5 || tangent.Size != 32 || bitangent.Size != 36 || removed.Size != 28
            || tangent[3].Semantic != FLVER.LayoutSemantic.Tangent
            || bitangent[4].Semantic != FLVER.LayoutSemantic.Bitangent
            || !removed.Select(m => m.Semantic).SequenceEqual(plain.Select(m => m.Semantic)))
            throw new InvalidDataException("Source tangent-layout repair changed field order, stride or the shared declaration");
        try
        {
            RepairSourceLayout(plain, 40);
            throw new InvalidOperationException("Unclassified source stride was accepted");
        }
        catch (InvalidDataException) { }
    }

    static FLVER2.BufferLayout RepairSourceLayout(FLVER2.BufferLayout declaration, int stride)
    {
        var result = new FLVER2.BufferLayout();
        result.AddRange(declaration.Select(m => new FLVER.LayoutMember(m)));
        int delta = stride - result.Size;
        if (delta == 0) return result;
        int normal = result.FindIndex(m => m.Semantic == FLVER.LayoutSemantic.Normal);
        int tangent = result.FindIndex(m => m.Semantic == FLVER.LayoutSemantic.Tangent);
        if (normal < 0 || result[normal].Type != FLVER.LayoutType.UByte4Norm
            || result.Any(m => m.Semantic == FLVER.LayoutSemantic.Bitangent || m.SpecialModifier != 0))
            throw new InvalidDataException("Unclassified source vertex-layout mismatch");
        if (delta == 4)
            result.Insert(tangent < 0 ? normal + 1 : tangent + 1, new FLVER.LayoutMember(FLVER.LayoutType.UByte4Norm,
                tangent < 0 ? FLVER.LayoutSemantic.Tangent : FLVER.LayoutSemantic.Bitangent));
        else if (delta == -4 && tangent >= 0 && result[tangent].Type == FLVER.LayoutType.UByte4Norm)
            result.RemoveAt(tangent);
        else
            throw new InvalidDataException($"Unclassified source vertex stride {stride} for declaration size {declaration.Size}");
        if (result.Size != stride) throw new InvalidDataException("Corrected source vertex declaration does not match its stride");
        return result;
    }

    static (FLVER2 Model, int Repaired) ReadSourceModel(byte[] bytes)
    {
        var raw = DCX.Decompress(bytes);
        using var reader = new BinaryReaderEx(false, raw);
        var model = new FLVER2();
        model.Read(reader, false);
        if (model.Header.Version != 0x2000C || model.Header.BigEndian)
            throw new InvalidDataException("Unclassified source visual format version or byte order");
        int dataOffset = reader.GetInt32(12), repaired = 0;
        // The library's automatic repair mutates shared layouts and can still leave the wrong stride.
        // Read metadata first, fix each buffer's declaration independently, then use its vertex decoder.
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var readVertices = typeof(FLVER2.Mesh).GetMethod("ReadVertices", flags)!;
        int Field(FLVER2.VertexBuffer buffer, string name) => (int)typeof(FLVER2.VertexBuffer).GetField(name, flags)!.GetValue(buffer)!;
        foreach (var mesh in model.Meshes)
        {
            if (mesh.VertexBuffers.Count != 1 || mesh.VertexBuffers[0].EdgeCompressed)
                throw new InvalidDataException("Source visual reader requires one ordinary vertex buffer");
            var buffer = mesh.VertexBuffers[0];
            int stride = Field(buffer, "VertexSize");
            var declaration = model.BufferLayouts[buffer.LayoutIndex];
            if (declaration.Size != stride) repaired++;
            var corrected = RepairSourceLayout(declaration, stride);
            buffer.LayoutIndex = model.BufferLayouts.Count;
            model.BufferLayouts.Add(corrected);
            readVertices.Invoke(mesh, [reader, dataOffset, model.BufferLayouts, model.Header]);
            if (corrected[0].Semantic != FLVER.LayoutSemantic.Position || corrected[0].Type != FLVER.LayoutType.Float3)
                throw new InvalidDataException("Source visual position must be the buffer's leading float vector");
            int start = dataOffset + Field(buffer, "BufferOffset");
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                int at = start + i * stride;
                var position = new Vector3(reader.GetSingle(at), reader.GetSingle(at + 4), reader.GetSingle(at + 8));
                if (mesh.Vertices[i].Position != position)
                    throw new InvalidDataException("Source vertex decoder did not follow the stored buffer stride");
            }
        }
        return (model, repaired);
    }

    static byte[] WriteModel(FLVER2 template, Surface[] surfaces, List<FLVER2.Material> materials)
    {
        if (template.Header.Version != 0x2001A || template.Nodes.Count != 1
            || template.Nodes[0].Translation != Vector3.Zero || template.Nodes[0].Rotation != Vector3.Zero
            || template.Nodes[0].Scale != Vector3.One || template.Meshes[0].UseBoneWeights)
            throw new InvalidDataException("Visual template must be the checked static host format");
        var vertexTemplate = template.Meshes[0].Vertices[0];
        var layout = new FLVER2.BufferLayout();
        layout.AddRange(template.BufferLayouts[template.Meshes[0].VertexBuffers.Single().LayoutIndex]
            .Select(member => new FLVER.LayoutMember(member)));
        if (layout.Single(m => m.Semantic == FLVER.LayoutSemantic.UV).Type != FLVER.LayoutType.Short2)
            throw new InvalidDataException("Visual UV encoding requires the checked host packed layout");
        // Keep the shader's native input format; sampler scaling recovers source UVs beyond its packed range.
        float uvScale = 1;
        float largest = surfaces.SelectMany(s => s.Vertices).Max(v => Math.Max(Math.Abs(v.UV.X), Math.Abs(v.UV.Y)));
        while (largest / uvScale > short.MaxValue / 2048f) uvScale *= 2;
        foreach (var texture in materials.SelectMany(m => m.Textures))
        {
            texture.TilingScale = new Vector2(uvScale);
            texture.TilingTypeU = texture.TilingTypeV = FLVER2.Texture.TilingType.Repeat;
        }
        template.BufferLayouts = [layout];
        template.Materials = materials;
        template.Meshes.Clear(); template.Dummies.Clear();
        foreach (var surface in surfaces)
        {
            var mesh = new FLVER2.Mesh { MaterialIndex = surface.Material, NodeIndex = 0,
                VertexBuffers = [new FLVER2.VertexBuffer(0)],
                FaceSets = [new FLVER2.FaceSet { Indices = surface.Indices.ToList(), CullBackfaces = surface.CullBackfaces }] };
            foreach (var source in surface.Vertices)
            {
                var vertex = new FLVER.Vertex(vertexTemplate) { Position = source.Position, Normal = source.Normal,
                    NormalW = 0, UVs = [new Vector3(source.UV / uvScale, 0)] };
                var tangent = Vector3.Normalize(Vector3.Cross(Math.Abs(source.Normal.Y) < 0.9f
                    ? Vector3.UnitY : Vector3.UnitZ, source.Normal));
                for (int i = 0; i < vertex.Tangents.Count; i++) vertex.Tangents[i] = new Vector4(tangent, 1);
                for (int i = 0; i < vertex.Colors.Count; i++) vertex.Colors[i] = new FLVER.VertexColor(1f, 1f, 1f, 1f);
                mesh.Vertices.Add(vertex);
            }
            var lo = surface.Vertices.Select(v => v.Position).Aggregate(Vector3.Min);
            var hi = surface.Vertices.Select(v => v.Position).Aggregate(Vector3.Max);
            mesh.BoundingBox = new FLVER2.Mesh.BoundingBoxes {
                Min = (hi - lo) / 2 + new Vector3(0.001f), Max = Vector3.Zero, Unk = (lo + hi) / 2,
            };
            template.Meshes.Add(mesh);
        }
        var all = surfaces.SelectMany(s => s.Vertices).Select(v => v.Position).ToArray();
        template.Header.BoundingBoxMin = template.Nodes[0].BoundingBoxMin = all.Aggregate(Vector3.Min);
        template.Header.BoundingBoxMax = template.Nodes[0].BoundingBoxMax = all.Aggregate(Vector3.Max);
        return template.Write();
    }

    static void VerifyModel(FLVER2 model, Surface[] surfaces)
    {
        if (model.Meshes.Count != surfaces.Length)
            throw new InvalidDataException("Visual surface count changed");
        for (int i = 0; i < surfaces.Length; i++)
        {
            var mesh = model.Meshes[i]; var expected = surfaces[i];
            var textures = model.Materials[mesh.MaterialIndex].Textures;
            var scale = textures.First().TilingScale;
            if (textures.Any(t => t.TilingScale != scale || t.TilingTypeU != FLVER2.Texture.TilingType.Repeat
                || t.TilingTypeV != FLVER2.Texture.TilingType.Repeat))
                throw new InvalidDataException("Visual material must consistently repeat and scale its packed UVs");
            var uvLayout = model.BufferLayouts[mesh.VertexBuffers.Single().LayoutIndex]
                .Single(m => m.Semantic == FLVER.LayoutSemantic.UV);
            if (uvLayout.Type != FLVER.LayoutType.Short2)
                throw new InvalidDataException("Visual UVs must retain the checked host shader's packed vertex layout");
            FirelinkDemo.CheckVisibleBounds(mesh);
            var faces = mesh.FaceSets.Single();
            if (mesh.MaterialIndex != expected.Material || faces.CullBackfaces != expected.CullBackfaces
                || faces.TriangleStrip || !faces.Indices.SequenceEqual(expected.Indices)
                || mesh.Vertices.Count != expected.Vertices.Length)
                throw new InvalidDataException("Visual surface material, facing or triangle corners changed");
            for (int v = 0; v < expected.Vertices.Length; v++)
            {
                var actual = mesh.Vertices[v]; var vertex = expected.Vertices[v];
                var sampledUV = actual.UVs.Single() * new Vector3(scale, 1);
                if (actual.Position != vertex.Position || sampledUV != new Vector3(vertex.UV, 0)
                    || Vector3.Distance(actual.Normal, vertex.Normal) > 0.02f
                    || actual.Colors.Count == 0 || actual.Colors.Any(c => c.A != 1f || c.R != 1f || c.G != 1f || c.B != 1f))
                    throw new InvalidDataException("Visual vertex position, normal or texture coordinate changed");
            }
        }
    }
}
