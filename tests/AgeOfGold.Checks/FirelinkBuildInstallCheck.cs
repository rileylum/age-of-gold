using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgeOfGold;
using AgeOfGold.Building;
using AgeOfGold.Shaders;
using SoulsFormats;

internal static class FirelinkBuildInstallCheck
{
    internal static void Run(string output)
    {
        if (Environment.GetEnvironmentVariable("CI") is not null) throw new InvalidOperationException("Install checks must never run in CI");
        output = Install.Canonical(output);
        if (!Install.Within(output, Install.Canonical(Path.GetTempPath())) || Directory.Exists(output) || File.Exists(output))
            throw new ArgumentException("Fresh build check output under system temp required");
        string source = Required("AOG_DARK_SOULS_REMASTERED"), host = Required("AOG_ELDEN_RING");
        Oodle.SearchPaths.Add(host);
        var compiler = new ShaderCompiler(Required("AOG_DXC"), Required("AOG_DXC_ASSEMBLER"));
        string first = Path.Combine(output, "first"), repeat = Path.Combine(output, "repeat");
        Directory.CreateDirectory(output);
        string toolchain = Required("AOG_LLVM_MINGW");
        FirelinkBuild.Run(source, host, first, compiler, toolchain);
        FirelinkBuild.Run(source, host, repeat, compiler, toolchain);
        string[] Files(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
        var files = Files(first);
        if (!files.SequenceEqual(Files(repeat))) throw new Exception("Independent builds contain different files");
        foreach (var file in files)
            if (!File.ReadAllBytes(Path.Combine(first, file)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(repeat, file))))
                throw new Exception("Independent builds differ: " + file);

        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(first, "manifest.json")));
        var recorded = manifest.RootElement.GetProperty("files").EnumerateArray()
            .ToDictionary(f => f.GetProperty("path").GetString()!, f => f.GetProperty("sha256").GetString()!);
        if (!recorded.Keys.Order(StringComparer.Ordinal).SequenceEqual(files.Where(f => f != "manifest.json")))
            throw new Exception("Manifest does not list exactly the written files");
        foreach (var (path, hash) in recorded)
            if (Hash(File.ReadAllBytes(Path.Combine(first, path))) != hash) throw new Exception("Manifest fingerprint differs: " + path);

        // The host waits indefinitely on a file named as compressed that is not.
        foreach (var file in files.Where(f => f.EndsWith(".dcx", StringComparison.Ordinal)))
        {
            using var stream = File.OpenRead(Path.Combine(first, file));
            var magic = new byte[4]; stream.ReadExactly(magic);
            if (!magic.AsSpan().SequenceEqual("DCX\0"u8)) throw new Exception("Built file is named compressed but is not: " + file);
        }
        string mod = Path.Combine(first, "mod");
        var layout = MSBE.Read(Path.Combine(mod, "map/mapstudio/m10_01_00_00.msb.dcx"));
        var pieces = layout.Parts.MapPieces.Where(p => p.GameEditionDisable == MSBE.Part.GameEditionDisableType.NeverDisable).ToArray();
        if (pieces.Length == 0 || pieces.Any(p => !p.ModelName.StartsWith("m81", StringComparison.Ordinal))
            || layout.Parts.Collisions.Count(p => p.GameEditionDisable == MSBE.Part.GameEditionDisableType.NeverDisable) != 39
            || layout.Parts.Enemies.Any(p => p.GameEditionDisable == MSBE.Part.GameEditionDisableType.NeverDisable))
            throw new Exception("Built map retains host placements or lacks source terrain and scenery");
        var materials = BND4.Read(Path.Combine(mod, "material/allmaterial.matbinbnd.dcx")).Files
            .ToLookup(f => string.Join('/', f.Name.Replace('\\', '/').Split('/')[^3..]).ToLowerInvariant(), f => f.Bytes);
        var shaders = File.ReadAllBytes(Path.Combine(mod, "shader/shaderbdle.shaderbdlebnd.dcx"));
        int surfaces = 0; var families = new HashSet<string>(StringComparer.Ordinal);
        foreach (var model in pieces.Select(p => p.ModelName).Distinct())
        {
            var binder = BND4.Read(Path.Combine(mod, $"map/m10/m10_01_00_00/m10_01_00_00_{model[1..]}.mapbnd.dcx"));
            var flver = FLVER2.Read(binder.Files.Single(f => f.Name.EndsWith(".flver", StringComparison.Ordinal)).Bytes);
            foreach (var mesh in flver.Meshes)
            {
                var material = flver.Materials[mesh.MaterialIndex];
                // The host resolves "<set>/<name>.mtd" to the definition stored as "<set>/matxml/<name>.matbin".
                var mtd = material.MTD.Replace('\\', '/').Split('/');
                var matches = materials[$"{mtd[^2]}/matxml/{Path.GetFileNameWithoutExtension(mtd[^1])}.matbin".ToLowerInvariant()].ToArray();
                if (matches.Length != 1) throw new Exception("Built model material does not resolve to one definition: " + material.MTD);
                var definition = MATBIN.Read(matches[0]);
                string family = Path.GetFileNameWithoutExtension(definition.ShaderPath.Replace('\\', '/'));
                families.Add(family);
                using var bundle = new BND4Reader(ShaderArchive.ReadFamily(shaders, family + ".shaderbdle"));
                var metadata = METAPARAM.Read(bundle.ReadFile(bundle.Files.Single(f => f.Name.EndsWith(".metaparam", StringComparison.Ordinal))));
                if (!metadata.Textures.Select(t => t.Name).Order(StringComparer.Ordinal).SequenceEqual(material.Textures.Select(t => t.ParamName).Order(StringComparer.Ordinal)))
                    throw new Exception("Built model and shader texture declarations disagree");
                foreach (var texture in material.Textures)
                {
                    var match = Regex.Match(texture.Path, @"^(AET\d{3})_(\d{3})_[a-z]\.tif$");
                    if (!match.Success) throw new Exception("Built model references a texture outside the build: " + texture.Path);
                    string stem = (match.Groups[1].Value + "_" + match.Groups[2].Value).ToLowerInvariant();
                    foreach (string suffix in new[] { "", "_l" })
                        if (!File.Exists(Path.Combine(mod, $"asset/aet/{stem[..6]}/{stem}{suffix}.tpf.dcx")))
                            throw new Exception("Built texture package missing: " + stem + suffix);
                }
                surfaces++;
            }
        }
        if (surfaces != 206) throw new Exception("Built opaque surface inventory differs");
        var lit = manifest.RootElement.GetProperty("groundFamilies").EnumerateObject().Select(f => f.Name).Order(StringComparer.Ordinal).ToArray();
        if (!lit.SequenceEqual(families.Order(StringComparer.Ordinal))) throw new Exception("Built models do not use exactly the recorded lit ground families");
        foreach (string family in families)
        {
            using var bundle = new BND4Reader(ShaderArchive.ReadFamily(shaders, family + ".shaderbdle"));
            foreach (var stage in bundle.Files.Where(f => Regex.IsMatch(f.Name, @"_Gbuf[^\\/]*\.vpo$")))
            {
                var signatures = new ShaderSignatures(compiler.Disassemble(bundle.ReadFile(stage)));
                foreach (int index in new[] { 2, 4, 6 }) signatures.Require(true, "TANGENT", index, 9);
            }
            var pixel = compiler.Disassemble(bundle.ReadFile(bundle.Files.Single(f => f.Name.EndsWith(family + "_Gbuf.ppo", StringComparison.Ordinal))));
            var inputs = new ShaderSignatures(pixel, "ps");
            foreach (int index in new[] { 4, 6 }) inputs.Find(false, "TANGENT", index);
            var declared = METAPARAM.Read(bundle.ReadFile(bundle.Files.Single(f => f.Name.EndsWith(".metaparam", StringComparison.Ordinal))));
            if (!pixel.Contains("TextureCube", StringComparison.Ordinal) || (declared.ShaderId & 0x800) == 0)
                throw new Exception("Built ground pixel stage lacks its environment cube or emission writes");
        }
        if (!File.ReadAllText(Path.Combine(first, "age-of-gold.me3")).Contains("start_online = false", StringComparison.Ordinal))
            throw new Exception("Mod loader profile does not keep the game offline");
        foreach (var (name, root) in new[] { ("source", source), ("host", host) })
            foreach (var input in manifest.RootElement.GetProperty("inputs").GetProperty(name).EnumerateObject())
            {
                var range = Regex.Match(input.Name, @"^(.*)@(\d+)\+(\d+)$");
                byte[] bytes;
                if (range.Success)
                {
                    using var stream = File.OpenRead(Path.Combine(root, range.Groups[1].Value)); stream.Position = long.Parse(range.Groups[2].Value);
                    bytes = new byte[int.Parse(range.Groups[3].Value)]; stream.ReadExactly(bytes);
                }
                else bytes = File.ReadAllBytes(Path.Combine(root, input.Name));
                if (Hash(bytes) != input.Value.GetString()) throw new Exception("Install input changed during the build");
            }
        Directory.Delete(repeat, true);
        Console.WriteLine($"PASS: Firelink build repeats across {files.Length} files; {surfaces} opaque lit surfaces, {pieces.Length} scenery placements and 39 terrain placements read back; installs unchanged");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new ArgumentException("Set " + name);
}
