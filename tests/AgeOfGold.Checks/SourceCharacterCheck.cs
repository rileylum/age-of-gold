using System.Buffers.Binary;
using AgeOfGold;
using AgeOfGold.Characters;
using SoulsFormats;

internal static class SourceCharacterCheck
{
    internal static void Run(string root)
    {
        // An authored source install holding only the character table, in the source's bundle.
        string source = Path.Combine(root, "authored-characters");
        Directory.CreateDirectory(Path.Combine(source, "param/GameParam"));
        File.WriteAllText(Path.Combine(source, "DarkSoulsRemastered.exe"), "authored executable");
        byte[] Table(int size, params (int Id, float Height, float Radius)[] rows)
        {
            var definition = new PARAMDEF { ParamType = "NPC_PARAM_ST" };
            definition.Fields.Add(new PARAMDEF.Field(definition, PARAMDEF.DefType.dummy8, "raw") { ArrayLength = size });
            var table = new PARAM { ParamType = "NPC_PARAM_ST", Rows = [] };
            table.ApplyParamdef(definition);
            foreach (var (id, height, radius) in rows)
            {
                var row = new PARAM.Row(id, "", definition); var raw = Enumerable.Range(0, size).Select(i => (byte)(i * 7 + id)).ToArray();
                BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(16), height); BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(20), radius);
                row["raw"].Value = raw; table.Rows.Add(row);
            }
            return table.Write();
        }
        void Install(byte[] table)
        {
            var bundle = new BND3();
            bundle.Files.Add(new(Binder.FileFlags.Flag1, 0, @"N:\authored\param\GameParam\OtherParam.param", [1, 2, 3]));
            bundle.Files.Add(new(Binder.FileFlags.Flag1, 1, @"N:\authored\param\GameParam\NpcParam.param", table));
            File.WriteAllBytes(Path.Combine(source, "param/GameParam/GameParam.parambnd.dcx"), bundle.Write());
        }
        // Rows without a body (no height, or no usable radius) are left out.
        Install(Table(336, (21, 0, 0.4f), (22, 1.5f, float.NaN), (254000, 1.5f, 0.4f), (290000, 1.75f, 0.35f)));
        var sizes = SourceCharacterReader.BodySizes(new Install(source, "DarkSoulsRemastered.exe"));
        if (sizes.Count != 2 || sizes[254000] != (1.5f, 0.4f) || sizes[290000] != (1.75f, 0.35f)) throw new Exception("Source body sizes differ");
        foreach (var bad in new[] { Table(340, (254000, 1.5f, 0.4f)), Table(336, (254000, 1.5f, 0.4f), (254000, 1.5f, 0.4f)) })
        {
            Install(bad);
            try { SourceCharacterReader.BodySizes(new Install(source, "DarkSoulsRemastered.exe")); throw new Exception("An unclassified source character table was accepted"); }
            catch (InvalidDataException) { }
        }
        Console.WriteLine("PASS: source characters' body sizes are read from the character table, leaving out rows without a body; other layouts fail");

        // A character's attacks: its row names a behaviour variation; the behaviour rows of that variation each name an attack row.
        byte[] Rows(string type, int size, params (int Id, Action<byte[]> Fill)[] rows)
        {
            var definition = new PARAMDEF { ParamType = type };
            definition.Fields.Add(new PARAMDEF.Field(definition, PARAMDEF.DefType.dummy8, "raw") { ArrayLength = size });
            var table = new PARAM { ParamType = type, Rows = [] };
            table.ApplyParamdef(definition);
            foreach (var (id, fill) in rows) { var row = new PARAM.Row(id, "", definition); var raw = new byte[size]; fill(raw); row["raw"].Value = raw; table.Rows.Add(row); }
            return table.Write();
        }
        Action<byte[]> Behaviour(int variation, int asked, byte kind, int refers) => raw =>
            { BinaryPrimitives.WriteInt32LittleEndian(raw, variation); BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(4), asked); raw[9] = kind; BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(12), refers); };
        void InstallAttacks(byte[] behaviourTable, byte[] attackTable)
        {
            var bundle = new BND3();
            bundle.Files.Add(new(Binder.FileFlags.Flag1, 1, @"N:\authored\param\GameParam\NpcParam.param", Rows("NPC_PARAM_ST", 336, (254000, raw => BinaryPrimitives.WriteInt32LittleEndian(raw, 25400)), (254002, raw => BinaryPrimitives.WriteInt32LittleEndian(raw, 25400)), (290000, raw => BinaryPrimitives.WriteInt32LittleEndian(raw, 29000)))));
            bundle.Files.Add(new(Binder.FileFlags.Flag1, 2, @"N:\authored\param\GameParam\BehaviorParam.param", behaviourTable));
            bundle.Files.Add(new(Binder.FileFlags.Flag1, 3, @"N:\authored\param\GameParam\AtkParam_Npc.param", attackTable));
            File.WriteAllBytes(Path.Combine(source, "param/GameParam/GameParam.parambnd.dcx"), bundle.Write());
        }
        var attackTable = Rows("ATK_PARAM_ST", 128, (254000, raw => raw[0] = 7), (254001, raw => raw[0] = 8), (290000, raw => raw[0] = 9));
        // Two behaviours share one attack row; one names a projectile, which is not an attack row; one belongs to another character.
        InstallAttacks(Rows("BEHAVIOR_PARAM_ST", 32, (225400100, Behaviour(25400, 100, 0, 254000)), (225400110, Behaviour(25400, 110, 0, 254001)), (225400120, Behaviour(25400, 120, 0, 254000)),
            (225400180, Behaviour(25400, 180, 1, 254001)), (229000100, Behaviour(29000, 100, 0, 290000))), attackTable);
        var attacks = SourceCharacterReader.Attacks(new Install(source, "DarkSoulsRemastered.exe"), 254002);
        if (attacks.Variation != 25400 || !attacks.Behaviours.Select(b => b.Id).SequenceEqual([225400100, 225400110, 225400120, 225400180]) || attacks.Behaviours.Any(b => b.Row.Length != 32)
            || !attacks.Hits.Select(h => (h.Id, h.Row[0])).SequenceEqual([(254000, (byte)7), (254001, (byte)8)]) || attacks.Projectiles != 1)
            throw new Exception("A source character's behaviour and attack rows were not gathered");
        foreach (var bad in new Action[]
        {
            () => SourceCharacterReader.Attacks(new Install(source, "DarkSoulsRemastered.exe"), 5),
            () => { InstallAttacks(Rows("BEHAVIOR_PARAM_ST", 32, (225400100, Behaviour(25400, 100, 0, 777))), attackTable); SourceCharacterReader.Attacks(new Install(source, "DarkSoulsRemastered.exe"), 254000); },
            () => { InstallAttacks(Rows("BEHAVIOR_PARAM_ST", 32, (225400100, Behaviour(25400, 100, 2, 254000))), attackTable); SourceCharacterReader.Attacks(new Install(source, "DarkSoulsRemastered.exe"), 254000); },
            () => { InstallAttacks(Rows("BEHAVIOR_PARAM_ST", 36, (225400100, Behaviour(25400, 100, 0, 254000))), attackTable); SourceCharacterReader.Attacks(new Install(source, "DarkSoulsRemastered.exe"), 254000); },
        })
        {
            try { bad(); throw new Exception("Invalid source attack rows were accepted"); }
            catch (InvalidDataException) { }
        }
        Console.WriteLine("PASS: a source character's behaviour rows and the attack rows they name are gathered by its variation; missing or unclassified rows fail");
    }
}
