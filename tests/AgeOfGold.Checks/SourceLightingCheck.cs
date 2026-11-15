using AgeOfGold;
using AgeOfGold.Params;
using SoulsFormats;

internal static class SourceLightingCheck
{
    internal static void Run(string root)
    {
        var path = Path.Combine(root, "authored-lighting");
        Directory.CreateDirectory(path);
        System.IO.File.WriteAllText(Path.Combine(path, "DarkSoulsRemastered.exe"), "authored source executable");
        var light = new PARAMDEF { ParamType = "LIGHT_BANK", DataVersion = 73 };
        var names = new List<string>();
        foreach (var suffix in new[] { "0", "1", "2", "s" })
            names.AddRange(new[] { "degRotX_" + suffix, "degRotY_" + suffix });
        foreach (var suffix in new[] { "0", "1", "2", "s", "u", "d" })
            names.AddRange("RGBA".Select(c => "col" + c + "_" + suffix));
        foreach (var prefix in new[] { "envDif", "envSpc" })
            names.AddRange("RGBA".Select(c => prefix + "_col" + c));
        names.AddRange(new[] { "envDif", "envSpc_0", "envSpc_1", "envSpc_2", "envSpc_3" });
        // An unusual field order proves the offsets come from this install's definition.
        foreach (var name in names.OrderDescending(StringComparer.Ordinal))
            light.Fields.Add(new(light, PARAMDEF.DefType.s16, name));
        var point = new PARAMDEF { ParamType = "POINT_LIGHT_BANK", DataVersion = 79 };
        point.Fields.Add(new(point, PARAMDEF.DefType.f32, "dwindleEnd"));
        foreach (var channel in "ABGR") point.Fields.Add(new(point, PARAMDEF.DefType.s16, "col" + channel));
        point.Fields.Add(new(point, PARAMDEF.DefType.f32, "dwindleBegin"));
        var definitions = new BND3 { Version = "AUTHORED", Files = [File(0, "light.paramdef", light.Write()), File(1, "point.paramdef", point.Write())] };
        var scenery = Table(light, 175);
        var points = Table(point, 125);
        var tables = new BND3 { Version = "AUTHORED", Files = [
            File(0, "m10_LightBank.param", scenery.Write()), File(1, "m10_PointLightBank.param", points.Write()),
        ] };
        Write("paramdef/paramdef.paramdefbnd.dcx", definitions.Write());
        Write("param/DrawParam/a10_DrawParam.parambnd.dcx", tables.Write());
        var actual = SourceLightingReader.Read(new Install(path, "DarkSoulsRemastered.exe"));
        var row = actual.Sets.Single(r => r.Id == 3);
        Require(row.Diffuse.Length == 3 && row.Diffuse[0].Elevation == 13 && row.Diffuse[0].Azimuth == -27, "directional light angles");
        Require(row.Diffuse[0].Colour == new SourceLightingReader.Colour(63f / 255, 127f / 255, 223f / 255, 1.75f), "colour and intensity units");
        Require(row.Upper == row.Lower && row.Specular.Colour == row.Upper, "hemisphere or specular colour");
        Require(row.DiffuseEnvironment.Textures.SequenceEqual(new[] { 17 })
            && row.SpecularEnvironment.Textures.SequenceEqual(new[] { 19, 23, 29, 31 }), "environment texture references");
        Require(actual.Points.Single(r => r.Id == 8) is { FadeStart: 2.5f, FadeEnd: 10.5f, Colour.Intensity: 1.25f }, "point light falloff");
        Require(actual.Sets.Select(r => r.Id).SequenceEqual(new[] { 3, 8 }), "stable row ordering");
        Console.WriteLine("PASS: source lighting uses installed definitions, retains light directions, colour units, environment references and falloff");
        void RejectCurrent(string description)
        {
            Write("paramdef/paramdef.paramdefbnd.dcx", definitions.Write());
            Write("param/DrawParam/a10_DrawParam.parambnd.dcx", tables.Write());
            try { SourceLightingReader.Read(new Install(path, "DarkSoulsRemastered.exe")); }
            catch (InvalidDataException) { return; }
            throw new Exception("Accepted " + description);
        }
        definitions.Files.Add(File(2, "duplicate.paramdef", light.Write()));
        RejectCurrent("ambiguous lighting definition");
        definitions.Files.RemoveAt(2);
        light.DataVersion++;
        definitions.Files[0].Bytes = light.Write();
        RejectCurrent("mismatched lighting definition revision");
        light.DataVersion--;
        definitions.Files[0].Bytes = light.Write();
        scenery.Rows[0].ID = 3;
        tables.Files[0].Bytes = scenery.Write();
        RejectCurrent("duplicate lighting identity");
        scenery.Rows[0].ID = 8;
        tables.Files[0].Bytes = scenery.Write();
        points.Rows[0]["dwindleEnd"].Value = float.NaN;
        tables.Files[1].Bytes = points.Write();
        RejectCurrent("non-finite point-light falloff");
        points.Rows[0]["dwindleEnd"].Value = 1f;
        tables.Files[1].Bytes = points.Write();
        RejectCurrent("reversed point-light falloff");
        points.Rows[0]["dwindleEnd"].Value = 10.5f;
        tables.Files[1].Bytes = points.Write();
        light.Fields.Add(new(light, PARAMDEF.DefType.s16, "unclassified"));
        definitions.Files[0].Bytes = light.Write();
        tables.Files[0].Bytes = Table(light, 175).Write();
        RejectCurrent("unclassified lighting field");
        Console.WriteLine("PASS: ambiguous definitions, unsupported layouts, duplicate rows, unknown fields and invalid falloff fail");

        void Write(string relative, byte[] bytes)
        {
            var target = Path.Combine(path, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            System.IO.File.WriteAllBytes(target, bytes);
        }
    }

    private static BinderFile File(int id, string name, byte[] bytes) => new(Binder.FileFlags.Flag1, id, name, bytes);

    private static PARAM Table(PARAMDEF definition, short intensity)
    {
        var table = new PARAM { ParamType = definition.ParamType, ParamdefDataVersion = definition.DataVersion, Rows = [] };
        table.ApplyParamdef(definition);
        foreach (var id in new[] { 8, 3 })
        {
            var row = new PARAM.Row(id, "authored light " + id, definition);
            foreach (var cell in row.Cells)
            {
                var name = cell.Def.InternalName;
                cell.Value = cell.Def.DisplayType == PARAMDEF.DefType.f32
                    ? (object)(name == "dwindleBegin" ? 2.5f : 10.5f)
                    : (short)(name.Contains("colR") ? 63 : name.Contains("colG") ? 127 : name.Contains("colB") ? 223
                    : name.Contains("colA") ? intensity : name.StartsWith("degRotX") ? 13 : name.StartsWith("degRotY") ? -27
                    : name == "envDif" ? 17 : name == "envSpc_0" ? 19 : name == "envSpc_1" ? 23 : name == "envSpc_2" ? 29 : 31);
            }
            table.Rows.Add(row);
        }
        return table;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("Source lighting lost " + message);
    }
}
