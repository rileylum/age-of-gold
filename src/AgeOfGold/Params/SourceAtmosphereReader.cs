using System.Globalization;
using SoulsFormats;

namespace AgeOfGold.Params;

/// <summary>Reads which atmosphere rows the source map selects and what those rows hold.</summary>
internal static class SourceAtmosphereReader
{
    /// <summary>The drawing-table rows a collision placement selects for a character standing on it.</summary>
    internal sealed record Selection(int Light, int Fog, int Scattering, int DepthOfField, int ToneMap, int ToneCorrection);

    /// <summary>A depth-of-field row: where far and near blur begin and end, in metres, and their strengths in percent.</summary>
    internal sealed record Focus(float FarStart, float FarEnd, float FarStrength, float NearStart, float NearEnd, float NearStrength, float Dispersion);

    internal static Dictionary<string, Selection> Selections(Install source) =>
        MSB1.Read(source.Read("map/MapStudio/m10_02_00_00.msb")).Parts.Collisions.ToDictionary(p => p.Name,
            p => new Selection(p.LightID, p.FogID, p.ScatterID, p.DofID, p.ToneMapID, p.ToneCorrectID), StringComparer.Ordinal);

    internal static SortedDictionary<int, Focus> DepthOfField(Install source)
    {
        var definitions = BND3.Read(source.Read("paramdef/paramdef.paramdefbnd.dcx")).Files.Select(f => PARAMDEF.Read(f.Bytes)).ToArray();
        var tables = BND3.Read(source.Read("param/DrawParam/a10_DrawParam.parambnd.dcx"));
        var entries = tables.Files.Where(f => Path.GetFileName(f.Name.Replace('\\', '/')) == "m10_DofBank.param").ToArray();
        if (entries.Length != 1) throw new InvalidDataException("Expected one source depth-of-field table");
        var table = PARAM.Read(entries[0].Bytes);
        var matches = definitions.Where(d => d.ParamType == "DOF_BANK" && d.DataVersion == table.ParamdefDataVersion).ToArray();
        if (table.ParamType != "DOF_BANK" || matches.Length != 1 || !table.ApplyParamdefCarefully(matches[0])
            || table.Rows.Select(r => r.ID).Distinct().Count() != table.Rows.Count)
            throw new InvalidDataException("Unsupported or ambiguous source depth-of-field table");
        var result = new SortedDictionary<int, Focus>();
        foreach (var row in table.Rows)
        {
            var read = new HashSet<string>(StringComparer.Ordinal);
            float Value(string name)
            {
                var cells = row.Cells.Where(c => c.Def.InternalName == name).ToArray();
                if (cells.Length != 1) throw new InvalidDataException($"Missing or ambiguous depth-of-field field {name}");
                read.Add(name);
                float value = Convert.ToSingle(cells[0].Value, CultureInfo.InvariantCulture);
                return float.IsFinite(value) ? value : throw new InvalidDataException($"Non-finite depth-of-field field {name} in row {row.ID}");
            }
            result.Add(row.ID, new(Value("farDofBegin"), Value("farDofEnd"), Value("farDofMul"), Value("nearDofBegin"), Value("nearDofEnd"), Value("nearDofMul"), Value("dispersionSq")));
            var unknown = row.Cells.FirstOrDefault(c => c.Def.DisplayType != PARAMDEF.DefType.dummy8 && !read.Contains(c.Def.InternalName));
            if (unknown is not null) throw new InvalidDataException($"Unclassified depth-of-field field {unknown.Def.InternalName}");
        }
        return result;
    }
}
