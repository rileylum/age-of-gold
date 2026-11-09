using HKLib.hk2018;
using HKLib.Serialization.hk2018.Binary;
using SoulsFormats;

namespace AgeOfGold.Collision;

internal static class HostCollisionWriter
{
    internal sealed record Part(string Model, TerrainGeometry.Mesh Geometry);
    internal sealed record Pair(byte[] Header, byte[] Data);

    internal static Pair Write(byte[] header, byte[] data, string detail, IReadOnlyList<Part> parts)
    {
        if (detail is not ("h" or "l") || parts.Count == 0 || parts.Select(p => p.Model).Distinct().Count() != parts.Count
            || parts.Any(p => p.Model.Length != 6 || p.Model.Any(c => c < '0' || c > '9')))
            throw new InvalidDataException("Expected distinct six-digit terrain model identities and high or low detail");
        var binder = BXF4.Read(header, data);
        var reader = new HavokBinarySerializer();
        reader.LoadCompendium(new MemoryStream(DCX.Decompress(
            binder.Files.Single(f => f.Name.EndsWith(".compendium.dcx", StringComparison.Ordinal)).Bytes)));
        var template = binder.Files.Single(f => f.Name.EndsWith("_001400.hkx.dcx", StringComparison.Ordinal));
        var templateRaw = DCX.Decompress(template.Bytes, out var compression);
        var templateRoot = reader.Read(new MemoryStream(templateRaw));
        var physics = (hknpPhysicsSceneData)((hkRootLevelContainer)templateRoot).m_namedVariants.Single().m_variant!;
        var body = physics.m_systemDatas.Single()!.m_bodyCinfos.Single();
        if (body.m_shape is not fsnpCustomParamCompressedMeshShape)
            throw new InvalidDataException("Host terrain template requires a compressed mesh shape");
        foreach (var part in parts)
            if (binder.Files.Any(f => f.Name.EndsWith($"_{part.Model}.hkx.dcx", StringComparison.Ordinal)))
                throw new InvalidDataException("Terrain model identity is already occupied: " + part.Model);

        // Keep the standalone metadata arrangement exercised by the accepted playtest.
        foreach (var file in binder.Files.Where(f => !f.Name.EndsWith(".compendium.dcx", StringComparison.Ordinal)))
        {
            if (!file.Name.EndsWith(".hkx.dcx", StringComparison.Ordinal))
                throw new InvalidDataException("Unclassified collision binder entry: " + file.Name);
            var raw = DCX.Decompress(file.Bytes, out var fileCompression);
            var root = reader.Read(new MemoryStream(raw));
            using var bytes = new MemoryStream();
            new HavokBinarySerializer(reader.TypeRegistry).Write(root, bytes);
            file.Bytes = DCX.Compress(bytes.ToArray(), fileCompression);
        }
        int id = checked(binder.Files.Max(f => f.ID) + 1);
        foreach (var part in parts)
        {
            var root = reader.Read(new MemoryStream(templateRaw));
            Build.ReplaceGeometry(root, part.Geometry.Vertices, part.Geometry.Triangles, detail + part.Model);
            using var bytes = new MemoryStream();
            new HavokBinarySerializer(reader.TypeRegistry).Write(root, bytes);
            string name = template.Name[..^"_001400.hkx.dcx".Length] + $"_{part.Model}.hkx.dcx";
            binder.Files.Add(new BinderFile(template.Flags, id, name, DCX.Compress(bytes.ToArray(), compression)));
            id = checked(id + 1);
        }
        binder.Write(out byte[] writtenHeader, out byte[] writtenData);
        return new(writtenHeader, writtenData);
    }
}
