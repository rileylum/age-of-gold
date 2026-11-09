using System.Security.Cryptography;
using AgeOfGold;
using AgeOfGold.Archives;
using AgeOfGold.Shaders;
using SoulsFormats;

internal static class ShaderArchiveInstallCheck
{
    internal static void Run()
    {
        if (Environment.GetEnvironmentVariable("CI") is not null)
            throw new InvalidOperationException("Install checks must never run in CI");
        var source = new Install(Required("AOG_DARK_SOULS_REMASTERED"), "DarkSoulsRemastered.exe");
        var host = new Install(Required("AOG_ELDEN_RING"), "eldenring.exe");
        Oodle.SearchPaths.Add(host.Root);
        var archive = new HostArchive(host);
        var deferred = source.Read("shader/FRPG_Deferred_DX11.shaderbnd.dcx");
        foreach (var file in BND3.Read(deferred).Files)
        {
            var name = Path.GetFileName(file.Name.Replace('\\', '/'));
            if (!ShaderArchive.Read(deferred, name).SequenceEqual(file.Bytes))
                throw new Exception("Source shader bytes differ");
        }
        var input = archive.Read("shader/shaderbdle.shaderbdlebnd.dcx");
        var family = ShaderArchive.ReadFamily(input, "M[AMSN_V][Mb2].shaderbdle");
        string[] stage = ["M[AMSN_V][Mb2]_Gbuf.ppo"];
        var program = ShaderArchive.Read(family, stage);
        var other = ShaderArchive.Read(family, "M[AMSN_V][Mb2]_Gbuf_[A].ppo");
        var output = ShaderArchive.Replace(family, stage, other);
        if (!ShaderArchive.Read(output, stage).SequenceEqual(other)
            || !ShaderArchive.Replace(family, stage, other).SequenceEqual(output))
            throw new Exception("Installed shader replacement or repeat differs");
        var restored = ShaderArchive.Replace(output, stage, program);
        var before = BND4.Read(family).Write(new DCX.NoCompressionInfo());
        var after = BND4.Read(restored).Write(new DCX.NoCompressionInfo());
        if (!before.SequenceEqual(after)) throw new Exception("Shader replacement changed an unrelated archive entry or metadata");
        foreach (var install in new[] { source, host })
            foreach (var fingerprint in install.Fingerprints)
            {
                var range = System.Text.RegularExpressions.Regex.Match(fingerprint.Key, @"^(.*)@(\d+)\+(\d+)$");
                byte[] bytes;
                if (range.Success)
                {
                    using var file = File.OpenRead(install.Locate(range.Groups[1].Value));
                    file.Position = long.Parse(range.Groups[2].Value);
                    bytes = new byte[int.Parse(range.Groups[3].Value)];
                    file.ReadExactly(bytes);
                }
                else bytes = File.ReadAllBytes(install.Locate(fingerprint.Key));
                if (Convert.ToHexString(SHA256.HashData(bytes)) != fingerprint.Value)
                    throw new Exception("Install input changed");
            }
        Console.WriteLine("PASS: installed source stages and host nested stages read; replacement repeats exactly and preserves other entries");
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new ArgumentException($"Set {name}");
}
