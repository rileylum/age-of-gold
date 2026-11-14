"""Adapt the pinned MIT-licensed HKLib reader to use each file's serialized metadata, and to write files the host resolves."""

from pathlib import Path


def replace_once(code, old, new):
    if code.count(old) != 1:
        raise RuntimeError("Pinned HKLib no longer has the expected metadata reader")
    return code.replace(old, new)


def apply(checkout: Path):
    registry = checkout / "HKLib.Reflection/hk2018/HavokTypeRegistry.cs"
    code = registry.read_text(encoding="utf-8-sig")
    code = replace_once(code, "Instance = LoadDefault();", "Instance = new(Array.Empty<HavokType>());")
    registry.write_text(code, encoding="utf-8", newline="\n")

    serializer = checkout / "HKLib.Serialization/hk2018/Binary/HavokBinarySerializer.cs"
    code = serializer.read_text(encoding="utf-8-sig")
    start = code.index("    private IReadOnlyList<HavokType> GetTypesFromRegistry(")
    end = code.index("    public override void LoadCompendium(Stream stream)", start)
    code = code[:start] + """    private IReadOnlyList<HavokType> BuildFileTypes(IReadOnlyList<HavokTypeBuilder> builders)
    {
        var types = new HavokType[builders.Count];
        for (int i = 1; i < builders.Count; i++) types[i] = builders[i].Build();
        TypeRegistry = new HavokTypeRegistry(types);
        return types;
    }

""" + code[end:]
    code = replace_once(code, "        _compendium = compendium;",
                        "        _compendium = compendium;\n        TypeRegistry = new HavokTypeRegistry(compendium.Types);")
    code = replace_once(code, "        IReadOnlyList<HavokType> types = GetTypesFromRegistry(typeBuilders);\n", "")
    code = replace_once(code, "            ReadTHSH(reader, types);", "            ReadTHSH(reader, typeBuilders);")
    code = replace_once(code, "        ReadTPAD(reader);", "        var types = BuildFileTypes(typeBuilders);\n        ReadTPAD(reader);")
    start = code.index("    private void ReadTHSH(")
    end = code.index("    private void WriteTHSH(", start)
    code = code[:start] + """    private void ReadTHSH(HavokBinaryReader reader, IReadOnlyList<HavokTypeBuilder> builders)
    {
        reader.EnterSection("THSH");
        int count = checked((int)reader.ReadHavokVarUInt());
        var seen = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            int index = checked((int)reader.ReadHavokVarUInt());
            if (index <= 0 || index >= builders.Count || !seen.Add(index))
                throw new InvalidDataException("Invalid or duplicate serialized type hash index");
            builders[index].WithHash(reader.ReadUInt32());
        }
        reader.ExitSection();
    }

""" + code[end:]
    # A type's alignment may carry flag bits above it; objects are laid out by the alignment alone.
    code = replace_once(code, "builder.WithSizeAlignment((int)reader.ReadHavokVarUInt(), (int)reader.ReadHavokVarUInt());",
                        "builder.WithSizeAlignment((int)reader.ReadHavokVarUInt(), (int)(reader.ReadHavokVarUInt() & 0xfff));")
    serializer.write_text(code, encoding="utf-8", newline="\n")

    # An inline array is reached through an item reference like any other; the host resolves it only if it is listed.
    arrays = checkout / "HKLib.Serialization/hk2018/Binary/FormatHandlers/ArrayFormatHandler.cs"
    code = arrays.read_text(encoding="utf-8-sig")
    code = replace_once(code, "        ulong pointer = EnqueueList(writer, type, context, list);\n        writer.WriteUInt32((uint)pointer);",
                        "        ulong pointer = EnqueueList(writer, type, context, list);\n        context.RegisterPatch(type, writer.Position);\n        writer.WriteUInt32((uint)pointer);")
    arrays.write_text(code, encoding="utf-8", newline="\n")


if __name__ == "__main__":
    import sys
    apply(Path(sys.argv[1]))
