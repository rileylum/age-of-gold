using System.Numerics;
using SoulsFormats;

namespace AgeOfGold.Scene;

internal static class ModelReader
{
    internal static FLVER2.BufferLayout RepairLayout(FLVER2.BufferLayout declaration, int stride)
    {
        var result = new FLVER2.BufferLayout();
        result.AddRange(declaration.Select(m => new FLVER.LayoutMember(m)));
        int delta = stride - result.Size;
        if (delta == 0) return result;
        int normal = result.FindIndex(m => m.Semantic == FLVER.LayoutSemantic.Normal);
        int tangent = result.FindIndex(m => m.Semantic == FLVER.LayoutSemantic.Tangent);
        if (normal < 0 || result[normal].Type != FLVER.LayoutType.UByte4Norm
            || result.Any(m => m.Semantic == FLVER.LayoutSemantic.Bitangent || m.SpecialModifier != 0))
            throw new InvalidDataException("Unclassified source vertex-layout mismatch");
        if (delta == 4)
            result.Insert(tangent < 0 ? normal + 1 : tangent + 1, new FLVER.LayoutMember(FLVER.LayoutType.UByte4Norm,
                tangent < 0 ? FLVER.LayoutSemantic.Tangent : FLVER.LayoutSemantic.Bitangent));
        else if (delta == -4 && tangent >= 0 && result[tangent].Type == FLVER.LayoutType.UByte4Norm)
            result.RemoveAt(tangent);
        else
            throw new InvalidDataException($"Unclassified source vertex stride {stride} for declaration size {declaration.Size}");
        if (result.Size != stride) throw new InvalidDataException("Corrected source vertex declaration does not match its stride");
        return result;
    }

    internal static (FLVER2 Model, int Repaired) Read(byte[] bytes)
    {
        var raw = DCX.Is(bytes) ? DCX.Decompress(bytes) : bytes;
        using var reader = new BinaryReaderEx(false, raw);
        var model = new FLVER2();
        model.Read(reader, false);
        if (model.Header.Version != 0x2000C || model.Header.BigEndian)
            throw new InvalidDataException("Unclassified source visual format version or byte order");
        int dataOffset = reader.GetInt32(12), repaired = 0;
        // The library's automatic repair mutates shared layouts and can still leave the wrong stride.
        // Read metadata first, fix each buffer's declaration independently, then use its vertex decoder.
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var readVertices = typeof(FLVER2.Mesh).GetMethod("ReadVertices", flags)!;
        int Field(FLVER2.VertexBuffer buffer, string name) => (int)typeof(FLVER2.VertexBuffer).GetField(name, flags)!.GetValue(buffer)!;
        foreach (var mesh in model.Meshes)
        {
            if (mesh.VertexBuffers.Count != 1 || mesh.VertexBuffers[0].EdgeCompressed)
                throw new InvalidDataException("Source visual reader requires one ordinary vertex buffer");
            var buffer = mesh.VertexBuffers[0];
            int stride = Field(buffer, "VertexSize");
            var declaration = model.BufferLayouts[buffer.LayoutIndex];
            if (declaration.Size != stride) repaired++;
            var corrected = RepairLayout(declaration, stride);
            buffer.LayoutIndex = model.BufferLayouts.Count;
            model.BufferLayouts.Add(corrected);
            readVertices.Invoke(mesh, [reader, dataOffset, model.BufferLayouts, model.Header]);
            if (corrected[0].Semantic != FLVER.LayoutSemantic.Position || corrected[0].Type != FLVER.LayoutType.Float3)
                throw new InvalidDataException("Source visual position must be the buffer's leading float vector");
            int start = dataOffset + Field(buffer, "BufferOffset");
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                int at = start + i * stride;
                var position = new Vector3(reader.GetSingle(at), reader.GetSingle(at + 4), reader.GetSingle(at + 8));
                if (mesh.Vertices[i].Position != position)
                    throw new InvalidDataException("Source vertex decoder did not follow the stored buffer stride");
            }
        }
        return (model, repaired);
    }

}
