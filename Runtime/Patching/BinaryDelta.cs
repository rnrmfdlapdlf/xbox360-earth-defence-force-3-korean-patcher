using System.IO.Compression;

namespace Edf3Koreanizer.Patching;

public static class BinaryDelta
{
    private const uint Magic = 0x31504445;
    private const int FormatVersion = 1;

    public static void Apply(string sourcePath, string deltaPath, string targetPath)
    {
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var deltaFile = new FileStream(deltaPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var gzip = new GZipStream(deltaFile, CompressionMode.Decompress, leaveOpen: false);
        using var reader = new BinaryReader(gzip);

        if (reader.ReadUInt32() != Magic || reader.ReadInt32() != FormatVersion)
        {
            throw new InvalidDataException("지원하지 않는 델타 파일입니다.");
        }

        var expectedSourceLength = reader.ReadInt64();
        var targetLength = reader.ReadInt64();
        var operationCount = reader.ReadInt32();
        if (source.Length != expectedSourceLength || targetLength < 0 || operationCount < 0)
        {
            throw new InvalidDataException("델타 파일의 크기 정보가 올바르지 않습니다.");
        }

        var parent = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        using var target = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[128 * 1024];

        for (var index = 0; index < operationCount; index++)
        {
            switch (reader.ReadByte())
            {
                case 0:
                    CopyRange(
                        source,
                        target,
                        reader.ReadInt64(),
                        reader.ReadInt32(),
                        buffer);
                    break;
                case 1:
                    CopyLiteral(reader, target, reader.ReadInt32(), buffer);
                    break;
                default:
                    throw new InvalidDataException("알 수 없는 델타 명령입니다.");
            }
        }

        if (target.Length != targetLength)
        {
            throw new InvalidDataException("델타 적용 결과의 크기가 일치하지 않습니다.");
        }
    }

    private static void CopyRange(
        FileStream source,
        FileStream target,
        long offset,
        int length,
        byte[] buffer)
    {
        if (offset < 0 || length < 0 || offset + length > source.Length)
        {
            throw new InvalidDataException("델타 복사 범위가 원본 파일을 벗어납니다.");
        }

        source.Position = offset;
        var remaining = length;
        while (remaining > 0)
        {
            var read = source.Read(buffer, 0, Math.Min(buffer.Length, remaining));
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            target.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static void CopyLiteral(
        BinaryReader reader,
        FileStream target,
        int length,
        byte[] buffer)
    {
        if (length < 0)
        {
            throw new InvalidDataException("델타 데이터 길이가 올바르지 않습니다.");
        }

        var remaining = length;
        while (remaining > 0)
        {
            var count = Math.Min(buffer.Length, remaining);
            var read = reader.Read(buffer, 0, count);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            target.Write(buffer, 0, read);
            remaining -= read;
        }
    }
}
