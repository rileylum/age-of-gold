using System.Globalization;
using SoulsFormats;

namespace AgeOfGold.Params;

/// <summary>Reads the source map's fog and light-scattering rows.</summary>
internal static class SourceAirReader
{
    /// <summary>Angles in degrees, colours as 0–1 channels with a separate strength, percentages as fractions.</summary>
    internal sealed record Scattering(float SunElevation, float SunAzimuth, float Distance, float[] Sun, float SunStrength, float Blend,
        float Anisotropy, float Rayleigh, float Mie, float[] Reflectance, float ReflectanceStrength);

    internal sealed record Fog(float Begin, float End, float Limit, float[] Colour, float ColourStrength);

    internal sealed record Air(SortedDictionary<int, Scattering> Scattering, SortedDictionary<int, Fog> Fog);

    internal static Air Read(Install source)
    {
        var definitions = BND3.Read(source.Read("paramdef/paramdef.paramdefbnd.dcx")).Files.Select(f => PARAMDEF.Read(f.Bytes)).ToArray();
        var tables = BND3.Read(source.Read("param/DrawParam/a10_DrawParam.parambnd.dcx"));
        SortedDictionary<int, T> Rows<T>(string name, string type, Func<Func<string, float>, T> read)
        {
            var entries = tables.Files.Where(f => Path.GetFileName(f.Name.Replace('\\', '/')) == name).ToArray();
            if (entries.Length != 1) throw new InvalidDataException($"Expected one source air table {name}");
            var table = PARAM.Read(entries[0].Bytes);
            var matches = definitions.Where(d => d.ParamType == type && d.DataVersion == table.ParamdefDataVersion).ToArray();
            if (table.ParamType != type || matches.Length != 1) throw new InvalidDataException($"Source air definition does not uniquely match {name}");
            if (!table.ApplyParamdefCarefully(matches[0])) throw new InvalidDataException($"Source air rows do not fit their definition: {name}");
            if (table.Rows.Select(r => r.ID).Distinct().Count() != table.Rows.Count) throw new InvalidDataException($"Duplicate source air rows in {name}");
            var result = new SortedDictionary<int, T>();
            foreach (var row in table.Rows)
            {
                var used = new HashSet<string>(StringComparer.Ordinal);
                float Value(string field)
                {
                    var cells = row.Cells.Where(c => c.Def.InternalName == field).ToArray();
                    if (cells.Length != 1) throw new InvalidDataException($"Missing or ambiguous air field {field}");
                    used.Add(field);
                    float value = Convert.ToSingle(cells[0].Value, CultureInfo.InvariantCulture);
                    return float.IsFinite(value) ? value : throw new InvalidDataException($"Non-finite air field {field} in row {row.ID}");
                }
                result.Add(row.ID, read(Value));
                var unknown = row.Cells.FirstOrDefault(c => c.Def.DisplayType != PARAMDEF.DefType.dummy8 && !used.Contains(c.Def.InternalName));
                if (unknown is not null) throw new InvalidDataException($"Unclassified air field {unknown.Def.InternalName} in {name}");
            }
            return result;
        }
        float[] Rgb(Func<string, float> value, string prefix) => [value(prefix + "R") / 255, value(prefix + "G") / 255, value(prefix + "B") / 255];
        return new(
            Rows("m10_LightScatteringBank.param", "LIGHT_SCATTERING_BANK", v => new Scattering(v("sunRotX"), v("sunRotY"), v("distanceMul") / 100,
                Rgb(v, "sun"), v("sunA") / 100, v("blendCoef") / 100, v("lsHGg"), v("lsBetaRay"), v("lsBetaMie"), Rgb(v, "reflectance"), v("reflectanceA") / 100)),
            Rows("m10_FogBank.param", "FOG_BANK", v =>
            {
                // lazy: the horizontal rotation field is not used by the surface programs translated so far.
                _ = v("degRotZ");
                return new Fog(v("fogBeginZ"), v("fogEndZ"), v("degRotW") / 100, Rgb(v, "col"), v("colA") / 100);
            }));
    }
}

/// <summary>The constants the source surface programs use for fog and for light scattering between a surface and the camera.</summary>
internal static class SourceAir
{
    /// <summary>
    /// Scattering follows Hoffman and Preetham's published outdoor model: extinction and angular terms per colour channel.
    /// Fog mixes toward a colour by view depth, from <c>FogBegin</c> at rate <c>FogScale</c> up to <c>FogLimit</c>.
    /// </summary>
    internal sealed record Values(float[] Extinction, float[] BetaDashRayleigh, float[] BetaDashMie, float[] Phase, float[] SunDirection,
        float[] SunColour, float[] Reflectance, float Distance, float Blend, float ReflectanceStrength,
        float[] FogColour, float FogBegin, float FogScale, float FogLimit);

    internal static Values Constants(SourceAirReader.Scattering scattering, SourceAirReader.Fog fog)
    {
        if (!(Math.Abs(scattering.Anisotropy) < 1) || scattering.Rayleigh < 0 || scattering.Mie < 0 || scattering.Rayleigh + scattering.Mie <= 0
            || scattering.SunStrength < 0 || scattering.Distance < 0 || scattering.Sun.Concat(scattering.Reflectance).Any(c => c is < 0 or > 1))
            throw new InvalidDataException("Invalid source scattering row");
        if (!(fog.End > fog.Begin) || fog.Limit < 0 || fog.ColourStrength < 0 || fog.Colour.Any(c => c is < 0 or > 1))
            throw new InvalidDataException("Invalid source fog row");
        // The model's reference atmosphere: refractive index, molecular density, depolarisation, turbidity two and three wavelengths.
        const double index = 1.0003, density = 2.545e25, depolarisation = 0.035, concentration = (6.544 * 2 - 6.51) * 1e-17;
        double[] wavelengths = [650e-9, 570e-9, 475e-9], mieK = [0.685, 0.679, 0.670];
        double molecular = Math.PI * Math.PI * Math.Pow(index * index - 1, 2) * (6 + 3 * depolarisation) / (6 - 7 * depolarisation) / density;
        double particles = 0.434 * concentration * Math.Pow(2 * Math.PI, 2);
        // lazy: the source's Rayleigh terms measure 0.27% above the reference model at this scale; the difference is not visible.
        double rayleigh = scattering.Rayleigh * 100;
        var dashRayleigh = wavelengths.Select(w => molecular / 2 / Math.Pow(w, 4) * rayleigh).ToArray();
        var dashMie = wavelengths.Select(w => particles * 0.5 / (w * w) * scattering.Mie).ToArray();
        var extinction = wavelengths.Select((w, i) => 8 * molecular * Math.PI / 3 / Math.Pow(w, 4) * rayleigh
            + particles * Math.PI * mieK[i] / (w * w) * scattering.Mie).ToArray();
        double x = scattering.SunElevation * Math.PI / 180, y = scattering.SunAzimuth * Math.PI / 180, g = scattering.Anisotropy;
        float[] F(IEnumerable<double> values) => values.Select(v => (float)v).ToArray();
        return new(F(extinction), F(dashRayleigh), F(dashMie), F([1 - g * g, 1 + g, 2 * g]),
            F([Math.Cos(x) * Math.Sin(y), -Math.Sin(x), Math.Cos(x) * Math.Cos(y)]),
            F(scattering.Sun.Select(c => (double)c * scattering.SunStrength)), scattering.Reflectance.ToArray(),
            scattering.Distance, scattering.Blend, scattering.ReflectanceStrength,
            // The fog colour is stored for the gamma-space mix the source performs.
            F(fog.Colour.Select(c => Math.Pow(c * (double)fog.ColourStrength, 2.2))), fog.Begin, 1 / (fog.End - fog.Begin), fog.Limit);
    }
}
