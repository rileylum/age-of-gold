using AgeOfGold;
using AgeOfGold.Maps;
using AgeOfGold.Params;
using SoulsFormats;

internal static class SourceAtmosphereCheck
{
    internal static void Run(string root)
    {
        var path = Path.Combine(root, "authored-atmosphere");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "DarkSoulsRemastered.exe"), "authored source executable");
        void Write(string relative, byte[] bytes)
        {
            var target = Path.Combine(path, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        }
        var layout = new MSB1();
        layout.Models.Collisions.Add(new() { Name = "h0000" });
        layout.Parts.Collisions.Add(new() { Name = "authored-floor", ModelName = "h0000", LightID = 25, FogID = 7, ScatterID = 8, DofID = 9, ToneMapID = 10, ToneCorrectID = 11, ShadowID = 10 });
        layout.Parts.Collisions.Add(new() { Name = "authored-cellar", ModelName = "h0000", DofID = 4 });
        Write("map/MapStudio/m10_02_00_00.msb", layout.Write());
        var definition = new PARAMDEF { ParamType = "DOF_BANK", DataVersion = 1 };
        // An unusual field order proves the offsets come from this install's definition.
        foreach (var name in new[] { "dispersionSq", "nearDofMul", "farDofEnd", "nearDofBegin", "farDofMul", "nearDofEnd", "farDofBegin" })
            definition.Fields.Add(new(definition, PARAMDEF.DefType.f32, name));
        PARAM Table()
        {
            var table = new PARAM { ParamType = definition.ParamType, ParamdefDataVersion = definition.DataVersion, Rows = [] };
            table.ApplyParamdef(definition);
            foreach (int id in new[] { 9, 4 })
            {
                var row = new PARAM.Row(id, "authored focus " + id, definition);
                foreach (var cell in row.Cells)
                    cell.Value = cell.Def.InternalName switch { "farDofBegin" => id + 1.5f, "farDofEnd" => id * 10f, "farDofMul" => 60f, "nearDofBegin" => 0.75f, "nearDofEnd" => 0f, "nearDofMul" => 100f, _ => 1f };
                table.Rows.Add(row);
            }
            return table;
        }
        // Tone map and tone correction rows, again in an order only the definition explains.
        PARAM Bank(PARAMDEF bank, Func<string, object> value)
        {
            var table = new PARAM { ParamType = bank.ParamType, ParamdefDataVersion = bank.DataVersion, Rows = [] };
            table.ApplyParamdef(bank);
            foreach (int id in new[] { 10, 11 })
            {
                var row = new PARAM.Row(id, "authored tone " + id, bank);
                foreach (var cell in row.Cells) cell.Value = value(cell.Def.InternalName);
                table.Rows.Add(row);
            }
            return table;
        }
        var toneMap = new PARAMDEF { ParamType = "TONE_MAP_BANK", DataVersion = 3 };
        foreach (var (name, type) in new[] { ("inverseToneMapMul", PARAMDEF.DefType.f32), ("maxAdapredLum", PARAMDEF.DefType.f32), ("bloomBegin", PARAMDEF.DefType.s16),
            ("grayKeyValue", PARAMDEF.DefType.f32), ("bloomMul", PARAMDEF.DefType.s16), ("minAdaptedLum", PARAMDEF.DefType.f32), ("bloomBeginFar", PARAMDEF.DefType.s16),
            ("adaptSpeed", PARAMDEF.DefType.f32), ("bloomMulFar", PARAMDEF.DefType.s16), ("lightShaftAttenRate", PARAMDEF.DefType.f32), ("bloomNearDist", PARAMDEF.DefType.f32),
            ("lightShaftPower", PARAMDEF.DefType.s16), ("bloomFarDist", PARAMDEF.DefType.f32), ("lightShaftBegin", PARAMDEF.DefType.s16) })
            toneMap.Fields.Add(new(toneMap, type, name));
        var correct = new PARAMDEF { ParamType = "TONE_CORRECT_BANK", DataVersion = 1 };
        foreach (var name in new[] { "saturation", "contrastB", "brightnessG", "hue", "contrastR", "sfxMultiplier", "brightnessR", "contrastG", "brightnessB" })
            correct.Fields.Add(new(correct, PARAMDEF.DefType.f32, name));
        object ToneValue(string name) => name switch { "grayKeyValue" => 0.18f, "minAdaptedLum" => 0.04f, "maxAdapredLum" => 0.089f, "adaptSpeed" => 1.3f,
            "bloomBegin" => (short)60, "bloomMul" => (short)30, "bloomBeginFar" => (short)10, "bloomMulFar" => (short)100, "bloomNearDist" => 20f, "bloomFarDist" => 60f,
            "lightShaftBegin" => (short)50, "lightShaftPower" => (short)0, "lightShaftAttenRate" => 0.95f, _ => 3f };
        object CorrectValue(string name) => name switch { "brightnessR" => 1.1f, "brightnessG" => 1f, "brightnessB" => 0.9f, "contrastR" => 1.15f, "contrastG" => 1.2f,
            "contrastB" => 1.25f, "saturation" => 1.2f, "hue" => 0f, _ => 1f };
        // Shadow rows: the light's two angles in whole degrees, how dark and what colour a shadow is, and how far from the camera it reaches.
        var shadow = new PARAMDEF { ParamType = "SHADOW_BANK", DataVersion = 2 };
        foreach (var (name, type) in new[] { ("fadeDist", PARAMDEF.DefType.f32), ("colB", PARAMDEF.DefType.s16), ("lightDegRotY", PARAMDEF.DefType.s16), ("beginDist", PARAMDEF.DefType.f32),
            ("densityRatio", PARAMDEF.DefType.s16), ("colR", PARAMDEF.DefType.s16), ("endDist", PARAMDEF.DefType.f32), ("lightDegRotX", PARAMDEF.DefType.s16), ("calibulateFar", PARAMDEF.DefType.f32),
            ("colG", PARAMDEF.DefType.s16), ("fadeBeginDist", PARAMDEF.DefType.f32), ("persedDepthOffset", PARAMDEF.DefType.f32), ("\uFF47radFactor", PARAMDEF.DefType.f32), ("shadowVolumeDepth", PARAMDEF.DefType.f32) })
            shadow.Fields.Add(new(shadow, type, name));
        object ShadowValue(string name) => name switch { "lightDegRotX" => (short)30, "lightDegRotY" => (short)-90, "densityRatio" => (short)80, "colR" => (short)0, "colG" => (short)51, "colB" => (short)255,
            "fadeBeginDist" => 3f, "fadeDist" => 7f, "endDist" => 10f, _ => 0.5f };
        var tables = new BND3 { Version = "AUTHORED", Files = [new(Binder.FileFlags.Flag1, 3, "m10_ShadowBank.param", Bank(shadow, ShadowValue).Write()), new(Binder.FileFlags.Flag1, 0, "m10_DofBank.param", Table().Write()),
            new(Binder.FileFlags.Flag1, 1, "m10_ToneMapBank.param", Bank(toneMap, ToneValue).Write()),
            new(Binder.FileFlags.Flag1, 2, "m10_ToneCorrectBank.param", Bank(correct, CorrectValue).Write())] };
        var definitions = new BND3 { Version = "AUTHORED", Files = [new(Binder.FileFlags.Flag1, 3, "shadow.paramdef", shadow.Write()), new(Binder.FileFlags.Flag1, 0, "dof.paramdef", definition.Write()),
            new(Binder.FileFlags.Flag1, 1, "tonemap.paramdef", toneMap.Write()), new(Binder.FileFlags.Flag1, 2, "tonecorrect.paramdef", correct.Write())] };
        Write("paramdef/paramdef.paramdefbnd.dcx", definitions.Write());
        Write("param/DrawParam/a10_DrawParam.parambnd.dcx", tables.Write());

        var install = new Install(path, "DarkSoulsRemastered.exe");
        var selection = SourceAtmosphereReader.Selections(install)["authored-floor"];
        if (selection != new SourceAtmosphereReader.Selection(25, 7, 8, 9, 10, 11, 10)) throw new Exception("Collision atmosphere selections differ");
        // The light stands thirty degrees above the horizon. It shines along (cos x sin y, -sin x, cos x cos y) for its angles x and y, as the
        // source's scattering sun does, so a quarter turn back puts it towards +X from a surface.
        var shade = SourceAtmosphereReader.Shadows(install)[10];
        var towards = shade.TowardsLight;
        if (shade.Density != 0.8f || !shade.Colour.SequenceEqual(new[] { 0f, 0.2f, 1f }) || shade.FadeStart != 3 || shade.FadeLength != 7
            || MathF.Abs(towards[0] - MathF.Cos(MathF.PI / 6)) > 1e-6f || MathF.Abs(towards[1] - 0.5f) > 1e-6f || MathF.Abs(towards[2]) > 1e-6f)
            throw new Exception($"Shadow row differs: {shade} towards {string.Join(",", towards)}");
        var focus = SourceAtmosphereReader.DepthOfField(install);
        if (!focus.Keys.Order().SequenceEqual(new[] { 4, 9 })
            || focus[9] != new SourceAtmosphereReader.Focus(10.5f, 90, 60, 0.75f, 0, 100, 1)) throw new Exception("Depth-of-field rows differ");

        var tone = SourceAtmosphereReader.ToneMap(install)[10];
        if (tone.Key != 0.18f || tone.MinLuminance != 0.04f || tone.MaxLuminance != 0.089f || tone.BloomBegin != 0.6f || tone.BloomStrengthFar != 1f
            || MathF.Abs(tone.BrightExposure - 2f) > 1e-6f) throw new Exception("Tone-map row or its bright-adapted exposure differs");
        var corrected = SourceAtmosphereReader.ToneCorrection(install)[11];
        if (!corrected.Brightness.SequenceEqual(new[] { 1.1f, 1f, 0.9f }) || !corrected.Contrast.SequenceEqual(new[] { 1.15f, 1.2f, 1.25f }) || corrected.Saturation != 1.2f || corrected.Hue != 0)
            throw new Exception("Tone-correction row differs");

        // The host keeps its own lens; only the far focus range follows the source row, and camera autofocus is off as in the source.
        var file = new GPARAM { Game = GPARAM.GPGame.Sekiro, Groups = [], Unk3s = [], UnkBlock2 = [] };
        var lens = new GPARAM.Group("Authored Lens", "lens") { Params = [], Comments = [] };
        file.Groups.Add(lens);
        void Add(string name, GPARAM.ParamType type, object value) =>
            lens.Params.Add(new GPARAM.Param(name, "authored", type) { Values = [value], ValueIDs = [0], TimeOfDay = [0] });
        Add("[GS]Lens Simulation Param FarStart", GPARAM.ParamType.Float, 3000f);
        Add("[GS]Lens Simulation Param FarEnd", GPARAM.ParamType.Float, 4000f);
        Add("[GS]Lens Simulation Param UseCameraFocus", GPARAM.ParamType.IntB, 1);
        Add("[GS]Lens Simulation Param Authored", GPARAM.ParamType.Float, 0.08f);
        var output = GPARAM.Read(HostAtmosphere.FocusLikeSource(file.Write(), focus[9])).Groups.Single().Params.ToDictionary(p => p.Name1, p => p.Values.Single());
        if (!output["[GS]Lens Simulation Param FarStart"].Equals(10.5f) || !output["[GS]Lens Simulation Param FarEnd"].Equals(90f)
            || !output["[GS]Lens Simulation Param UseCameraFocus"].Equals(0) || !output["[GS]Lens Simulation Param Authored"].Equals(0.08f))
            throw new Exception("Host far focus does not follow the source row");
        try { HostAtmosphere.FocusLikeSource(file.Write(), focus[9] with { FarEnd = 5 }); throw new Exception("Accepted a reversed focus range"); }
        catch (InvalidDataException) { }
        definition.Fields.Add(new(definition, PARAMDEF.DefType.f32, "unclassified"));
        definitions.Files[0].Bytes = definition.Write(); tables.Files[0].Bytes = Table().Write();
        Write("paramdef/paramdef.paramdefbnd.dcx", definitions.Write()); Write("param/DrawParam/a10_DrawParam.parambnd.dcx", tables.Write());
        try { SourceAtmosphereReader.DepthOfField(new Install(path, "DarkSoulsRemastered.exe")); throw new Exception("Accepted an unclassified focus field"); }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS: source collision atmosphere selections and depth-of-field rows read by installed definitions; host far focus follows the source row");
    }
}
