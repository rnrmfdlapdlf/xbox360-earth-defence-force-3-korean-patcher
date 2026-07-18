using Edf3Koreanizer.Core.Formats;

namespace Edf3Koreanizer.Patching;

public static class FontMetadataPatch
{
    public static byte[] Apply(byte[] sourceRaw, FontMetadataPatchFile patch)
    {
        var sourceCompressed = SgslCodec.IsSgsl(sourceRaw);
        if (sourceCompressed != patch.IsSgslCompressed)
        {
            throw new InvalidDataException("ABC 압축 방식이 패치 데이터와 일치하지 않습니다.");
        }

        var source = sourceCompressed ? SgslCodec.Decompress(sourceRaw) : sourceRaw;
        if (patch.TargetDecompressedLength < source.Length)
        {
            throw new InvalidDataException("ABC 패치 결과의 크기가 원본보다 작습니다.");
        }

        var target = new byte[patch.TargetDecompressedLength];
        Buffer.BlockCopy(source, 0, target, 0, source.Length);

        foreach (var edit in patch.Edits)
        {
            var data = Convert.FromBase64String(edit.Data);
            if (edit.Offset < 0 || edit.Offset > target.Length - data.Length)
            {
                throw new InvalidDataException("ABC 패치 범위가 결과 버퍼를 벗어납니다.");
            }

            Buffer.BlockCopy(data, 0, target, edit.Offset, data.Length);
        }

        return sourceCompressed ? SgslCodec.CompressLiteralOnly(target) : target;
    }
}
