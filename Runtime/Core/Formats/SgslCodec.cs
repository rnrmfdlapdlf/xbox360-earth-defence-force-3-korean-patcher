using System.Buffers.Binary;

namespace Edf3Koreanizer.Core.Formats;

/// <summary>
/// Decompresses the SGSL wrapper used by Earth Defense Force 2017 / EDF3.
/// SGSL is an LZSS variant with a 4096-byte history buffer whose cursor
/// advances by 16 physical slots for every logical byte.
/// </summary>
public static class SgslCodec
{
    private const int HeaderSize = 8;
    private const int HistorySize = 4096;
    private const int InitialHistoryPosition = 3823;

    public static bool IsSgsl(ReadOnlySpan<byte> data) =>
        data.Length >= 4 &&
        data[0] == (byte)'S' &&
        data[1] == (byte)'G' &&
        data[2] == (byte)'S' &&
        data[3] == (byte)'L';

    public static byte[] Decompress(ReadOnlySpan<byte> input)
    {
        if (input.Length < HeaderSize)
        {
            throw new InvalidDataException("SGSL header is truncated.");
        }

        if (!IsSgsl(input))
        {
            throw new InvalidDataException("The input does not start with an SGSL header.");
        }

        var outputSize = BinaryPrimitives.ReadInt32LittleEndian(input.Slice(4, 4));
        if (outputSize < 0)
        {
            throw new InvalidDataException($"Invalid SGSL output size: {outputSize}.");
        }

        var output = new byte[outputSize];
        var history = new byte[HistorySize];
        var inputPosition = HeaderSize;
        var outputPosition = 0;
        var historyPosition = InitialHistoryPosition;
        byte flags = 0;
        var availableFlagBits = 0;

        while (outputPosition < output.Length)
        {
            if (availableFlagBits == 0)
            {
                flags = ReadByte(input, ref inputPosition, "flag byte");
                availableFlagBits = 8;
            }

            if ((flags & 1) != 0)
            {
                var value = ReadByte(input, ref inputPosition, "literal byte");
                WriteByte(value);
            }
            else
            {
                if (inputPosition + 2 > input.Length)
                {
                    throw new InvalidDataException(
                        $"SGSL back-reference is truncated at input offset 0x{inputPosition:X}.");
                }

                var token = BinaryPrimitives.ReadUInt16BigEndian(input.Slice(inputPosition, 2));
                inputPosition += 2;

                var copyPosition = token >> 4;
                var copyLength = (token & 0x0F) + 3;

                for (var index = 0; index < copyLength && outputPosition < output.Length; index++)
                {
                    var value = history[copyPosition];
                    WriteByte(value);
                    copyPosition = AdvanceHistoryPosition(copyPosition);
                }
            }

            flags >>= 1;
            availableFlagBits--;
        }

        return output;

        void WriteByte(byte value)
        {
            output[outputPosition++] = value;
            history[historyPosition] = value;
            historyPosition = AdvanceHistoryPosition(historyPosition);
        }
    }

    /// <summary>
    /// Produces a valid SGSL stream using literal blocks only. It is larger than
    /// an optimized stream, but deterministic and ideal for safe first-pass
    /// patching. A match-finding encoder can replace this without changing the
    /// file-format API.
    /// </summary>
    public static byte[] CompressLiteralOnly(ReadOnlySpan<byte> input)
    {
        using var output = new MemoryStream(HeaderSize + input.Length + (input.Length + 7) / 8);
        output.WriteByte((byte)'S');
        output.WriteByte((byte)'G');
        output.WriteByte((byte)'S');
        output.WriteByte((byte)'L');

        Span<byte> sizeBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(sizeBytes, input.Length);
        var sizeBuffer = sizeBytes.ToArray();
        output.Write(sizeBuffer, 0, sizeBuffer.Length);

        var position = 0;
        while (position < input.Length)
        {
            var literalCount = Math.Min(8, input.Length - position);
            output.WriteByte((byte)((1 << literalCount) - 1));
            var literal = input.Slice(position, literalCount).ToArray();
            output.Write(literal, 0, literal.Length);
            position += literalCount;
        }

        return output.ToArray();
    }

    public static byte[] DecompressFile(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("Input path is required.", nameof(inputPath));
        }
        return Decompress(File.ReadAllBytes(inputPath));
    }

    private static byte ReadByte(ReadOnlySpan<byte> input, ref int position, string purpose)
    {
        if ((uint)position >= (uint)input.Length)
        {
            throw new InvalidDataException(
                $"SGSL {purpose} is missing at input offset 0x{position:X}.");
        }

        return input[position++];
    }

    private static int AdvanceHistoryPosition(int position)
    {
        var next = position + 16;

        if (next == 4111)
        {
            return 0;
        }

        if (next >= HistorySize)
        {
            return next - 4095;
        }

        return next;
    }
}
