using System.Buffers.Binary;
using AgeOfGold;
using AgeOfGold.Characters;
using SoulsFormats;

internal static class SourceHumanCheck
{
    internal static void Run(string root)
    {
        string source = Path.Combine(root, "authored-humans");
        Directory.CreateDirectory(Path.Combine(source, "param/GameParam"));
        File.WriteAllText(Path.Combine(source, "DarkSoulsRemastered.exe"), "authored executable");
        static byte[] Table(string type, int size, params (int Id, (int At, int Value, bool Short)[] Fields)[] rows)
        {
            var definition = new PARAMDEF { ParamType = type };
            definition.Fields.Add(new PARAMDEF.Field(definition, PARAMDEF.DefType.dummy8, "raw") { ArrayLength = size });
            var table = new PARAM { ParamType = type, Rows = [] };
            table.ApplyParamdef(definition);
            foreach (var (id, fields) in rows)
            {
                var row = new PARAM.Row(id, "", definition); var raw = new byte[size];
                foreach (var (at, value, small) in fields) { if (small) BinaryPrimitives.WriteInt16LittleEndian(raw.AsSpan(at), (short)value); else BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(at), value); }
                row["raw"].Value = raw; table.Rows.Add(row);
            }
            return table.Write();
        }
        void Install(byte[] starts)
        {
            var bundle = new BND3();
            bundle.Files.Add(new(Binder.FileFlags.Flag1, 0, @"N:\authored\param\GameParam\CharaInitParam.param", starts));
            // Armour 171000 and 173000 are model 9300; weapon 201000 is model 201 and shield 1450000 model 1515.
            bundle.Files.Add(new(Binder.FileFlags.Flag1, 1, @"N:\authored\param\GameParam\EquipParamProtector.param",
                Table("EQUIP_PARAM_PROTECTOR_ST", 232, (171000, [(160, 9300, true)]), (173000, [(160, 9300, true)]), (500000, [(160, -1, true)]))));
            bundle.Files.Add(new(Binder.FileFlags.Flag1, 2, @"N:\authored\param\GameParam\EquipParamWeapon.param",
                Table("EQUIP_PARAM_WEAPON_ST", 272, (201000, [(184, 201, true)]), (1450000, [(184, 1515, true)]))));
            File.WriteAllBytes(Path.Combine(source, "param/GameParam/GameParam.parambnd.dcx"), bundle.Write());
        }
        (int, int, bool)[] Start(int right, int left, int head, int body, int arms, int legs) =>
            [(16, right, false), (20, -1, false), (24, left, false), (28, -1, false), (32, head, false), (36, body, false), (40, arms, false), (44, legs, false)];
        // A warrior: a reinforced sword and shield, body and leg armour reinforced, nothing on head or arms.
        Install(Table("CHARACTER_INIT_PARAM", 240, (6270, Start(201010, 1450010, -1, 171008, -1, 173008)), (6999, Start(-1, -1, -1, 500000, -1, -1)), (6998, Start(777, -1, -1, -1, -1, -1))));
        var install = new Install(source, "DarkSoulsRemastered.exe");
        var human = SourceHumanReader.Read(install, 6270);
        if (!human.Parts.SequenceEqual(["HD_M_0000", "BD_M_9300", "AM_M_0000", "LG_M_9300", "FC_M_0000"]) || human.Right != "WP_A_0201" || human.Left != "WP_A_1515")
            throw new Exception("A human's parts differ: " + string.Join(",", human.Parts) + " " + human.Right + " " + human.Left);
        foreach (int bad in new[] { 1, 6999, 6998 })
        {
            try { SourceHumanReader.Read(install, bad); throw new Exception("An unreadable human was accepted"); }
            catch (InvalidDataException) { }
        }
        Install(Table("CHARACTER_INIT_PARAM", 244, (6270, Start(201010, 1450010, -1, 171008, -1, 173008))));
        try { SourceHumanReader.Read(install, 6270); throw new Exception("An unknown start-up row length was accepted"); }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS: a source human's part models are read from its start-up row and its equipment's rows; missing rows and other layouts fail");
    }
}
