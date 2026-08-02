using System.Buffers.Binary;
using System.Numerics;
using XunxianDpkViewer.Models;

namespace XunxianDpkViewer.Core;

public static class PmfParser
{
    public static PmfMesh Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 48 || !data.Slice(4, 4).SequenceEqual("PMF\0"u8))
            throw new InvalidDataException("不是受支持的寻仙 PMF 模型。");

        uint headerSize = ReadUInt(data, 0);
        uint version = ReadUInt(data, 8);
        uint vertexFlags = ReadUInt(data, 12);
        uint vertexCount = ReadUInt(data, 16);
        uint triangleCount = ReadUInt(data, 20);
        uint uvChannels = ReadUInt(data, 24);

        if (headerSize != 28 || vertexCount == 0 || vertexCount > 5_000_000)
            throw new InvalidDataException("PMF 头部或顶点数量异常。");

        int positionOffset = 48;
        long positionBytes = (long)vertexCount * 12;
        long indexBytes = (long)triangleCount * 6;
        long uvBytes = (long)vertexCount * uvChannels * 8;
        long vertexDataBytes = data.Length - positionOffset - indexBytes - uvBytes;
        if (positionOffset + positionBytes > data.Length || indexBytes > data.Length - positionOffset ||
            vertexDataBytes < positionBytes || vertexDataBytes % vertexCount != 0)
            throw new InvalidDataException("PMF 顶点或索引数据不完整。");

        var vertices = new Vector3[vertexCount];
        for (int i = 0; i < vertices.Length; i++)
        {
            int offset = positionOffset + i * 12;
            vertices[i] = new Vector3(
                ReadSingle(data, offset),
                ReadSingle(data, offset + 4),
                ReadSingle(data, offset + 8));
        }

        var boneWeights = Array.Empty<Vector4>();
        var boneIndices = Array.Empty<PmfBoneIndices>();
        int vertexStride = checked((int)(vertexDataBytes / vertexCount));
        bool hasSkinning = (vertexFlags & 0x10) != 0 && vertexStride >= 32;
        if (hasSkinning)
        {
            int weightOffset = checked(positionOffset + (int)positionBytes);
            int boneIndexOffset = checked(weightOffset + (int)vertexCount * 12);
            boneWeights = new Vector4[vertexCount];
            boneIndices = new PmfBoneIndices[vertexCount];
            for (int i = 0; i < boneWeights.Length; i++)
            {
                int offset = weightOffset + i * 12;
                float x = Math.Max(0f, ReadSingle(data, offset));
                float y = Math.Max(0f, ReadSingle(data, offset + 4));
                float z = Math.Max(0f, ReadSingle(data, offset + 8));
                float w = Math.Max(0f, 1f - x - y - z);
                float total = x + y + z + w;
                boneWeights[i] = total > 0.000001f
                    ? new Vector4(x, y, z, w) / total
                    : Vector4.Zero;

                offset = boneIndexOffset + i * 8;
                boneIndices[i] = new PmfBoneIndices(
                    BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2)),
                    BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset + 2, 2)),
                    BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset + 4, 2)),
                    BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset + 6, 2)));
            }
        }

        var textureCoordinates = Array.Empty<Vector2>();
        if (uvChannels > 0)
        {
            int uvOffset = checked(positionOffset + (int)vertexDataBytes);
            textureCoordinates = new Vector2[vertexCount];
            for (int i = 0; i < textureCoordinates.Length; i++)
            {
                int offset = uvOffset + i * 8;
                textureCoordinates[i] = new Vector2(ReadSingle(data, offset), ReadSingle(data, offset + 4));
            }
        }

        // 寻仙 PMF 的三角形索引固定置于文件尾部。这样既兼容静态模型，
        // 也兼容带骨骼权重、颜色、法线和多 UV 通道的顶点声明。
        int indexOffset = checked(data.Length - (int)indexBytes);
        var indices = new ushort[triangleCount * 3];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(indexOffset + i * 2, 2));

        if (indices.Any(index => index >= vertexCount))
            throw new InvalidDataException("PMF 索引超出顶点范围。");

        return new PmfMesh(vertices, textureCoordinates, indices, version, vertexFlags, uvChannels, triangleCount)
        {
            BoneWeights = boneWeights,
            BoneIndices = boneIndices
        };
    }

    private static uint ReadUInt(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));

    private static float ReadSingle(ReadOnlySpan<byte> data, int offset) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4)));
}
