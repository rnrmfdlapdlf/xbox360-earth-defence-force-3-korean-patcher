using System.Buffers.Binary;
using System.Text;

namespace Edf3Koreanizer.Core.Formats;

public sealed record SgoPatchResult(
    byte[] Data,
    int PatchedStringCount,
    int DecompressedSize,
    bool IsSgslCompressed);

/// <summary>
/// Repoints translated EDF2017 string nodes to newly appended UTF-16 data.
/// Existing blocks remain at their original offsets, so unknown SGO data is
/// preserved byte-for-byte.
/// </summary>
public static class SgoTextPatcher
{
    public static SgoPatchResult Patch(
        ReadOnlySpan<byte> input,
        IReadOnlyDictionary<string, string> translationsByNodePath)
    {
        var isCompressed = SgslCodec.IsSgsl(input);
        var original = isCompressed ? SgslCodec.Decompress(input) : input.ToArray();
        var document = SgoTextExtractor.Extract(original);
        var byteOrder = document.ByteOrder == "LE"
            ? SgoByteOrder.LittleEndian
            : SgoByteOrder.BigEndian;
        var encoding = byteOrder == SgoByteOrder.LittleEndian
            ? Encoding.Unicode
            : Encoding.BigEndianUnicode;

        using var output = new MemoryStream(original.Length + EstimateAdditionalSize(translationsByNodePath));
        output.Write(original, 0, original.Length);

        var patchedCount = 0;
        foreach (var entry in document.Entries)
        {
            if (!translationsByNodePath.TryGetValue(entry.NodePath, out var target) ||
                string.IsNullOrEmpty(target))
            {
                continue;
            }

            Align(output, 2);
            var stringOffset = checked((int)output.Position);
            var encoded = encoding.GetBytes(target + '\0');
            output.Write(encoded, 0, encoded.Length);

            var relativePointer = checked(stringOffset - entry.NodeOffset);
            WriteInt32(output, entry.NodeOffset + 4, target.Length, byteOrder);
            WriteInt32(output, entry.NodeOffset + 8, relativePointer, byteOrder);
            patchedCount++;
        }

        Align(output, 0x10);
        var decompressed = output.ToArray();
        var result = isCompressed
            ? SgslCodec.CompressLiteralOnly(decompressed)
            : decompressed;

        return new SgoPatchResult(result, patchedCount, decompressed.Length, isCompressed);
    }

    private static int EstimateAdditionalSize(IReadOnlyDictionary<string, string> translations) =>
        translations.Values.Sum(value => checked((value.Length + 1) * 2 + 2));

    private static void Align(Stream stream, int alignment)
    {
        while (stream.Position % alignment != 0)
        {
            stream.WriteByte(0);
        }
    }

    private static void WriteInt32(
        MemoryStream stream,
        int offset,
        int value,
        SgoByteOrder byteOrder)
    {
        var returnPosition = stream.Position;
        stream.Position = offset;

        Span<byte> bytes = stackalloc byte[4];
        if (byteOrder == SgoByteOrder.LittleEndian)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        }
        else
        {
            BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        }

        var buffer = bytes.ToArray();
        stream.Write(buffer, 0, buffer.Length);
        stream.Position = returnPosition;
    }
}
