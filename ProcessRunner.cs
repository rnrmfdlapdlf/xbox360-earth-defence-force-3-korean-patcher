using System.Diagnostics;

namespace Edf3Koreanizer.Patcher;

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string CombinedOutput => StandardOutput + Environment.NewLine + StandardError;
}

internal static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = string.Join(" ", arguments.Select(QuoteArgument)),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
        };

        using var process = new Process { StartInfo = startInfo };
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => exited.TrySetResult(true);
        if (!process.Start())
        {
            throw new InvalidOperationException("외부 도구를 실행하지 못했습니다.");
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        if (process.HasExited)
        {
            exited.TrySetResult(true);
        }

        using (cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
                // The process may already have exited.
            }
        }))
        {
            await exited.Task;
        }

        cancellationToken.ThrowIfCancellationRequested();
        process.WaitForExit();

        return new ProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static string QuoteArgument(string argument)
    {
        if (argument.Length > 0 &&
            argument.All(character => !char.IsWhiteSpace(character) && character != '"'))
        {
            return argument;
        }

        var result = new System.Text.StringBuilder(argument.Length + 2);
        result.Append('"');
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                result.Append('\\', backslashes * 2 + 1);
                result.Append('"');
                backslashes = 0;
                continue;
            }

            result.Append('\\', backslashes);
            backslashes = 0;
            result.Append(character);
        }

        result.Append('\\', backslashes * 2);
        result.Append('"');
        return result.ToString();
    }
}
