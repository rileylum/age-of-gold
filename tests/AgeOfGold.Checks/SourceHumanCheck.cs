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
            // A piece of armour hides the meshes that carry certain marks, on whatever is worn: one bit a mark, the first
            // mark's six bits into the row's 217th byte, after the bits that say where the piece is worn. The body armour
            // hides marks 3, 20 and 30, the leg armour mark 0.
            bundle.Files.Add(new(Binder.FileFlags.Flag1, 1, @"N:\authored\param\GameParam\EquipParamProtector.param",
                Table("EQUIP_PARAM_PROTECTOR_ST", 232, (171000, [(160, 9300, true), (216, 0b100 | 1 << 9 | 1 << 26, false), (220, 1 << 4, false)]), (173000, [(160, 9300, true), (216, 0b10000 | 1 << 6, false)]),
                    (500000, [(160, -1, true)]),
                    // Hair is a row of the same table, numbered from a thousand by the style for a man: style 2 is model 7
                    // and hides mark 2; style 3 names a row that is not hair, and style 4 has no row.
                    (1200, [(160, 7, true), (208, 4, false), (216, 1 << 8, false)]), (1300, [(160, 4, true), (208, 5, false)]))));
            // A face row: fifty bytes that shape the face, fifty that colour its skin, the hair's style and colour, the eyes' colour.
            bundle.Files.Add(new(Binder.FileFlags.Flag1, 3, @"N:\authored\param\GameParam\FaceGenParam.param",
                Table("FACE_PARAM_ST", 128, (77, [(0, 200, false), (48, 30 << 8, false), (50, 10, true), (98, 250 << 8, true), (100, 2 | 0 << 8 | 12 << 16 | 34 << 24, false), (104, 56 | 65 << 8 | 43 << 16 | 21 << 24, false)]),
                    (78, [(100, 3, false)]), (79, [(100, 4, false)]))));
            bundle.Files.Add(new(Binder.FileFlags.Flag1, 2, @"N:\authored\param\GameParam\EquipParamWeapon.param",
                Table("EQUIP_PARAM_WEAPON_ST", 272, (201000, [(184, 201, true)]), (1450000, [(184, 1515, true)]))));
            File.WriteAllBytes(Path.Combine(source, "param/GameParam/GameParam.parambnd.dcx"), bundle.Write());
        }
        // A start-up row names its face row, and says whether the person is a man.
        (int, int, bool)[] Start(int right, int left, int head, int body, int arms, int legs, int face = 77, int man = 1) =>
            [(16, right, false), (20, -1, false), (24, left, false), (28, -1, false), (32, head, false), (36, body, false), (40, arms, false), (44, legs, false), (164, face, false), (228, man, false)];
        // A warrior: a reinforced sword and shield, body and leg armour reinforced, nothing on head or arms.
        Install(Table("CHARACTER_INIT_PARAM", 240, (6270, Start(201010, 1450010, -1, 171008, -1, 173008)), (6999, Start(-1, -1, -1, 500000, -1, -1)), (6998, Start(777, -1, -1, -1, -1, -1)),
            (6997, Start(-1, -1, -1, -1, -1, -1, face: 5)), (6996, Start(-1, -1, -1, -1, -1, -1, face: 78)), (6995, Start(-1, -1, -1, -1, -1, -1, face: 79)), (6994, Start(-1, -1, -1, -1, -1, -1, man: 0))));
        var install = new Install(source, "DarkSoulsRemastered.exe");
        var human = SourceHumanReader.Read(install, 6270);
        if (!human.Parts.SequenceEqual(["HD_M_0000", "BD_M_9300", "AM_M_0000", "LG_M_9300", "FC_M_0000", "HR_M_0007", "FG_A_0000"]) || human.Right != "WP_A_0201" || human.Left != "WP_A_1515")
            throw new Exception("A human's parts differ: " + string.Join(",", human.Parts) + " " + human.Right + " " + human.Left);
        if (!human.Hidden.SequenceEqual([0, 2, 3, 20, 30])) throw new Exception("The marks a human's armour and hair hide differ: " + string.Join(",", human.Hidden));
        var face = human.Face;
        if (face.Shape.Length != 50 || face.Shape[0] != 200 || face.Shape[49] != 30 || face.Shape[1..49].Any(b => b != 0) || face.Skin.Length != 50 || face.Skin[0] != 10 || face.Skin[49] != 250
            || !face.Hair.SequenceEqual(new byte[] { 12, 34, 56 }) || !face.Eyes.SequenceEqual(new byte[] { 65, 43, 21 }))
            throw new Exception("A human's face row is read differently");
        // No start-up row, armour with no model, an unknown weapon; then no face row, a style whose row is not hair, a
        // style with no row, and a woman, whose part models are not chosen.
        foreach (int bad in new[] { 1, 6999, 6998, 6997, 6996, 6995, 6994 })
        {
            try { SourceHumanReader.Read(install, bad); throw new Exception("An unreadable human was accepted"); }
            catch (InvalidDataException) { }
        }
        Install(Table("CHARACTER_INIT_PARAM", 244, (6270, Start(201010, 1450010, -1, 171008, -1, 173008))));
        try { SourceHumanReader.Read(install, 6270); throw new Exception("An unknown start-up row length was accepted"); }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS: a source human's part models, hair and face row and the marks its armour and hair hide are read from its start-up row and the rows that names; missing rows, a woman and other layouts fail");
    }
}
