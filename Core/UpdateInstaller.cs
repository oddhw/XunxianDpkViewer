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
    private const string RecoveryArgument = "--update-recovered";
    private const string VerifyManifestArgument = "--verify-update-manifest=";
    private const string CanonicalExecutableName = "XunxianDpkViewer.exe";

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
        bool backupCreated = false;

        try
        {
            WriteUpdateLog($"开始更新：{source} -> {target}");
            if (int.TryParse(ReadValue(arguments, WaitPidArgument), out int processId))
                WaitForProcessExit(processId);

            Directory.CreateDirectory(targetFolder);
            TryDelete(backupPath);
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
                backupCreated = true;
            }
            RetryFileOperation(
                () => File.Move(stagedPath, target, overwrite: true),
                "替换旧版本失败。");

            string? cleanupFolder = ReadValue(arguments, CleanupArgument);
            string? verifyManifest = ReadValue(arguments, VerifyManifestArgument);
            StartTarget(target, cleanupFolder, backupPath, recovered: false, verifyManifest);
            WriteUpdateLog("更新完成并已启动新版本。");
        }
        catch (Exception exception)
        {
            WriteUpdateLog($"更新失败：{exception}");
            if (TryRestoreBackup(target, backupPath, backupCreated))
            {
                try
                {
                    StartTarget(
                        target,
                        null,
                        backupPath,
                        recovered: true,
                        ReadValue(arguments, VerifyManifestArgument));
                    WriteUpdateLog("[RECOVERY_RESTARTED] 已恢复并重新启动旧版本。");
                }
                catch (Exception restartException)
                {
                    WriteUpdateLog($"旧版本已恢复，但重新启动失败：{restartException}");
                }
            }
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
                        if (IsPathInside(updateRoot, fullPath) &&
                            Directory.Exists(fullPath))
                        {
                            Directory.Delete(fullPath, recursive: true);
                        }
                    }
                    CleanupOldLaunchers();
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

    public static bool IsRecoveryRestart(IReadOnlyList<string> arguments) =>
        arguments.Any(argument => argument.Equals(RecoveryArgument, StringComparison.OrdinalIgnoreCase));

    public static string PrepareCanonicalLauncher(string sourcePath)
    {
        string source = Path.GetFullPath(sourcePath);
        using SHA256 sha256 = SHA256.Create();
        using FileStream stream = File.OpenRead(source);
        string fingerprint = Convert.ToHexString(sha256.ComputeHash(stream));
        string launcherFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XunxianDpkViewer",
            "launcher",
            fingerprint);
        string canonicalPath = Path.Combine(launcherFolder, CanonicalExecutableName);
        if (File.Exists(canonicalPath))
        {
            try
            {
                VerifyCopiedFile(source, canonicalPath);
                return canonicalPath;
            }
            catch
            {
                TryDelete(canonicalPath);
            }
        }

        Directory.CreateDirectory(launcherFolder);
        string temporaryPath = canonicalPath + ".new";
        TryDelete(temporaryPath);
        RetryFileOperation(
            () => File.Copy(source, temporaryPath, overwrite: true),
            "创建标准名称启动副本失败。");
        VerifyCopiedFile(source, temporaryPath);
        RetryFileOperation(
            () => File.Move(temporaryPath, canonicalPath, overwrite: true),
            "保存标准名称启动副本失败。");
        return canonicalPath;
    }

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

    private static bool TryRestoreBackup(string target, string backup, bool backupCreated)
    {
        try
        {
            if (!backupCreated || !File.Exists(backup)) return File.Exists(target);
            RetryFileOperation(
                () => File.Copy(backup, target, overwrite: true),
                "恢复旧版本失败。");
            return true;
        }
        catch (Exception exception)
        {
            WriteUpdateLog($"恢复旧版本失败：{exception}");
            return false;
        }
    }

    private static void StartTarget(
        string target,
        string? cleanupFolder,
        string backupPath,
        bool recovered,
        string? verifyManifest)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = target,
            WorkingDirectory = Path.GetDirectoryName(target) ?? Directory.GetCurrentDirectory(),
            UseShellExecute = false
        };
        if (!string.IsNullOrWhiteSpace(cleanupFolder))
            startInfo.ArgumentList.Add(CleanupArgument + cleanupFolder);
        if (!string.IsNullOrWhiteSpace(backupPath))
            startInfo.ArgumentList.Add(BackupArgument + backupPath);
        if (recovered)
            startInfo.ArgumentList.Add(RecoveryArgument);
        if (!string.IsNullOrWhiteSpace(verifyManifest))
            startInfo.ArgumentList.Add(VerifyManifestArgument + verifyManifest);
        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动程序。");
    }

    private static void CleanupOldLaunchers()
    {
        string launcherRoot = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XunxianDpkViewer",
            "launcher"));
        if (!Directory.Exists(launcherRoot)) return;

        string? processPath = Environment.ProcessPath;
        string? currentFolder = string.IsNullOrWhiteSpace(processPath)
            ? null
            : Path.GetDirectoryName(Path.GetFullPath(processPath));
        foreach (string directory in Directory.EnumerateDirectories(launcherRoot))
        {
            if (currentFolder is not null &&
                Path.GetFullPath(directory).Equals(currentFolder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // A different running instance may still use this launcher.
            }
        }
        foreach (string file in Directory.EnumerateFiles(launcherRoot))
        {
            if (processPath is not null &&
                Path.GetFullPath(file).Equals(Path.GetFullPath(processPath), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            TryDelete(file);
        }
    }

    private static void RetryFileOperation(Action operation, string message)
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < 40; attempt++)
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

    private static bool IsPathInside(string root, string candidate)
    {
        string normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedCandidate = Path.GetFullPath(candidate);
        return normalizedCandidate.StartsWith(
            normalizedRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
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
