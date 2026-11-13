using System.Numerics;
using SoulsFormats;

namespace AgeOfGold.Scene;

internal static class LayerModelWriter
{
    internal static byte[] Write(byte[] template, byte[] layerTemplate, int layerMesh, LayerGeometry.Surface[] surfaces,
        MaterialWriter.Bound[]? bindings = null)
    {
        var model = FLVER2.Read(template); var layer = FLVER2.Read(layerTemplate);
        if (bindings is not null && bindings.Length != surfaces.Length)
            throw new InvalidDataException("Each layered surface requires its material binding");
        if (model.Header.Version != 0x2001a || layer.Header.Version != 0x2001a || model.Header.BigEndian || layer.Header.BigEndian
            || model.Nodes.Count != 1 || model.Nodes[0].Translation != Vector3.Zero || model.Nodes[0].Rotation != Vector3.Zero
            || model.Nodes[0].Scale != Vector3.One || surfaces.Length == 0)
            throw new InvalidDataException("Layer model requires a static host template and at least one surface");
        if (layerMesh < 0 || layerMesh >= layer.Meshes.Count) throw new InvalidDataException("Missing host layer mesh");
        var exemplar = layer.Meshes[layerMesh];
        if (exemplar.UseBoneWeights || exemplar.VertexBuffers.Count != 1)
            throw new InvalidDataException("Host layer layout must be one rigid vertex buffer");
        var layout = layer.BufferLayouts[exemplar.VertexBuffers[0].LayoutIndex];
        if (layout.Count != 7 || layout.Any(m => m.SpecialModifier != 0)
            || layout.Count(m => m.Semantic == FLVER.LayoutSemantic.Position) != 1
            || layout.Count(m => m.Semantic == FLVER.LayoutSemantic.Normal) != 1
            || layout.Count(m => m.Semantic == FLVER.LayoutSemantic.Tangent) != 1
            || layout.Count(m => m.Semantic == FLVER.LayoutSemantic.UV) != 2
            || layout.Count(m => m.Semantic == FLVER.LayoutSemantic.VertexColor) != 2
            || layout.Single(m => m.Semantic == FLVER.LayoutSemantic.UV && m.Index == 0).Type != FLVER.LayoutType.Short2)
            throw new InvalidDataException("Unsupported native two-layer vertex layout");
        layout.Single(m => m.Semantic == FLVER.LayoutSemantic.UV && m.Index == 1).Type = FLVER.LayoutType.Float4;
        layout.Single(m => m.Semantic == FLVER.LayoutSemantic.VertexColor && m.Index == 0).Type = FLVER.LayoutType.Float4;
        var tangent = layout.Single(m => m.Semantic == FLVER.LayoutSemantic.Tangent);
        layout.Add(new(tangent.Type, FLVER.LayoutSemantic.Tangent, 2));
        layout.Add(new(FLVER.LayoutType.Float4, FLVER.LayoutSemantic.Tangent, 4));
        layout.Add(new(FLVER.LayoutType.Float4, FLVER.LayoutSemantic.Tangent, 6));
        var material = layer.Materials[exemplar.MaterialIndex];
        model.GXLists = [layer.GXLists[material.GXIndex]];
        material.GXIndex = 0; material.Index = 0;
        model.Materials = [material]; model.BufferLayouts = [layout]; model.Meshes.Clear(); model.Dummies.Clear();
        if (bindings is not null)
            model.Materials = bindings.Select((binding, i) =>
            {
                var definition = MATBIN.Read(binding.Definition);
                var name = Path.GetFileNameWithoutExtension(definition.SourcePath.Replace('\\', '/'));
                if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("Bound material lacks an identity");
                // The host finds the definition from this path: its directory without the definition folder, as a material name.
                int start = definition.SourcePath.LastIndexOfAny(['/', '\\']) + 1;
                string directory = definition.SourcePath[..start];
                foreach (string folder in new[] { "matxml\\", "matxml/" })
                    if (directory.EndsWith(folder, StringComparison.OrdinalIgnoreCase)) directory = directory[..^folder.Length];
                var path = directory + name + ".mtd";
                return new FLVER2.Material { Name = name + "_" + i, MTD = path, GXIndex = 0, Index = i, Textures = binding.Textures.ToList() };
            }).ToList();
        foreach (var surface in surfaces)
        {
            var mesh = new FLVER2.Mesh
            {
                NodeIndex = 0, MaterialIndex = bindings is null ? 0 : model.Meshes.Count, VertexBuffers = [new(0)],
                FaceSets = [new() { CullBackfaces = surface.CullBackfaces, Indices = surface.Indices.ToList() }],
                Vertices = surface.Vertices.Select(v => new FLVER.Vertex
                {
                    Positions = [v.Position], Normals = [v.Normal], NormalWs = [0],
                    UVs = [new(v.Primary, 0), new(v.Secondary, 0), Vector3.Zero],
                    Tangents = [v.Tangent, v.SecondaryTangent, v.Bitangent, v.SecondaryBitangent],
                    Colors = [new(v.Colour.W, v.Colour.X, v.Colour.Y, v.Colour.Z), new(1f, 1f, 1f, 1f)],
                }).ToList(),
            };
            var lo = surface.Vertices.Select(v => v.Position).Aggregate(Vector3.Min);
            var hi = surface.Vertices.Select(v => v.Position).Aggregate(Vector3.Max);
            // This host revision stores extent, reserved zero and centre in its mesh bounds fields.
            mesh.BoundingBox = new() { Min = (hi - lo) / 2 + new Vector3(0.001f), Max = Vector3.Zero, Unk = (hi + lo) / 2 };
            model.Meshes.Add(mesh);
        }
        var positions = surfaces.SelectMany(s => s.Vertices).Select(v => v.Position);
        model.Header.BoundingBoxMin = model.Nodes[0].BoundingBoxMin = positions.Aggregate(Vector3.Min);
        model.Header.BoundingBoxMax = model.Nodes[0].BoundingBoxMax = positions.Aggregate(Vector3.Max);
        return model.Write();
    }
}
