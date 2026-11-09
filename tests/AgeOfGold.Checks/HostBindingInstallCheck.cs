using System.Security.Cryptography;
using System.Text.Json;
using AgeOfGold;
using AgeOfGold.Runtime;

internal static class HostBindingInstallCheck
{
    internal static void Run(string output)
    {
        if (Environment.GetEnvironmentVariable("CI") is not null) throw new InvalidOperationException("Install checks must never run in CI");
        output = Install.Canonical(output);
        if (!Install.Within(output, Install.Canonical(Path.GetTempPath())) || Directory.Exists(output) || File.Exists(output))
            throw new ArgumentException("Use a fresh output directory under system temp");
        var source = new Install(Required("AOG_DARK_SOULS_REMASTERED"), "DarkSoulsRemastered.exe");
        var host = new Install(Required("AOG_ELDEN_RING"), "eldenring.exe");
        if (Install.Within(output, source.Root) || Install.Within(output, host.Root)) throw new ArgumentException("Output enters an install");
        var toolchain = Required("AOG_LLVM_MINGW");
        var first = HostBinding.Read(host.Locate("eldenring.exe"), toolchain);
        var second = HostBinding.Read(host.Locate("eldenring.exe"), toolchain);
        HostBindingCheck.Require(first == second && first.Write().SequenceEqual(second.Write()), "Independent runtime bindings differ");
        if (Environment.GetEnvironmentVariable("AOG_RUNTIME_REFERENCE") is { } reference)
        {
            using var oracle = JsonDocument.Parse(File.ReadAllBytes(reference));
            HostBindingCheck.Require(first.WorldSlot == oracle.RootElement.GetProperty("binding").GetProperty("slot").GetUInt32()
                && first.Fingerprint.Equals(oracle.RootElement.GetProperty("hostSha256").GetString(), StringComparison.OrdinalIgnoreCase),
                "Independent private discovery oracle differs");
        }
        foreach (var install in new[] { source, host })
            foreach (var input in install.Fingerprints)
            {
                using var stream = File.OpenRead(install.Locate(input.Key));
                HostBindingCheck.Require(Convert.ToHexString(SHA256.HashData(stream)) == input.Value, "Install input changed");
            }
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, "host-state.bin"), first.Write());
        File.WriteAllText(Path.Combine(output, "check.json"), JsonSerializer.Serialize(new
        {
            repeatIdentical = true, unchangedInputs = true,
            independentOracle = Environment.GetEnvironmentVariable("AOG_RUNTIME_REFERENCE") is not null,
            configurationSha256 = Convert.ToHexString(SHA256.HashData(first.Write()))
        }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine("PASS: install-derived runtime binding, independent repeat, private oracle when supplied, unchanged installs");
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new ArgumentException("Set " + name);
}
