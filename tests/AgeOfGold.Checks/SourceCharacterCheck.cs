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
    }
}
