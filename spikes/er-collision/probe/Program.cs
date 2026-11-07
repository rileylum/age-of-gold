// Spike: can we open Elden Ring's archives from the player's own install alone?
// Keys are read from the player's executable at run time and never printed or stored.
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

var game = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("AOG_ELDEN_RING")
    ?? throw new ArgumentException("pass the Elden Ring Game directory or set AOG_ELDEN_RING");
var paths = args.Skip(1).ToArray();

if (paths.Length > 0 && paths[0] == "firelink-ambientcheck")
{
    FirelinkAmbientCheck.Reader();
    FirelinkAmbientCheck.Writer();
    return;
}

if (paths.Length > 0 && paths[0] == "firelink-ambient")
{
    if (Environment.GetEnvironmentVariable("CI") is not null)
        throw new InvalidOperationException("Install checks must never run in CI");
    var source = Environment.GetEnvironmentVariable("AOG_DARK_SOULS_REMASTERED")
        ?? throw new ArgumentException("set AOG_DARK_SOULS_REMASTERED");
    var input = Path.GetFullPath(paths[1]); var output = Path.GetFullPath(paths[2]);
    foreach (var protectedPath in new[] { source, game, input })
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(protectedPath));
        if (output.Equals(root, StringComparison.OrdinalIgnoreCase)
            || output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("output must be outside both installs and the input build");
    }
    using var host = File.OpenRead(Path.Combine(game, "eldenring.exe"));
    if (host.ReadByte() < 0) throw new InvalidDataException("empty host executable");
    SoulsFormats.Oodle.SearchPaths.Add(game);
    FirelinkAmbient.Build(source, input, output);
    return;
}

if (paths.Length > 0 && paths[0] == "firelink-lightcheck")
{
    FirelinkLighting.Check();
    return;
}

if (paths.Length > 0 && paths[0] == "firelink-arrivalcheck")
{
    FirelinkArrival.Check();
    return;
}

if (paths.Length > 0 && paths[0] == "firelink-transformcheck")
{
    FirelinkDemo.CheckTransform();
    return;
}

if (paths.Length > 0 && paths[0] == "firelink-layout")
{
    if (Environment.GetEnvironmentVariable("CI") is not null)
        throw new InvalidOperationException("Install checks must never run in CI");
    var source = Environment.GetEnvironmentVariable("AOG_DARK_SOULS_REMASTERED")
        ?? throw new ArgumentException("set AOG_DARK_SOULS_REMASTERED");
    var output = Path.GetFullPath(paths[1]);
    foreach (var install in new[] { source, game })
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(install));
        if (output.Equals(root, StringComparison.OrdinalIgnoreCase)
            || output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("output must be outside both installs");
    }
    using var host = File.OpenRead(Path.Combine(game, "eldenring.exe"));
    if (host.ReadByte() < 0) throw new InvalidDataException("empty host executable");
    FirelinkSource.Export(source, output);
    return;
}

var exe = File.ReadAllBytes(Path.Combine(game, "eldenring.exe"));
var keys = PemKeys(Encoding.Latin1.GetString(exe)).ToList();
Console.WriteLine($"public keys found in executable: {keys.Count}");

var archives = Directory.GetFiles(game, "*.bhd").OrderBy(p => p).ToList();
var index = new Dictionary<ulong, (string Bdt, Entry Entry)>();
foreach (var bhd in archives)
{
    var cipher = File.ReadAllBytes(bhd);
    var (plain, keyNo) = keys.Select((k, i) => (Decrypt(cipher, k), i)).First(t => t.Item1.Length >= 4 && t.Item1.AsSpan(0, 4).SequenceEqual("BHD5"u8));
    var entries = ReadBhd5(plain).ToList();
    Console.WriteLine($"{Path.GetFileName(bhd)}: key #{keyNo}, {entries.Count} files");
    foreach (var e in entries) index[e.Hash] = (Path.ChangeExtension(bhd, ".bdt"), e);
}

byte[] Get(string path) =>
    index.TryGetValue(Hash(path), out var hit) ? ReadFile(hit.Bdt, hit.Entry) : throw new FileNotFoundException(path);

if (paths.Length > 0 && paths[0] == "firelink-demo")
{
    if (Environment.GetEnvironmentVariable("CI") is not null)
        throw new InvalidOperationException("Install checks must never run in CI");
    var source = Environment.GetEnvironmentVariable("AOG_DARK_SOULS_REMASTERED")
        ?? throw new ArgumentException("set AOG_DARK_SOULS_REMASTERED");
    using var sourceHeader = File.OpenRead(Path.Combine(source, "map", "m10_02_00_00", "l10_02_00_00.hkxbhd"));
    if (sourceHeader.ReadByte() < 0) throw new InvalidDataException("empty source collision header");
    var output = Path.GetFullPath(paths[2]);
    foreach (var install in new[] { source, game })
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(install));
        if (output.Equals(root, StringComparison.OrdinalIgnoreCase)
            || output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("output must be outside both installs");
    }
    if (Directory.Exists(output) || File.Exists(output)) throw new ArgumentException("use a fresh output directory");
    SoulsFormats.Oodle.SearchPaths.Add(game);
    FirelinkDemo.Run(paths[1], output, Get);
    return;
}

if (paths.Length > 0 && paths[0] == "geometrycheck")
{
    if (Environment.GetEnvironmentVariable("CI") is not null)
        throw new InvalidOperationException("Install checks must never run in CI");
    var source = Environment.GetEnvironmentVariable("AOG_DARK_SOULS_REMASTERED")
        ?? throw new ArgumentException("set AOG_DARK_SOULS_REMASTERED");
    var checkOutput = Path.GetFullPath(paths[1]);
    foreach (var install in new[] { source, game })
    {
        var installRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(install));
        if (checkOutput.Equals(installRoot, StringComparison.OrdinalIgnoreCase)
            || checkOutput.StartsWith(installRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("geometry check output must be outside both installs");
    }
    using var sourceHeader = File.OpenRead(Path.Combine(source, "map", "m10_02_00_00", "l10_02_00_00.hkxbhd"));
    if (sourceHeader.ReadByte() < 0) throw new InvalidDataException("empty source collision header");
    SoulsFormats.Oodle.SearchPaths.Add(game);
    var binder = BxfIn("m10_01_00_00", "l");
    var reader = Serializer(binder);
    GeometryCheck.CheckTrees(reader.Read(new MemoryStream(SoulsFormats.DCX.Decompress(
        binder.Files.Single(f => f.Name.EndsWith("_001000.hkx.dcx")).Bytes))));
    Console.WriteLine("PASS: tree decoder checked against a host collision part");
    var template = SoulsFormats.DCX.Decompress(binder.Files.Single(f => f.Name.EndsWith("_001400.hkx.dcx")).Bytes);
    GeometryCheck.Run(() => reader.Read(new MemoryStream(template)), checkOutput);
    return;
}

if (paths.Length > 0 && paths[0] == "cagetest")
{
    if (Environment.GetEnvironmentVariable("CI") is not null)
        throw new InvalidOperationException("Install checks must never run in CI");
    var source = Environment.GetEnvironmentVariable("AOG_DARK_SOULS_REMASTERED")
        ?? throw new ArgumentException("set AOG_DARK_SOULS_REMASTERED");
    var outDir = Path.GetFullPath(paths[1]);
    foreach (var install in new[] { source, game })
    {
        var installRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(install));
        if (outDir.Equals(installRoot, StringComparison.OrdinalIgnoreCase)
            || outDir.StartsWith(installRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("cage output must be outside both installs");
    }
    if (Directory.Exists(outDir) || File.Exists(outDir))
        throw new ArgumentException("use a fresh cage output directory");
    using var sourceHeader = File.OpenRead(Path.Combine(source, "map", "m10_02_00_00", "l10_02_00_00.hkxbhd"));
    if (sourceHeader.ReadByte() < 0) throw new InvalidDataException("empty source collision header");
    SoulsFormats.Oodle.SearchPaths.Add(game);
    // The template supplies physics settings and the triangle material; an ordinary part, not a 9xxxxx special one.
    var tmplSpec = (Environment.GetEnvironmentVariable("AOG_TEMPLATE") ?? "m10_01_00_00:l:001400").Split(':');
    var tmplBxf = BxfIn(tmplSpec[0], tmplSpec[1]);
    var tmplSer = Serializer(tmplBxf);
    var template = tmplBxf.Files.First(f => f.Name.EndsWith($"_{tmplSpec[2]}.hkx.dcx")).Bytes;

    var layout = SoulsFormats.MSBE.Read(Get("map/mapstudio/m10_01_00_00.msb.dcx"));
    var s = layout.Parts.Players.Single(p => p.Name == "c0000_0000").Position;
    float y0 = s.Y - 0.1f;
    System.Numerics.Vector3 V(float dx, float y, float dz) => new(s.X + dx, y, s.Z + dz);
    var up = System.Numerics.Vector3.UnitY;
    // A quad as two triangles, wound so they face `facing`.
    IEnumerable<(System.Numerics.Vector3, System.Numerics.Vector3, System.Numerics.Vector3)> Quad(System.Numerics.Vector3 a, System.Numerics.Vector3 b, System.Numerics.Vector3 c, System.Numerics.Vector3 d, System.Numerics.Vector3 facing)
    {
        foreach (var (p, q, r) in new[] { (a, b, c), (a, c, d) })
            yield return System.Numerics.Vector3.Dot(System.Numerics.Vector3.Cross(q - p, r - p), facing) > 0 ? (p, q, r) : (p, r, q);
    }
    float w = float.Parse(Environment.GetEnvironmentVariable("AOG_CAGE") ?? "10", System.Globalization.CultureInfo.InvariantCulture);
    if (!float.IsFinite(w) || w <= 0) throw new ArgumentException("AOG_CAGE must be finite and positive");
    float top = y0 + 10, bottom = y0 - 1;
    var shapes = new[]
    {
        Quad(V(-w, bottom, w), V(w, bottom, w), V(w, top, w), V(-w, top, w), -System.Numerics.Vector3.UnitZ)          // north wall, facing in
            .Concat(Quad(V(-w, bottom, -w), V(w, bottom, -w), V(w, top, -w), V(-w, top, -w), System.Numerics.Vector3.UnitZ)).ToList(),
        Quad(V(w, bottom, -w), V(w, bottom, w), V(w, top, w), V(w, top, -w), -System.Numerics.Vector3.UnitX)           // east and west walls
            .Concat(Quad(V(-w, bottom, -w), V(-w, bottom, w), V(-w, top, w), V(-w, top, -w), System.Numerics.Vector3.UnitX)).ToList(),
        Quad(V(-20, y0, -20), V(20, y0, -20), V(20, y0, 20), V(-20, y0, 20), up)                                     // floor
            .Concat(Quad(V(4, y0, -6), V(8, y0, -6), V(8, y0 + 3, 6), V(4, y0 + 3, 6), up)).ToList(),                    // ramp beside the spawn, rising 3 m over 12 m
    };

    int refinements = int.Parse(Environment.GetEnvironmentVariable("AOG_REFINEMENTS") ?? "0");
    // lazy: five subdivisions fit one shared-vertex page; raise only when the writer supports more pages.
    if (refinements is < 0 or > 5) throw new ArgumentException("AOG_REFINEMENTS must be between 0 and 5");
    for (int pass = 0; pass < refinements; pass++)
        shapes = shapes.Select(shape => shape.SelectMany(t =>
        {
            var (a, b, c) = t;
            var ab = (a + b) / 2; var bc = (b + c) / 2; var ca = (c + a) / 2;
            return new[] { (a, ab, ca), (ab, b, bc), (ca, bc, c), (ab, bc, ca) };
        }).ToList()).ToArray();

    var writer = new HKLib.Serialization.hk2018.Binary.HavokBinarySerializer();
    // HKLib writes standalone files, so every part is rewritten to keep the established test setup.
    var cageSets = (Environment.GetEnvironmentVariable("AOG_SETS") ?? "h,l").Split(',', StringSplitOptions.RemoveEmptyEntries);
    if (cageSets.Length == 0 || cageSets.Any(d => d is not ("h" or "l")))
        throw new ArgumentException("AOG_SETS must name h and/or l");
    bool roundTrip = Environment.GetEnvironmentVariable("AOG_ROUNDTRIP") is not null;
    foreach (var detail in cageSets)
    {
        var bxf = BxfIn("m10_01_00_00", detail);
        var checkedParts = new Dictionary<string, byte[]>();
        int k = 0;
        if (roundTrip)
        {
            var own = Serializer(bxf);
            // AOG_REPLACE lists parts that get our shapes instead; everything else is rewritten unchanged.
            var replace = (Environment.GetEnvironmentVariable("AOG_REPLACE") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            foreach (var id in replace)
            {
                var part = layout.Parts.Collisions.Single(p => p.ModelName == "h" + id);
                if (part.HitFilterID != SoulsFormats.MSBE.Part.Collision.HitFilterType.Standard
                    || part.Position != Vector3.Zero || part.Rotation != Vector3.Zero || part.Scale != Vector3.One)
                    throw new InvalidDataException($"h{id}: expected Standard collision with identity placement");
                if (!bxf.Files.Any(f => f.Name.EndsWith($"_{id}.hkx.dcx")))
                    throw new InvalidDataException($"{detail}{id}: missing collision file");
            }
            foreach (var f in bxf.Files.Where(f => !f.Name.EndsWith(".compendium.dcx")))
            {
                var raw = SoulsFormats.DCX.Decompress(f.Bytes, out var info);
                var ms = new MemoryStream();
                var rid = System.Text.RegularExpressions.Regex.Match(f.Name, @"_(\d{6})\.hkx").Groups[1].Value;
                if (replace.Contains(rid))
                {
                    // One Standard part carries every shape.
                    var tris = shapes.SelectMany(t => t).ToList();
                    var verts = tris.SelectMany(t => new[] { t.Item1, t.Item2, t.Item3 }).ToList();
                    var root = tmplSer.Read(new MemoryStream(SoulsFormats.DCX.Decompress(template)));
                    var indices = Enumerable.Range(0, tris.Count).Select(i => (3 * i, 3 * i + 1, 3 * i + 2)).ToList();
                    Build.ReplaceGeometry(root, verts, indices, detail + rid);
                    writer.Write(root, ms);
                    var written = ms.ToArray();
                    int sections = GeometryCheck.CheckMesh(new HKLib.Serialization.hk2018.Binary.HavokBinarySerializer()
                        .Read(new MemoryStream(written)), verts, indices);
                    checkedParts.Add(f.Name, written);
                    Console.WriteLine($"  {detail}{rid}: our {tris.Count} triangles, {sections} sections checked");
                }
                else writer.Write(own.Read(new MemoryStream(raw)), ms);
                f.Bytes = SoulsFormats.DCX.Compress(ms.ToArray(), info);
                k++;
            }
        }
        else foreach (var f in bxf.Files.Where(f => !f.Name.EndsWith(".compendium.dcx")))
        {
            var tris = shapes[k++ % shapes.Length];
            var verts = tris.SelectMany(t => new[] { t.Item1, t.Item2, t.Item3 }).ToList();
            var idx = Enumerable.Range(0, tris.Count).Select(i => (3 * i, 3 * i + 1, 3 * i + 2)).ToList();
            var root = tmplSer.Read(new MemoryStream(SoulsFormats.DCX.Decompress(template)));
            var id = System.Text.RegularExpressions.Regex.Match(f.Name, @"_(\d{6})\.hkx").Groups[1].Value;
            Build.ReplaceGeometry(root, verts, idx, detail + id);
            var ms = new MemoryStream();
            writer.Write(root, ms);
            new HKLib.Serialization.hk2018.Binary.HavokBinarySerializer().Read(new MemoryStream(ms.ToArray()));
            SoulsFormats.DCX.Decompress(f.Bytes, out var dcx);
            f.Bytes = SoulsFormats.DCX.Compress(ms.ToArray(), dcx);
        }
        var mapDir = Path.Combine(outDir, "mod", "map", "m10", "m10_01_00_00");
        Directory.CreateDirectory(mapDir);
        var headerPath = Path.Combine(mapDir, $"{detail}10_01_00_00.hkxbhd");
        var dataPath = Path.Combine(mapDir, $"{detail}10_01_00_00.hkxbdt");
        bxf.Write(headerPath, dataPath);
        var readback = SoulsFormats.BXF4.Read(headerPath, dataPath);
        foreach (var (name, expected) in checkedParts)
            if (!SoulsFormats.DCX.Decompress(readback.Files.Single(f => f.Name == name).Bytes).AsSpan().SequenceEqual(expected))
                throw new InvalidDataException("written binder changed checked collision bytes");
        Console.WriteLine($"{detail}10_01_00_00: {k} parts written, {checkedParts.Count} synthetic parts verified in binder");
    }
    File.WriteAllText(Path.Combine(outDir, "age-of-gold-cagetest.me3"), """
        profileVersion = "v1"
        savefile = "AgeOfGoldSpike.sl2"
        start_online = false

        [[supports]]
        game = "eldenring"

        [[packages]]
        path = 'mod/'
        """);
    return;
}

SoulsFormats.BXF4 BxfIn(string map, string detail)
{
    var dir = $"map/{map[..3]}/{map}";
    return SoulsFormats.BXF4.Read(Get($"{dir}/{detail}{map[1..]}.hkxbhd"), Get($"{dir}/{detail}{map[1..]}.hkxbdt"));
}

static HKLib.Serialization.hk2018.Binary.HavokBinarySerializer Serializer(SoulsFormats.BXF4 bxf)
{
    var s = new HKLib.Serialization.hk2018.Binary.HavokBinarySerializer();
    s.LoadCompendium(new MemoryStream(SoulsFormats.DCX.Decompress(bxf.Files.First(f => f.Name.EndsWith(".compendium.dcx")).Bytes)));
    return s;
}

if (paths.Length > 0 && paths[0] == "msb")
{
    SoulsFormats.Oodle.SearchPaths.Add(game);
    var msb = SoulsFormats.MSBE.Read(Get($"map/mapstudio/{paths[1]}.msb.dcx"));
    Console.WriteLine($"players {msb.Parts.Players.Count}  collisions {msb.Parts.Collisions.Count}  mapPieces {msb.Parts.MapPieces.Count}  assets {msb.Parts.Assets.Count}  enemies {msb.Parts.Enemies.Count}");
    foreach (var pl in msb.Parts.Players) Console.WriteLine($"  player {pl.Name} model {pl.ModelName} at {pl.Position} rot {pl.Rotation}");
    foreach (var c in msb.Parts.Collisions.Where(c => paths.Length < 3 || c.ModelName.Contains(paths[2])))
    {
        Console.WriteLine($"  collision {c.Name} model {c.ModelName} at {c.Position} rot {c.Rotation}");
        if (Environment.GetEnvironmentVariable("AOG_PROPS") is not null)
            foreach (var prop in c.GetType().GetProperties().Where(q => q.PropertyType.IsPrimitive || q.PropertyType.IsEnum))
                Console.Write($" {prop.Name}={prop.GetValue(c)}");
        if (Environment.GetEnvironmentVariable("AOG_PROPS") is not null) Console.WriteLine();
    }
    return;
}

if (paths.Length > 0 && paths[0] == "collision")
{
    SoulsFormats.Oodle.SearchPaths.Add(game);
    var map = paths[1];                       // e.g. m18_00_00_00
    var dir = $"map/{map[..3]}/{map}";
    var outDir = Path.Combine(paths.Length > 2 ? paths[2] : ".", map);
    Directory.CreateDirectory(outDir);
    var det = Environment.GetEnvironmentVariable("AOG_DETAIL") ?? "h";
    var bxf = SoulsFormats.BXF4.Read(Get($"{dir}/{det}{map[1..]}.hkxbhd"), Get($"{dir}/{det}{map[1..]}.hkxbdt"));
    Console.WriteLine($"{det}{map[1..]}: {bxf.Files.Count} files");
    var serializer = new HKLib.Serialization.hk2018.Binary.HavokBinarySerializer();
    var comp = bxf.Files.First(f => f.Name.EndsWith(".compendium.dcx"));
    serializer.LoadCompendium(new MemoryStream(SoulsFormats.DCX.Decompress(comp.Bytes)));
    foreach (var f in bxf.Files.Where(f => f != comp).Take(int.Parse(Environment.GetEnvironmentVariable("AOG_TAKE") ?? "41")))
    {
        var raw = SoulsFormats.DCX.Decompress(f.Bytes, out var dcx);
        var name = Path.GetFileName(f.Name.Replace('\\', '/'));
        File.WriteAllBytes(Path.Combine(outDir, name.Replace(".dcx", "")), raw);
        Console.WriteLine($"  {name}  {dcx}  {f.Bytes.Length} -> {raw.Length} bytes  magic '{System.Text.Encoding.ASCII.GetString(raw, 4, 4)}'");
        try
        {
            var compendium = serializer.ReadAllObjects(new MemoryStream(raw));
            if (Environment.GetEnvironmentVariable("AOG_SUMMARY") is not null) { Summary(compendium.First()); continue; }
            if (Environment.GetEnvironmentVariable("AOG_INSPECT") is { } want && name.Contains(want)) Inspect.Dump(compendium.First(), "    ");
            else foreach (var o in compendium.Take(12)) Console.WriteLine($"    {o.GetType().Name}");
            if (Environment.GetEnvironmentVariable("AOG_DUMP") is { } dump && name.Contains(dump)) new HKLib.Serialization.hk2018.Xml.HavokXmlSerializer().Write(compendium.First(), Path.Combine(outDir, name.Replace(".hkx.dcx", ".xml")));
        }
        catch (Exception ex) { Console.WriteLine($"    HKLib: {ex.GetType().Name}: {ex.Message}"); }
    }
    return;
}

foreach (var p in paths)
{
    if (!index.TryGetValue(Hash(p), out var hit)) { Console.WriteLine($"MISS {p}"); continue; }
    var data = ReadFile(hit.Bdt, hit.Entry);
    var magic = Encoding.ASCII.GetString(data, 0, 4).Replace('\0', '.');
    Console.WriteLine($"HIT  {p}  in {Path.GetFileName(hit.Bdt)}  {hit.Entry.Size} bytes  magic '{magic}'");
}

static void Summary(HKLib.hk2018.IHavokObject root)
{
    dynamic scene = ((HKLib.hk2018.hkRootLevelContainer)root).m_namedVariants[0].m_variant!;
    dynamic body = scene.m_systemDatas[0].m_bodyCinfos[0];
    dynamic shape = body.m_shape;
    dynamic tree = shape.m_data.m_meshTree;
    int sections = tree.m_sections.Count;
    int maxSecNodes = 0; foreach (var s in tree.m_sections) maxSecNodes = Math.Max(maxSecNodes, (int)s.m_nodes.Count);
    Console.WriteLine($"    bodies {scene.m_systemDatas[0].m_bodyCinfos.Count}  sections {sections}  prims {tree.m_primitives.Count}  packed {tree.m_packedVertices.Count}  shared {tree.m_sharedVertices.Count}  topNodes {tree.m_nodes.Count}  maxSectionNodes {maxSecNodes}  simd {shape.m_data.m_hasSimdTree}  tris {shape.m_pParam.m_triangleDataArray.Count}");
}

static IEnumerable<RSAParameters> PemKeys(string text)
{
    const string begin = "-----BEGIN RSA PUBLIC KEY-----", end = "-----END RSA PUBLIC KEY-----";
    for (int at = text.IndexOf(begin); at >= 0; at = text.IndexOf(begin, at + 1))
    {
        // Parsed with the managed ASN.1 reader: Wine's platform RSA import is unreliable.
        var body = text[(at + begin.Length)..text.IndexOf(end, at)];
        var der = Convert.FromBase64String(string.Concat(body.Where(c => !char.IsWhiteSpace(c))));
        var seq = new System.Formats.Asn1.AsnReader(der, System.Formats.Asn1.AsnEncodingRules.DER).ReadSequence();
        var n = seq.ReadIntegerBytes().ToArray().SkipWhile(b => b == 0).ToArray();
        var e = seq.ReadIntegerBytes().ToArray();
        yield return new RSAParameters { Modulus = n, Exponent = e };
    }
}

// Textbook RSA with the public key, block by block; each plaintext block is one byte shorter.
static byte[] Decrypt(byte[] cipher, RSAParameters key)
{
    int size = key.Modulus!.Length;
    var n = new BigInteger(key.Modulus, true, true);
    var e = new BigInteger(key.Exponent!, true, true);
    var output = new MemoryStream();
    for (int at = 0; at + size <= cipher.Length; at += size)
    {
        var m = BigInteger.ModPow(new BigInteger(cipher.AsSpan(at, size), true, true), e, n);
        var block = new byte[size - 1];
        var bytes = m.ToByteArray(true, true);
        if (bytes.Length > block.Length) return []; // wrong key
        bytes.CopyTo(block, block.Length - bytes.Length);
        output.Write(block);
    }
    return output.ToArray();
}

static IEnumerable<Entry> ReadBhd5(byte[] b)
{
    int I32(long o) => BitConverter.ToInt32(b, (int)o);
    long I64(long o) => BitConverter.ToInt64(b, (int)o);
    long buckets = I32(0x10), bucketsAt = I32(0x14);
    for (long i = 0; i < buckets; i++)
    {
        long at = bucketsAt + i * 8;
        int count = I32(at);
        long headers = I32(at + 4);
        for (int j = 0; j < count; j++)
        {
            long h = headers + j * 40;
            long keyAt = I64(h + 32);
            byte[]? aes = null; var ranges = new List<(long, long)>();
            if (keyAt != 0)
            {
                aes = b[(int)keyAt..(int)(keyAt + 16)];
                int rc = I32(keyAt + 16);
                for (int r = 0; r < rc; r++) ranges.Add((I64(keyAt + 20 + r * 16), I64(keyAt + 28 + r * 16)));
            }
            yield return new Entry(BitConverter.ToUInt64(b, (int)h), I32(h + 8), I64(h + 16), aes, ranges);
        }
    }
}

static byte[] ReadFile(string bdt, Entry e)
{
    using var f = File.OpenRead(bdt);
    var bytes = new byte[e.Size];
    f.Position = e.Offset;
    f.ReadExactly(bytes);
    if (e.Aes is null) return bytes;
    using var aes = Aes.Create();
    aes.Key = e.Aes;
    foreach (var (start, stop) in e.Ranges.Where(r => r.Item1 >= 0 && r.Item2 > r.Item1))
        aes.DecryptEcb(bytes.AsSpan((int)start, (int)(stop - start)), bytes.AsSpan((int)start), PaddingMode.None);
    return bytes;
}

// Elden Ring names files by a 64-bit polynomial hash of the lower-case path.
static ulong Hash(string path)
{
    ulong h = 0;
    foreach (char c in "/" + path.ToLowerInvariant().Replace('\\', '/').TrimStart('/')) h = h * 0x85 + c;
    return h;
}

record Entry(ulong Hash, int Size, long Offset, byte[]? Aes, List<(long, long)> Ranges);
