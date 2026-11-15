using SoulsFormats;

namespace AgeOfGold.Params;

internal static class SourceLightingReader
{
    internal sealed record Colour(float Red, float Green, float Blue, float Intensity);
    internal sealed record Directional(float Elevation, float Azimuth, Colour Colour);
    internal sealed record EnvironmentLight(Colour Colour, int[] Textures);
    internal sealed record LightSet(int Id, Directional[] Diffuse, Directional Specular,
        Colour Upper, Colour Lower, EnvironmentLight DiffuseEnvironment, EnvironmentLight SpecularEnvironment);
    internal sealed record PointLight(int Id, float FadeStart, float FadeEnd, Colour Colour);
    /// <summary>The area's light rows, which its scenery and the characters standing in it both select from, and its point lights.</summary>
    internal sealed record Lighting(LightSet[] Sets, PointLight[] Points);

    internal static Lighting Read(Install source)
    {
        var definitions = BND3.Read(source.Read("paramdef/paramdef.paramdefbnd.dcx")).Files
            .Select(f => PARAMDEF.Read(f.Bytes)).ToArray();
        var tables = BND3.Read(source.Read("param/DrawParam/a10_DrawParam.parambnd.dcx"));
        return new Lighting(
            Rows("m10_LightBank.param", "LIGHT_BANK").Select(Set).ToArray(),
            Rows("m10_PointLightBank.param", "POINT_LIGHT_BANK").Select(Point).ToArray());

        IEnumerable<PARAM.Row> Rows(string name, string type)
        {
            var entries = tables.Files.Where(f => Path.GetFileName(f.Name.Replace('\\', '/')) == name).ToArray();
            if (entries.Length != 1) throw new InvalidDataException($"Expected one source lighting table {name}");
            var table = PARAM.Read(entries[0].Bytes);
            if (table.ParamType != type || table.Rows.Count == 0 || table.Rows.Select(r => r.ID).Distinct().Count() != table.Rows.Count)
                throw new InvalidDataException($"Unsupported or ambiguous source lighting rows: {name}");
            var matches = definitions.Where(d => d.ParamType == type && d.DataVersion == table.ParamdefDataVersion).ToArray();
            if (matches.Length != 1 || !table.ApplyParamdefCarefully(matches[0]))
                throw new InvalidDataException($"Source lighting definition does not uniquely match {name}");
            return table.Rows.OrderBy(r => r.ID);
        }
    }

    private static LightSet Set(PARAM.Row row)
    {
        var fields = new Fields(row);
        Colour Colour(string prefix, string suffix = "") => fields.Colour(prefix, suffix);
        Directional Direction(string suffix) => new(fields.Short("degRotX_" + suffix), fields.Short("degRotY_" + suffix), Colour("col", "_" + suffix));
        int Texture(string name)
        {
            int id = fields.Short(name);
            if (id < 0) throw new InvalidDataException($"Unclassified environment texture reference in lighting row {row.ID}");
            return id;
        }
        var result = new LightSet(row.ID, [Direction("0"), Direction("1"), Direction("2")], Direction("s"),
            Colour("col", "_u"), Colour("col", "_d"),
            new(Colour("envDif_col"), [Texture("envDif")]),
            new(Colour("envSpc_col"), Enumerable.Range(0, 4).Select(i => Texture("envSpc_" + i)).ToArray()));
        fields.Complete();
        return result;
    }

    private static PointLight Point(PARAM.Row row)
    {
        var fields = new Fields(row);
        var result = new PointLight(row.ID, fields.Float("dwindleBegin"), fields.Float("dwindleEnd"), fields.Colour("col"));
        if (result.FadeStart < 0 || result.FadeEnd <= result.FadeStart)
            throw new InvalidDataException($"Invalid point light falloff in row {row.ID}");
        fields.Complete();
        return result;
    }

    private sealed class Fields(PARAM.Row row)
    {
        private readonly HashSet<string> consumed = new(StringComparer.Ordinal);

        private T Get<T>(string name)
        {
            var cells = row.Cells.Where(c => c.Def.InternalName == name).ToArray();
            if (cells.Length != 1 || cells[0].Value is not T value)
                throw new InvalidDataException($"Missing, ambiguous or unsupported lighting field {name} in row {row.ID}");
            consumed.Add(name);
            return value;
        }

        internal short Short(string name) => Get<short>(name);
        internal float Float(string name)
        {
            var value = Get<float>(name);
            if (!float.IsFinite(value)) throw new InvalidDataException($"Non-finite lighting field {name} in row {row.ID}");
            return value;
        }

        // Keep encoded colour and intensity separate; the renderer determines their colour-space conversion.
        internal Colour Colour(string prefix, string suffix = "")
        {
            float Channel(string channel, float scale)
            {
                short value = Short(prefix + channel + suffix);
                if (value < 0) throw new InvalidDataException($"Negative lighting colour in row {row.ID}");
                return value / scale;
            }
            return new(Channel("R", 255), Channel("G", 255), Channel("B", 255), Channel("A", 100));
        }

        internal void Complete()
        {
            var unknown = row.Cells.FirstOrDefault(c => c.Def.DisplayType != PARAMDEF.DefType.dummy8 && !consumed.Contains(c.Def.InternalName));
            if (unknown is not null) throw new InvalidDataException($"Unclassified lighting field {unknown.Def.InternalName} in row {row.ID}");
        }
    }
}
