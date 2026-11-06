using System.Numerics;
using SoulsFormats;

static partial class FirelinkDemo
{
    static readonly string[] RenderSampleIds = ["890000", "890001", "890002"];

    static void WriteRenderSamples(byte[] template, MSBE layout, MSBE.Part.Player player, string mapDir)
    {
        var nativeFile = BND4.Read(template).Files.Single(f => f.Name.EndsWith(".flver"));
        var native = FLVER2.Read(nativeFile.Bytes);
        if (native.Nodes.Count != 1 || native.Nodes[0].Translation != Vector3.Zero
            || native.Nodes[0].Rotation != Vector3.Zero || native.Nodes[0].Scale != Vector3.One
            || native.Meshes.Any(m => m.UseBoneWeights || m.BoundingBox is null
                || m.Vertices.Any(v => v.Positions.Count != 1 || v.NormalWs.Any(w => w != 0))))
            throw new InvalidDataException("Render comparison requires static meshes bound to one identity node");
        var mesh = native.Meshes[0];
        if (mesh.FaceSets.Count != 1 || mesh.FaceSets[0].Flags != FLVER2.FaceSet.FSFlags.None)
            throw new InvalidDataException("Render comparison requires one ordinary native face set");
        var indices = mesh.FaceSets[0].Triangulate(mesh.Vertices.Count < 65536);
        var positions = indices.Select(i => mesh.Vertices[i].Position).ToArray();
        var lo = positions.Aggregate(Vector3.Min); var hi = positions.Aggregate(Vector3.Max);
        var extent = hi - lo;
        float size = Math.Max(extent.X, Math.Max(extent.Y, extent.Z));
        if (!float.IsFinite(size) || size <= 0) throw new InvalidDataException("Invalid render sample extent");
        var scale = new Vector3(4 / size); var pivot = (lo + hi) / 2;
        var yaw = Matrix4x4.CreateRotationY(player.Rotation.Y * MathF.PI / 180);
        var partTemplate = layout.Parts.MapPieces.First(p => p.ModelName == "m000000");
        var modelTemplate = layout.Models.MapPieces.Single(m => m.Name == partTemplate.ModelName);
        for (int i = 0; i < RenderSampleIds.Length; i++)
        {
            string id = RenderSampleIds[i];
            if (layout.Models.MapPieces.Any(m => m.Name == "m" + id)
                || layout.Parts.MapPieces.Any(p => p.Name == "m" + id + "_0000"))
                throw new InvalidDataException("Render sample ID is already occupied");
            var target = player.Position + Vector3.Transform(new Vector3((i - 1) * 5, 2, 6), yaw);
            var shift = target - pivot * scale;
            var part = (MSBE.Part.MapPiece)partTemplate.DeepCopy();
            SetPlacement(part, "m" + id); Enable(part.Unk1);
            layout.Parts.MapPieces.Add(part);
            var model = (MSBE.Model.MapPiece)modelTemplate.DeepCopy();
            model.Name = part.ModelName; model.SibPath = "";
            layout.Models.MapPieces.Add(model);
            if (i == 0)
            {
                part.Scale = scale; part.Position = shift;
                Save(nativeFile.Bytes, id);
            }
            else if (i == 1)
            {
                var baked = FLVER2.Read(nativeFile.Bytes);
                foreach (var m in baked.Meshes)
                {
                    foreach (var v in m.Vertices) v.Position = v.Position * scale + shift;
                    // Transform the stored bounds as they are; this control preserves native metadata conventions.
                    m.BoundingBox.Min = m.BoundingBox.Min * scale + shift;
                    m.BoundingBox.Max = m.BoundingBox.Max * scale + shift;
                }
                baked.Header.BoundingBoxMin = baked.Header.BoundingBoxMin * scale + shift;
                baked.Header.BoundingBoxMax = baked.Header.BoundingBoxMax * scale + shift;
                foreach (var node in baked.Nodes)
                {
                    node.BoundingBoxMin = node.BoundingBoxMin * scale + shift;
                    node.BoundingBoxMax = node.BoundingBoxMax * scale + shift;
                }
                Save(baked.Write(), id);
            }
            else
            {
                var triangles = Enumerable.Range(0, indices.Count / 3)
                    .Select(t => (indices[3 * t], indices[3 * t + 2], indices[3 * t + 1])).ToList();
                WriteVisible(new Mesh(mesh.Vertices.Select(v => v.Position * scale + shift).ToList(), triangles, 0), template, id, mapDir);
            }
        }

        void Save(byte[] model, string id)
        {
            var binder = BND4.Read(template);
            var file = binder.Files.Single(f => f.Name.EndsWith(".flver"));
            file.Bytes = model;
            file.Name = file.Name.Replace("m10_01_00_00_000000", "m10_01_00_00_" + id);
            binder.Files = [file];
            binder.Write(Path.Combine(mapDir, $"m10_01_00_00_{id}.mapbnd.dcx"));
        }
    }

    static void CheckRenderSamples(byte[] template, MSBE layout, MSBE.Part.Player player, string mapDir)
    {
        var nativeFile = BND4.Read(template).Files.Single(f => f.Name.EndsWith(".flver"));
        var native = FLVER2.Read(nativeFile.Bytes);
        var models = RenderSampleIds.Select(id => BND4.Read(Path.Combine(mapDir,
            $"m10_01_00_00_{id}.mapbnd.dcx")).Files.Single().Bytes).ToArray();
        if (!models[0].AsSpan().SequenceEqual(nativeFile.Bytes))
            throw new InvalidDataException("Left render sample must retain the native model bytes");
        var placements = RenderSampleIds.Select(id => layout.Parts.MapPieces.Single(p => p.ModelName == "m" + id)).ToArray();
        var samples = models.Select(FLVER2.Read).ToArray();
        var rotation = Matrix4x4.CreateRotationY(player.Rotation.Y * MathF.PI / 180);
        var right = Vector3.TransformNormal(Vector3.UnitX, rotation);
        var forward = Vector3.TransformNormal(Vector3.UnitZ, rotation);
        var centers = new Vector3[3];
        for (int i = 0; i < samples.Length; i++)
        {
            var part = placements[i];
            if (part.Rotation != Vector3.Zero || part.Scale.X <= 0 || part.Scale.Y != part.Scale.X || part.Scale.Z != part.Scale.X
                || (i > 0 && (part.Scale != Vector3.One || part.Position != Vector3.Zero)))
                throw new InvalidDataException("Render sample placement changed");
            var mesh = samples[i].Meshes[0];
            var positions = mesh.FaceSets.Single().Triangulate(mesh.Vertices.Count < 65536)
                .Select(v => mesh.Vertices[v].Position * part.Scale + part.Position).ToArray();
            var lo = positions.Aggregate(Vector3.Min); var hi = positions.Aggregate(Vector3.Max);
            var extent = hi - lo;
            if (Math.Abs(Math.Max(extent.X, Math.Max(extent.Y, extent.Z)) - 4) > 0.001f)
                throw new InvalidDataException("Render sample must span four units along its longest axis");
            centers[i] = (lo + hi) / 2;
            var delta = centers[i] - player.Position;
            if (Math.Abs(Vector3.Dot(delta, right) - (i - 1) * 5) > 0.001f
                || Math.Abs(Vector3.Dot(delta, forward) - 6) > 0.001f || Math.Abs(delta.Y - 2) > 0.001f)
                throw new InvalidDataException("Render samples must be left, centre and right in front of the spawn");
        }

        var placed = placements[0];
        var baked = samples[1];
        if (baked.Meshes.Count != native.Meshes.Count || baked.Nodes.Count != native.Nodes.Count)
            throw new InvalidDataException("Baked render sample changed native structure");
        for (int m = 0; m < native.Meshes.Count; m++)
        {
            if (baked.Meshes[m].Vertices.Count != native.Meshes[m].Vertices.Count)
                throw new InvalidDataException("Baked render sample changed native vertex count");
            for (int v = 0; v < native.Meshes[m].Vertices.Count; v++)
            {
                var expected = native.Meshes[m].Vertices[v].Position * placed.Scale + placed.Position - centers[0];
                if (Vector3.Distance(baked.Meshes[m].Vertices[v].Position - centers[1], expected) > 0.001f)
                    throw new InvalidDataException("Baked and placed render samples differ in geometry");
                baked.Meshes[m].Vertices[v].Position = native.Meshes[m].Vertices[v].Position;
            }
            CheckBounds(baked.Meshes[m].BoundingBox.Min, baked.Meshes[m].BoundingBox.Max,
                native.Meshes[m].BoundingBox.Min, native.Meshes[m].BoundingBox.Max);
            baked.Meshes[m].BoundingBox = native.Meshes[m].BoundingBox;
        }
        CheckBounds(baked.Header.BoundingBoxMin, baked.Header.BoundingBoxMax, native.Header.BoundingBoxMin, native.Header.BoundingBoxMax);
        baked.Header.BoundingBoxMin = native.Header.BoundingBoxMin; baked.Header.BoundingBoxMax = native.Header.BoundingBoxMax;
        for (int n = 0; n < native.Nodes.Count; n++)
        {
            CheckBounds(baked.Nodes[n].BoundingBoxMin, baked.Nodes[n].BoundingBoxMax, native.Nodes[n].BoundingBoxMin, native.Nodes[n].BoundingBoxMax);
            baked.Nodes[n].BoundingBoxMin = native.Nodes[n].BoundingBoxMin; baked.Nodes[n].BoundingBoxMax = native.Nodes[n].BoundingBoxMax;
        }
        if (!baked.Write().AsSpan().SequenceEqual(native.Write()))
            throw new InvalidDataException("Baked render sample changed data beyond positions and bounds");

        var nativeMesh = native.Meshes[0];
        var nativeIndices = nativeMesh.FaceSets.Single().Triangulate(nativeMesh.Vertices.Count < 65536);
        var generated = samples[2].Meshes.Single();
        var generatedIndices = generated.FaceSets.Single().Indices;
        if (generatedIndices.Count != nativeIndices.Count)
            throw new InvalidDataException("Generated render sample changed triangle count");
        for (int i = 0; i < nativeIndices.Count; i++)
        {
            var expected = nativeMesh.Vertices[nativeIndices[i]].Position * placed.Scale + placed.Position - centers[0];
            if (Vector3.Distance(generated.Vertices[generatedIndices[i]].Position - centers[2], expected) > 0.001f)
                throw new InvalidDataException("Generated render sample changed triangle corners or order");
        }
        Console.WriteLine("PASS: placed, baked and generated render samples read back; native data and matching geometry checked");

        void CheckBounds(Vector3 lo, Vector3 hi, Vector3 nativeLo, Vector3 nativeHi)
        {
            var shift = placed.Position + centers[1] - centers[0];
            if (Vector3.Distance(lo, nativeLo * placed.Scale + shift) > 0.001f
                || Vector3.Distance(hi, nativeHi * placed.Scale + shift) > 0.001f)
                throw new InvalidDataException("Baked render sample bounds did not follow placement");
        }
    }
}
