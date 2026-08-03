param(
    [Parameter(Mandatory = $true)]
    [string]$Manifest,

    [Parameter(Mandatory = $true)]
    [string]$PackagePath
)

$ErrorActionPreference = "Stop"
$manifestPath = (Resolve-Path -LiteralPath $Manifest).Path
$packageFullPath = (Resolve-Path -LiteralPath $PackagePath).Path
$manifestObject = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$packageInfo = Get-Item -LiteralPath $packageFullPath
$packageVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($packageFullPath).FileVersion
$manifestVersion = (([string]$manifestObject.version).Trim().TrimStart([char[]]"vV") -split "[-+]")[0]
if (-not $packageVersion.StartsWith($manifestVersion + ".") -and $packageVersion -ne $manifestVersion) {
    throw "Version mismatch: manifest $manifestVersion, package $packageVersion."
}

$packageHash = (Get-FileHash -LiteralPath $packageFullPath -Algorithm SHA256).Hash
$manifestPackage = @($manifestObject.packages) | Select-Object -First 1
if ($packageHash -ne ([string]$manifestPackage.sha256).ToUpperInvariant()) {
    throw "The package SHA-256 does not match the manifest."
}
if ($packageInfo.Length -ne [long]$manifestPackage.size) {
    throw "The package size does not match the manifest."
}

$appDataFolder = Join-Path $env:LOCALAPPDATA "XunxianDpkViewer"
$verificationLog = Join-Path $appDataFolder "update-manifest-test.log"
$updateLog = Join-Path $appDataFolder "update.log"
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("XunxianDpkViewer-update-test-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $testRoot | Out-Null

function Wait-ForValidLog {
    param([datetime]$After)
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $verificationLog) {
            $item = Get-Item -LiteralPath $verificationLog
            if ($item.LastWriteTimeUtc -ge $After -and
                (Get-Content -LiteralPath $verificationLog -Raw -Encoding UTF8).Trim() -eq "VALID") {
                return
            }
        }
        Start-Sleep -Milliseconds 250
    }
    throw "The restarted application did not validate the signed manifest in time."
}

try {
    $target = Join-Path $testRoot "RenamedViewer.exe"
    [IO.File]::WriteAllBytes($target, [byte[]](1, 2, 3, 4))
    Remove-Item -LiteralPath $verificationLog -Force -ErrorAction SilentlyContinue
    $startedAt = [DateTime]::UtcNow
    $arguments = @(
        "--apply-update",
        "--update-target=$target",
        "--update-wait-pid=0",
        "--verify-update-manifest=$manifestPath"
    )
    $process = Start-Process -FilePath $packageFullPath -ArgumentList $arguments -PassThru -Wait -WindowStyle Hidden
    if ($process.ExitCode -ne 0) {
        throw "Updater exited with code $($process.ExitCode)."
    }
    Wait-ForValidLog -After $startedAt
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $packageHash) {
        throw "The installed target differs from the signed package."
    }
    if (-not (Test-Path -LiteralPath ($target + ".update-backup"))) {
        throw "The updater did not create a backup before replacement."
    }

    $recoveryTarget = Join-Path $testRoot "RenamedRecovery.exe"
    Copy-Item -LiteralPath $packageFullPath -Destination $recoveryTarget
    [IO.File]::WriteAllBytes(($recoveryTarget + ".update-backup"), [byte[]](9, 8, 7, 6))
    Remove-Item -LiteralPath $verificationLog -Force -ErrorAction SilentlyContinue
    $logLength = if (Test-Path -LiteralPath $updateLog) { (Get-Item -LiteralPath $updateLog).Length } else { 0 }
    $startedAt = [DateTime]::UtcNow
    $recoveryArguments = @(
        "--apply-update",
        "--update-target=$recoveryTarget",
        "--update-wait-pid=0",
        "--verify-update-manifest=$manifestPath"
    )
    $targetLock = [IO.File]::Open(
        $recoveryTarget,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::Read)
    try {
        $recoveryProcess = Start-Process -FilePath $packageFullPath -ArgumentList $recoveryArguments -PassThru -WindowStyle Hidden
        Start-Sleep -Seconds 12
    }
    finally {
        $targetLock.Dispose()
    }
    $recoveryProcess.WaitForExit()
    if ($recoveryProcess.ExitCode -ne 0) {
        throw "Recovery updater exited with code $($recoveryProcess.ExitCode)."
    }
    Wait-ForValidLog -After $startedAt
    $newLog = if (Test-Path -LiteralPath $updateLog) {
        $stream = [IO.File]::Open($updateLog, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        try {
            [void]$stream.Seek($logLength, [IO.SeekOrigin]::Begin)
            $reader = New-Object IO.StreamReader($stream, [Text.Encoding]::UTF8)
            try { $reader.ReadToEnd() } finally { $reader.Dispose() }
        }
        finally { $stream.Dispose() }
    } else { "" }
    if ($newLog -notmatch "RECOVERY_RESTARTED") {
        throw "The forced replacement failure did not restart the recovered version."
    }

    Write-Host "Update release test passed."
    Write-Host "Version: $manifestVersion"
    Write-Host "SHA-256: $packageHash"
}
finally {
    $fullTestRoot = [IO.Path]::GetFullPath($testRoot)
    $fullTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($fullTestRoot.StartsWith($fullTempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $fullTestRoot)) {
        Remove-Item -LiteralPath $fullTestRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
