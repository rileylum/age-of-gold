using SoulsFormats;

static class FirelinkWorldIsolation
{
    /// <summary>Disable retained host placements and effects while preserving translated content and map services.</summary>
    public static byte[] Apply(byte[] native, byte[] input)
    {
        var original = MSBE.Read(native);
        var map = MSBE.Read(input);
        var targets = map.Parts.GetEntries().ToDictionary(p => p.Name);
        var previous = new Dictionary<string, MSBE.Part.GameEditionDisableType>();
        foreach (var part in original.Parts.GetEntries())
        {
            if (part is MSBE.Part.Player) continue;
            if (part is not (MSBE.Part.MapPiece or MSBE.Part.Asset or MSBE.Part.Collision
                or MSBE.Part.ConnectCollision or MSBE.Part.Enemy or MSBE.Part.DummyEnemy or MSBE.Part.DummyAsset))
                throw new InvalidDataException("Unclassified retained host placement");
            if (!targets.TryGetValue(part.Name, out var target) || target.GetType() != part.GetType()
                || target.ModelName != part.ModelName)
                throw new InvalidDataException("Retained host placement identity changed");
            previous.Add(target.Name, target.GameEditionDisable);
            target.GameEditionDisable = MSBE.Part.GameEditionDisableType.DisableInRelease;
        }
        var effects = map.Regions.SFX.ToDictionary(r => r.Name);
        var previousEffects = new Dictionary<string, int>();
        foreach (var effect in original.Regions.SFX)
        {
            if (!effects.TryGetValue(effect.Name, out var target) || target.EffectID != effect.EffectID)
                throw new InvalidDataException("Retained host effect identity changed");
            previousEffects.Add(target.Name, target.StartDisabled);
            target.StartDisabled = 1;
        }
        var written = map.Write();
        var actual = MSBE.Read(written);
        foreach (var part in actual.Parts.GetEntries().Where(p => previous.ContainsKey(p.Name)))
        {
            if (part.GameEditionDisable != MSBE.Part.GameEditionDisableType.DisableInRelease)
                throw new InvalidDataException("Host placement disable flag did not round trip");
            part.GameEditionDisable = previous[part.Name];
        }
        foreach (var effect in actual.Regions.SFX.Where(r => previousEffects.ContainsKey(r.Name)))
        {
            if (effect.StartDisabled != 1) throw new InvalidDataException("Host effect disable flag did not round trip");
            effect.StartDisabled = previousEffects[effect.Name];
        }
        if (!actual.Write(new DCX.NoCompressionInfo()).AsSpan().SequenceEqual(DCX.Is(input) ? DCX.Decompress(input) : input))
            throw new InvalidDataException("World isolation changed unrelated map bytes");
        return written;
    }

    /// <summary>Check isolation against authored serialized layouts, including translated host-model placements.</summary>
    public static void Check()
    {
        var native = new MSBE();
        native.Parts.MapPieces.Add(new() { Name = "host-wall" });
        native.Parts.Assets.Add(new() { Name = "host-prop" });
        native.Parts.Collisions.Add(new() { Name = "host-ground" });
        native.Parts.ConnectCollisions.Add(new() { Name = "host-connection", CollisionName = "host-ground" });
        native.Parts.Enemies.Add(new() { Name = "host-character" });
        native.Parts.DummyEnemies.Add(new() { Name = "host-dummy-character" });
        native.Parts.DummyAssets.Add(new() { Name = "host-dummy-prop" });
        native.Parts.Players.Add(new() { Name = "player" });
        native.Regions.SFX.Add(new() { Name = "host-effect", EffectID = 17 });
        native.Regions.EnvironmentMapPoints.Add(new() { Name = "environment" });
        var original = native.Write(new DCX.NoCompressionInfo());
        var map = MSBE.Read(original);
        map.Parts.Collisions.Add(new() { Name = "translated-ground" });
        map.Parts.Enemies.Add(new() { Name = "translated-character" });
        map.Regions.SFX.Add(new() { Name = "translated-effect", EffectID = 17 });
        var input = map.Write(new DCX.NoCompressionInfo());
        var actual = MSBE.Read(Apply(original, input));
        foreach (var part in actual.Parts.GetEntries().Where(p => p.Name.StartsWith("host-")))
        {
            if (part.GameEditionDisable != MSBE.Part.GameEditionDisableType.DisableInRelease)
                throw new InvalidDataException("Retained host world placement must be disabled");
            part.GameEditionDisable = MSBE.Part.GameEditionDisableType.NeverDisable;
        }
        var effect = actual.Regions.SFX.Single(r => r.Name == "host-effect");
        if (effect.StartDisabled != 1) throw new InvalidDataException("Retained host effect must start disabled");
        effect.StartDisabled = 0;
        if (!actual.Write(new DCX.NoCompressionInfo()).AsSpan().SequenceEqual(input))
            throw new InvalidDataException("World isolation changed translated content or map services");
        Console.WriteLine("PASS: retained host world placements and effects disabled; translated content and map services preserved");
    }
}
