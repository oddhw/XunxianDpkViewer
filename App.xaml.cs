using Microsoft.UI.Xaml;
using System.Diagnostics;
using XunxianDpkViewer.Core;

namespace XunxianDpkViewer;

public partial class App : Application
{
    private const string CanonicalExecutableName = "XunxianDpkViewer.exe";
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            System.Diagnostics.Debug.WriteLine(args.Exception);
            WriteCrashLog(args.Exception);
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            string[] commandLineArgs = Environment.GetCommandLineArgs();
            if (commandLineArgs.Any(argument => argument.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "XunxianDpkViewer");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "self-test.log"), SelfTest.Run());
                Exit();
                return;
            }
            if (TryRelaunchWithCanonicalExecutableName(commandLineArgs))
            {
                Exit();
                return;
            }

            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception exception)
        {
            WriteCrashLog(exception);
            throw;
        }
    }

    private static bool TryRelaunchWithCanonicalExecutableName(IReadOnlyList<string> commandLineArgs)
    {
        string? processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath)) return false;
        if (Path.GetFileName(processPath).Equals(CanonicalExecutableName, StringComparison.OrdinalIgnoreCase))
            return false;

        string launcherFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XunxianDpkViewer", "launcher");
        string canonicalPath = Path.Combine(launcherFolder, CanonicalExecutableName);

        try
        {
            Directory.CreateDirectory(launcherFolder);
            File.Copy(processPath, canonicalPath, overwrite: true);
        }
        catch
        {
            if (!File.Exists(canonicalPath)) return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = canonicalPath,
            WorkingDirectory = Directory.GetCurrentDirectory(),
            UseShellExecute = false
        };
        foreach (string argument in commandLineArgs.Skip(1))
            startInfo.ArgumentList.Add(argument);
        Process.Start(startInfo);
        return true;
    }

    private static void WriteCrashLog(Exception exception)
    {
        try
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XunxianDpkViewer");
            Directory.CreateDirectory(folder);
            string properties = string.Join("\n", exception.GetType().GetProperties()
                .Select(property =>
                {
                    try { return $"{property.Name}: {property.GetValue(exception)}"; }
                    catch { return $"{property.Name}: <unavailable>"; }
                }));
            File.WriteAllText(
                Path.Combine(folder, "crash.log"),
                $"{DateTimeOffset.Now:O}\nHRESULT: 0x{exception.HResult:X8}\n{properties}\n\n{exception}");
        }
        catch
        {
            // 不能让诊断日志覆盖原始异常。
        }
    }
}
