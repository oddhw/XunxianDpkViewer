param(
    [Parameter(Mandatory = $true)]
    [string]$Manifest,

    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [string]$MirrorUrl,

    [string]$MirrorLabel = "GitCode Mirror",

    [string]$PrivateKey = $env:XUNXIAN_UPDATE_PRIVATE_KEY
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($PrivateKey)) {
    throw "Pass -PrivateKey or set XUNXIAN_UPDATE_PRIVATE_KEY."
}
$manifestPath = (Resolve-Path -LiteralPath $Manifest).Path
$packageFullPath = (Resolve-Path -LiteralPath $PackagePath).Path
$privateKeyPath = (Resolve-Path -LiteralPath $PrivateKey).Path

$manifestObject = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$manifestVersionText = (([string]$manifestObject.version).Trim().TrimStart([char[]]"vV") -split "[-+]")[0]
$manifestVersion = $null
if (-not [Version]::TryParse($manifestVersionText, [ref]$manifestVersion)) {
    throw "Manifest version is invalid: $($manifestObject.version)"
}
$packageVersionText = [Diagnostics.FileVersionInfo]::GetVersionInfo($packageFullPath).FileVersion
$packageVersion = $null
if (-not [Version]::TryParse($packageVersionText, [ref]$packageVersion)) {
    throw "Package file version is invalid: $packageVersionText"
}
if ($manifestVersion.Major -ne $packageVersion.Major -or
    $manifestVersion.Minor -ne $packageVersion.Minor -or
    $manifestVersion.Build -ne $packageVersion.Build) {
    throw "Version mismatch: manifest $manifestVersion, package $packageVersion."
}

if (-not [string]::IsNullOrWhiteSpace($MirrorUrl)) {
    $mirrorUri = $null
    if (-not [Uri]::TryCreate($MirrorUrl, [UriKind]::Absolute, [ref]$mirrorUri) -or
        $mirrorUri.Scheme -ne "https") {
        throw "MirrorUrl must be an absolute HTTPS URL."
    }

    $packages = @($manifestObject.packages)
    $existingMirror = $packages | Where-Object { $_.url -eq $MirrorUrl } | Select-Object -First 1
    if ($null -eq $existingMirror) {
        $mirrorPackage = [PSCustomObject]@{
            url       = $MirrorUrl
            sha256    = ""
            signature = ""
            size      = 0
            priority  = 10
            label     = $MirrorLabel
        }
        $manifestObject.packages = @($mirrorPackage) + $packages
    }
    else {
        $existingMirror.priority = 10
        $existingMirror.label = $MirrorLabel
    }
}

$sha256 = [System.Security.Cryptography.SHA256]::Create()
$packageStream = [System.IO.File]::OpenRead($packageFullPath)
try {
    $hash = $sha256.ComputeHash($packageStream)
}
finally {
    $packageStream.Dispose()
    $sha256.Dispose()
}

$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
try {
    $privateBlob = [Convert]::FromBase64String(
        [System.IO.File]::ReadAllText($privateKeyPath).Trim())
    $rsa.ImportCspBlob($privateBlob)
    $signature = $rsa.SignHash(
        $hash,
        [System.Security.Cryptography.CryptoConfig]::MapNameToOID("SHA256"))
}
finally {
    $rsa.Dispose()
}

$hashHex = -join ($hash | ForEach-Object { $_.ToString("X2") })
$signatureBase64 = [Convert]::ToBase64String($signature)
$fileSize = (Get-Item -LiteralPath $packageFullPath).Length

foreach ($package in $manifestObject.packages) {
    $package.sha256 = $hashHex
    $package.signature = $signatureBase64
    $package.size = $fileSize
}

$payload = New-Object System.Text.StringBuilder
[void]$payload.Append([string]$manifestObject.schemaVersion).Append("`n")
[void]$payload.Append(([string]$manifestObject.channel).Trim()).Append("`n")
[void]$payload.Append(([string]$manifestObject.version).Trim()).Append("`n")
foreach ($url in $manifestObject.bootstrapUrls) {
    [void]$payload.Append("B:").Append(([string]$url).Trim()).Append("`n")
}
foreach ($package in $manifestObject.packages) {
    [void]$payload.Append("P:")
    [void]$payload.Append(([string]$package.url).Trim()).Append("|")
    [void]$payload.Append(([string]$package.sha256).Trim().ToUpperInvariant()).Append("|")
    [void]$payload.Append(([string]$package.signature).Trim()).Append("|")
    [void]$payload.Append([string]$package.size).Append("|")
    [void]$payload.Append([string]$package.priority).Append("`n")
}

$payloadBytes = [System.Text.Encoding]::UTF8.GetBytes($payload.ToString())
$payloadHasher = [System.Security.Cryptography.SHA256]::Create()
try {
    $payloadHash = $payloadHasher.ComputeHash($payloadBytes)
}
finally {
    $payloadHasher.Dispose()
}

$manifestSigner = New-Object System.Security.Cryptography.RSACryptoServiceProvider
try {
    $manifestSigner.ImportCspBlob($privateBlob)
    $manifestSignature = $manifestSigner.SignHash(
        $payloadHash,
        [System.Security.Cryptography.CryptoConfig]::MapNameToOID("SHA256"))
}
finally {
    $manifestSigner.Dispose()
}
$manifestObject.signature = [Convert]::ToBase64String($manifestSignature)

$json = $manifestObject | ConvertTo-Json -Depth 12
[System.IO.File]::WriteAllText(
    $manifestPath,
    $json,
    (New-Object System.Text.UTF8Encoding($false)))

Write-Host "Manifest signed: $manifestPath"
Write-Host "SHA-256: $hashHex"
Write-Host "Size: $fileSize"
