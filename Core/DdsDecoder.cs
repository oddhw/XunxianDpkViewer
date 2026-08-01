using System.Buffers.Binary;
using XunxianDpkViewer.Models;

namespace XunxianDpkViewer.Core;

public static class DdsDecoder
{
    private const uint DdsMagic = 0x20534444;
    private const uint FourCcDxt1 = 0x31545844;
    private const uint FourCcDxt3 = 0x33545844;
    private const uint FourCcDxt5 = 0x35545844;
    private const uint FourCcDx10 = 0x30315844;

    public static DecodedTexture Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 128 || ReadUInt(data, 0) != DdsMagic || ReadUInt(data, 4) != 124)
            throw new InvalidDataException("不是受支持的 DDS 贴图。");

        int height = checked((int)ReadUInt(data, 12));
        int width = checked((int)ReadUInt(data, 16));
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
            throw new InvalidDataException("DDS 尺寸异常。");

        uint pixelFlags = ReadUInt(data, 80);
        uint fourCc = ReadUInt(data, 84);
        int payloadOffset = 128;
        string format;
        if (fourCc == FourCcDx10)
        {
            if (data.Length < 148) throw new InvalidDataException("DDS DX10 头部不完整。");
            uint dxgiFormat = ReadUInt(data, 128);
            payloadOffset = 148;
            fourCc = dxgiFormat switch
            {
                71 or 72 => FourCcDxt1,
                74 or 75 => FourCcDxt3,
                77 or 78 => FourCcDxt5,
                _ => throw new NotSupportedException($"暂不支持 DDS DXGI 格式 {dxgiFormat}。")
            };
        }

        byte[] pixels = new byte[checked(width * height * 4)];
        switch (fourCc)
        {
            case FourCcDxt1:
                DecodeBlocks(data[payloadOffset..], width, height, pixels, 8, DecodeBc1Block);
                format = "BC1 / DXT1";
                break;
            case FourCcDxt3:
                DecodeBlocks(data[payloadOffset..], width, height, pixels, 16, DecodeBc2Block);
                format = "BC2 / DXT3";
                break;
            case FourCcDxt5:
                DecodeBlocks(data[payloadOffset..], width, height, pixels, 16, DecodeBc3Block);
                format = "BC3 / DXT5";
                break;
            default:
                if ((pixelFlags & 0x40) == 0)
                    throw new NotSupportedException($"暂不支持 DDS FourCC 0x{fourCc:X8}。");
                DecodeUncompressed(data[payloadOffset..], width, height, pixels,
                    checked((int)ReadUInt(data, 88)),
                    ReadUInt(data, 92), ReadUInt(data, 96), ReadUInt(data, 100), ReadUInt(data, 104));
                format = "未压缩 RGB(A)";
                break;
        }
        return new DecodedTexture(width, height, pixels, format);
    }

    private delegate void BlockDecoder(ReadOnlySpan<byte> block, Span<byte> output);

    private static void DecodeBlocks(
        ReadOnlySpan<byte> source,
        int width,
        int height,
        byte[] destination,
        int blockSize,
        BlockDecoder decoder)
    {
        int blocksWide = (width + 3) / 4;
        int blocksHigh = (height + 3) / 4;
        int required = checked(blocksWide * blocksHigh * blockSize);
        if (source.Length < required) throw new InvalidDataException("DDS 压缩数据不完整。");

        Span<byte> blockPixels = stackalloc byte[64];
        int sourceOffset = 0;
        for (int blockY = 0; blockY < blocksHigh; blockY++)
        {
            for (int blockX = 0; blockX < blocksWide; blockX++)
            {
                decoder(source.Slice(sourceOffset, blockSize), blockPixels);
                sourceOffset += blockSize;
                for (int y = 0; y < 4; y++)
                {
                    int targetY = blockY * 4 + y;
                    if (targetY >= height) continue;
                    for (int x = 0; x < 4; x++)
                    {
                        int targetX = blockX * 4 + x;
                        if (targetX >= width) continue;
                        blockPixels.Slice((y * 4 + x) * 4, 4)
                            .CopyTo(destination.AsSpan((targetY * width + targetX) * 4, 4));
                    }
                }
            }
        }
    }

    private static void DecodeBc1Block(ReadOnlySpan<byte> block, Span<byte> output) =>
        DecodeBcColor(block, output, forceFourColors: false);

    private static void DecodeBc2Block(ReadOnlySpan<byte> block, Span<byte> output)
    {
        DecodeBcColor(block[8..], output, forceFourColors: true);
        ulong alphaBits = BinaryPrimitives.ReadUInt64LittleEndian(block);
        for (int i = 0; i < 16; i++)
            output[i * 4 + 3] = (byte)(((alphaBits >> (i * 4)) & 0xF) * 17);
    }

    private static void DecodeBc3Block(ReadOnlySpan<byte> block, Span<byte> output)
    {
        DecodeBcColor(block[8..], output, forceFourColors: true);
        Span<byte> alpha = stackalloc byte[8];
        alpha[0] = block[0];
        alpha[1] = block[1];
        if (alpha[0] > alpha[1])
        {
            for (int i = 1; i <= 6; i++) alpha[i + 1] = (byte)(((7 - i) * alpha[0] + i * alpha[1]) / 7);
        }
        else
        {
            for (int i = 1; i <= 4; i++) alpha[i + 1] = (byte)(((5 - i) * alpha[0] + i * alpha[1]) / 5);
            alpha[6] = 0;
            alpha[7] = 255;
        }

        ulong indices = 0;
        for (int i = 0; i < 6; i++) indices |= (ulong)block[2 + i] << (8 * i);
        for (int i = 0; i < 16; i++) output[i * 4 + 3] = alpha[(int)((indices >> (i * 3)) & 7)];
    }

    private static void DecodeBcColor(ReadOnlySpan<byte> block, Span<byte> output, bool forceFourColors)
    {
        ushort color0 = BinaryPrimitives.ReadUInt16LittleEndian(block);
        ushort color1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
        Span<byte> palette = stackalloc byte[16];
        Expand565(color0, palette);
        Expand565(color1, palette[4..]);
        if (color0 > color1 || forceFourColors)
        {
            MixColor(palette, 0, 1, 2, 1, 2, 3);
            MixColor(palette, 0, 1, 1, 2, 3, 3);
        }
        else
        {
            MixColor(palette, 0, 1, 1, 1, 2, 2);
            palette.Slice(12, 4).Clear();
        }

        uint indices = ReadUInt(block, 4);
        for (int i = 0; i < 16; i++)
            palette.Slice((int)((indices >> (i * 2)) & 3) * 4, 4).CopyTo(output.Slice(i * 4, 4));
    }

    private static void Expand565(ushort color, Span<byte> bgra)
    {
        bgra[2] = (byte)(((color >> 11) & 31) * 255 / 31);
        bgra[1] = (byte)(((color >> 5) & 63) * 255 / 63);
        bgra[0] = (byte)((color & 31) * 255 / 31);
        bgra[3] = 255;
    }

    private static void MixColor(Span<byte> palette, int first, int second, int firstWeight, int secondWeight, int target, int divisor)
    {
        for (int channel = 0; channel < 3; channel++)
            palette[target * 4 + channel] = (byte)((palette[first * 4 + channel] * firstWeight + palette[second * 4 + channel] * secondWeight) / divisor);
        palette[target * 4 + 3] = 255;
    }

    private static void DecodeUncompressed(
        ReadOnlySpan<byte> source,
        int width,
        int height,
        byte[] destination,
        int bitsPerPixel,
        uint redMask,
        uint greenMask,
        uint blueMask,
        uint alphaMask)
    {
        if (bitsPerPixel is not (24 or 32)) throw new NotSupportedException($"暂不支持 {bitsPerPixel} 位 DDS。");
        int bytesPerPixel = bitsPerPixel / 8;
        int required = checked(width * height * bytesPerPixel);
        if (source.Length < required) throw new InvalidDataException("DDS 像素数据不完整。");
        for (int i = 0; i < width * height; i++)
        {
            uint value = bytesPerPixel == 4
                ? ReadUInt(source, i * 4)
                : (uint)(source[i * 3] | source[i * 3 + 1] << 8 | source[i * 3 + 2] << 16);
            int target = i * 4;
            destination[target] = ExtractChannel(value, blueMask, 0);
            destination[target + 1] = ExtractChannel(value, greenMask, 0);
            destination[target + 2] = ExtractChannel(value, redMask, 0);
            destination[target + 3] = alphaMask == 0 ? (byte)255 : ExtractChannel(value, alphaMask, 255);
        }
    }

    private static byte ExtractChannel(uint value, uint mask, byte fallback)
    {
        if (mask == 0) return fallback;
        int shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
        uint maximum = mask >> shift;
        return (byte)(((value & mask) >> shift) * 255 / maximum);
    }

    private static uint ReadUInt(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
}
