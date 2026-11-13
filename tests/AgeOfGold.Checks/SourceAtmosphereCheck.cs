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
        layout.Parts.Collisions.Add(new() { Name = "authored-floor", ModelName = "h0000", LightID = 25, FogID = 7, ScatterID = 8, DofID = 9, ToneMapID = 10, ToneCorrectID = 11 });
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
        var tables = new BND3 { Version = "AUTHORED", Files = [new(Binder.FileFlags.Flag1, 0, "m10_DofBank.param", Table().Write())] };
        var definitions = new BND3 { Version = "AUTHORED", Files = [new(Binder.FileFlags.Flag1, 0, "dof.paramdef", definition.Write())] };
        Write("paramdef/paramdef.paramdefbnd.dcx", definitions.Write());
        Write("param/DrawParam/a10_DrawParam.parambnd.dcx", tables.Write());

        var install = new Install(path, "DarkSoulsRemastered.exe");
        var selection = SourceAtmosphereReader.Selections(install)["authored-floor"];
        if (selection != new SourceAtmosphereReader.Selection(25, 7, 8, 9, 10, 11)) throw new Exception("Collision atmosphere selections differ");
        var focus = SourceAtmosphereReader.DepthOfField(install);
        if (!focus.Keys.Order().SequenceEqual(new[] { 4, 9 })
            || focus[9] != new SourceAtmosphereReader.Focus(10.5f, 90, 60, 0.75f, 0, 100, 1)) throw new Exception("Depth-of-field rows differ");

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
