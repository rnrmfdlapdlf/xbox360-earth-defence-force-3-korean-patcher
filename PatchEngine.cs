using Edf3Koreanizer.Core.Formats;
using Edf3Koreanizer.Patching;
using System.Web.Script.Serialization;

namespace Edf3Koreanizer.Patcher;

internal enum PatchStage
{
    IsoValidation,
    WorkFolder,
    Extraction,
    XexValidation,
    DeltaPatch,
    Repackaging
}

internal enum PatchStageState
{
    Waiting,
    Running,
    Completed,
    Failed
}

internal sealed record PatchEvent(
    PatchStage Stage,
    PatchStageState State,
    string Message,
    int? Current = null,
    int? Total = null);

internal sealed record ToolValidationResult(
    bool XexToolValid,
    bool ExisoValid,
    bool PackageValid,
    string XexToolMessage,
    string ExisoMessage,
    string PackageMessage)
{
    public bool IsValid => XexToolValid && ExisoValid && PackageValid;
}

internal sealed record PatchResult(string OutputIsoPath, string PatchVersion);

internal sealed class PatchEngine
{
    private readonly string _assetRoot;
    private readonly string _toolRoot;
    private readonly string _packageRoot;
    private readonly string _exisoPath;
    private readonly string _manifestPath;

    public PatchEngine()
    {
        _assetRoot = Path.Combine(AppContext.BaseDirectory, "Assets");
        _toolRoot = Path.Combine(_assetRoot, "Tools");
        _packageRoot = Path.Combine(_assetRoot, "Patch");
        _exisoPath = Path.Combine(_toolRoot, "exiso.exe");
        _manifestPath = Path.Combine(_packageRoot, "manifest.json");
    }

    public async Task<ToolValidationResult> ValidateEnvironmentAsync(
        string? xexToolPath,
        CancellationToken cancellationToken = default)
    {
        var xexValid = false;
        var exisoValid = false;
        var packageValid = false;
        var xexMessage = "XexTool v6.3을 찾을 수 없습니다.";
        var exisoMessage = "ISO 도구를 찾을 수 없습니다.";
        var packageMessage = "패치 데이터를 찾을 수 없습니다.";

        if (!string.IsNullOrWhiteSpace(xexToolPath) && File.Exists(xexToolPath))
        {
            try
            {
                var result = await ProcessRunner.RunAsync(
                    xexToolPath!,
                    Array.Empty<string>(),
                    cancellationToken);
                xexValid = ContainsIgnoreCase(result.CombinedOutput, "XexTool v6.3");
                xexMessage = xexValid
                    ? "XexTool v6.3 확인"
                    : "XexTool 버전이 6.3이 아닙니다.";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                xexMessage = "XexTool 실행 실패";
            }
        }

        if (File.Exists(_exisoPath))
        {
            try
            {
                var result = await ProcessRunner.RunAsync(
                    _exisoPath,
                    new[] { "-v" },
                    cancellationToken);
                exisoValid = result.ExitCode == 0 &&
                             ContainsIgnoreCase(result.CombinedOutput, "extract-xiso");
                exisoMessage = exisoValid ? "ISO 도구 확인" : "ISO 도구 확인 실패";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                exisoMessage = "ISO 도구 실행 실패";
            }
        }

        try
        {
            var manifest = await LoadManifestAsync(cancellationToken);
            var patchFileCount = GetPatchFileCount(manifest);
            packageValid = manifest.FormatVersion == 1 && patchFileCount > 0;
            packageMessage = packageValid
                ? $"전체 패치 확인 ({patchFileCount:N0}개 파일)"
                : "패치 데이터가 올바르지 않습니다.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            packageMessage = "패치 데이터 확인 실패";
        }

        return new ToolValidationResult(
            xexValid,
            exisoValid,
            packageValid,
            xexMessage,
            exisoMessage,
            packageMessage);
    }

    public async Task<PatchResult> RunAsync(
        string isoPath,
        string xexToolPath,
        IProgress<PatchEvent> progress,
        IProgress<string> log,
        CancellationToken cancellationToken)
    {
        var environment = await ValidateEnvironmentAsync(xexToolPath, cancellationToken);
        if (!environment.IsValid)
        {
            throw new InvalidOperationException("필수 도구 또는 패치 데이터 검증에 실패했습니다.");
        }

        var manifest = await LoadManifestAsync(cancellationToken);
        var inputIsoPath = Path.GetFullPath(isoPath);
        var inputDirectory = Path.GetDirectoryName(inputIsoPath)
                             ?? throw new InvalidOperationException("ISO 폴더를 확인할 수 없습니다.");
        var workContainer = Path.Combine(inputDirectory, ".edf3-koreanizer-work");
        var workRoot = Path.Combine(workContainer, Guid.NewGuid().ToString("N"));
        var extractedRoot = Path.Combine(workRoot, "disc");
        var outputIsoPath = CreateUniqueOutputPath(inputIsoPath);
        var outputStarted = false;

        try
        {
            Report(progress, PatchStage.IsoValidation, PatchStageState.Running, "ISO 형식 확인 중");
            ValidateInputIso(inputIsoPath);
            var listResult = await ProcessRunner.RunAsync(
                _exisoPath,
                new[] { "-l", inputIsoPath },
                cancellationToken);
            if (listResult.ExitCode != 0)
            {
                throw new InvalidDataException("지원되는 Xbox 360 ISO가 아닙니다.");
            }

            Report(progress, PatchStage.IsoValidation, PatchStageState.Completed, "ISO 형식 확인 완료");
            log.Report("ISO 컨테이너를 확인했습니다.");

            Report(progress, PatchStage.WorkFolder, PatchStageState.Running, "작업 폴더 생성 중");
            Directory.CreateDirectory(extractedRoot);
            Report(progress, PatchStage.WorkFolder, PatchStageState.Completed, "작업 폴더 준비 완료");
            log.Report("임시 작업 폴더를 준비했습니다.");

            Report(progress, PatchStage.Extraction, PatchStageState.Running, "원본 파일 추출 중");
            var extractResult = await ProcessRunner.RunAsync(
                _exisoPath,
                new[] { "-q", "-d", extractedRoot, "-x", inputIsoPath },
                cancellationToken);
            if (extractResult.ExitCode != 0)
            {
                throw new InvalidDataException("ISO 추출에 실패했습니다.");
            }

            Report(progress, PatchStage.Extraction, PatchStageState.Completed, "XISO 추출 완료");
            log.Report("ISO에서 파일을 추출했습니다.");

            Report(progress, PatchStage.XexValidation, PatchStageState.Running, "XEX 식별 중");
            await ValidateGameXexAsync(
                extractedRoot,
                xexToolPath,
                manifest.TitleId,
                cancellationToken);
            Report(progress, PatchStage.XexValidation, PatchStageState.Completed, "일본판 XEX 확인 완료");
            log.Report($"XexTool v{manifest.RequiredXexToolVersion}로 타이틀 ID를 확인했습니다.");

            Report(
                progress,
                PatchStage.DeltaPatch,
                PatchStageState.Running,
                "원본 해시 확인 중",
                0,
                GetPatchFileCount(manifest));
            var targets = FindPatchTargets(extractedRoot, manifest);
            await VerifyAndApplyPatchesAsync(
                targets,
                manifest,
                progress,
                log,
                cancellationToken);
            var patchFileCount = GetPatchFileCount(manifest);
            Report(
                progress,
                PatchStage.DeltaPatch,
                PatchStageState.Completed,
                $"전체 패치 {patchFileCount:N0}개 적용 완료",
                patchFileCount,
                patchFileCount);

            Report(progress, PatchStage.Repackaging, PatchStageState.Running, "ISO 재패킹 중");
            outputStarted = true;
            var createResult = await ProcessRunner.RunAsync(
                _exisoPath,
                new[] { "-q", "-c", extractedRoot, outputIsoPath },
                cancellationToken);
            if (createResult.ExitCode != 0 || !File.Exists(outputIsoPath))
            {
                throw new IOException("ISO 재패킹에 실패했습니다.");
            }

            var outputLength = new FileInfo(outputIsoPath).Length;
            if (outputLength < 1024 * 1024)
            {
                throw new IOException("생성된 ISO의 크기가 올바르지 않습니다.");
            }

            Report(progress, PatchStage.Repackaging, PatchStageState.Completed, "ISO 생성 완료");
            log.Report($"완료: {outputIsoPath}");
            return new PatchResult(outputIsoPath, manifest.PatchVersion);
        }
        catch
        {
            if (outputStarted)
            {
                TryDeleteCreatedOutput(outputIsoPath);
            }

            throw;
        }
        finally
        {
            TryDeleteWorkDirectory(workRoot, workContainer);
        }
    }

    private async Task<PatchManifest> LoadManifestAsync(CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            _manifestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, 64 * 1024);
        cancellationToken.ThrowIfCancellationRequested();
        var json = await reader.ReadToEndAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var serializer = new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue,
            RecursionLimit = 256
        };
        var manifest = serializer.Deserialize<PatchManifest>(json);
        return manifest ?? throw new InvalidDataException("패치 매니페스트가 비어 있습니다.");
    }

    private async Task ValidateGameXexAsync(
        string extractedRoot,
        string xexToolPath,
        string expectedTitleId,
        CancellationToken cancellationToken)
    {
        var candidates = Directory
            .EnumerateFiles(extractedRoot, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path).Equals(".xex", StringComparison.OrdinalIgnoreCase));

        foreach (var candidate in candidates)
        {
            var result = await ProcessRunner.RunAsync(
                xexToolPath,
                new[] { "-x", "t", "-x", "r", candidate },
                cancellationToken);
            if (result.ExitCode == 0 &&
                ContainsIgnoreCase(result.CombinedOutput, expectedTitleId))
            {
                return;
            }
        }

        throw new InvalidDataException("지원되는 지구방위군 3 일본판 XEX를 찾지 못했습니다.");
    }

    private static Dictionary<string, string> FindPatchTargets(
        string extractedRoot,
        PatchManifest manifest)
    {
        var wanted = new HashSet<string>(
            EnumeratePathIds(manifest),
            StringComparer.OrdinalIgnoreCase);
        var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.EnumerateFiles(extractedRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = GetRelativePath(extractedRoot, path);
            var pathId = PatchIdentity.FromRelativePath(relativePath);
            if (wanted.Contains(pathId))
            {
                if (targets.ContainsKey(pathId))
                {
                    throw new InvalidDataException("중복된 패치 대상이 발견되었습니다.");
                }

                targets.Add(pathId, path);
            }
        }

        if (targets.Count != wanted.Count)
        {
            throw new InvalidDataException(
                $"원본 파일 구성이 일치하지 않습니다. ({targets.Count}/{wanted.Count})");
        }

        return targets;
    }

    private async Task VerifyAndApplyPatchesAsync(
        Dictionary<string, string> targets,
        PatchManifest manifest,
        IProgress<PatchEvent> progress,
        IProgress<string> log,
        CancellationToken cancellationToken)
    {
        var total = GetPatchFileCount(manifest);
        var completed = 0;

        foreach (var entry in manifest.TextFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetPath = targets[entry.PathId];
            await VerifySourceAsync(
                targetPath,
                entry.SourceLength,
                entry.SourceSha256,
                entry.PathId,
                cancellationToken);

            var source = await ReadAllBytesAsync(targetPath, cancellationToken);
            var document = SgoTextExtractor.Extract(source);
            var replacements = entry.Replacements.ToDictionary(
                replacement => replacement.NodeId,
                StringComparer.OrdinalIgnoreCase);
            var translations = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var sourceEntry in document.Entries)
            {
                var nodeId = PatchIdentity.FromNodePath(sourceEntry.NodePath);
                if (!replacements.TryGetValue(nodeId, out var replacement))
                {
                    continue;
                }

                var sourceTextHash = PatchIdentity.FromSourceText(sourceEntry.Source);
                if (!sourceTextHash.Equals(replacement.SourceSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"원문 해시 검증 실패 (ID {ShortId(entry.PathId)})");
                }

                translations[sourceEntry.NodePath] = replacement.Target;
            }

            if (translations.Count != entry.Replacements.Count)
            {
                throw new InvalidDataException($"텍스트 항목 검증 실패 (ID {ShortId(entry.PathId)})");
            }

            var result = SgoTextPatcher.Patch(source, translations).Data;
            await ReplaceWithVerifiedDataAsync(
                targetPath,
                result,
                entry.TargetLength,
                entry.TargetSha256,
                entry.PathId,
                cancellationToken);

            completed++;
            ReportPatchProgress(progress, log, completed, total, "텍스트 적용");
        }

        foreach (var entry in manifest.FontMetadataFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetPath = targets[entry.PathId];
            await VerifySourceAsync(
                targetPath,
                entry.SourceLength,
                entry.SourceSha256,
                entry.PathId,
                cancellationToken);

            var source = await ReadAllBytesAsync(targetPath, cancellationToken);
            var result = FontMetadataPatch.Apply(source, entry);
            await ReplaceWithVerifiedDataAsync(
                targetPath,
                result,
                entry.TargetLength,
                entry.TargetSha256,
                entry.PathId,
                cancellationToken);

            completed++;
            ReportPatchProgress(progress, log, completed, total, "글꼴 메타데이터 적용");
        }

        foreach (var entry in manifest.ImageDeltas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetPath = targets[entry.PathId];
            await VerifySourceAsync(
                targetPath,
                entry.SourceLength,
                entry.SourceSha256,
                entry.PathId,
                cancellationToken);

            var deltaPath = ResolvePackagePath(entry.DeltaFile);
            var temporaryPath = targetPath + ".edf3ko.tmp";
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            await Task.Run(
                () => BinaryDelta.Apply(targetPath, deltaPath, temporaryPath),
                cancellationToken);

            var targetHash = await HashUtility.ComputeSha256Async(temporaryPath, cancellationToken);
            var targetInfo = new FileInfo(temporaryPath);
            if (targetInfo.Length != entry.TargetLength ||
                !targetHash.Equals(entry.TargetSha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temporaryPath);
                throw new InvalidDataException($"패치 결과 검증 실패 (ID {ShortId(entry.PathId)})");
            }

            File.Replace(temporaryPath, targetPath, null);
            completed++;
            ReportPatchProgress(progress, log, completed, total, "이미지 델타 적용");
        }
    }

    private static async Task VerifySourceAsync(
        string path,
        long expectedLength,
        string expectedHash,
        string pathId,
        CancellationToken cancellationToken)
    {
        if (new FileInfo(path).Length != expectedLength)
        {
            throw new InvalidDataException($"원본 크기 검증 실패 (ID {ShortId(pathId)})");
        }

        var sourceHash = await HashUtility.ComputeSha256Async(path, cancellationToken);
        if (!sourceHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"원본 해시 검증 실패 (ID {ShortId(pathId)})");
        }
    }

    private static async Task ReplaceWithVerifiedDataAsync(
        string targetPath,
        byte[] data,
        long expectedLength,
        string expectedHash,
        string pathId,
        CancellationToken cancellationToken)
    {
        var temporaryPath = targetPath + ".edf3ko.tmp";
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }

        await WriteAllBytesAsync(temporaryPath, data, cancellationToken);
        var actualHash = await HashUtility.ComputeSha256Async(temporaryPath, cancellationToken);
        if (data.LongLength != expectedLength ||
            !actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(temporaryPath);
            throw new InvalidDataException($"패치 결과 검증 실패 (ID {ShortId(pathId)})");
        }

        File.Replace(temporaryPath, targetPath, null);
    }

    private static Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return File.ReadAllBytes(path);
        }, cancellationToken);

    private static Task WriteAllBytesAsync(
        string path,
        byte[] data,
        CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllBytes(path, data);
        }, cancellationToken);

    private static string GetRelativePath(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        var prefix = fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("패치 대상이 추출 폴더 밖에 있습니다.");
        }

        return fullPath.Substring(prefix.Length);
    }

    private static string ShortId(string value) =>
        value.Length <= 8 ? value : value.Substring(0, 8);

    private static bool ContainsIgnoreCase(string source, string value) =>
        source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

    private static void ReportPatchProgress(
        IProgress<PatchEvent> progress,
        IProgress<string> log,
        int completed,
        int total,
        string operation)
    {
        Report(
            progress,
            PatchStage.DeltaPatch,
            PatchStageState.Running,
            $"{operation} 중 ({completed:N0}/{total:N0})",
            completed,
            total);

        if (completed == 1 || completed % 10 == 0 || completed == total)
        {
            log.Report($"패치 적용 {completed:N0}/{total:N0}");
        }
    }

    private static IEnumerable<string> EnumeratePathIds(PatchManifest manifest)
    {
        foreach (var entry in manifest.TextFiles)
        {
            yield return entry.PathId;
        }

        foreach (var entry in manifest.FontMetadataFiles)
        {
            yield return entry.PathId;
        }

        foreach (var entry in manifest.ImageDeltas)
        {
            yield return entry.PathId;
        }
    }

    private static int GetPatchFileCount(PatchManifest manifest) =>
        manifest.TextFiles.Count + manifest.FontMetadataFiles.Count + manifest.ImageDeltas.Count;

    private string ResolvePackagePath(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_packageRoot, relativePath));
        var root = Path.GetFullPath(_packageRoot) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
        {
            throw new InvalidDataException("패치 델타 파일이 없거나 경로가 올바르지 않습니다.");
        }

        return fullPath;
    }

    private static void ValidateInputIso(string isoPath)
    {
        if (!File.Exists(isoPath) ||
            !Path.GetExtension(isoPath).Equals(".iso", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("일본판 원본 ISO를 선택해 주세요.");
        }

        if (new FileInfo(isoPath).Length < 1024 * 1024)
        {
            throw new InvalidDataException("ISO 파일의 크기가 올바르지 않습니다.");
        }
    }

    private static string CreateUniqueOutputPath(string inputIsoPath)
    {
        var directory = Path.GetDirectoryName(inputIsoPath)!;
        var stem = Path.GetFileNameWithoutExtension(inputIsoPath);
        var baseName = $"{stem}_repacked";
        var candidate = Path.Combine(directory, baseName + ".iso");
        var suffix = 2;

        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{baseName}_{suffix}.iso");
            suffix++;
        }

        return candidate;
    }

    private static void TryDeleteCreatedOutput(string outputPath)
    {
        try
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
        catch
        {
            // A partial output is reported in the error log if it cannot be removed.
        }
    }

    private static void TryDeleteWorkDirectory(string workRoot, string workContainer)
    {
        try
        {
            var fullWorkRoot = Path.GetFullPath(workRoot);
            var fullContainer = Path.GetFullPath(workContainer);
            if (fullWorkRoot.StartsWith(
                    fullContainer + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(fullWorkRoot))
            {
                Directory.Delete(fullWorkRoot, recursive: true);
            }

            if (Directory.Exists(fullContainer) &&
                !Directory.EnumerateFileSystemEntries(fullContainer).Any())
            {
                Directory.Delete(fullContainer);
            }
        }
        catch
        {
            // Cleanup is best effort; patch result validation has already completed.
        }
    }

    private static void Report(
        IProgress<PatchEvent> progress,
        PatchStage stage,
        PatchStageState state,
        string message,
        int? current = null,
        int? total = null)
    {
        progress.Report(new PatchEvent(stage, state, message, current, total));
    }
}
