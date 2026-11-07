using SoulsFormats;

static class FirelinkEffects
{
    /// <summary>Suppress explicitly selected host effects without removing their loadable definitions.</summary>
    public static byte[] Suppress(byte[] input, IReadOnlySet<int> selected)
    {
        var binder = BND4.Read(input);
        var originals = new Dictionary<int, byte[]>();
        foreach (var file in binder.Files.Where(f => f.Name.EndsWith(".fxr", StringComparison.OrdinalIgnoreCase)))
        {
            var effect = FXR3.Read(file.Bytes);
            if (!selected.Contains(effect.Id)) continue;
            originals.Add(effect.Id, file.Bytes);
            effect.RootContainer.Actions.Clear();
            effect.RootContainer.Effects.Clear();
            effect.RootContainer.Containers.Clear();
            file.Bytes = effect.Write();
            var actual = FXR3.Read(file.Bytes);
            if (actual.RootContainer.Actions.Count != 0 || actual.RootContainer.Effects.Count != 0
                || actual.RootContainer.Containers.Count != 0)
                throw new InvalidDataException("Selected effect emission did not round trip");
            actual.RootContainer = FXR3.Read(originals[effect.Id]).RootContainer;
            if (!actual.Write().AsSpan().SequenceEqual(FXR3.Read(originals[effect.Id]).Write()))
                throw new InvalidDataException("Suppression changed effect identity or state");
        }
        if (!selected.SetEquals(originals.Keys))
            throw new InvalidDataException("Selected host effects are missing: "
                + string.Join(", ", selected.Except(originals.Keys).Order()));
        var written = binder.Write();
        var read = BND4.Read(written);
        for (int i = 0; i < binder.Files.Count; i++)
            if (!read.Files[i].Bytes.AsSpan().SequenceEqual(binder.Files[i].Bytes))
                throw new InvalidDataException("Effect bundle member did not round trip");
        return written;
    }

    /// <summary>Check selective suppression through an authored serialized effect bundle.</summary>
    public static void Check()
    {
        var effect = new FXR3 { Id = 41, Version = FXR3.FXRVersion.Sekiro };
        effect.ReferenceList.Add(43);
        effect.ExternalValueList.Add(7);
        effect.RootContainer.Id = 3;
        effect.RootContainer.Containers.Add(new() { Id = 5 });
        effect.RootContainer.Effects.Add(new() { Id = 9 });
        var retained = new FXR3(effect) { Id = 43 };
        var binder = new BND4();
        binder.Files.Add(new(Binder.FileFlags.Flag1, 0, "our-selected.fxr", effect.Write()));
        binder.Files.Add(new(Binder.FileFlags.Flag1, 1, "our-retained.fxr", retained.Write()));
        binder.Files.Add(new(Binder.FileFlags.Flag1, 2, "our-texture.bin", [9, 4, 2]));
        var input = binder.Write(new DCX.NoCompressionInfo());
        var output = Suppress(input, new HashSet<int> { 41 });
        var actual = BND4.Read(output);
        var suppressed = FXR3.Read(actual.Files[0].Bytes);
        if (suppressed.RootContainer.Actions.Count != 0 || suppressed.RootContainer.Effects.Count != 0
            || suppressed.RootContainer.Containers.Count != 0)
            throw new InvalidDataException("Selected host effect must not emit");
        suppressed.RootContainer = effect.RootContainer;
        if (!suppressed.Write().AsSpan().SequenceEqual(effect.Write()))
            throw new InvalidDataException("Suppression changed effect identity or state");
        actual.Files[0].Bytes = effect.Write();
        if (!actual.Write(new DCX.NoCompressionInfo()).AsSpan().SequenceEqual(input))
            throw new InvalidDataException("Suppression changed unrelated bundle bytes");
        if (!Suppress(input, new HashSet<int> { 41 }).AsSpan().SequenceEqual(output))
            throw new InvalidDataException("Effect suppression must be deterministic");
        bool rejected = false;
        try { Suppress(input, new HashSet<int> { 47 }); }
        catch (InvalidDataException) { rejected = true; }
        if (!rejected) throw new InvalidDataException("Missing selected effect must reject the build");
        Console.WriteLine("PASS: selected effect suppressed; identity, state and other bundle members preserved");
    }
}
