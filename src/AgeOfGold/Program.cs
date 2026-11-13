namespace AgeOfGold;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 7 && args[0] == "build-firelink")
            {
                var build = args.Skip(1).Chunk(2).ToDictionary(pair => pair[0], pair => pair[1]);
                if (!build.Keys.Order().SequenceEqual(new[] { "--host", "--output", "--source" }))
                    throw new ArgumentException("Usage: AgeOfGold build-firelink --source INSTALL --host INSTALL --output FRESH_DIRECTORY");
                string Tool(string name) => Environment.GetEnvironmentVariable(name) ?? throw new ArgumentException("Set " + name + " to the pinned external tool");
                Building.FirelinkBuild.Run(build["--source"], build["--host"], build["--output"],
                    new Shaders.ShaderCompiler(Tool("AOG_DXC"), Tool("AOG_DXC_ASSEMBLER")), Tool("AOG_LLVM_MINGW"));
                return 0;
            }
            if (args.Length is not (7 or 9) || args[0] is not ("prepare" or "prepare-firelink"))
                throw new ArgumentException("Usage: AgeOfGold build-firelink|prepare-firelink --source INSTALL --host INSTALL --output FRESH_DIRECTORY; prepare also requires --request JSON");
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
