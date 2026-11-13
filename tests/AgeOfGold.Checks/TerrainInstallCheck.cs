using System.Numerics;
using System.Security.Cryptography;
using AgeOfGold;
using AgeOfGold.Archives;
using AgeOfGold.Collision;
using AgeOfGold.Maps;
using HKLib.hk2018;
using HKLib.Serialization.hk2018.Binary;
using SoulsFormats;

internal static class TerrainInstallCheck
{
    internal static void Run(string output)
    {
        if (Environment.GetEnvironmentVariable("CI") is not null)
            throw new InvalidOperationException("Install checks must never run in CI");
        var source = new Install(Required("AOG_DARK_SOULS_REMASTERED"), "DarkSoulsRemastered.exe");
        var host = new Install(Required("AOG_ELDEN_RING"), "eldenring.exe");
        Oodle.SearchPaths.Add(host.Root);
        var archive = new HostArchive(host);
        var scene = SourceCollisionReader.ReadScene(source);
        var placements = scene.Placements.Where(p => p.Role == "solid" && !p.Disabled).ToArray();
        if (placements.Length != 39 || scene.Placements.Length - placements.Length != 13)
            throw new Exception("Installed Firelink terrain scope differs from the checked playtest");
        var layout = MSBE.Read(archive.Read("map/mapstudio/m10_01_00_00.msb.dcx"));
        var player = layout.Parts.Players.Single(p => p.Name == "c0000_0000");
        var spawn = new Vector3(scene.Spawn.Position[0], scene.Spawn.Position[1], scene.Spawn.Position[2]);
        var offset = player.Position + new Vector3(0,200,0) - spawn;
        var oldPosition = player.Position; var oldRotation = player.Rotation;
        var beforeMap = layout.Write(new DCX.NoCompressionInfo());
        var terrain = FirelinkMapWriter.AddTerrain(layout, scene);
        var mapBytes = layout.Write();
        var repeatedMap = MSBE.Read(beforeMap);
        FirelinkMapWriter.AddTerrain(repeatedMap, scene);
        if (!mapBytes.SequenceEqual(repeatedMap.Write(layout.Compression)) || terrain.Offset != offset
            || terrain.Excluded.Length != 13)
            throw new Exception("Installed map placement or repeat differs");
        var sourceMap = MSB1.Read(source.Read("map/MapStudio/m10_02_00_00.msb"));
        var mapCheck = MSBE.Read(mapBytes);
        for (int i = 0; i < placements.Length; i++)
        {
            var original = sourceMap.Parts.Collisions.Single(p => p.Name == placements[i].Name);
            var added = mapCheck.Parts.Collisions.Single(p => p.ModelName == $"h{800000 + i:D6}");
            if (!placements[i].DisplayGroups.SequenceEqual(original.DispGroups) || added.Unk1.DisplayGroups.Any(v => v != uint.MaxValue))
                throw new Exception("Source collision visibility was misread or terrain is not always visible");
            mapCheck.Parts.Collisions.Remove(added);
            mapCheck.Models.Collisions.RemoveAll(m => m.Name == added.ModelName);
        }
        var movedPlayer = mapCheck.Parts.Players.Single(p => p.Name == "c0000_0000");
        if (movedPlayer.Position != terrain.Spawn || movedPlayer.Rotation != new Vector3(scene.Spawn.Rotation[0],scene.Spawn.Rotation[1],scene.Spawn.Rotation[2]))
            throw new Exception("Source spawn did not survive map readback");
        movedPlayer.Position = oldPosition; movedPlayer.Rotation = oldRotation;
        if (!mapCheck.Write(new DCX.NoCompressionInfo()).SequenceEqual(beforeMap))
            throw new Exception("Unrelated installed map fields changed");
        File.WriteAllBytes(Path.Combine(output, "m10_01_00_00.msb.dcx"), mapBytes);
        foreach (string detail in new[] { "h", "l" })
        {
            var parts = detail == "h" ? terrain.High : terrain.Low;
            int removed = parts.Sum(p => p.Geometry.ZeroAreaFaces);
            if (removed != (detail == "h" ? 87 : 49)) throw new Exception("Degenerate-face accounting differs from independent source inspection");
            float floor = TerrainGeometry.GroundBelow(parts.Select(p => p.Geometry), spawn + offset + new Vector3(0,2,0));
            if (Math.Abs(floor - (spawn.Y + offset.Y)) > 3) throw new Exception("Translated spawn has no nearby support");
            string stem = $"map/m10/m10_01_00_00/{detail}10_01_00_00";
            var header = archive.Read(stem + ".hkxbhd");
            var data = archive.Read(stem + ".hkxbdt");
            var first = HostCollisionWriter.Write(header, data, detail, parts);
            var repeat = HostCollisionWriter.Write(header, data, detail, parts);
            if (!first.Header.SequenceEqual(repeat.Header) || !first.Data.SequenceEqual(repeat.Data))
                throw new Exception("Independent collision assembly changed bytes");
            var written = BXF4.Read(first.Header, first.Data);
            var before = BXF4.Read(header, data);
            if (written.Files.Count != before.Files.Count + parts.Length) throw new Exception("Collision binder entry count differs");
            var reader = new HavokBinarySerializer();
            reader.LoadCompendium(new MemoryStream(DCX.Decompress(before.Files.Single(f => f.Name.EndsWith(".compendium.dcx", StringComparison.Ordinal)).Bytes)));
            var templateRoot = (hkRootLevelContainer)reader.Read(new MemoryStream(DCX.Decompress(
                before.Files.Single(f => f.Name.EndsWith("_001400.hkx.dcx", StringComparison.Ordinal)).Bytes)));
            var templatePhysics = (hknpPhysicsSceneData)templateRoot.m_namedVariants.Single().m_variant!;
            var templateBody = templatePhysics.m_systemDatas.Single()!.m_bodyCinfos.Single();
            foreach (var file in before.Files)
            {
                var after = written.Files.Single(f => f.Name == file.Name);
                if (after.ID != file.ID || after.Flags != file.Flags) throw new Exception("Existing collision identity changed");
                var expected = file.Bytes;
                if (!file.Name.EndsWith(".compendium.dcx", StringComparison.Ordinal))
                {
                    var raw = DCX.Decompress(file.Bytes, out var compression);
                    var value = reader.Read(new MemoryStream(raw));
                    using var bytes = new MemoryStream();
                    new HavokBinarySerializer(reader.TypeRegistry).Write(value, bytes);
                    expected = DCX.Compress(bytes.ToArray(), compression);
                }
                if (!expected.SequenceEqual(after.Bytes)) throw new Exception("Existing collision physics changed");
            }
            foreach (var part in parts)
            {
                var raw = DCX.Decompress(written.Files.Single(f => f.Name.EndsWith($"_{part.Model}.hkx.dcx", StringComparison.Ordinal)).Bytes);
                var root = new HavokBinarySerializer().Read(new MemoryStream(raw));
                GeometryCheck.CheckMesh(root, part.Geometry.Vertices, part.Geometry.Triangles);
                var physics = (hknpPhysicsSceneData)((hkRootLevelContainer)root).m_namedVariants.Single().m_variant!;
                var body = physics.m_systemDatas.Single()!.m_bodyCinfos.Single();
                if (body.m_name != detail + part.Model || body.m_position != templateBody.m_position
                    || body.m_orientation != templateBody.m_orientation)
                    throw new Exception("Placed collision body identity or transform differs");
            }
            File.WriteAllBytes(Path.Combine(output, detail + ".hkxbhd"), first.Header);
            File.WriteAllBytes(Path.Combine(output, detail + ".hkxbdt"), first.Data);
            Console.WriteLine($"PASS: {detail} terrain: {parts.Length} placed meshes, {parts.Sum(p => p.Geometry.Triangles.Count)} triangles, {removed} zero-area faces excluded; exact repeated output and retained host physics");
        }
        foreach (var install in new[] { source, host })
            foreach (var input in install.Fingerprints)
            {
                var range = System.Text.RegularExpressions.Regex.Match(input.Key, @"^(.*)@(\d+)\+(\d+)$");
                byte[] bytes;
                if (range.Success)
                {
                    using var file = File.OpenRead(install.Locate(range.Groups[1].Value));
                    file.Position = long.Parse(range.Groups[2].Value);
                    bytes = new byte[int.Parse(range.Groups[3].Value)];
                    file.ReadExactly(bytes);
                }
                else bytes = File.ReadAllBytes(install.Locate(input.Key));
                if (Convert.ToHexString(SHA256.HashData(bytes)) != input.Value)
                    throw new Exception("Install input changed");
            }
        Console.WriteLine("PASS: supported spawn and unchanged consumed install inputs");
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new ArgumentException($"Set {name}");
}
