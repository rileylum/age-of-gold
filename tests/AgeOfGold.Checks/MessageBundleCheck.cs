using AgeOfGold.Text;
using SoulsFormats;

internal static class MessageBundleCheck
{
    internal static void Run()
    {
        static byte[] Table(params (int Id, string? Text)[] entries)
        {
            var table = new FMG { Version = FMG.FMGVersion.DarkSouls3, BigEndian = false };
            foreach (var (id, text) in entries) table.Entries.Add(new(id, text));
            return table.Write();
        }
        // A source bundle holding one table twice, as the remaster's patched bundles do.
        var source = new BND3();
        source.Files.Add(new(Binder.FileFlags.Flag1, 1, @"N:\authored\Event_text_.fmg", Table((10, "authored prompt"), (11, null), (12, "first reading"))));
        source.Files.Add(new(Binder.FileFlags.Flag1, 2, @"N:\authored\patch\Event_text_.fmg", Table((10, "authored prompt"), (12, "second reading"), (13, "patched only"))));
        var sourceBytes = source.Write();
        if (MessageBundle.ReadSource(sourceBytes, "Event_text_.fmg", 10) != "authored prompt" || MessageBundle.ReadSource(sourceBytes, "event_text_.fmg", 13) != "patched only")
            throw new Exception("Source messages differ");
        foreach (int id in new[] { 11, 12, 99 }) Reject(() => MessageBundle.ReadSource(sourceBytes, "Event_text_.fmg", id));

        var host = new BND4();
        host.Files.Add(new(Binder.FileFlags.Flag1, 1, @"N:\authored\ActionButtonText.fmg", Table((5, "host prompt"))));
        host.Files.Add(new(Binder.FileFlags.Flag1, 2, @"N:\authored\EventTextForTalk.fmg", Table((7, "host message"))));
        var hostBytes = host.Write();
        (string, int, string)[] added = [("ActionButtonText.fmg", 9100000, "authored prompt"), ("EventTextForTalk.fmg", 9100001, "authored message")];
        var output = MessageBundle.Add(hostBytes, added);
        if (!output.SequenceEqual(MessageBundle.Add(hostBytes, added))) throw new Exception("Message bundle does not repeat");
        var read = BND4.Read(output);
        var prompts = FMG.Read(read.Files[0].Bytes); var talk = FMG.Read(read.Files[1].Bytes);
        if (prompts[9100000] != "authored prompt" || prompts[5] != "host prompt" || talk[9100001] != "authored message" || talk[7] != "host message"
            || prompts.Entries.Count != 2 || talk.Entries.Count != 2) throw new Exception("Host messages differ");
        Reject(() => MessageBundle.Add(output, added));
        Reject(() => MessageBundle.Add(hostBytes, [("Missing.fmg", 1, "text")]));
        Reject(() => MessageBundle.Add(hostBytes, [("ActionButtonText.fmg", 6, "")]));
        Console.WriteLine("PASS: source messages read once from repeated tables; host tables gain messages and reject occupied identities");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Expected message rejection");
    }
}
