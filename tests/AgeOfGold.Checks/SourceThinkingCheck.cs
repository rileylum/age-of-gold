using System.Buffers.Binary;
using AgeOfGold;
using AgeOfGold.Thinking;
using SoulsFormats;

internal static class SourceThinkingCheck
{
    internal static void Run(string root)
    {
        string source = Path.Combine(root, "authored-thinking");
        Directory.CreateDirectory(Path.Combine(source, "param/GameParam")); Directory.CreateDirectory(Path.Combine(source, "script"));
        File.WriteAllText(Path.Combine(source, "DarkSoulsRemastered.exe"), "authored executable");
        // A think table: each row opens with the number of what its enemy does out of battle, then its battle number.
        void Table(int size, params (int Id, int Battle)[] rows)
        {
            var definition = new PARAMDEF { ParamType = "NPC_THINK_PARAM_ST" };
            definition.Fields.Add(new PARAMDEF.Field(definition, PARAMDEF.DefType.dummy8, "raw") { ArrayLength = size });
            var table = new PARAM { ParamType = "NPC_THINK_PARAM_ST", Rows = [] };
            table.ApplyParamdef(definition);
            foreach (var (id, battle) in rows)
            {
                var row = new PARAM.Row(id, "", definition); var raw = new byte[size];
                BinaryPrimitives.WriteInt32LittleEndian(raw, 9); BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(4), battle);
                row["raw"].Value = raw; table.Rows.Add(row);
            }
            File.WriteAllBytes(Path.Combine(source, "param/GameParam/GameParam.parambnd.dcx"), new BND3 { Files = [new(Binder.FileFlags.Flag1, 0, @"N:\authored\param\GameParam\NpcThinkParam.param", table.Write())] }.Write());
        }
        Table(112, (300, 4100), (301, 4200), (302, 0));
        var install = new Install(source, "DarkSoulsRemastered.exe");
        var rows = SourceThinking.Rows(install);
        if (rows.Count != 3 || rows[300].Length != 112 || SourceThinking.Battle(rows[300]) != 4100 || SourceThinking.Battle(rows[301]) != 4200 || SourceThinking.Battle(rows[302]) != 0 || SourceThinking.Logic(rows[300]) != 9) throw new Exception("Source think rows and their battle numbers are read otherwise");

        // A map's binder holds each enemy's battle script by its number, among other scripts; the shared binder's scripts are
        // every enemy's. Members that are not scripts are left out.
        byte[] first = [1, 2, 3], second = [4, 5], shared = [6];
        File.WriteAllBytes(Path.Combine(source, "script/m77_00_00_00.luabnd.dcx"), new BND3
        {
            Files = [new(Binder.FileFlags.Flag1, 0, @"N:\authored\script\4100_battle.lua", first), new(Binder.FileFlags.Flag1, 1, @"N:\authored\script\4100_logic.lua", second)],
        }.Write());
        File.WriteAllBytes(Path.Combine(source, "script/aiCommon.luabnd.dcx"), new BND3
        {
            Files = [new(Binder.FileFlags.Flag1, 0, @"N:\authored\script\z_helpers.lua", shared), new(Binder.FileFlags.Flag1, 1, @"N:\authored\script\names.luagnl", [9]), new(Binder.FileFlags.Flag1, 2, @"N:\authored\script\a_helpers.lua", second)],
        }.Write());
        if (!SourceThinking.BattleScript(install, "m77_00_00_00", 4100).SequenceEqual(first)) throw new Exception("A map's battle script is not found by its number");
        var helpers = SourceThinking.Shared(install);
        if (!helpers.Select(h => h.Name).SequenceEqual(["a_helpers.lua", "z_helpers.lua"]) || !helpers[1].Script.SequenceEqual(shared)) throw new Exception("The source's shared scripts are read otherwise");
        try { SourceThinking.BattleScript(install, "m77_00_00_00", 4200); throw new Exception("A battle script the map does not hold was found"); }
        catch (InvalidDataException) { }
        Table(116, (300, 4100));
        try { SourceThinking.Rows(install); throw new Exception("An unknown think row length was accepted"); }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS: the source's think rows, a row's battle number, a map's battle script and the shared scripts are read; a missing script and another layout fail");
    }
}
