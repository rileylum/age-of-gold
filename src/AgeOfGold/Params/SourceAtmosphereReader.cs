using System.Globalization;
using SoulsFormats;

namespace AgeOfGold.Params;

/// <summary>Reads which atmosphere rows the source map selects and what those rows hold.</summary>
internal static class SourceAtmosphereReader
{
    /// <summary>The drawing-table rows a collision placement selects for a character standing on it.</summary>
    internal sealed record Selection(int Light, int Fog, int Scattering, int DepthOfField, int ToneMap, int ToneCorrection, int Shadow);

    /// <summary>A depth-of-field row: where far and near blur begin and end, in metres, and their strengths in percent.</summary>
    internal sealed record Focus(float FarStart, float FarEnd, float FarStrength, float NearStart, float NearEnd, float NearStrength, float Dispersion);

    internal static Dictionary<string, Selection> Selections(Install source) =>
        MSB1.Read(source.Read("map/MapStudio/m10_02_00_00.msb")).Parts.Collisions.ToDictionary(p => p.Name,
            p => new Selection(p.LightID, p.FogID, p.ScatterID, p.DofID, p.ToneMapID, p.ToneCorrectID, p.ShadowID), StringComparer.Ordinal);

    /// <summary>
    /// A shadow row: the casting light's two angles in degrees, how much of the light a shadow removes, the shadow's colour,
    /// and the distance from the camera up to which shadows are whole and over which they then fade out, in metres.
    /// </summary>
    internal sealed record Shadow(float Elevation, float Azimuth, float Density, float[] Colour, float FadeStart, float FadeLength)
    {
        /// <summary>The unit direction from a surface to the light. The light shines along (cos x sin y, -sin x, cos x cos y), as the source's scattering sun does.</summary>
        // lazy: the convention is the scattering sun's, carried over to this light; it is checked by eye against the source game's shadows.
        internal float[] TowardsLight
        {
            get
            {
                double x = Elevation * Math.PI / 180, y = Azimuth * Math.PI / 180;
                return [(float)(-Math.Cos(x) * Math.Sin(y)), (float)Math.Sin(x), (float)(-Math.Cos(x) * Math.Cos(y))];
            }
        }
    }

    internal static SortedDictionary<int, Shadow> Shadows(Install source) => Rows(source, "m10_ShadowBank.param", "SHADOW_BANK", v =>
    {
        // lazy: where the shadow map begins and ends, its depth offsets and its volume are the source renderer's own
        // business; the host casts the shadows with its own map.
        // The install's definition spells one field with a full-width first letter.
        foreach (string unused in new[] { "beginDist", "endDist", "calibulateFar", "persedDepthOffset", "\uFF47radFactor", "shadowVolumeDepth" }) _ = v(unused);
        var shadow = new Shadow(v("lightDegRotX"), v("lightDegRotY"), v("densityRatio") / 100, [v("colR") / 255, v("colG") / 255, v("colB") / 255], v("fadeBeginDist"), v("fadeDist"));
        return shadow.Density is >= 0 and <= 1 && shadow.Colour.All(c => c is >= 0 and <= 1) && shadow.FadeStart >= 0 && shadow.FadeLength >= 0 ? shadow : throw new InvalidDataException("Invalid source shadow row");
    });

    /// <summary>A tone-map row: the exposure key and the luminance range the eye adapts within; bloom and shaft settings.</summary>
    internal sealed record Tone(float Key, float MinLuminance, float MaxLuminance, float AdaptSpeed, float BloomBegin, float BloomStrength,
        float BloomBeginFar, float BloomStrengthFar, float BloomNear, float BloomFar, float ShaftBegin, float ShaftPower, float ShaftFalloff, float InverseScale)
    {
        /// <summary>The exposure once the eye has adapted to a scene at least as bright as the row's upper luminance.</summary>
        internal float BrightExposure => Key / (MaxLuminance + 0.001f);
    }

    /// <summary>A tone-correction row: per-channel brightness and contrast, saturation, and hue rotation in degrees.</summary>
    internal sealed record Correction(float[] Brightness, float[] Contrast, float Saturation, float Hue);

    internal static SortedDictionary<int, Correction> ToneCorrection(Install source) => Rows(source, "m10_ToneCorrectBank.param", "TONE_CORRECT_BANK", v =>
    {
        // lazy: the effect multiplier scales effect brightness, which no translated stage uses yet.
        _ = v("sfxMultiplier");
        return new Correction([v("brightnessR"), v("brightnessG"), v("brightnessB")], [v("contrastR"), v("contrastG"), v("contrastB")], v("saturation"), v("hue"));
    });

    internal static SortedDictionary<int, Focus> DepthOfField(Install source) => Rows(source, "m10_DofBank.param", "DOF_BANK",
        v => new Focus(v("farDofBegin"), v("farDofEnd"), v("farDofMul"), v("nearDofBegin"), v("nearDofEnd"), v("nearDofMul"), v("dispersionSq")));

    internal static SortedDictionary<int, Tone> ToneMap(Install source) => Rows(source, "m10_ToneMapBank.param", "TONE_MAP_BANK", v =>
    {
        var tone = new Tone(v("grayKeyValue"), v("minAdaptedLum"), v("maxAdapredLum"), v("adaptSpeed"), v("bloomBegin") / 100, v("bloomMul") / 100,
            v("bloomBeginFar") / 100, v("bloomMulFar") / 100, v("bloomNearDist"), v("bloomFarDist"), v("lightShaftBegin") / 100, v("lightShaftPower") / 100,
            v("lightShaftAttenRate"), v("inverseToneMapMul"));
        return tone.Key > 0 && tone.MinLuminance > 0 && tone.MaxLuminance >= tone.MinLuminance ? tone : throw new InvalidDataException("Invalid source tone-map row");
    });

    private static SortedDictionary<int, T> Rows<T>(Install source, string name, string type, Func<Func<string, float>, T> read)
    {
        var definitions = BND3.Read(source.Read("paramdef/paramdef.paramdefbnd.dcx")).Files.Select(f => PARAMDEF.Read(f.Bytes)).ToArray();
        var tables = BND3.Read(source.Read("param/DrawParam/a10_DrawParam.parambnd.dcx"));
        var entries = tables.Files.Where(f => Path.GetFileName(f.Name.Replace('\\', '/')) == name).ToArray();
        if (entries.Length != 1) throw new InvalidDataException("Expected one source table " + name);
        var table = PARAM.Read(entries[0].Bytes);
        var matches = definitions.Where(d => d.ParamType == type && d.DataVersion == table.ParamdefDataVersion).ToArray();
        if (table.ParamType != type || matches.Length != 1 || !table.ApplyParamdefCarefully(matches[0])
            || table.Rows.Select(r => r.ID).Distinct().Count() != table.Rows.Count)
            throw new InvalidDataException("Unsupported or ambiguous source table " + name);
        var result = new SortedDictionary<int, T>();
        foreach (var row in table.Rows)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            float Value(string field)
            {
                var cells = row.Cells.Where(c => c.Def.InternalName == field).ToArray();
                if (cells.Length != 1) throw new InvalidDataException($"Missing or ambiguous field {field} in {name}");
                used.Add(field);
                float value = Convert.ToSingle(cells[0].Value, CultureInfo.InvariantCulture);
                return float.IsFinite(value) ? value : throw new InvalidDataException($"Non-finite field {field} in row {row.ID} of {name}");
            }
            result.Add(row.ID, read(Value));
            var unknown = row.Cells.FirstOrDefault(c => c.Def.DisplayType != PARAMDEF.DefType.dummy8 && !used.Contains(c.Def.InternalName));
            if (unknown is not null) throw new InvalidDataException($"Unclassified field {unknown.Def.InternalName} in {name}");
        }
        return result;
    }
}
