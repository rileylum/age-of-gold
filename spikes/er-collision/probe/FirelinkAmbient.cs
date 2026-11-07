using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using SoulsFormats;

// Spike only: translate source hemisphere colours while retaining the host's direct-light tracks.
static class FirelinkAmbient
{
    /// <summary>Normalized hemisphere RGB colours with independent strength multipliers.</summary>
    public record Ambient(Vector4 Upper, Vector4 Lower);
    /// <summary>Written host files and the source-row to host-light-set mapping.</summary>
    public record Written(byte[] Graphics, byte[] Layout, SortedDictionary<int, int> LightSets);

    /// <summary>Add checked source ambient lighting to a previously checked diagnostic build.</summary>
    public static void Build(string source, string input, string output)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Ambient output must be fresh");
        var fingerprints = new SortedDictionary<string, string>(StringComparer.Ordinal);
        byte[] read(string path)
        {
            var bytes = File.ReadAllBytes(Path.Combine(source, path));
            fingerprints.Add(path, Convert.ToHexString(SHA256.HashData(bytes)));
            return bytes;
        }
        const string sourceMap = "map/MapStudio/m10_02_00_00.msb";
        const string graphicsPath = "mod/param/drawparam/m10_01_0000.gparam.dcx";
        const string layoutPath = "mod/map/mapstudio/m10_01_00_00.msb.dcx";
        var sourceLayout = MSB1.Read(read(sourceMap));
        using var report = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(input, "check.json")));
        var visuals = report.RootElement.GetProperty("source_visuals");
        if (visuals.GetProperty("source_fingerprints").GetProperty(sourceMap).GetString() != fingerprints[sourceMap])
            throw new InvalidDataException("Source layout differs from the checked visual build");
        var bindings = visuals.GetProperty("source_placements").Deserialize<Dictionary<string, string>>()!;
        if (bindings.Count != visuals.GetProperty("placements").GetInt32())
            throw new InvalidDataException("Source placement bindings are incomplete");
        var assignments = bindings.ToDictionary(b => b.Key,
            b => (int)sourceLayout.Parts.MapPieces.Single(p => p.Name == b.Value).LightID, StringComparer.Ordinal);
        var definitions = BND3.Read(read("paramdef/paramdef.paramdefbnd.dcx"));
        var definition = definitions.Files.Single(f => PARAMDEF.Read(f.Bytes).ParamType == "LIGHT_BANK");
        var drawing = BND3.Read(read("param/DrawParam/a10_DrawParam.parambnd.dcx"));
        var table = drawing.Files.Single(f => f.Name.Replace('\\', '/').Split('/').Last() == "m10_LightBank.param");
        var ambient = Read(definition.Bytes, table.Bytes, assignments.Values.ToArray());
        var graphicsInput = File.ReadAllBytes(Path.Combine(input, graphicsPath));
        var layoutInput = File.ReadAllBytes(Path.Combine(input, layoutPath));
        var written = Write(graphicsInput, layoutInput, assignments, ambient);

        var inputFiles = Directory.GetFiles(input, "*", SearchOption.AllDirectories);
        var inputFingerprints = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in inputFiles)
        {
            string relative = Path.GetRelativePath(input, path);
            var bytes = File.ReadAllBytes(path);
            inputFingerprints.Add(relative.Replace('\\', '/'), Convert.ToHexString(SHA256.HashData(bytes)));
            string target = Path.Combine(output, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        }
        File.WriteAllBytes(Path.Combine(output, graphicsPath), written.Graphics);
        File.WriteAllBytes(Path.Combine(output, layoutPath), written.Layout);
        Verify(graphicsInput, layoutInput, File.ReadAllBytes(Path.Combine(output, graphicsPath)),
            File.ReadAllBytes(Path.Combine(output, layoutPath)), assignments, ambient, written.LightSets);
        foreach (var (relative, hash) in inputFingerprints.Where(f => f.Key != graphicsPath && f.Key != layoutPath))
            if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(output, relative)))) != hash)
                throw new InvalidDataException($"Ambient build changed an unrelated file: {relative}");
        File.WriteAllText(Path.Combine(output, "ambient-check.json"), JsonSerializer.Serialize(new {
            purpose = "Source upper/lower hemisphere lighting with per-placement selection",
            placements = assignments.Count, light_sets = written.LightSets,
            source_fingerprints = fingerprints, input_fingerprints = inputFingerprints,
            tool_sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(FirelinkAmbient).Assembly.Location))),
            unchanged_input_files = inputFiles.Length - 2,
            mapping = "Source RGB / 255 and percentage strength / 100; constant across inherited time keyframes",
            limits = "Numeric mapping only; cross-engine visual response unproven. Host default direct-light tracks retained; environment maps, baked lighting and source sun remain deferred.",
            inherited_reports = "Earlier reports describe the input stages",
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: {assignments.Count} source placements select {ambient.Count} hemisphere settings; other light tracks, map data and {inputFiles.Length - 2} files preserved");
    }

    static void Verify(byte[] graphicsInput, byte[] layoutInput, byte[] graphicsOutput, byte[] layoutOutput,
        Dictionary<string, int> assignments, SortedDictionary<int, Ambient> ambient, SortedDictionary<int, int> lightSets)
    {
        var before = GPARAM.Read(graphicsInput); var after = GPARAM.Read(graphicsOutput);
        var beforeGroup = before.Groups.Single(g => g.Name1 == "LightSet ParamEditor");
        var afterGroup = after.Groups.Single(g => g.Name1 == "LightSet ParamEditor");
        foreach (var field in afterGroup.Params)
        {
            var original = beforeGroup.Params.Single(p => p.Name1 == field.Name1);
            var template = Enumerable.Range(0, original.Values.Count).Where(i => original.ValueIDs[i] == 0).ToArray();
            foreach (var (sourceRow, hostId) in lightSets)
            {
                var actual = Enumerable.Range(0, field.Values.Count).Where(i => field.ValueIDs[i] == hostId).ToArray();
                if (!actual.Select(i => field.TimeOfDay[i]).SequenceEqual(template.Select(i => original.TimeOfDay[i])))
                    throw new InvalidDataException("Written light set changed the default time track");
                for (int i = 0; i < actual.Length; i++)
                {
                    object expected = field.Name1 switch {
                        "Directional Light Hemi Color Up" => ambient[sourceRow].Upper,
                        "Directional Light Hemi Color Down" => ambient[sourceRow].Lower,
                        _ => original.Values[template[i]],
                    };
                    if (!field.Values[actual[i]].Equals(expected))
                        throw new InvalidDataException($"Written source ambient or inherited light field changed: {field.Name1}");
                }
            }
            for (int i = field.Values.Count - 1; i >= 0; i--)
                if (lightSets.ContainsValue(field.ValueIDs[i]))
                { field.Values.RemoveAt(i); field.ValueIDs.RemoveAt(i); field.TimeOfDay.RemoveAt(i); }
        }
        var index = after.Unk3s.Single(g => g.GroupIndex == after.Groups.IndexOf(afterGroup));
        if (!lightSets.Values.All(index.ValueIDs.Contains)) throw new InvalidDataException("Written light index is incomplete");
        index.ValueIDs.RemoveAll(lightSets.ContainsValue);
        if (!after.Write(new DCX.NoCompressionInfo()).AsSpan().SequenceEqual(DCX.Decompress(graphicsInput)))
            throw new InvalidDataException("Ambient translation changed existing graphics bytes");
        var originalMap = MSBE.Read(layoutInput); var writtenMap = MSBE.Read(layoutOutput);
        foreach (var (name, sourceRow) in assignments)
        {
            var part = writtenMap.Parts.MapPieces.Single(p => p.Name == name);
            if (part.Gparam.LightSetID != lightSets[sourceRow]) throw new InvalidDataException("Written source light selection changed");
            part.Gparam.LightSetID = originalMap.Parts.MapPieces.Single(p => p.Name == name).Gparam.LightSetID;
        }
        if (!writtenMap.Write(new DCX.NoCompressionInfo()).AsSpan().SequenceEqual(DCX.Decompress(layoutInput)))
            throw new InvalidDataException("Ambient translation changed other map bytes");
    }

    /// <summary>Read source hemisphere colours and percentage strengths using the install's own schema.</summary>
    public static SortedDictionary<int, Ambient> Read(byte[] schema, byte[] table, int[] rows)
    {
        var definition = PARAMDEF.Read(schema);
        var source = PARAM.Read(table);
        if (source.ParamType != "LIGHT_BANK" || !source.ApplyParamdefCarefully(definition))
            throw new InvalidDataException("Source light table does not match its install schema");
        var result = new SortedDictionary<int, Ambient>();
        foreach (int id in rows.Distinct().Order())
        {
            var matching = source.Rows.Where(r => r.ID == id).ToArray();
            if (matching.Length != 1) throw new InvalidDataException($"Source light row {id} is missing or duplicated");
            var row = matching[0];
            float component(string channel, string suffix)
            {
                string name = $"col{channel}_{suffix}";
                var cells = row.Cells.Where(c => c.Def.InternalName == name).ToArray();
                if (cells.Length != 1 || cells[0].Def.DisplayType != PARAMDEF.DefType.s16)
                    throw new InvalidDataException($"Unclassified source ambient field {name}");
                int value = (short)cells[0].Value;
                if (value < 0 || (channel != "A" && value > 255))
                    throw new InvalidDataException($"Source ambient field {name} is outside its colour/strength range");
                return value / (channel == "A" ? 100f : 255f);
            }
            Vector4 hemisphere(string suffix) => new(component("R", suffix), component("G", suffix),
                component("B", suffix), component("A", suffix));
            result.Add(id, new(hemisphere("u"), hemisphere("d")));
        }
        return result;
    }

    /// <summary>Give source placements independent ambient settings, inheriting other light fields from the host default.</summary>
    public static Written Write(byte[] graphics, byte[] layout, Dictionary<string, int> assignments,
        SortedDictionary<int, Ambient> ambient)
    {
        var host = GPARAM.Read(graphics);
        var map = MSBE.Read(layout);
        if (host.Game != GPARAM.GPGame.Sekiro) throw new InvalidDataException("Unclassified host graphics version");
        if (assignments.Count == 0 || !assignments.Values.Distinct().Order().SequenceEqual(ambient.Keys))
            throw new InvalidDataException("Source ambient rows do not match the scenery assignments");
        var group = host.Groups.Single(g => g.Name1 == "LightSet ParamEditor");
        var index = host.Unk3s.Single(g => g.GroupIndex == host.Groups.IndexOf(group));
        var used = group.Params.SelectMany(p => p.ValueIDs).ToHashSet();
        if (!index.ValueIDs.SequenceEqual(used.Order()))
            throw new InvalidDataException("Unclassified host light-set index");
        foreach (string name in new[] { "Directional Light Hemi Color Up", "Directional Light Hemi Color Down" })
            if (group.Params.Single(p => p.Name1 == name).Type != GPARAM.ParamType.Float4)
                throw new InvalidDataException($"Unclassified hemisphere colour type: {name}");
        var lightSets = new SortedDictionary<int, int>();
        int next = 1;
        foreach (int sourceRow in ambient.Keys)
        {
            while (used.Contains(next)) next = checked(next + 1);
            lightSets.Add(sourceRow, next); used.Add(next);
        }
        foreach (var field in group.Params)
        {
            if (field.Values.Count != field.ValueIDs.Count || field.Values.Count != field.TimeOfDay.Count
                || !field.ValueIDs.SequenceEqual(field.ValueIDs.Order()))
                throw new InvalidDataException($"Unclassified host light track: {field.Name1}");
            var template = Enumerable.Range(0, field.Values.Count).Where(i => field.ValueIDs[i] == 0)
                .Select(i => (Value: field.Values[i], Time: field.TimeOfDay[i])).ToArray();
            if (template.Length == 0 || field.Values.Count + template.Length * ambient.Count > byte.MaxValue)
                throw new InvalidDataException($"Missing default light track or graphics field capacity exceeded: {field.Name1}");
            foreach (var (sourceRow, hostId) in lightSets)
            {
                int at = field.ValueIDs.FindIndex(id => id > hostId);
                if (at < 0) at = field.Values.Count;
                foreach (var key in template)
                {
                    object value = field.Name1 switch {
                        "Directional Light Hemi Color Up" => ambient[sourceRow].Upper,
                        "Directional Light Hemi Color Down" => ambient[sourceRow].Lower,
                        _ => key.Value,
                    };
                    field.Values.Insert(at, value); field.ValueIDs.Insert(at, hostId);
                    field.TimeOfDay.Insert(at++, key.Time);
                }
            }
        }
        index.ValueIDs = used.Order().ToList();
        foreach (var (name, sourceRow) in assignments)
        {
            var part = map.Parts.MapPieces.Single(p => p.Name == name);
            if (part.Gparam.LightSetID is not (-1 or 0))
                throw new InvalidDataException($"Scenery already has a non-default host light set: {name}");
            part.Gparam.LightSetID = lightSets[sourceRow];
        }
        return new(host.Write(), map.Write(), lightSets);
    }
}
