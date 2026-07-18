using System.Buffers.Binary;
using System.Text;

namespace Edf3Koreanizer.Core.Formats;

public enum SgoByteOrder
{
    LittleEndian,
    BigEndian
}

public sealed record SgoTextEntry(
    int TopLevelIndex,
    string Name,
    string NodePath,
    int NodeOffset,
    int StringOffset,
    int DeclaredLength,
    string Source);

public sealed record SgoTextDocument(
    string Format,
    string ByteOrder,
    bool WasSgslCompressed,
    int TopLevelVariableCount,
    IReadOnlyList<SgoTextEntry> Entries);

/// <summary>
/// Reads the EDF2017 SGO variant and flattens its string nodes while retaining
/// stable node paths and original offsets for later reinsertion.
/// </summary>
public static class SgoTextExtractor
{
    private const int NodeSize = 0x0C;

    public static SgoTextDocument Extract(ReadOnlySpan<byte> input)
    {
        var wasCompressed = SgslCodec.IsSgsl(input);
        var data = wasCompressed ? SgslCodec.Decompress(input) : input.ToArray();
        var reader = new SgoReader(data);

        if (!reader.IsEdf2017Header)
        {
            throw new InvalidDataException(
                "This first implementation supports the EDF2017 SGO header only.");
        }

        var variableCount = reader.ReadInt32(0x04);
        var variablesOffset = reader.ReadInt32(0x08);
        var labelsOffset = reader.ReadInt32(0x0C);

        if (variableCount < 0)
        {
            throw new InvalidDataException($"Invalid SGO variable count: {variableCount}.");
        }

        reader.RequireRange(variablesOffset, checked(variableCount * NodeSize), "variable table");
        reader.RequireRange(labelsOffset, checked(variableCount * 4), "label table");

        var entries = new List<SgoTextEntry>();
        for (var index = 0; index < variableCount; index++)
        {
            var labelEntryOffset = labelsOffset + index * 4;
            var labelPointer = checked(labelEntryOffset + reader.ReadInt32(labelEntryOffset));
            var name = reader.ReadString(labelPointer);
            var nodeOffset = variablesOffset + index * NodeSize;
            ReadNode(reader, nodeOffset, index, name, $"/variables/{index}", 0, entries);
        }

        return new SgoTextDocument(
            "SGO-EDF2017",
            reader.ByteOrder == SgoByteOrder.LittleEndian ? "LE" : "BE",
            wasCompressed,
            variableCount,
            entries);
    }

    public static SgoTextDocument ExtractFile(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("Input path is required.", nameof(inputPath));
        }

        return Extract(File.ReadAllBytes(inputPath));
    }

    private static void ReadNode(
        SgoReader reader,
        int nodeOffset,
        int topLevelIndex,
        string name,
        string path,
        int depth,
        ICollection<SgoTextEntry> entries)
    {
        if (depth > 128)
        {
            throw new InvalidDataException($"SGO node nesting is too deep at {path}.");
        }

        reader.RequireRange(nodeOffset, NodeSize, $"node {path}");
        var nodeType = reader.ReadInt32(nodeOffset);
        var size = reader.ReadInt32(nodeOffset + 4);
        var dataOffset = reader.ReadInt32(nodeOffset + 8);

        switch (nodeType)
        {
            case 0:
                if (size < 0)
                {
                    throw new InvalidDataException($"Invalid child count {size} at {path}.");
                }

                var childrenOffset = checked(nodeOffset + dataOffset);
                reader.RequireRange(childrenOffset, checked(size * NodeSize), $"children of {path}");
                for (var childIndex = 0; childIndex < size; childIndex++)
                {
                    ReadNode(
                        reader,
                        childrenOffset + childIndex * NodeSize,
                        topLevelIndex,
                        name,
                        $"{path}/value/{childIndex}",
                        depth + 1,
                        entries);
                }

                break;

            case 3:
                var stringOffset = checked(nodeOffset + dataOffset);
                entries.Add(new SgoTextEntry(
                    topLevelIndex,
                    name,
                    path,
                    nodeOffset,
                    stringOffset,
                    size,
                    reader.ReadString(stringOffset)));
                break;

            case 1:
            case 2:
            case 4:
            case 5:
                break;

            default:
                throw new InvalidDataException($"Unknown SGO node type {nodeType} at {path}.");
        }
    }

    private sealed class SgoReader
    {
        private readonly byte[] _data;
        private readonly Encoding _encoding;

        public SgoReader(byte[] data)
        {
            _data = data;
            if (data.Length < 0x10)
            {
                throw new InvalidDataException("SGO header is truncated.");
            }

            if (data[0] == (byte)'S' && data[1] == (byte)'G' &&
                data[2] == (byte)'O' && data[3] == 0)
            {
                ByteOrder = SgoByteOrder.LittleEndian;
                _encoding = Encoding.Unicode;
            }
            else if (data[0] == 0 && data[1] == (byte)'O' &&
                     data[2] == (byte)'G' && data[3] == (byte)'S')
            {
                ByteOrder = SgoByteOrder.BigEndian;
                _encoding = Encoding.BigEndianUnicode;
            }
            else
            {
                throw new InvalidDataException("The decompressed data is not an SGO file.");
            }

            IsEdf2017Header = ReadInt32(0x08) == 0x10 && ReadInt32(0x04) != 0x0102;
        }

        public SgoByteOrder ByteOrder { get; }

        public bool IsEdf2017Header { get; }

        public int ReadInt32(int offset)
        {
            RequireRange(offset, 4, "32-bit value");
            var span = _data.AsSpan(offset, 4);
            return ByteOrder == SgoByteOrder.LittleEndian
                ? BinaryPrimitives.ReadInt32LittleEndian(span)
                : BinaryPrimitives.ReadInt32BigEndian(span);
        }

        public string ReadString(int offset)
        {
            RequireRange(offset, 2, "UTF-16 string");

            var end = offset;
            while (end + 1 < _data.Length && (_data[end] != 0 || _data[end + 1] != 0))
            {
                end += 2;
            }

            if (end + 1 >= _data.Length)
            {
                throw new InvalidDataException($"UTF-16 string at 0x{offset:X} is not terminated.");
            }

            return _encoding.GetString(_data, offset, end - offset);
        }

        public void RequireRange(int offset, int length, string purpose)
        {
            if (offset < 0 || length < 0 || offset > _data.Length - length)
            {
                throw new InvalidDataException(
                    $"SGO {purpose} range 0x{offset:X}+0x{length:X} is outside the file.");
            }
        }
    }
}
