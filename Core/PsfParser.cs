using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using XunxianDpkViewer.Models;

namespace XunxianDpkViewer.Core;

public static class PsfParser
{
    private const int BoneFloatCount = 20;

    public static SkeletonData Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 20 || !data.Slice(0, 4).SequenceEqual("PSF\0"u8))
            throw new InvalidDataException("不是受支持的寻仙 PSF 骨骼文件。");

        int coreBoneCount = checked((int)ReadUInt(data, 4));
        int boneCount = checked((int)ReadUInt(data, 16));
        if (boneCount <= 0 || boneCount > 100_000)
            throw new InvalidDataException("PSF 骨骼数量异常。");

        int offset = 20;
        var bones = new SkeletonBone[boneCount];
        for (int index = 0; index < boneCount; index++)
        {
            uint nameLength = ReadUInt(data, ref offset);
            int nameBytes = checked((int)nameLength * 2);
            EnsureAvailable(data, offset, nameBytes);
            string name = Encoding.Unicode.GetString(data.Slice(offset, nameBytes)).TrimEnd('\0');
            offset += nameBytes;

            int parentIndex = ReadInt(data, ref offset);
            var values = new float[BoneFloatCount];
            for (int i = 0; i < values.Length; i++)
                values[i] = ReadSingle(data, ref offset);

            EnsureAvailable(data, offset, 4);
            offset += 4;

            int childCount = checked((int)ReadUInt(data, ref offset));
            if (childCount < 0 || childCount > boneCount)
                throw new InvalidDataException("PSF 子骨骼数量异常。");
            var children = new int[childCount];
            for (int i = 0; i < children.Length; i++)
                children[i] = ReadInt(data, ref offset);

            bones[index] = new SkeletonBone(
                index,
                name,
                parentIndex,
                new Vector3(values[0], values[1], values[2]),
                NormalizeQuaternion(new Quaternion(values[3], values[4], values[5], values[6])),
                new Vector3(values[7], values[8], values[9]),
                NormalizeQuaternion(new Quaternion(values[10], values[11], values[12], values[13])),
                children);
        }

        return new SkeletonData(coreBoneCount, bones);
    }

    private static Quaternion NormalizeQuaternion(Quaternion value) =>
        value.LengthSquared() > 0.000001f ? Quaternion.Normalize(value) : Quaternion.Identity;

    private static uint ReadUInt(ReadOnlySpan<byte> data, int offset)
    {
        EnsureAvailable(data, offset, 4);
        return BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    }

    private static uint ReadUInt(ReadOnlySpan<byte> data, ref int offset)
    {
        uint value = ReadUInt(data, offset);
        offset += 4;
        return value;
    }

    private static int ReadInt(ReadOnlySpan<byte> data, ref int offset)
    {
        EnsureAvailable(data, offset, 4);
        int value = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;
        return value;
    }

    private static float ReadSingle(ReadOnlySpan<byte> data, ref int offset)
    {
        int bits = ReadInt(data, ref offset);
        return BitConverter.Int32BitsToSingle(bits);
    }

    private static void EnsureAvailable(ReadOnlySpan<byte> data, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > data.Length - count)
            throw new InvalidDataException("PSF 骨骼数据不完整。");
    }
}
