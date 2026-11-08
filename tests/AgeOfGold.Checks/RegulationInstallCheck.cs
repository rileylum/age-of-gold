using System.Security.Cryptography;
using AgeOfGold;
using AgeOfGold.Archives;

internal static class RegulationInstallCheck
{
    internal static void Run(string output)
    {
        if (Environment.GetEnvironmentVariable("CI") is not null)
            throw new InvalidOperationException("Install checks must never run in CI");
        _ = new Install(Required("AOG_DARK_SOULS_REMASTERED"), "DarkSoulsRemastered.exe");
        var host = new Install(Required("AOG_ELDEN_RING"), "eldenring.exe");
        var executable = host.Read("eldenring.exe");
        var encrypted = host.Read("regulation.bin");
        var regulation = Regulation.Read(executable, encrypted);
        var first = regulation.Write();
        var independent = Regulation.Read(executable, encrypted).Write();
        if (!first.SequenceEqual(independent)) throw new Exception("Independent regulation rebuilds differ");
        var path = Path.Combine(output, "regulation.bin");
        File.WriteAllBytes(path, first);
        var checkedRegulation = Regulation.Read(executable, File.ReadAllBytes(path));
        if (!checkedRegulation.Tables.Write().SequenceEqual(regulation.Tables.Write())
            || !checkedRegulation.Write().SequenceEqual(first))
            throw new Exception("Regulation contents changed during readback");
        foreach (var input in host.Fingerprints)
        {
            using var stream = File.OpenRead(host.Locate(input.Key));
            if (Convert.ToHexString(SHA256.HashData(stream)) != input.Value) throw new Exception("Host install input changed");
        }
        Console.WriteLine($"PASS: install-derived regulation key opens {regulation.Tables.Files.Count} tables; independent rebuilds and readback match; inputs unchanged");
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new ArgumentException($"Set {name}");
}
