using System.Buffers.Binary;
using System.Numerics;
using XunxianDpkViewer.Models;

namespace XunxianDpkViewer.Core;

public static class PafParser
{
    private const float PackedQuaternionScale = 32767f;

    public static SkeletalAnimation Parse(ReadOnlySpan<byte> data, string name, AssetEntry sourceAsset)
    {
        if (data.Length < 20 || !data.Slice(0, 4).SequenceEqual("PAF\0"u8))
            throw new InvalidDataException("不是受支持的寻仙 PAF 动画文件。");

        uint version = ReadUInt(data, 4);
        int sampleRate = checked((int)ReadUInt(data, 8));
        float duration = ReadSingle(data, 12);
        int trackCount = checked((int)ReadUInt(data, 16));
        if (version is not (100 or 101) ||
            sampleRate <= 0 || sampleRate > 1000 ||
            !float.IsFinite(duration) || duration < 0 ||
            trackCount < 0 || trackCount > 100_000)
            throw new InvalidDataException("PAF 动画头部异常。");

        int offset = 20;
        var tracks = new Dictionary<int, AnimationTrack>();
        for (int i = 0; i < trackCount; i++)
        {
            int boneIndex = ReadInt(data, ref offset);
            int rotationCount = ReadCount(data, ref offset);
            var rotations = new Quaternion[rotationCount];
            for (int key = 0; key < rotations.Length; key++)
            {
                Quaternion rotation = version == 101
                    ? new Quaternion(
                        ReadInt16(data, ref offset) / PackedQuaternionScale,
                        ReadInt16(data, ref offset) / PackedQuaternionScale,
                        ReadInt16(data, ref offset) / PackedQuaternionScale,
                        ReadInt16(data, ref offset) / PackedQuaternionScale)
                    : new Quaternion(
                        ReadSingle(data, ref offset),
                        ReadSingle(data, ref offset),
                        ReadSingle(data, ref offset),
                        ReadSingle(data, ref offset));
                rotations[key] = rotation.LengthSquared() > 0.000001f
                    ? Quaternion.Normalize(rotation)
                    : Quaternion.Identity;
            }

            int translationCount = ReadCount(data, ref offset);
            var translations = new Vector3[translationCount];
            for (int key = 0; key < translations.Length; key++)
            {
                translations[key] = new Vector3(
                    ReadSingle(data, ref offset),
                    ReadSingle(data, ref offset),
                    ReadSingle(data, ref offset));
            }

            tracks[boneIndex] = new AnimationTrack(boneIndex, rotations, translations);
        }

        return new SkeletalAnimation(name, sampleRate, duration, tracks, sourceAsset);
    }

    private static int ReadCount(ReadOnlySpan<byte> data, ref int offset)
    {
        int count = checked((int)ReadUInt(data, ref offset));
        if (count < 0 || count > 10_000_000)
            throw new InvalidDataException("PAF 关键帧数量异常。");
        return count;
    }

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

    private static short ReadInt16(ReadOnlySpan<byte> data, ref int offset)
    {
        EnsureAvailable(data, offset, 2);
        short value = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2));
        offset += 2;
        return value;
    }

    private static float ReadSingle(ReadOnlySpan<byte> data, int offset)
    {
        EnsureAvailable(data, offset, 4);
        return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4)));
    }

    private static float ReadSingle(ReadOnlySpan<byte> data, ref int offset)
    {
        float value = ReadSingle(data, offset);
        offset += 4;
        return value;
    }

    private static void EnsureAvailable(ReadOnlySpan<byte> data, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > data.Length - count)
            throw new InvalidDataException("PAF 动画数据不完整。");
    }
}
