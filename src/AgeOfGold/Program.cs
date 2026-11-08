namespace AgeOfGold;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length is not (7 or 9) || args[0] is not ("prepare" or "prepare-firelink"))
                throw new ArgumentException("Usage: AgeOfGold prepare-firelink --source INSTALL --host INSTALL --output FRESH_DIRECTORY; prepare also requires --request JSON");
            var options = args.Skip(1).Chunk(2).ToDictionary(pair => pair[0], pair => pair[1]);
            var expected = args[0] == "prepare" ? new[] { "--host", "--output", "--request", "--source" } : new[] { "--host", "--output", "--source" };
            if (!options.Keys.Order().SequenceEqual(expected))
                throw new ArgumentException("Expected each named option exactly once: " + string.Join(", ", expected));
            Preparation.Run(options["--source"], options["--host"], options.GetValueOrDefault("--request"), options["--output"]);
            return 0;
        }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidDataException or System.Text.Json.JsonException
            or System.Security.Cryptography.CryptographicException or OverflowException)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}
