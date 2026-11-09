using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgeOfGold;
using AgeOfGold.Archives;
using AgeOfGold.Scene;
using AgeOfGold.Shaders;
using SoulsFormats;

internal static class ShaderMetadataInstallCheck
{
    internal static void Run(string output)
    {
        if (Environment.GetEnvironmentVariable("CI") is not null) throw new InvalidOperationException("Install checks must never run in CI");
        var source = new Install(Required("AOG_DARK_SOULS_REMASTERED"), "DarkSoulsRemastered.exe");
        var host = new Install(Required("AOG_ELDEN_RING"), "eldenring.exe");
        output = Install.Canonical(output);
        if (!Install.Within(output, Install.Canonical(Path.GetTempPath())) || Directory.Exists(output) || File.Exists(output)
            || new[] { source.Root, host.Root }.Any(r => Install.Within(output, r) || Install.Within(r, output)))
            throw new ArgumentException("Fresh metadata output under system temp required");
        Oodle.SearchPaths.Add(host.Root);
        var archive = new HostArchive(host);
        var shaders = archive.Read("shader/shaderbdle.shaderbdlebnd.dcx");
        const string family = "M[AMSN_V][Mb2]", extra = "M[AMSN][Mb2]";
        byte[] Metadata(string name)
        {
            using var reader = new BND4Reader(ShaderArchive.ReadFamily(shaders, name + ".shaderbdle"));
            return reader.ReadFile(reader.Files.Single(f => f.Name.EndsWith(".metaparam", StringComparison.Ordinal)));
        }
        var template = Metadata(family); var donor = Metadata(extra);
        var first = ShaderMetadata.AppendTextures(template, donor, 5, 6);
        if (!first.SequenceEqual(ShaderMetadata.AppendTextures(template, donor, 6, 5))) throw new Exception("Installed metadata rebuild differs");
        var before = METAPARAM.Read(template); var after = METAPARAM.Read(first); var donorRead = METAPARAM.Read(donor);
        var additions = donorRead.Textures.Where(t => t.Slot is 5 or 6).OrderBy(t => t.Slot).ToArray();
        if (before.Textures.Count != 5 || after.Textures.Count != 7 || additions.Length != 2
            || additions.Any(t => t.Type != METAPARAM.TextureType.Metallic)
            || JsonSerializer.Serialize(after.Textures.Skip(5)) != JsonSerializer.Serialize(additions))
            throw new Exception("Installed metadata lacks exact donor texture declarations");
        CheckPreserved(before, METAPARAM.Read(first));
        using var scene = JsonDocument.Parse(SceneReader.Read(source));
        var materials = scene.RootElement.GetProperty("models").EnumerateArray().SelectMany(m => m.GetProperty("surfaces").EnumerateArray())
            .Select(s => s.GetProperty("material")).Where(m => m.GetProperty("definition").GetString() == "M[DB][M]").ToArray();
        if (materials.Length != 85 || materials.Any(m => m.GetProperty("textures").EnumerateArray()
            .Count(t => t.GetProperty("input").GetString() is "g_Specular" or "g_Specular_2" && t.GetProperty("texture").GetString() != "") != 2))
            throw new Exception("Source packed material input inventory differs");
        var definitions = BND4.Read(archive.Read("material/allmaterial.matbinbnd.dcx"));
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, "template.metaparam"), template);
        File.WriteAllBytes(Path.Combine(output, "donor.metaparam"), donor);
        File.WriteAllBytes(Path.Combine(output, "rebuilt.metaparam"), first);
        foreach (var name in new[] { family, extra })
        {
            var file = definitions.Files.Single(f => f.Name.Replace('\\', '/').EndsWith("/Map_Base/matxml/" + name + ".matbin", StringComparison.Ordinal));
            var material = MATBIN.Read(file.Bytes);
            var metadata = METAPARAM.Read(name == family ? template : donor);
            if (!material.Samplers.Select(s => s.Type).Order(StringComparer.Ordinal)
                .SequenceEqual(metadata.Textures.Select(t => t.Name).Order(StringComparer.Ordinal)))
                throw new Exception("Installed material and shader texture declarations disagree");
            File.WriteAllBytes(Path.Combine(output, name == family ? "template.matbin" : "donor.matbin"), file.Bytes);
        }
        foreach (var install in new[] { source, host })
            foreach (var input in install.Fingerprints)
            {
                var range = Regex.Match(input.Key, @"^(.*)@(\d+)\+(\d+)$");
                byte[] bytes;
                if (range.Success)
                {
                    using var stream = File.OpenRead(install.Locate(range.Groups[1].Value)); stream.Position = long.Parse(range.Groups[2].Value);
                    bytes = new byte[int.Parse(range.Groups[3].Value)]; stream.ReadExactly(bytes);
                }
                else bytes = File.ReadAllBytes(install.Locate(input.Key));
                if (Convert.ToHexString(SHA256.HashData(bytes)) != input.Value) throw new Exception("Installed material input changed");
            }
        File.WriteAllText(Path.Combine(output, "check.json"), JsonSerializer.Serialize(new { surfaces = materials.Length,
            before = before.Textures.Count, after = after.Textures.Count, slots = additions.Select(t => new { t.Slot, t.Name, t.Type }),
            output = Convert.ToHexString(SHA256.HashData(first)), source = source.Fingerprints, host = host.Fingerprints }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: installed shader metadata extended with two packed slots, preserved parameters/stages, repeated bytes and unchanged installs");
    }

    private static void CheckPreserved(METAPARAM before, METAPARAM after)
    {
        // Compare every field exposed by the independent reader after undoing only the requested additions.
        after.Textures = after.Textures.Take(before.Textures.Count).ToList();
        for (int i = 0; i < before.RenderPasses.Entries.Count; i++)
        {
            int mask = before.RenderPasses.Entries[i].SamplerMask;
            if (after.RenderPasses.Entries[i].SamplerMask != (mask == 0 ? 0 : mask | (1 << 5) | (1 << 6)))
                throw new Exception("Shader pass texture mask differs");
            after.RenderPasses.Entries[i].SamplerMask = mask;
        }
        if (JsonSerializer.Serialize(before) != JsonSerializer.Serialize(after)) throw new Exception("Unrelated shader metadata changed");
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new ArgumentException("Set " + name);
}
