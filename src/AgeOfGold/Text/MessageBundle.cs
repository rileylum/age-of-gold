using SoulsFormats;

namespace AgeOfGold.Text;

/// <summary>Reads messages from a source text bundle and adds messages to a host one.</summary>
internal static class MessageBundle
{
    /// <summary>The one text a source bundle gives an identity in the named table.</summary>
    internal static string ReadSource(byte[] bundle, string table, int id)
    {
        // A bundle can hold a table twice (the remaster's patch); a message must mean one thing.
        var texts = BND3.Read(bundle).Files.Where(f => Leaf(f.Name).Equals(table, StringComparison.OrdinalIgnoreCase))
            .Select(f => FMG.Read(f.Bytes).Entries.Where(e => e.ID == id && e.Text is not null).Select(e => e.Text).FirstOrDefault())
            .Where(t => t is not null).Distinct(StringComparer.Ordinal).ToArray();
        return texts.Length == 1 ? texts[0]! : throw new InvalidDataException($"Source message {id} in {table} is missing or ambiguous");
    }

    /// <summary>Adds messages to a host bundle's tables; an identity already in use stops the build.</summary>
    internal static byte[] Add(byte[] bundle, IReadOnlyList<(string Table, int Id, string Text)> messages)
    {
        var binder = BND4.Read(bundle);
        foreach (var group in messages.GroupBy(m => m.Table, StringComparer.OrdinalIgnoreCase))
        {
            var files = binder.Files.Where(f => Leaf(f.Name).Equals(group.Key, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (files.Length != 1) throw new InvalidDataException("Host message bundle does not hold one table named " + group.Key);
            var table = FMG.Read(files[0].Bytes);
            foreach (var (_, id, text) in group)
            {
                if (id <= 0 || string.IsNullOrEmpty(text) || table.Entries.Any(e => e.ID == id)) throw new InvalidDataException($"Message identity {id} is invalid or occupied in {group.Key}");
                table.Entries.Add(new(id, text));
            }
            files[0].Bytes = table.Write();
        }
        return binder.Write();
    }

    private static string Leaf(string name) => name.Replace('\\', '/').Split('/')[^1];
}
