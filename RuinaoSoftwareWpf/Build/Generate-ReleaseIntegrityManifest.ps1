param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$manifestName = 'release-integrity.manifest'
$header = '# ruinao-release-integrity-v1'
$authenticationKeyHex = 'E50E19CE92051AE1297D82D9B1F57E93A80406D9077E35705F50E141D7EF6428'
$root = [System.IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\', '/')

if (-not [System.IO.Directory]::Exists($root)) {
    throw "Release output directory does not exist: $root"
}

function Convert-HexToBytes([string]$hex) {
    if (($hex.Length % 2) -ne 0) {
        throw 'Hexadecimal key length is invalid.'
    }

    $bytes = New-Object byte[] ($hex.Length / 2)
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        $bytes[$index] = [Convert]::ToByte($hex.Substring($index * 2, 2), 16)
    }

    return $bytes
}

$lines = New-Object 'System.Collections.Generic.List[string]'
$lines.Add($header)
$lines.Add("version=$Version")

$nestedPublishDirectory = (Join-Path $root 'publish') + [System.IO.Path]::DirectorySeparatorChar
$files = Get-ChildItem -LiteralPath $root -Recurse -File |
    Where-Object {
        ($_.Extension -ieq '.exe' -or $_.Extension -ieq '.dll') -and
        -not $_.FullName.StartsWith($nestedPublishDirectory, [StringComparison]::OrdinalIgnoreCase)
    } |
    Sort-Object FullName

foreach ($file in $files) {
    $relativePath = $file.FullName.Substring($root.Length).TrimStart('\', '/').Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
    $lines.Add("file=$relativePath|$($file.Length)|$hash")
}

if ($files.Count -eq 0) {
    throw "No executable code files were found in: $root"
}

$payload = [string]::Join("`n", $lines)
$hmac = New-Object System.Security.Cryptography.HMACSHA256
try {
    $hmac.Key = Convert-HexToBytes $authenticationKeyHex
    $signature = -join ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($payload)) | ForEach-Object { $_.ToString('X2') })
}
finally {
    $hmac.Dispose()
}

$lines.Add("hmac=$signature")
$content = [string]::Join("`n", $lines) + "`n"
$manifestPath = Join-Path $root $manifestName
[System.IO.File]::WriteAllText($manifestPath, $content, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Generated integrity manifest: $manifestPath ($($files.Count) files)"
