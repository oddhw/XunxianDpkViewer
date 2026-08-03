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
            if (UpdateInstaller.IsApplyMode(commandLineArgs))
            {
                UpdateInstaller.ApplyUpdateAndRestart(commandLineArgs);
                Exit();
                return;
            }
            if (TryRelaunchWithCanonicalExecutableName(commandLineArgs))
            {
                Exit();
                return;
            }
            string? verifyManifestArgument = commandLineArgs.FirstOrDefault(argument =>
                argument.StartsWith("--verify-update-manifest=", StringComparison.OrdinalIgnoreCase));
            if (verifyManifestArgument is not null)
            {
                string manifestPath = verifyManifestArgument[(verifyManifestArgument.IndexOf('=') + 1)..].Trim('"');
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "XunxianDpkViewer");
                Directory.CreateDirectory(folder);
                File.WriteAllText(
                    Path.Combine(folder, "update-manifest-test.log"),
                    UpdateService.ValidateManifestFile(manifestPath) ? "VALID" : "INVALID");
                Exit();
                return;
            }
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
            UpdateInstaller.ScheduleCleanup(commandLineArgs);
            var mainWindow = new MainWindow();
            _window = mainWindow;
            mainWindow.Activate();
            if (UpdateInstaller.IsRecoveryRestart(commandLineArgs))
            {
                mainWindow.DispatcherQueue.TryEnqueue(async () =>
                {
                    await Task.Delay(250);
                    await mainWindow.ShowUpdateRecoveryNoticeAsync();
                });
            }
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

        try
        {
            string canonicalPath = UpdateInstaller.PrepareCanonicalLauncher(processPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = canonicalPath,
                WorkingDirectory = Directory.GetCurrentDirectory(),
                UseShellExecute = false
            };
            foreach (string argument in commandLineArgs.Skip(1))
                startInfo.ArgumentList.Add(argument);
            if (!UpdateInstaller.HasLauncherSourceArgument(commandLineArgs))
                startInfo.ArgumentList.Add(UpdateInstaller.BuildLauncherSourceArgument(processPath));
            _ = Process.Start(startInfo) ??
                throw new InvalidOperationException("无法启动标准名称程序副本。");
            return true;
        }
        catch (Exception exception)
        {
            WriteCrashLog(exception);
            return false;
        }
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
