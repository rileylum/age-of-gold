using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var root = Path.Combine(Path.GetTempPath(), "aog-authored-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    if (args.SequenceEqual(new[] { "shaders" }))
    {
        ShaderCheck.Run();
        return;
    }
    if (args.SequenceEqual(new[] { "collision-metadata" }))
    {
        CollisionMetadataCheck.Run();
        return;
    }
    if (args.SequenceEqual(new[] { "collision-installs" }))
    {
        HostCollisionCheck.Run(root);
        return;
    }
    if (args.SequenceEqual(new[] { "source-tagfile" }))
    {
        SourceTagfileCheck.Run();
        return;
    }
    if (args.SequenceEqual(new[] { "source-collision-installs" }))
    {
        SourceCollisionCheck.Run();
        return;
    }
    if (args.SequenceEqual(new[] { "regulation" }))
    {
        RegulationCheck.Run();
        return;
    }
    if (args.SequenceEqual(new[] { "gameplay-tables" }))
    {
        GameplayTablesCheck.Run();
        return;
    }
    if (args.SequenceEqual(new[] { "gameplay-installs" }))
    {
        GameplayInstallCheck.Run(root);
        return;
    }
    if (args.SequenceEqual(new[] { "regulation-installs" }))
    {
        RegulationInstallCheck.Run(root);
        return;
    }
    if (args.Length == 2 && args[0] == "prepared-host-installs")
    {
        PreparedHostCheck.Run(args[1]);
        return;
    }
    var source = Path.Combine(root, "source");
    var host = Path.Combine(root, "host");
    Directory.CreateDirectory(source);
    Directory.CreateDirectory(host);
    File.WriteAllText(Path.Combine(source, "DarkSoulsRemastered.exe"), "authored source executable");
    File.WriteAllText(Path.Combine(source, "scene.bin"), "authored source scene");
    var expected = Encoding.UTF8.GetBytes("authored archive contents: aligned encrypted range and plain tail");
    WriteHost(host, expected);
    var request = Path.Combine(root, "request.json");
    File.WriteAllText(request, """{"source":["scene.bin"],"host":["Scene/Example.bin"]}""");
    var output = Path.Combine(root, "prepared");
    Run(source, host, request, output, true);
    Require(File.ReadAllText(Path.Combine(output, "source/scene.bin")) == "authored source scene", "source bytes differ");
    Require(File.ReadAllBytes(Path.Combine(output, "host/scene/example.bin")).SequenceEqual(expected), "host decryption differs");
    using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "manifest.json")));
    Require(manifest.RootElement.GetProperty("files").GetArrayLength() == 2, "manifest must cover both inputs");
    Run(source, host, request, Path.Combine(root, "repeat"), true);
    Require(File.ReadAllBytes(Path.Combine(output, "manifest.json")).SequenceEqual(
        File.ReadAllBytes(Path.Combine(root, "repeat/manifest.json"))), "repeat manifest differs");
    WriteHost(host, expected, hasContentSize: false);
    var paddedOutput = Path.Combine(root, "padded");
    Run(source, host, request, paddedOutput, true);
    var padded = new byte[96];
    expected.CopyTo(padded, 0);
    Require(File.ReadAllBytes(Path.Combine(paddedOutput, "host/scene/example.bin")).SequenceEqual(padded),
        "absent unpadded length discarded the stored archive data");
    Run(source, host, request, output, false);
    Require(File.ReadAllBytes(Path.Combine(output, "host/scene/example.bin")).SequenceEqual(expected), "rejected rebuild changed output");
    var protectedHash = SHA256.HashData(File.ReadAllBytes(Path.Combine(source, "scene.bin")));
    Run(source, host, request, Path.Combine(source, "forbidden"), false);
    Require(!Directory.Exists(Path.Combine(source, "forbidden")), "output entered source install");
    Run(source, host, request, Path.Combine(host, "forbidden"), false);
    Require(!Directory.Exists(Path.Combine(host, "forbidden")), "output entered host install");
    Run(Path.Combine(root, "missing"), host, request, Path.Combine(root, "missing-output"), false);
    Require(!Directory.Exists(Path.Combine(root, "missing-output")), "missing install left output");
    if (!OperatingSystem.IsWindows())
    {
        var link = Path.Combine(root, "link");
        Directory.CreateSymbolicLink(link, source);
        Run(source, host, request, Path.Combine(link, "forbidden"), false);
        Require(!Directory.Exists(Path.Combine(source, "forbidden")), "symlink output entered install");
        File.CreateSymbolicLink(Path.Combine(source, "outside.bin"), request);
        File.WriteAllText(request, """{"source":["outside.bin"],"host":[]}""");
        Run(source, host, request, Path.Combine(root, "escape-output"), false);
        Require(!Directory.Exists(Path.Combine(root, "escape-output")), "escaped install file was accepted");
    }
    File.WriteAllText(request, """{"source":["../request.json"],"host":[]}""");
    Run(source, host, request, Path.Combine(root, "traversal-output"), false);
    Require(!Directory.Exists(Path.Combine(root, "traversal-output")), "path traversal left output");
    File.WriteAllText(request, """{"source":["scene.bin"],"host":["missing.bin"]}""");
    Run(source, host, request, Path.Combine(root, "failed-output"), false);
    Require(!Directory.Exists(Path.Combine(root, "failed-output")), "failed preparation left a completed directory");
    Require(!Directory.GetDirectories(root, ".aog-preparing-*").Any(), "failed preparation left staging data");
    Require(SHA256.HashData(File.ReadAllBytes(Path.Combine(source, "scene.bin"))).SequenceEqual(protectedHash), "install changed");
    File.WriteAllText(request, """{"source":[],"host":["scene/example.bin"]}""");
    File.Copy(Path.Combine(host, "Data0.bhd"), Path.Combine(host, "Data1.bhd"));
    File.Copy(Path.Combine(host, "Data0.bdt"), Path.Combine(host, "Data1.bdt"));
    Run(source, host, request, Path.Combine(root, "identical"), true);
    var conflict = File.ReadAllBytes(Path.Combine(host, "Data1.bdt"));
    conflict[40] ^= 1;
    File.WriteAllBytes(Path.Combine(host, "Data1.bdt"), conflict);
    Run(source, host, request, Path.Combine(root, "ambiguous"), false);
    Require(!Directory.Exists(Path.Combine(root, "ambiguous")), "conflicting entries were accepted");
    Console.WriteLine("PASS: authored installs prepare through the CLI, decrypt ranges, repeat exactly and refuse overwrite");
    Console.WriteLine("PASS: missing inputs, install writes, symlink escapes and failed preparation leave no output");
    SceneCheck.Run(root);
    TextureCheck.Run(root);
    CollisionMetadataCheck.Run();
    SourceTagfileCheck.Run();
    RegulationCheck.Run();
    GameplayTablesCheck.Run();
}
finally { Directory.Delete(root, true); }

void Run(string source, string host, string request, string output, bool success)
{
    var app = Path.Combine(AppContext.BaseDirectory, "AgeOfGold.dll");
    var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var arg in new[] { app, "prepare", "--source", source, "--host", host, "--request", request, "--output", output })
        start.ArgumentList.Add(arg);
    using var process = Process.Start(start)!;
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    process.WaitForExit();
    Require((process.ExitCode == 0) == success, stdout.Result + stderr.Result);
}

static void Require(bool value, string message)
{
    if (!value) throw new Exception(message);
}

// This complete tiny archive is authored here, including freshly generated test keys.
static void WriteHost(string host, byte[] contents, bool hasContentSize = true)
{
    using var rsa = RSA.Create(1024);
    File.WriteAllText(Path.Combine(host, "eldenring.exe"), "authored executable\n" + rsa.ExportRSAPublicKeyPem());
    var key = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
    var data = new byte[96];
    contents.CopyTo(data, 0);
    using (var aes = Aes.Create())
    {
        aes.Key = key;
        aes.EncryptEcb(data.AsSpan(0, 32), data.AsSpan(0, 32), PaddingMode.None);
    }
    File.WriteAllBytes(Path.Combine(host, "Data0.bdt"), data);
    using var plain = new MemoryStream();
    using (var w = new BinaryWriter(plain, Encoding.ASCII, true))
    {
        w.Write("BHD5"u8); w.Write(new byte[] { 255, 0, 0, 0 });
        w.Write(1); w.Write(127); w.Write(1); w.Write(32);
        w.Write(0); w.Write(0);
        w.Write(1); w.Write(40);
        // Precalculated polynomial hash for /scene/example.bin, independent of the reader.
        w.Write(0x0a5154a0784fe6c3UL); w.Write(data.Length); w.Write(hasContentSize ? contents.Length : 0);
        w.Write(0L); w.Write(0L); w.Write(80L);
        w.Write(key); w.Write(1); w.Write(0L); w.Write(32L);
        w.Write(new byte[127 - plain.Length]);
    }
    var parameters = rsa.ExportParameters(true);
    var block = BigInteger.ModPow(new BigInteger(plain.ToArray(), true, true),
        new BigInteger(parameters.D!, true, true), new BigInteger(parameters.Modulus!, true, true)).ToByteArray(true, true);
    var cipher = new byte[128];
    block.CopyTo(cipher, cipher.Length - block.Length);
    File.WriteAllBytes(Path.Combine(host, "Data0.bhd"), cipher);
}
