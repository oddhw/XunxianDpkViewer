param(
    [Parameter(Mandatory = $true)]
    [string]$Manifest,

    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

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
$packageBytes = [System.IO.File]::ReadAllBytes($packageFullPath)
$sha256 = [System.Security.Cryptography.SHA256]::Create()
try {
    $hash = $sha256.ComputeHash($packageBytes)
}
finally {
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
