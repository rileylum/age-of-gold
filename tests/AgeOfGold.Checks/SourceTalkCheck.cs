using System.Buffers.Binary;
using AgeOfGold;
using AgeOfGold.Talk;
using SoulsFormats;

internal static class SourceTalkCheck
{
    internal static void Run(string root)
    {
        string source = Path.Combine(root, "authored-talk");
        foreach (string folder in new[] { "param/GameParam", "script/talk", "msg/ENGLISH" }) Directory.CreateDirectory(Path.Combine(source, folder));
        File.WriteAllText(Path.Combine(source, "DarkSoulsRemastered.exe"), "authored executable");
        // Talk rows: a character's lines share their leading digits; each row begins with the identity of its text.
        var definition = new PARAMDEF { ParamType = "TALK_PARAM_ST" };
        definition.Fields.Add(new PARAMDEF.Field(definition, PARAMDEF.DefType.dummy8, "raw") { ArrayLength = 32 });
        var table = new PARAM { ParamType = "TALK_PARAM_ST", Rows = [] };
        table.ApplyParamdef(definition);
        foreach (var (id, line) in new[] { (10010200, 5), (37000000, 700), (37000010, 710), (37000020, 720), (38000000, 800) })
        {
            var row = new PARAM.Row(id, "", definition); var raw = new byte[32];
            BinaryPrimitives.WriteInt32LittleEndian(raw, line); row["raw"].Value = raw; table.Rows.Add(row);
        }
        var tables = new BND3();
        tables.Files.Add(new(Binder.FileFlags.Flag1, 0, @"N:\authored\param\GameParam\TalkParam.param", table.Write()));
        File.WriteAllBytes(Path.Combine(source, "param/GameParam/GameParam.parambnd.dcx"), tables.Write());
        // A talk script holds the rows it speaks as plain numbers among its other bytes: here a later line first, then the
        // opening one, a line shared with every script, and a number that is no row.
        byte[] Script(params int[] values) => [1, 2, 3, .. values.SelectMany(BitConverter.GetBytes), 9];
        void Scripts(params (int Talk, byte[] Bytes)[] scripts)
        {
            var bundle = new BND3();
            foreach (var (talk, bytes) in scripts) bundle.Files.Add(new(Binder.FileFlags.Flag1, bundle.Files.Count, $@"N:\authored\script\talk\t{talk}.esd", bytes));
            File.WriteAllBytes(Path.Combine(source, "script/talk/m10_02_00_00.talkesdbnd.dcx"), bundle.Write());
        }
        Scripts((102627, Script(37000020, 37000000, 10010200, 55555555, 37000010)), (102628, Script(37000000, 38000000)), (102629, Script(55555555)));
        var words = new FMG(); words.Entries.Add(new(700, "authored opening line")); words.Entries.Add(new(710, "authored second line")); words.Entries.Add(new(720, "authored third line"));
        var text = new BND3();
        text.Files.Add(new(Binder.FileFlags.Flag1, 0, @"N:\authored\msg\Conversation_.fmg", words.Write()));
        File.WriteAllBytes(Path.Combine(source, "msg/ENGLISH/menu.msgbnd.dcx"), text.Write());

        var install = new Install(source, "DarkSoulsRemastered.exe");
        if (SourceTalkReader.OpeningLine(install, "m10_02_00_00", 102627) != "authored opening line") throw new Exception("The opening line is not the lowest of the script's own character's lines");
        // A script that names two characters' lines equally, one that names none, and one that is absent.
        foreach (int bad in new[] { 102628, 102629, 102630 })
        {
            try { SourceTalkReader.OpeningLine(install, "m10_02_00_00", bad); throw new Exception("An unreadable talk script was accepted"); }
            catch (InvalidDataException) { }
        }
        Console.WriteLine("PASS: a source character's opening line is the lowest-numbered line its talk script names for it; unclear scripts fail");
    }
}
