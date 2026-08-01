using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using XunxianDpkViewer.Models;

namespace XunxianDpkViewer.Core;

public sealed class DpkReader : IDisposable
{
    private const int BlockSize = 1024;
    private const int RootPayloadOffset = 32;
    private const int ChainPayloadOffset = 8;
    private static readonly byte[] PackagePassword = "tianxiawudi"u8.ToArray();
    private static readonly byte[] ContentPassword = "gandiaosifu"u8.ToArray();

    private readonly FileStream _stream;
    private readonly object _streamGate = new();
    private IReadOnlyList<DpkEntry>? _entries;

    public DpkReader(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        _stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 64, FileOptions.RandomAccess);
    }

    public string Path { get; }

    public IReadOnlyList<DpkEntry> ReadEntries()
    {
        if (_entries is not null) return _entries;

        byte[] header = ReadHeader();
        uint treeRoot = ReadUInt(header, 0x84);
        uint namesRoot = ReadUInt(header, 0x88);
        byte[] tree = DecodeWhsc(ReadVirtualFile(treeRoot));
        byte[] names = DecodeWhsc(ReadVirtualFile(namesRoot));

        var nameByOffset = new Dictionary<uint, string>();
        int nameOffset = 0;
        while (nameOffset < names.Length)
        {
            int end = Array.IndexOf(names, (byte)0, nameOffset);
            if (end < 0) throw new InvalidDataException("DPK 路径字符串表损坏。");
            nameByOffset[(uint)nameOffset] = Encoding.ASCII.GetString(names, nameOffset, end - nameOffset);
            nameOffset = end + 1;
        }

        var entries = new List<DpkEntry>();
        var directories = new List<string>();
        int offset = 0;
        while (offset < tree.Length)
        {
            byte recordType = tree[offset++];
            switch (recordType)
            {
                case 1:
                {
                    uint entryNameOffset = ReadUInt(tree, offset);
                    uint rootBlock = ReadUInt(tree, offset + 4);
                    offset += 8;
                    string entryName = nameByOffset[entryNameOffset];
                    string path = directories.Count == 0
                        ? entryName
                        : string.Join('/', directories.Append(entryName));
                    entries.Add(new DpkEntry(path, rootBlock));
                    break;
                }
                case 2:
                {
                    uint directoryNameOffset = ReadUInt(tree, offset);
                    offset += 4;
                    directories.Add(nameByOffset[directoryNameOffset]);
                    break;
                }
                case 3:
                    if (directories.Count == 0) throw new InvalidDataException("DPK 目录树不平衡。");
                    directories.RemoveAt(directories.Count - 1);
                    break;
                default:
                    throw new InvalidDataException($"未知的 DPK 目录记录：{recordType}。");
            }
        }

        _entries = entries;
        return _entries;
    }

    public byte[] Extract(DpkEntry entry) => DecodeWhsc(ReadVirtualFile(entry.RootBlock));

    public void ExtractTo(DpkEntry entry, string targetPath)
    {
        string? directory = System.IO.Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllBytes(targetPath, Extract(entry));
    }

    private byte[] ReadHeader()
    {
        byte[] header = ReadAt(0, BlockSize);
        if (!header.AsSpan(0, 12).SequenceEqual("whpackage1.0"u8))
            throw new InvalidDataException("文件不是 whpackage1.0 DPK 包。");
        if (!MD5.HashData(PackagePassword).AsSpan().SequenceEqual(header.AsSpan(32, 16)))
            throw new InvalidDataException("DPK 包密码校验失败。");
        return header;
    }

    private byte[] ReadVirtualFile(uint rootBlock)
    {
        byte[] root = ReadAt((long)rootBlock * BlockSize, BlockSize);
        uint previous = ReadUInt(root, 0);
        uint next = ReadUInt(root, 4);
        uint tail = ReadUInt(root, 8);
        uint fileSize = ReadUInt(root, 12);
        if (previous != 0) throw new InvalidDataException($"块 {rootBlock} 不是根块。");
        if (fileSize > int.MaxValue) throw new InvalidDataException("DPK 单文件超过当前工具限制。");

        using var output = new MemoryStream((int)fileSize);
        output.Write(root, RootPayloadOffset, Math.Min((int)fileSize, BlockSize - RootPayloadOffset));
        uint current = next;
        uint last = rootBlock;
        var seen = new HashSet<uint> { rootBlock };

        while (current != 0 && output.Length < fileSize)
        {
            if (!seen.Add(current)) throw new InvalidDataException($"DPK 块链在 {current} 形成循环。");
            byte[] block = ReadAt((long)current * BlockSize, BlockSize);
            uint blockPrevious = ReadUInt(block, 0);
            uint blockNext = ReadUInt(block, 4);
            if (blockPrevious != last) throw new InvalidDataException($"DPK 块链 {last} -> {current} 断裂。");
            int count = (int)Math.Min(fileSize - output.Length, BlockSize - ChainPayloadOffset);
            output.Write(block, ChainPayloadOffset, count);
            last = current;
            current = blockNext;
        }

        if (last != tail) throw new InvalidDataException("DPK 尾块校验失败。");
        return output.ToArray();
    }

    private static byte[] DecodeWhsc(byte[] data)
    {
        if (data.Length < 48 || !data.AsSpan(0, 7).SequenceEqual("whsc1.0"u8))
            throw new InvalidDataException("DPK 文件流不是 WHSC 格式。");

        byte flags = data[15];
        ReadOnlySpan<byte> expectedDigest = data.AsSpan(16, 16);
        uint reserved = ReadUInt(data, 32);
        uint plainSize = ReadUInt(data, 36);
        uint storedSize = ReadUInt(data, 40);
        if (reserved != 0 || storedSize > data.Length - 48)
            throw new InvalidDataException("WHSC 头部损坏。");

        byte[] payload = data.AsSpan(48, checked((int)storedSize)).ToArray();
        if ((flags & 2) != 0)
        {
            byte[] sourceKey = (flags & 4) != 0 ? ContentPassword : PackagePassword;
            byte[] key = new byte[8];
            sourceKey.AsSpan(0, Math.Min(8, sourceKey.Length)).CopyTo(key);
            payload = DecryptPayload(payload, key);
        }

        if ((flags & 1) != 0)
        {
            using var input = new MemoryStream(payload, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(checked((int)plainSize));
            zlib.CopyTo(output);
            payload = output.ToArray();
        }

        if (payload.Length != plainSize)
            throw new InvalidDataException($"WHSC 解压长度不符：应为 {plainSize}，实际为 {payload.Length}。");
        if (!MD5.HashData(payload).AsSpan().SequenceEqual(expectedDigest))
            throw new InvalidDataException("WHSC 内容 MD5 校验失败。");
        return payload;
    }

    private static byte[] DecryptPayload(byte[] payload, byte[] key)
    {
        int fullSize = payload.Length / 8 * 8;
        byte[] result = new byte[payload.Length];
        byte[] previous = new byte[8];

        if (fullSize > 0)
        {
            using DES des = DES.Create();
            des.Key = key;
            des.Mode = CipherMode.ECB;
            des.Padding = PaddingMode.None;
            using ICryptoTransform decryptor = des.CreateDecryptor();
            byte[] decoded = decryptor.TransformFinalBlock(payload, 0, fullSize);
            for (int offset = 0; offset < fullSize; offset += 8)
            {
                for (int i = 0; i < 8; i++) result[offset + i] = (byte)(decoded[offset + i] ^ previous[i]);
                Buffer.BlockCopy(payload, offset, previous, 0, 8);
            }
        }

        if (fullSize < payload.Length)
        {
            ReadOnlySpan<byte> residual = payload.AsSpan(fullSize);
            byte[] transformed = residual.ToArray();
            int parity = (transformed[^1] + transformed[0]) & 1;
            for (int left = 0; left < transformed.Length / 2; left++)
            {
                int right = transformed.Length - left - 1;
                if (((transformed[right] + transformed[left]) & 1) == parity)
                    (transformed[left], transformed[right]) = (transformed[right], transformed[left]);
            }

            byte priorCipherByte = 0;
            for (int i = 0; i < transformed.Length; i++)
            {
                byte value = transformed[i];
                transformed[i] = (byte)(value ^ key[i] ^ priorCipherByte ^ previous[i]);
                priorCipherByte = value;
                result[fullSize + i] = transformed[i];
            }
        }

        return result;
    }

    private byte[] ReadAt(long offset, int count)
    {
        byte[] buffer = new byte[count];
        lock (_streamGate)
        {
            _stream.Position = offset;
            int read = 0;
            while (read < buffer.Length)
            {
                int bytesRead = _stream.Read(buffer, read, buffer.Length - read);
                if (bytesRead == 0) throw new EndOfStreamException($"读取 DPK 时在偏移 {offset + read} 提前结束。");
                read += bytesRead;
            }
        }
        return buffer;
    }

    private static uint ReadUInt(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));

    public void Dispose() => _stream.Dispose();
}
