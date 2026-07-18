namespace Edf3Koreanizer.Patching;

public sealed class PatchManifest
{
    public int FormatVersion { get; set; } = 1;

    public string PatchVersion { get; set; } = string.Empty;

    public string TitleId { get; set; } = string.Empty;

    public string RequiredXexToolVersion { get; set; } = "6.3";

    public List<TextPatchFile> TextFiles { get; set; } = new();

    public List<FontMetadataPatchFile> FontMetadataFiles { get; set; } = new();

    public List<AssetDeltaEntry> ImageDeltas { get; set; } = new();
}

public sealed class AssetDeltaEntry
{
    public string PathId { get; set; } = string.Empty;

    public long SourceLength { get; set; }

    public string SourceSha256 { get; set; } = string.Empty;

    public long TargetLength { get; set; }

    public string TargetSha256 { get; set; } = string.Empty;

    public string DeltaFile { get; set; } = string.Empty;
}

public sealed class TextPatchFile
{
    public string PathId { get; set; } = string.Empty;

    public long SourceLength { get; set; }

    public string SourceSha256 { get; set; } = string.Empty;

    public long TargetLength { get; set; }

    public string TargetSha256 { get; set; } = string.Empty;

    public List<TextReplacement> Replacements { get; set; } = new();
}

public sealed class TextReplacement
{
    public string NodeId { get; set; } = string.Empty;

    public string SourceSha256 { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;
}

public sealed class FontMetadataPatchFile
{
    public string PathId { get; set; } = string.Empty;

    public long SourceLength { get; set; }

    public string SourceSha256 { get; set; } = string.Empty;

    public long TargetLength { get; set; }

    public string TargetSha256 { get; set; } = string.Empty;

    public bool IsSgslCompressed { get; set; }

    public int TargetDecompressedLength { get; set; }

    public List<FontMetadataEdit> Edits { get; set; } = new();
}

public sealed class FontMetadataEdit
{
    public int Offset { get; set; }

    public string Data { get; set; } = string.Empty;
}
