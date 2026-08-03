using System.Diagnostics;
using System.Security.Cryptography;

namespace XunxianDpkViewer.Core;

public static class UpdateInstaller
{
    private const string ApplyArgument = "--apply-update";
    private const string TargetArgument = "--update-target=";
    private const string WaitPidArgument = "--update-wait-pid=";
    private const string CleanupArgument = "--update-cleanup=";
    private const string BackupArgument = "--update-backup=";
    private const string LauncherSourceArgument = "--launcher-source=";

    public static bool IsApplyMode(IReadOnlyList<string> arguments) =>
        arguments.Any(argument => argument.Equals(ApplyArgument, StringComparison.OrdinalIgnoreCase));

    public static string ResolveUpdateTargetPath(IReadOnlyList<string> arguments)
    {
        string? launcherSource = ReadValue(arguments, LauncherSourceArgument);
        string? processPath = Environment.ProcessPath;
        string target = !string.IsNullOrWhiteSpace(launcherSource) ? launcherSource : processPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(target))
            throw new InvalidOperationException("无法确定当前程序文件的位置。");
        return Path.GetFullPath(target);
    }

    public static void StartApplyUpdate(string downloadedExecutable, string targetExecutable)
    {
        string source = Path.GetFullPath(downloadedExecutable);
        string target = Path.GetFullPath(targetExecutable);
        if (!File.Exists(source)) throw new FileNotFoundException("更新文件不存在。", source);
        RetryFileOperation(
            () =>
            {
                using FileStream stream = new(
                    source,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                if (stream.Length == 0) throw new InvalidDataException("更新文件为空。");
            },
            "更新文件仍被占用，无法启动安装程序。");

        var startInfo = new ProcessStartInfo
        {
            FileName = source,
            WorkingDirectory = Path.GetDirectoryName(source) ?? Directory.GetCurrentDirectory(),
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add(ApplyArgument);
        startInfo.ArgumentList.Add(TargetArgument + target);
        startInfo.ArgumentList.Add(WaitPidArgument + Environment.ProcessId);
        startInfo.ArgumentList.Add(CleanupArgument + (Path.GetDirectoryName(source) ?? string.Empty));

        if (!CanWriteDirectory(Path.GetDirectoryName(target)))
            startInfo.Verb = "runas";

        Process.Start(startInfo);
    }

    public static void ApplyUpdateAndRestart(IReadOnlyList<string> arguments)
    {
        string? processPath = Environment.ProcessPath;
        string? targetValue = ReadValue(arguments, TargetArgument);
        if (string.IsNullOrWhiteSpace(processPath) || string.IsNullOrWhiteSpace(targetValue))
            return;

        string source = Path.GetFullPath(processPath);
        string target = Path.GetFullPath(targetValue);
        string targetFolder = Path.GetDirectoryName(target) ??
                              throw new InvalidOperationException("更新目标目录无效。");
        string stagedPath = target + ".update-new";
        string backupPath = target + ".update-backup";

        try
        {
            WriteUpdateLog($"开始更新：{source} -> {target}");
            if (int.TryParse(ReadValue(arguments, WaitPidArgument), out int processId))
                WaitForProcessExit(processId);

            Directory.CreateDirectory(targetFolder);
            TryDelete(stagedPath);
            RetryFileOperation(
                () => File.Copy(source, stagedPath, overwrite: true),
                "复制更新文件失败。");
            RetryFileOperation(
                () => VerifyCopiedFile(source, stagedPath),
                "校验更新文件失败。");

            if (File.Exists(target))
            {
                RetryFileOperation(
                    () => File.Copy(target, backupPath, overwrite: true),
                    "备份旧版本失败。");
            }
            RetryFileOperation(
                () => File.Move(stagedPath, target, overwrite: true),
                "替换旧版本失败。");

            var startInfo = new ProcessStartInfo
            {
                FileName = target,
                WorkingDirectory = targetFolder,
                UseShellExecute = false
            };
            string? cleanupFolder = ReadValue(arguments, CleanupArgument);
            if (!string.IsNullOrWhiteSpace(cleanupFolder))
                startInfo.ArgumentList.Add(CleanupArgument + cleanupFolder);
            startInfo.ArgumentList.Add(BackupArgument + backupPath);
            Process.Start(startInfo);
            WriteUpdateLog("更新完成并已启动新版本。");
        }
        catch (Exception exception)
        {
            WriteUpdateLog($"更新失败：{exception}");
            TryRestoreBackup(target, backupPath);
        }
        finally
        {
            TryDelete(stagedPath);
        }
    }

    public static void ScheduleCleanup(IReadOnlyList<string> arguments)
    {
        string? cleanupFolder = ReadValue(arguments, CleanupArgument);
        string? backupPath = ReadValue(arguments, BackupArgument);
        if (string.IsNullOrWhiteSpace(cleanupFolder) && string.IsNullOrWhiteSpace(backupPath)) return;

        _ = Task.Run(async () =>
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt == 0 ? 4 : 2));
                try
                {
                    if (!string.IsNullOrWhiteSpace(backupPath) &&
                        backupPath.EndsWith(".update-backup", StringComparison.OrdinalIgnoreCase))
                    {
                        TryDelete(Path.GetFullPath(backupPath));
                    }

                    if (!string.IsNullOrWhiteSpace(cleanupFolder))
                    {
                        string fullPath = Path.GetFullPath(cleanupFolder);
                        string updateRoot = Path.GetFullPath(Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "XunxianDpkViewer",
                            "updates"));
                        if (fullPath.StartsWith(updateRoot, StringComparison.OrdinalIgnoreCase) &&
                            Directory.Exists(fullPath))
                        {
                            Directory.Delete(fullPath, recursive: true);
                        }
                    }
                    return;
                }
                catch
                {
                    // The updater process may still hold its own executable briefly.
                }
            }
        });
    }

    public static string BuildLauncherSourceArgument(string sourcePath) =>
        LauncherSourceArgument + Path.GetFullPath(sourcePath);

    public static bool HasLauncherSourceArgument(IReadOnlyList<string> arguments) =>
        ReadValue(arguments, LauncherSourceArgument) is not null;

    private static string? ReadValue(IReadOnlyList<string> arguments, string prefix)
    {
        string? argument = arguments.FirstOrDefault(item =>
            item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return argument is null ? null : argument[prefix.Length..].Trim().Trim('"');
    }

    private static void WaitForProcessExit(int processId)
    {
        if (processId <= 0 || processId == Environment.ProcessId) return;
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (!process.WaitForExit((int)TimeSpan.FromMinutes(2).TotalMilliseconds))
                throw new TimeoutException("等待旧版本退出超时。");
        }
        catch (ArgumentException)
        {
            // The previous process has already exited.
        }
    }

    private static bool CanWriteDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;
        try
        {
            Directory.CreateDirectory(directory);
            string probe = Path.Combine(directory, $".xunxian-update-{Guid.NewGuid():N}.tmp");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void VerifyCopiedFile(string source, string target)
    {
        var sourceInfo = new FileInfo(source);
        var targetInfo = new FileInfo(target);
        if (sourceInfo.Length != targetInfo.Length)
            throw new InvalidDataException("复制后的更新文件大小不一致。");

        using SHA256 sha256 = SHA256.Create();
        using FileStream sourceStream = File.OpenRead(source);
        byte[] sourceHash = sha256.ComputeHash(sourceStream);
        using FileStream targetStream = File.OpenRead(target);
        byte[] targetHash = sha256.ComputeHash(targetStream);
        if (!sourceHash.SequenceEqual(targetHash))
            throw new InvalidDataException("复制后的更新文件校验失败。");
    }

    private static void TryRestoreBackup(string target, string backup)
    {
        try
        {
            if (File.Exists(backup))
            {
                RetryFileOperation(
                    () => File.Copy(backup, target, overwrite: true),
                    "恢复旧版本失败。");
            }
        }
        catch
        {
            // The update log retains the original failure for manual recovery.
        }
    }

    private static void RetryFileOperation(Action operation, string message)
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                operation();
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lastError = exception;
                Thread.Sleep(250);
            }
        }

        throw new IOException(message, lastError);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static void WriteUpdateLog(string message)
    {
        try
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XunxianDpkViewer");
            Directory.CreateDirectory(folder);
            File.AppendAllText(
                Path.Combine(folder, "update.log"),
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never prevent startup or recovery.
        }
    }
}
