using System.Globalization;
using System.Text.RegularExpressions;

namespace AgeOfGold.Shaders;

/// <summary>Rewrites an installed host character's deferred pixel program so the character is lit as the source game lights it.</summary>
internal static class CharacterShader
{
    /// <summary>
    /// Texture registers by role. The program already reads the first three (the source's colour map, its specular map and
    /// the relief map); the diffuse cube, the sky cube and the reflectance table are declared here.
    /// </summary>
    internal sealed record Slots(int Colour, int Specular, int Relief, int Diffuse, int Sky, int Table);

    /// <summary>
    /// Where the material's settings sit: a constant buffer's register, a row each for the plain and metal colours, the
    /// row and column of the number that says which way the specular map is read (nought the newer, one the older), and
    /// those of the share of a shadow the surface takes.
    /// </summary>
    internal sealed record Colours(int Register, int Plain, int Metal, int ReadingRow, int ReadingColumn, int ShareRow, int ShareColumn);

    /// <summary>
    /// The source's light on a character. <paramref name="Diffuse"/> and <paramref name="Sky"/> scale the two cubes;
    /// <paramref name="Upper"/> and <paramref name="Lower"/> are the hemisphere's colours; all in linear light.
    /// <paramref name="Sun"/> is the unit direction to the light that casts the shadows. Shade is whole up to
    /// <paramref name="ShadeStart"/> metres from the camera and gone <paramref name="ShadeLength"/> metres further;
    /// <paramref name="Shade"/>, times the share the material states, is how much a full shadow takes from the factor
    /// the sky's light is multiplied by, squared.
    /// A surface is in its own shadow by how far its normal is from the light: wholly when the cosine between them is
    /// <paramref name="ShadeTurn"/> less one over <paramref name="ShadeEdge"/>, not at all from <paramref name="ShadeTurn"/> up.
    /// <paramref name="Glow"/> multiplies what a surface gives off where its specular map is not open.
    /// <paramref name="Air"/> is the fog and scattering between the surface and the camera, when the place has them.
    /// </summary>
    internal sealed record Light(float[] Diffuse, float[] Sky, int SkyLevels, float[] Upper, float[] Lower, float[] Sun, float ShadeStart, float ShadeLength, float[] Shade,
        float ShadeTurn, float ShadeEdge, float Glow, int TableSize, AgeOfGold.Params.SourceAir.Values? Air);

    /// <summary>Finds the two colours a host character material carries, by the names its program declares them under.</summary>
    internal static Colours FindColours(string module)
    {
        string? buffer = null; var registers = new Dictionary<string, int>(); var plain = new List<(string Buffer, int Offset)>(); var metal = new List<(string Buffer, int Offset)>();
        var reading = new List<(string Buffer, int Offset)>(); var share = new List<(string Buffer, int Offset)>();
        foreach (string line in module.Replace("\r\n", "\n").Split('\n'))
        {
            var opened = Regex.Match(line, @"^; cbuffer (\w+)$");
            if (opened.Success) buffer = opened.Groups[1].Value;
            var member = Regex.Match(line, @"^;\s+float4? (\w+);\s+; Offset:\s*(\d+)$");
            if (member.Success && buffer is not null)
            {
                int offset = int.Parse(member.Groups[2].Value, CultureInfo.InvariantCulture);
                if (member.Groups[1].Value == "FC_DifMapMulCol") plain.Add((buffer, offset));
                // The host declares a material's own colour whole or as four numbers; either way its first number names it.
                if (Regex.IsMatch(member.Groups[1].Value, @"_snp_0_color_0(_0)?$")) metal.Add((buffer, offset));
                // The material's first number of its own.
                if (Regex.IsMatch(member.Groups[1].Value, @"_snp_0_float_0(_0)?$")) reading.Add((buffer, offset));
                // And its second.
                if (Regex.IsMatch(member.Groups[1].Value, @"_snp_0_float_1(_0)?$")) share.Add((buffer, offset));
            }
            var bound = Regex.Match(line, @"^; (\w+)\s+cbuffer\s+\S+\s+\S+\s+CB\d+\s+cb(\d+)\s+1$");
            if (bound.Success) registers[bound.Groups[1].Value] = int.Parse(bound.Groups[2].Value, CultureInfo.InvariantCulture);
        }
        if (plain.Count != 1 || metal.Count != 1 || reading.Count != 1 || share.Count != 1 || plain[0].Buffer != metal[0].Buffer || plain[0].Buffer != reading[0].Buffer || plain[0].Buffer != share[0].Buffer
            || plain[0].Offset % 16 != 0 || metal[0].Offset % 16 != 0 || reading[0].Offset % 4 != 0 || share[0].Offset % 4 != 0 || !registers.TryGetValue(plain[0].Buffer, out int register))
            throw new InvalidDataException("Host character program does not declare one plain colour, one material colour and two material numbers together");
        return new(register, plain[0].Offset / 16, metal[0].Offset / 16, reading[0].Offset / 16, reading[0].Offset % 16 / 4, share[0].Offset / 16, share[0].Offset % 16 / 4);
    }

    private const string Name = @"%[\w.$-]+";
    private sealed record Instruction(int Line, string Text, string[] Operands);

    /// <summary>
    /// The source lights a character by a diffuse cube and a sky reflection, both darkened by shadow, and by a hemisphere
    /// colour and the surface's own glow, which shadow leaves alone; the whole then passes through the place's air. The
    /// host's deferred stage has no shadow to read, so the rewritten program emits the wholly shadowed light, stores as
    /// its surface colour what a surface out of the host's shadow shows above that, and marks the surface with whole metal
    /// and no gloss; the host's lighting stages multiply a marked surface's stored colour by the sun's shadow and nothing
    /// else (<see cref="LightShader"/>).
    /// </summary>
    internal static string Compose(string module, string vertex, Slots slots, PixelShader.Targets targets, Colours colours, Light light)
    {
        if (module.Contains("%aog_", StringComparison.Ordinal)) throw new InvalidDataException("Character program is already composed");
        foreach (var factor in new[] { light.Diffuse, light.Sky, light.Upper, light.Lower, light.Sun, light.Shade })
            if (factor.Length != 3 || !factor.All(float.IsFinite)) throw new InvalidDataException("Character light factors must be three finite values");
        if (MathF.Abs(light.Sun.Sum(v => v * v) - 1) > 1e-4f) throw new InvalidDataException("The direction to the shadows' light must be a unit vector");
        if (light.Shade.Any(v => v is < 0 or > 1)) throw new InvalidDataException("A shadow removes between none and all of the light");
        if (!(light.ShadeLength > 0) || !float.IsFinite(light.ShadeStart) || light.SkyLevels < 3 || light.TableSize < 2) throw new InvalidDataException("Unsupported shade reach, sky levels or table size");
        if (!(light.ShadeEdge > 0) || !float.IsFinite(light.ShadeTurn) || !(light.Glow >= 0)) throw new InvalidDataException("Unsupported shade edge or glow");
        int[] surface = [slots.Colour, slots.Specular, slots.Relief], added = [slots.Diffuse, slots.Sky, slots.Table];
        if (surface.Concat(added).Distinct().Count() != 6) throw new InvalidDataException("A character needs six distinct texture slots");

        var produced = new ShaderSignatures(vertex);
        var signatures = new ShaderSignatures(module, "ps");
        signatures.Link(produced.Find(true, "TANGENT", 8));
        int viewInput = signatures.Find(false, "TANGENT", 8).Id;
        signatures.Use(false, viewInput, light.Air is null ? 7 : 15);
        int Target(int index) => signatures.Find(true, "SV_Target", index).Id;
        int normalTarget = Target(targets.Normal), colourTarget = Target(targets.Colour), emissionTarget = Target(targets.Emission);
        // The host keeps metal and gloss in the target after its normal.
        int glossTarget = Target(targets.Normal + 1);
        int normalInput = signatures.Find(false, "NORMAL", 0).Id, tangentInput = signatures.Find(false, "TANGENT", 0).Id;
        int faceInput = signatures.Find(false, "SV_IsFrontFace", 0).Id;

        var lines = signatures.Lines.ToList();
        int start = lines.FindIndex(l => l == $"define void @{signatures.Entry}() {{");
        int end = lines.FindIndex(start + 1, l => l == "}");
        if (start < 0 || end < 0 || lines.Count(l => l.StartsWith("define ", StringComparison.Ordinal)) != 1) throw new InvalidDataException("Expected one pixel entry function");
        var instructions = new Dictionary<string, Instruction>();
        string Code(int line) => lines[line].Split(';')[0].Trim();
        for (int i = start + 1; i < end; i++)
        {
            var match = Regex.Match(Code(i), $"^({Name}) = (.+)$");
            if (match.Success && !instructions.TryAdd(match.Groups[1].Value, new(i, match.Groups[2].Value, Regex.Matches(match.Groups[2].Value, Name).Select(m => m.Value).ToArray())))
                throw new InvalidDataException("Duplicate pixel instruction result");
        }
        var closures = new Dictionary<string, HashSet<string>>();
        HashSet<string> Closure(string name)
        {
            if (closures.TryGetValue(name, out var known)) return known;
            var result = new HashSet<string> { name };
            if (instructions.TryGetValue(name, out var instruction))
                foreach (var operand in instruction.Operands) result.UnionWith(Closure(operand));
            return closures[name] = result;
        }

        // Resources: the three surface maps the program samples once each, and the list the three added textures join.
        int root = lines.FindIndex(l => Regex.IsMatch(l, @"^!dx\.resources = !\{!\d+\}$"));
        var lists = root < 0 ? Match.Empty : Regex.Match(lines.Single(l => l.StartsWith(Regex.Match(lines[root], @"!\d+").Value + " = ", StringComparison.Ordinal)), @"^!\d+ = !\{(!\d+), ");
        if (!lists.Success) throw new InvalidDataException("Pixel program declares no textures to extend");
        int list = lines.FindIndex(l => l.StartsWith(lists.Groups[1].Value + " = !{", StringComparison.Ordinal));
        var declared = Regex.Matches(lines[list][(lines[list].IndexOf('{') + 1)..], @"!\d+").Select(m =>
            Regex.Match(lines.Single(l => l.StartsWith(m.Value + " = ", StringComparison.Ordinal)), @"^!\d+ = !\{i32 (\d+), %""class\.Texture2D<vector<float, 4> >""\* undef, !""[^""]*"", i32 (\d+), i32 (\d+), i32 1, i32 2, i32 0, (!\d+)\}$")).ToArray();
        if (declared.Any(d => !d.Success)) throw new InvalidDataException("Unsupported texture declarations in the character program");
        int Register(Match d) => int.Parse(d.Groups[3].Value, CultureInfo.InvariantCulture);
        if (added.Any(slot => declared.Any(d => d.Groups[2].Value == "0" && Register(d) == slot))) throw new InvalidDataException("An added texture slot is already declared");
        var samples = new Dictionary<int, (string Result, string Sampler)>();
        foreach (int slot in surface)
        {
            var record = declared.Where(d => d.Groups[2].Value == "0" && Register(d) == slot).ToArray();
            if (record.Length != 1) throw new InvalidDataException("Pixel program does not declare one surface texture at texture slot " + slot);
            var handle = instructions.Where(p => Regex.IsMatch(p.Value.Text, $@"^call %dx\.types\.Handle @dx\.op\.createHandle\(i32 57, i8 0, i32 {record[0].Groups[1].Value}, i32 \d+, i1 false\)$")).ToArray();
            var uses = handle.Length == 1 ? instructions.Where(p => p.Value.Operands.Contains(handle[0].Key)).ToArray() : [];
            var call = uses.Length == 1 ? Regex.Match(uses[0].Value.Text, $@"^call %dx\.types\.ResRet\.f32 @dx\.op\.sample\.f32\(i32 60, %dx\.types\.Handle {Name}, %dx\.types\.Handle ({Name}), ") : Match.Empty;
            if (!call.Success) throw new InvalidDataException("Expected exactly one plain sample of texture slot " + slot);
            samples.Add(slot, (uses[0].Key, call.Groups[1].Value));
        }
        var constants = instructions.Where(p => Regex.IsMatch(p.Value.Text, $@"^call %dx\.types\.Handle @dx\.op\.createHandle\(i32 57, i8 2, i32 \d+, i32 {colours.Register}, i1 false\)$")).ToArray();
        if (constants.Length != 1) throw new InvalidDataException("Pixel program does not open the material's constant buffer");
        int range = declared.Max(d => int.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture)) + 1;
        int node = lines.SelectMany(l => Regex.Matches(l, @"!(\d+)").Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))).Max() + 1;
        var records = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            // Shape five is a cube, two a flat texture.
            string kind = i < 2 ? "TextureCube" : "Texture2D";
            records.Add($"!{node + i} = !{{i32 {range + i}, %\"class.{kind}<vector<float, 4> >\"* undef, !\"\", i32 0, i32 {added[i]}, i32 1, i32 {(i < 2 ? 5 : 2)}, i32 0, {declared[0].Groups[4].Value}}}");
        }
        lines[list] = lines[list][..^1] + string.Concat(Enumerable.Range(0, 3).Select(i => $", !{node + i}")) + "}";
        lines.InsertRange(list + 1, records);

        // The host's normal: the three values it normalises into its normal target, and the surface's own normal behind them.
        var stored = new Regex(@"^call void @dx\.op\.storeOutput\.f32\(i32 5, i32 (?<id>\d+), i32 0, i8 (?<column>[0-3]), float (?<value>\S+)\)$");
        var normalStores = Enumerable.Range(start + 1, end - start - 1).Select(i => stored.Match(Code(i)))
            .Where(m => m.Success && int.Parse(m.Groups["id"].Value, CultureInfo.InvariantCulture) == normalTarget && m.Groups["column"].Value != "3" && instructions.ContainsKey(m.Groups["value"].Value)).ToArray();
        if (normalStores.Length != 3) throw new InvalidDataException("Expected one computed store for each normal target axis");
        var lengths = instructions.Where(p =>
        {
            var reciprocal = Regex.Match(p.Value.Text, $@"^call float @dx\.op\.unary\.f32\(i32 25, float ({Name})\)$");
            if (!reciprocal.Success || !instructions.TryGetValue(reciprocal.Groups[1].Value, out var dot)) return false;
            var self = Regex.Match(dot.Text, $@"^call float @dx\.op\.dot3\.f32\(i32 55, float ({Name}), float ({Name}), float ({Name}), float \1, float \2, float \3\)$");
            return self.Success && normalStores.All(s => Closure(s.Groups["value"].Value).Contains(p.Key));
        }).OrderByDescending(p => p.Value.Line).ToArray();
        if (lengths.Length == 0) throw new InvalidDataException("Normal target is not written from a normalised vector");
        string reciprocalLength = lengths[0].Key;
        var axes = instructions[instructions[reciprocalLength].Operands[0]].Operands.Take(3).ToArray();
        if (axes.Distinct().Count() != 3 || axes.Any(a => !instructions.ContainsKey(a))) throw new InvalidDataException("Unsupported host normal vector");
        bool Sampled(string name) => surface.Any(slot => Closure(name).Contains(samples[slot].Result));
        HashSet<int> Inputs(string name) => Closure(name).Where(n => instructions.TryGetValue(n, out var i) && i.Text.Contains("@dx.op.loadInput.", StringComparison.Ordinal))
            .Select(n => int.Parse(Regex.Match(instructions[n].Text, @"\(i32 4, i32 (\d+),").Groups[1].Value, CultureInfo.InvariantCulture)).ToHashSet();
        var surfaceNormal = new string[3];
        for (int axis = 0; axis < 3; axis++)
        {
            var normals = new HashSet<string>();
            void Visit(string name)
            {
                if (!instructions.TryGetValue(name, out var instruction)) return;
                if (!Sampled(name))
                {
                    var inputs = Inputs(name); inputs.Remove(faceInput);
                    if (inputs.SetEquals([normalInput])) normals.Add(name);
                    return;
                }
                if (!Regex.IsMatch(instruction.Text, @"^(fmul|fadd|fsub)(?: fast)? float |^call float @dx\.op\.tertiary\.f32\(i32 46,")) return;
                foreach (var operand in instruction.Operands) Visit(operand);
            }
            Visit(axes[axis]);
            if (normals.Count != 1) throw new InvalidDataException("Host normal vector lacks one surface normal per axis");
            surfaceNormal[axis] = normals.Single();
        }

        var body = new List<string>();
        string Emit(string label, string expression) { body.Add($"  %aog_{label} = {expression}"); return "%aog_" + label; }
        string Mul(string label, string a, string b) => Emit(label, $"fmul fast float {a}, {b}");
        string Add(string label, string a, string b) => Emit(label, $"fadd fast float {a}, {b}");
        string Sub(string label, string a, string b) => Emit(label, $"fsub fast float {a}, {b}");
        string Dot3(string label, string[] a, string[] b) => Emit(label, $"call float @dx.op.dot3.f32(i32 55, float {a[0]}, float {a[1]}, float {a[2]}, float {b[0]}, float {b[1]}, float {b[2]})");
        string Unary(string label, int code, string value) => Emit(label, $"call float @dx.op.unary.f32(i32 {code}, float {value})");
        string Mad(string label, string a, string b, string c) => Emit(label, $"call float @dx.op.tertiary.f32(i32 46, float {a}, float {b}, float {c})");
        string Min(string label, string a, string b) => Emit(label, $"call float @dx.op.binary.f32(i32 36, float {a}, float {b})");
        string Max(string label, string a, string b) => Emit(label, $"call float @dx.op.binary.f32(i32 35, float {a}, float {b})");
        string Part(string label, string texel, int part) => Emit(label, $"extractvalue %dx.types.ResRet.f32 {texel}, {part}");
        // The source raises its gamma-space products to linear light.
        string Linear(string label, string value) => Unary(label, 21, Mul(label + "scaled", Unary(label + "log", 23, Unary(label + "abs", 6, value)), Float(2.2f)));
        string Handle(string label, int index) => Emit(label, $"call %dx.types.Handle @dx.op.createHandle(i32 57, i8 0, i32 {range + index}, i32 {added[index]}, i1 false)");
        string sampler = samples[slots.Colour].Sampler;
        string Level(string label, string handle, string[] at, string level) => Emit(label, at.Length == 3
            ? $"call %dx.types.ResRet.f32 @dx.op.sampleLevel.f32(i32 62, %dx.types.Handle {handle}, %dx.types.Handle {sampler}, float {at[0]}, float {at[1]}, float {at[2]}, float undef, i32 undef, i32 undef, i32 undef, float {level})"
            : $"call %dx.types.ResRet.f32 @dx.op.sampleLevel.f32(i32 62, %dx.types.Handle {handle}, %dx.types.Handle {sampler}, float {at[0]}, float {at[1]}, float undef, float undef, i32 0, i32 0, i32 undef, float {level})");
        const string channels = "rgb", axisNames = "xyz";

        // Inputs: the unit normal, the unit vector to the camera and its length, and the surface's maps.
        var normal = axisNames.Select((a, i) => Mul("n" + a, axes[i], reciprocalLength)).ToArray();
        var view = axisNames.Select((a, i) => Emit("view" + a, $"call float @dx.op.loadInput.f32(i32 4, i32 {viewInput}, i32 0, i8 {i}, i32 undef)")).ToArray();
        string square = Dot3("viewsquare", view, view), distance = Unary("distance", 24, square), inverse = Unary("viewinverse", 25, square);
        var eye = axisNames.Select((a, i) => Mul("eye" + a, view[i], inverse)).ToArray();
        string colourTexel = samples[slots.Colour].Result, specularTexel = samples[slots.Specular].Result;
        var mapped = Enumerable.Range(0, 4).Select(i => Part("map" + i, specularTexel, i)).ToArray();
        // The source reads the specular map one of two ways, as the definition says. The newer: roughness, metal, a reflecting
        // level, and openness. The older: a reflection colour, with roughness in the fourth channel, on a surface that
        // scatters all of its colour.
        string readingRow = Emit("readingrow", $"call %dx.types.CBufRet.f32 @dx.op.cbufferLoadLegacy.f32(i32 59, %dx.types.Handle {constants[0].Key}, i32 {colours.ReadingRow})");
        string older = Emit("older", $"fcmp fast ogt float {Emit("reading", $"extractvalue %dx.types.CBufRet.f32 {readingRow}, {colours.ReadingColumn}")}, 5.000000e-01");
        string Either(string label, string then, string otherwise) => Emit(label, $"select i1 {older}, float {then}, float {otherwise}");
        string rough = Either("rough", mapped[3], mapped[0]), metal = Either("metal", "0.000000e+00", mapped[1]), level = mapped[2], open = Either("open", "1.000000e+00", mapped[3]);
        string plainRow = Emit("plainrow", $"call %dx.types.CBufRet.f32 @dx.op.cbufferLoadLegacy.f32(i32 59, %dx.types.Handle {constants[0].Key}, i32 {colours.Plain})");
        string metalRow = Emit("metalrow", $"call %dx.types.CBufRet.f32 @dx.op.cbufferLoadLegacy.f32(i32 59, %dx.types.Handle {constants[0].Key}, i32 {colours.Metal})");
        var plain = channels.Select((c, i) => Emit("plain" + c, $"extractvalue %dx.types.CBufRet.f32 {plainRow}, {i}")).ToArray();
        var metallic = channels.Select((c, i) => Emit("metallic" + c, $"extractvalue %dx.types.CBufRet.f32 {metalRow}, {i}")).ToArray();

        // The surface: a colour that is the plain one or the metal one, what of it scatters, and what it reflects head on
        // and at a grazing angle. A plain surface reflects a fifth of its specular level times the metal colour's brightness.
        string brightness = Max("brightness", Dot3("luminance", metallic, [Float(0.2126729f), Float(0.7151522f), Float(0.072175f)]), Float(0.001f));
        string dull = Unary("dull", 7, Mul("dullscaled", Mul("dulllevel", level, Float(0.2f)), brightness));
        string plainShare = Sub("plainshare", "1.000000e+00", metal);
        var scatter = new string[3]; var facing = new string[3]; var basis = new string[3];
        for (int i = 0; i < 3; i++)
        {
            char c = channels[i];
            string tint = Mad("tint" + c, Sub("tintdelta" + c, metallic[i], plain[i]), metal, plain[i]);
            basis[i] = Linear("basis" + c, Mul("raw" + c, Part("colour" + c, colourTexel, i), tint));
            scatter[i] = Mul("scatter" + c, Mul("scatterplain" + c, basis[i], plainShare), open);
            string newer = Unary("facingnewer" + c, 7, Mul("facingopen" + c, Mad("facingmix" + c, Sub("facingdelta" + c, basis[i], dull), metal, dull), open));
            facing[i] = Either("facing" + c, Linear("facingolder" + c, Unary("facingmapped" + c, 7, Mul("facingtinted" + c, mapped[i], metallic[i]))), newer);
        }
        string grazing = Min("grazing", Mul("grazingscaled", Add("grazingsum", Add("grazingpair", facing[0], facing[1]), facing[2]), Float(50 * 0.33f)), "1.000000e+00");

        // The sky is looked up between the mirror direction and the normal, nearer the normal and more blurred the rougher
        // the surface; it fades as the mirror direction sinks below the surface itself.
        string towards = Dot3("towards", normal, eye);
        var mirrored = axisNames.Select((a, i) => Sub("mirror" + a, Mul("mirrortwice" + a, Mul("mirrorscaled" + a, normal[i], towards), "2.000000e+00"), eye[i])).ToArray();
        string sharp = Unary("sharp", 7, Sub("sharpness", "1.000000e+00", rough));
        string lean = Mul("lean", Add("leansum", rough, Unary("sharproot", 24, sharp)), sharp);
        var looked = axisNames.Select((a, i) => Mad("looked" + a, Sub("lookeddelta" + a, mirrored[i], normal[i]), lean, normal[i])).ToArray();
        string horizon = Unary("horizon", 7, Mad("horizonscaled", Dot3("horizondot", surfaceNormal, mirrored), Float(1.3f), "1.000000e+00"));
        string horizonSquare = Mul("horizonsquare", horizon, horizon);
        string blur = Mad("blur", Unary("roughlog", 23, rough), Float(1.2f), Float(light.SkyLevels - 3));
        string diffuseTexel = Level("diffusetexel", Handle("diffusecube", 0), normal, "0.000000e+00");
        string skyTexel = Level("skytexel", Handle("skycube", 1), looked, blur);
        // The table is read at texel centres: its edges must not blend into each other.
        float span = (light.TableSize - 1f) / light.TableSize, inset = 0.5f / light.TableSize;
        string tableTexel = Level("tabletexel", Handle("table", 2), [Mad("tableu", rough, Float(span), Float(inset)), Mad("tablev", Unary("tableangle", 7, towards), Float(span), Float(inset))], "0.000000e+00");
        string scale = Part("tablescale", tableTexel, 0), offset = Part("tableoffset", tableTexel, 1);
        string grazed = Mul("grazed", grazing, offset);
        string hemisphere = Mad("hemisphere", normal[1], "5.000000e-01", "5.000000e-01");
        // Shade is whole near the camera and fades out with distance.
        string reach = Unary("reach", 7, Mul("reachscaled", Sub("reachleft", Float(light.ShadeStart + light.ShadeLength), distance), Float(1 / light.ShadeLength)));

        // A surface turned from the shadows' light is in its own shadow, by its relief-bent normal.
        string turned = Unary("turned", 7, Mul("turnscaled", Sub("turn", Float(light.ShadeTurn), Dot3("turndot", normal, [Float(light.Sun[0]), Float(light.Sun[1]), Float(light.Sun[2])])), Float(light.ShadeEdge)));
        // Of a shadow's density the surface takes the share its material states.
        string shareRow = colours.ShareRow == colours.ReadingRow ? readingRow : Emit("sharerow", $"call %dx.types.CBufRet.f32 @dx.op.cbufferLoadLegacy.f32(i32 59, %dx.types.Handle {constants[0].Key}, i32 {colours.ShareRow})");
        string shared = Mul("shared", reach, Emit("share", $"extractvalue %dx.types.CBufRet.f32 {shareRow}, {colours.ShareColumn}"));
        var shown = new string[3]; var dark = new string[3];
        for (int i = 0; i < 3; i++)
        {
            char c = channels[i];
            string reflected = Mul("reflected" + c, Mul("skyfaded" + c, Mul("sky" + c, Part("skytexel" + c, skyTexel, i), Float(light.Sky[i])), horizonSquare), Mad("reflectance" + c, facing[i], scale, grazed));
            string lit = Mul("lit" + c, Mad("skylit" + c, Mul("diffuse" + c, Part("diffusetexel" + c, diffuseTexel, i), Float(light.Diffuse[i])), scatter[i], reflected), open);
            // Shadow leaves the hemisphere's light alone, and what the surface gives off: ten times its colour where its
            // specular map is not open.
            string steady = Mad("steady" + c, Mad("hemi" + c, hemisphere, Float(light.Upper[i] - light.Lower[i]), Float(light.Lower[i])), scatter[i],
                Mul("glow" + c, Mul("glowing" + c, Sub("closed" + c, "1.000000e+00", open), Float(10 * light.Glow)), basis[i]));
            // The sky's light is multiplied by the square of what the shadow leaves of it.
            // lazy: the square is the source's own setting, read where the build is put together; another power needs a
            // power here.
            string whole = Mul("whole" + c, shared, Float(light.Shade[i]));
            string leftDark = Sub("leftdark" + c, "1.000000e+00", whole), leftShown = Sub("leftshown" + c, "1.000000e+00", Mul("selfshade" + c, whole, turned));
            dark[i] = Mad("dark" + c, lit, Mul("dimdark" + c, leftDark, leftDark), steady);
            shown[i] = Mad("shown" + c, lit, Mul("dimshown" + c, leftShown, leftShown), steady);
        }
        if (light.Air is not null)
        {
            string depth = Emit("viewdepth", $"call float @dx.op.loadInput.f32(i32 4, i32 {viewInput}, i32 0, i8 3, i32 undef)");
            var aired = ShaderAir.Lit(Emit, light.Air, [.. view, depth], ("shown", shown), ("dark", dark));
            shown = aired[0]; dark = aired[1];
        }
        // What the host's shadow removes is stored for it to shade. A stored colour stops at one, which is past the
        // brightest level the display shows.
        var kept = channels.Select((c, i) => Sub("kept" + c, shown[i], dark[i])).ToArray(); var emitted = dark;

        // The stores: every value the program wrote stays except metal and gloss (the mark), colour and emitted light.
        var final = new Dictionary<(int Id, int Column), int>();
        for (int i = start + 1; i < end; i++)
        {
            var store = stored.Match(Code(i));
            if (store.Success) final[(int.Parse(store.Groups["id"].Value, CultureInfo.InvariantCulture), store.Groups["column"].Value[0] - '0')] = i;
        }
        string Store(int id, int column, string value) => $"  call void @dx.op.storeOutput.f32(i32 5, i32 {id}, i32 0, i8 {column}, float {value})";
        var edits = new Dictionary<int, string>();
        for (int i = 0; i < 3; i++)
        {
            edits[final[(colourTarget, i)]] = Store(colourTarget, i, kept[i]);
            edits[final[(emissionTarget, i)]] = Store(emissionTarget, i, emitted[i]);
        }
        edits[final[(glossTarget, 0)]] = Store(glossTarget, 0, "1.000000e+00");
        edits[final[(glossTarget, 1)]] = Store(glossTarget, 1, "0.000000e+00");
        edits[final[(emissionTarget, 3)]] = Store(emissionTarget, 3, Float(targets.EmissionWeight));
        int first = edits.Keys.Min();
        // Everything read here must be worked out on every path to the final stores.
        var labels = Enumerable.Range(start + 1, end - start - 1).Where(i => Regex.IsMatch(lines[i], @"^; <label>:|^[\w.$-]+:")).ToArray();
        int block = labels.LastOrDefault(l => l < first, start);
        if (edits.Keys.Any(k => k < block) || labels.Any(l => l > first)) throw new InvalidDataException("Character stores are not in the program's final block");
        var jumps = new List<(int From, int To)>();
        for (int i = start + 1; i < end; i++)
            foreach (Match target in Regex.Matches(Code(i).StartsWith("br ", StringComparison.Ordinal) ? Code(i) : "", @"label %([\w.$-]+)"))
            {
                int to = Array.FindIndex(labels, l => Regex.IsMatch(lines[l], $@"^; <label>:{Regex.Escape(target.Groups[1].Value)}\b|^{Regex.Escape(target.Groups[1].Value)}:"));
                if (to < 0 || labels[to] <= i) throw new InvalidDataException("Pixel program loops or branches to an unknown block");
                jumps.Add((i, labels[to]));
            }
        foreach (string value in axes.Append(reciprocalLength).Concat(surfaceNormal).Append(colourTexel).Append(specularTexel).Append(constants[0].Key))
        {
            int made = instructions[value].Line, home = labels.LastOrDefault(l => l <= made, start);
            if (made > first || jumps.Any(j => j.From < home && j.To > home)) throw new InvalidDataException("Character program computes a needed value on a conditional path");
        }

        var result = new List<string>();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i == start && !lines.Any(l => l.StartsWith("%\"class.TextureCube<vector<float, 4> >\" = type", StringComparison.Ordinal)))
                result.AddRange(["%\"class.TextureCube<vector<float, 4> >\" = type { <4 x float>, %\"class.TextureCube<vector<float, 4> >::mips_type\" }",
                    "%\"class.TextureCube<vector<float, 4> >::mips_type\" = type { i32 }", ""]);
            if (i == first) result.AddRange(body);
            result.Add(edits.GetValueOrDefault(i, lines[i]));
        }
        foreach (var (function, declaration) in new[]
        {
            ("loadInput.f32", "declare float @dx.op.loadInput.f32(i32, i32, i32, i8, i32)"),
            ("unary.f32", "declare float @dx.op.unary.f32(i32, float)"),
            ("binary.f32", "declare float @dx.op.binary.f32(i32, float, float)"),
            ("tertiary.f32", "declare float @dx.op.tertiary.f32(i32, float, float, float)"),
            ("dot3.f32", "declare float @dx.op.dot3.f32(i32, float, float, float, float, float, float)"),
            ("sampleLevel.f32", "declare %dx.types.ResRet.f32 @dx.op.sampleLevel.f32(i32, %dx.types.Handle, %dx.types.Handle, float, float, float, float, i32, i32, i32, float)"),
        })
            if (!lines.Any(l => l.StartsWith("declare ", StringComparison.Ordinal) && l.Contains($"@dx.op.{function}(", StringComparison.Ordinal)))
                result.Add(declaration);
        return string.Join('\n', result) + "\n";
    }

    // A float constant in the assembler's exact form: the bits of the equal double.
    private static string Float(float value) => "0x" + BitConverter.DoubleToInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture);
}
