namespace Edf3Koreanizer.Patcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Developer smoke-test mode. Double-click launches only the WinForms UI.
        if (args.Length == 3 && args[0].Equals("--patch", StringComparison.OrdinalIgnoreCase))
        {
            RunPatchFromCommandLineAsync(args[1], args[2]).GetAwaiter().GetResult();
            return;
        }

        Application.Run(new MainForm());
    }

    private static async Task RunPatchFromCommandLineAsync(string isoPath, string xexToolPath)
    {
        var engine = new PatchEngine();
        var stageProgress = new InlineProgress<PatchEvent>(patchEvent =>
            Console.WriteLine($"{patchEvent.Stage}: {patchEvent.Message}"));
        var logProgress = new InlineProgress<string>(Console.WriteLine);
        var result = await engine.RunAsync(
            isoPath,
            xexToolPath,
            stageProgress,
            logProgress,
            CancellationToken.None);
        Console.WriteLine("OUTPUT=" + result.OutputIsoPath);
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _action;

        public InlineProgress(Action<T> action)
        {
            _action = action;
        }

        public void Report(T value) => _action(value);
    }
}
