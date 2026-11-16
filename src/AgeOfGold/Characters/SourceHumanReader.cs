using System.Buffers.Binary;
using SoulsFormats;

namespace AgeOfGold.Characters;

/// <summary>Reads how the source dresses a human character: the part models of what it wears and holds.</summary>
internal static class SourceHumanReader
{
    /// <param name="Parts">The part models of the body, by file stem: what is worn on head, body, arms and legs, then the head itself, its hair and the front of its face.</param>
    /// <param name="Right">The part model held in the right hand, if any; <paramref name="Left"/> likewise.</param>
    /// <param name="Hidden">The marks what is worn hides, hair included: a mesh of any part whose material's name begins with one of them between hashes is not drawn.</param>
    internal sealed record Human(string[] Parts, string? Right, string? Left, int[] Hidden, Face Face);

    /// <summary>What the source generates a person's face from, as its face row keeps it.</summary>
    /// <param name="Shape">Fifty bytes, one for each way the source's shaping data bends the face front.</param>
    /// <param name="Skin">Fifty bytes, one for each way it recolours the face's skin.</param>
    /// <param name="Hair">The hair's colour, a byte each of red, green and blue; <paramref name="Eyes"/> likewise.</param>
    internal sealed record Face(byte[] Shape, byte[] Skin, byte[] Hair, byte[] Eyes);

    // The source's tables by the row length this reader knows, and where a row keeps what is read. A start-up row lists
    // two right-hand and two left-hand weapons, then what is worn on head, body, arms and legs, and further on its face
    // row and its sex; an equipment row names its part model by number.
    private const int StartSize = 240, RightHand = 16, LeftHand = 24, Worn = 32, FaceRow = 164, Sex = 228, Man = 1;
    private const int ProtectorSize = 232, ProtectorModel = 160, WeaponSize = 272, WeaponModel = 184;
    // An armour row keeps one bit for each of 48 marks it hides, from the seventh bit of this byte on.
    private const int ProtectorFlags = 216, FirstHidden = 6, Marks = 48;
    // Hair is a row of the armour table, of a kind of its own: a man's styles are numbered from this row, a hundred apart.
    private const int ProtectorKind = 208, HairKind = 4, FirstHair = 1000, HairStep = 100;
    // A face row: the bytes that shape, the bytes that colour, the hair's style, a byte this reader has only seen empty,
    // then the colours of hair and eyes.
    private const int FaceSize = 128, Modes = 50, HairStyle = 100, HairColour = 102, EyeColour = 105;
    private static readonly string[] Slots = ["HD", "BD", "AM", "LG"];
    // The head model is the skull and neck; the front of the face is a model every person shares, which the source
    // bends and colours for each by its face row.
    private const string Head = "FC_M_0000";
    internal const string FaceFront = "FG_A_0000";

    // lazy: a man's body only; a woman's part models are not chosen, so a woman is refused.
    internal static Human Read(Install source, int startRow)
    {
        var bundle = BND3.Read(source.Read("param/GameParam/GameParam.parambnd.dcx"));
        Dictionary<int, byte[]> Table(string name, string type, int size)
        {
            var file = bundle.Files.SingleOrDefault(f => f.Name.Replace('\\', '/').EndsWith($"/{name}.param", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("Source install has no table " + name);
            var table = PARAM.Read(file.Bytes);
            if (table.ParamType != type || table.DetectedSize != size || table.BigEndian) throw new InvalidDataException($"Unsupported source table {name}: {table.ParamType}, row size {table.DetectedSize}");
            var definition = new PARAMDEF { ParamType = table.ParamType, DataVersion = table.ParamdefDataVersion };
            definition.Fields.Add(new PARAMDEF.Field(definition, PARAMDEF.DefType.dummy8, "raw") { ArrayLength = size });
            table.ApplyParamdef(definition);
            return table.Rows.GroupBy(r => r.ID).ToDictionary(g => g.Key, g => g.Count() == 1 ? (byte[])g.Single()["raw"].Value : throw new InvalidDataException($"Source table {name} repeats row {g.Key}"));
        }
        if (!Table("CharaInitParam", "CHARACTER_INIT_PARAM", StartSize).TryGetValue(startRow, out var start)) throw new InvalidDataException("Source has no start-up row " + startRow);
        var protectors = Table("EquipParamProtector", "EQUIP_PARAM_PROTECTOR_ST", ProtectorSize); var weapons = Table("EquipParamWeapon", "EQUIP_PARAM_WEAPON_ST", WeaponSize);
        // An item's last two digits count its reinforcement; the row of the unreinforced item names the model.
        static int Model(Dictionary<int, byte[]> rows, int item, int at, string what)
        {
            var row = rows.TryGetValue(item, out var exact) ? exact : rows.TryGetValue(item - item % 100, out var plain) ? plain : throw new InvalidDataException($"Source has no {what} row for {item}");
            int model = BinaryPrimitives.ReadInt16LittleEndian(row.AsSpan(at));
            return model is >= 0 and <= 9999 ? model : throw new InvalidDataException($"Source {what} {item} names no part model");
        }
        if (start[Sex] != Man) throw new InvalidDataException($"Source start-up row {startRow} is not a man's; only a man's part models are chosen");
        var hidden = new SortedSet<int>();
        void Hide(byte[] row)
        {
            for (int mark = 0; mark < Marks; mark++)
                if ((row[ProtectorFlags + (FirstHidden + mark) / 8] >> (FirstHidden + mark) % 8 & 1) != 0) hidden.Add(mark);
        }
        var worn = Slots.Select((slot, i) =>
        {
            int item = BinaryPrimitives.ReadInt32LittleEndian(start.AsSpan(Worn + 4 * i));
            // An empty slot shows the bare body part.
            if (item <= 0) return $"{slot}_M_0000";
            Hide(protectors.TryGetValue(item, out var exact) ? exact : protectors[item - item % 100]);
            return $"{slot}_M_{Model(protectors, item, ProtectorModel, "armour"):D4}";
        }).ToArray();
        int faceRow = BinaryPrimitives.ReadInt32LittleEndian(start.AsSpan(FaceRow));
        if (!Table("FaceGenParam", "FACE_PARAM_ST", FaceSize).TryGetValue(faceRow, out var face)) throw new InvalidDataException($"Source has no face row {faceRow} for start-up row {startRow}");
        int hairItem = FirstHair + HairStep * face[HairStyle];
        if (!protectors.TryGetValue(hairItem, out var hair) || hair[ProtectorKind] != HairKind) throw new InvalidDataException($"Source has no hair of style {face[HairStyle]} for a man");
        Hide(hair);
        string[] parts = [.. worn, Head, $"HR_M_{Model(protectors, hairItem, ProtectorModel, "hair"):D4}", FaceFront];
        string? Held(int at)
        {
            int item = BinaryPrimitives.ReadInt32LittleEndian(start.AsSpan(at));
            return item > 0 ? $"WP_A_{Model(weapons, item, WeaponModel, "weapon"):D4}" : null;
        }
        return new(parts, Held(RightHand), Held(LeftHand), [.. hidden], new(face[..Modes], face[Modes..(2 * Modes)], face[HairColour..(HairColour + 3)], face[EyeColour..(EyeColour + 3)]));
    }
}
