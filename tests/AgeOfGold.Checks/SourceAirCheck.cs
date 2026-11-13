using AgeOfGold;
using AgeOfGold.Params;
using SoulsFormats;

internal static class SourceAirCheck
{
    internal static void Run(string root)
    {
        var path = Path.Combine(root, "authored-air");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "DarkSoulsRemastered.exe"), "authored source executable");
        void Write(string relative, byte[] bytes)
        {
            var target = Path.Combine(path, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        }
        var scattering = new PARAMDEF { ParamType = "LIGHT_SCATTERING_BANK", DataVersion = 1 };
        // An unusual field order proves the offsets come from this install's definition.
        foreach (var (name, type) in new[] { ("lsBetaMie", PARAMDEF.DefType.f32), ("reflectanceA", PARAMDEF.DefType.s16), ("sunRotY", PARAMDEF.DefType.s16),
            ("distanceMul", PARAMDEF.DefType.s16), ("sunB", PARAMDEF.DefType.s16), ("lsHGg", PARAMDEF.DefType.f32), ("sunR", PARAMDEF.DefType.s16),
            ("blendCoef", PARAMDEF.DefType.s16), ("reflectanceG", PARAMDEF.DefType.s16), ("sunA", PARAMDEF.DefType.s16), ("lsBetaRay", PARAMDEF.DefType.f32),
            ("reflectanceR", PARAMDEF.DefType.s16), ("sunRotX", PARAMDEF.DefType.s16), ("sunG", PARAMDEF.DefType.s16), ("reflectanceB", PARAMDEF.DefType.s16) })
            scattering.Fields.Add(new(scattering, type, name));
        var fog = new PARAMDEF { ParamType = "FOG_BANK", DataVersion = 1 };
        foreach (var name in new[] { "colA", "degRotW", "fogEndZ", "colG", "degRotZ", "colR", "fogBeginZ", "colB" })
            fog.Fields.Add(new(fog, PARAMDEF.DefType.s16, name));
        PARAM Table(PARAMDEF definition, Dictionary<string, object> values)
        {
            var table = new PARAM { ParamType = definition.ParamType, ParamdefDataVersion = definition.DataVersion, Rows = [] };
            table.ApplyParamdef(definition);
            // Two rows let the reader measure the row size against the definition.
            foreach (int id in new[] { 9, 7 })
            {
                var row = new PARAM.Row(id, "authored air " + id, definition);
                foreach (var cell in row.Cells) cell.Value = values.TryGetValue(cell.Def.InternalName, out var value) ? value : (object)(short)0;
                table.Rows.Add(row);
            }
            return table;
        }
        var sky = new Dictionary<string, object>
        {
            ["sunRotX"] = (short)30, ["sunRotY"] = (short)60, ["distanceMul"] = (short)200, ["sunR"] = (short)51, ["sunG"] = (short)102, ["sunB"] = (short)204,
            ["sunA"] = (short)150, ["lsHGg"] = 0.5f, ["lsBetaRay"] = 0.2f, ["lsBetaMie"] = 0.3f, ["blendCoef"] = (short)25,
            ["reflectanceR"] = (short)255, ["reflectanceG"] = (short)128, ["reflectanceB"] = (short)64, ["reflectanceA"] = (short)80,
        };
        var mist = new Dictionary<string, object>
        {
            ["fogBeginZ"] = (short)10, ["fogEndZ"] = (short)60, ["degRotZ"] = (short)0, ["degRotW"] = (short)40,
            ["colR"] = (short)51, ["colG"] = (short)102, ["colB"] = (short)153, ["colA"] = (short)200,
        };
        var definitions = new BND3 { Version = "AUTHORED", Files = [new(Binder.FileFlags.Flag1, 0, "scattering.paramdef", scattering.Write()), new(Binder.FileFlags.Flag1, 1, "fog.paramdef", fog.Write())] };
        void Install()
        {
            Write("paramdef/paramdef.paramdefbnd.dcx", definitions.Write());
            Write("param/DrawParam/a10_DrawParam.parambnd.dcx", new BND3 { Version = "AUTHORED", Files = [
                new(Binder.FileFlags.Flag1, 0, "m10_LightScatteringBank.param", Table(scattering, sky).Write()),
                new(Binder.FileFlags.Flag1, 1, "m10_FogBank.param", Table(fog, mist).Write())] }.Write());
        }
        Install();
        var air = SourceAirReader.Read(new Install(path, "DarkSoulsRemastered.exe"));
        var constants = SourceAir.Constants(air.Scattering[7], air.Fog[7]);

        // Expected values from the published scattering model, worked out independently in double precision.
        Near(constants.BetaDashRayleigh, [8.29911250041599e-06, 1.403413518791237e-05, 2.9101182725655094e-05], "Rayleigh angular term");
        Near(constants.BetaDashMie, [0.0004001362347501834, 0.0005203372089318328, 0.0007492855808618393], "Mie angular term");
        Near(constants.Extinction, [0.0018612320906716238, 0.0024550498889639046, 0.003641888098805955], "total extinction");
        Near(constants.Phase, [0.75, 1.5, 1.0], "phase terms");
        Near(constants.SunDirection, [0.75, -0.5, 0.43301270189221946], "sun direction");
        Near(constants.SunColour, [0.3, 0.6, 1.2], "sun colour");
        Near(constants.Reflectance, [1.0, 0.5019607843137255, 0.25098039215686274], "reflectance");
        Near(constants.FogColour, [0.1332085131842997, 0.6120655998656237, 1.4934776966447731], "fog colour");
        Near([constants.Distance, constants.Blend, constants.ReflectanceStrength, constants.FogBegin, constants.FogScale, constants.FogLimit],
            [2.0, 0.25, 0.8, 10, 1 / 50.0, 0.4], "scalar air terms");

        foreach (var (name, value) in new (string, object)[] { ("lsHGg", 1f), ("lsBetaMie", -0.1f), ("lsBetaRay", float.NaN), ("sunA", (short)-1) })
        {
            var saved = sky[name]; sky[name] = value; Install();
            try { var bad = SourceAirReader.Read(new Install(path, "DarkSoulsRemastered.exe")); SourceAir.Constants(bad.Scattering[7], bad.Fog[7]); throw new Exception("Accepted invalid scattering " + name); }
            catch (InvalidDataException) { }
            sky[name] = saved;
        }
        mist["fogEndZ"] = (short)10; Install();
        try { var bad = SourceAirReader.Read(new Install(path, "DarkSoulsRemastered.exe")); SourceAir.Constants(bad.Scattering[7], bad.Fog[7]); throw new Exception("Accepted an empty fog range"); }
        catch (InvalidDataException) { }
        mist["fogEndZ"] = (short)60;
        fog.Fields.Add(new(fog, PARAMDEF.DefType.s16, "unclassified"));
        definitions.Files[1].Bytes = fog.Write(); Install();
        try { SourceAirReader.Read(new Install(path, "DarkSoulsRemastered.exe")); throw new Exception("Accepted an unclassified fog field"); }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS: source fog and scattering rows read by installed definitions; air constants match the published model; invalid rows fail");
    }

    private static void Near(float[] actual, double[] expected, string what)
    {
        if (actual.Length != expected.Length || actual.Zip(expected).Any(p => !(Math.Abs(p.First - p.Second) <= 2e-6 * Math.Max(1e-6, Math.Abs(p.Second)) + 1e-12)))
            throw new Exception($"Source air check failed: {what}: {string.Join(", ", actual)}");
    }
}
