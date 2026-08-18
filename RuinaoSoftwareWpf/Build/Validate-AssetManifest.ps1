param(
    [switch]$Update
)

$ErrorActionPreference = 'Stop'

Import-Module Microsoft.PowerShell.Utility

$projectDirectory = Split-Path -Parent $PSScriptRoot
$assetsDirectory = Join-Path $projectDirectory 'Assets'
$manifestPath = Join-Path $assetsDirectory 'asset-manifest.sha256'
$header = '# ruinao-asset-manifest-v1'
$assetsPrefix = $assetsDirectory.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

$entries = Get-ChildItem -LiteralPath $assetsDirectory -Recurse -File |
    Where-Object { $_.FullName -ne $manifestPath } |
    ForEach-Object {
        $relativePath = $_.FullName.Substring($assetsPrefix.Length).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        "$hash|$($_.Length)|$relativePath"
    } |
    Sort-Object

$assetFilesByName = Get-ChildItem -LiteralPath $assetsDirectory -Recurse -File |
    Where-Object { $_.FullName -ne $manifestPath } |
    Group-Object -Property Name -AsHashTable -AsString
$sourceAssetReferences = Get-ChildItem -LiteralPath $projectDirectory -Recurse -File -Filter '*.cs' |
    Where-Object {
        $_.FullName -notlike "*\bin\*" -and
        $_.FullName -notlike "*\obj\*"
    } |
    ForEach-Object {
        $sourcePath = $_.FullName
        $source = [IO.File]::ReadAllText($sourcePath)
        [regex]::Matches($source, '"([^"\r\n]+\.(?:png|mp4|json))"') |
            ForEach-Object {
                [pscustomobject]@{
                    SourcePath = $sourcePath
                    FileName = [IO.Path]::GetFileName($_.Groups[1].Value)
                }
            }
    }

foreach ($reference in $sourceAssetReferences)
{
    $matches = @($assetFilesByName[$reference.FileName])
    if ($matches.Count -eq 0)
    {
        $relativeSource = $reference.SourcePath.Substring($projectDirectory.Length + 1)
        throw "Referenced asset '$($reference.FileName)' from '$relativeSource' does not exist."
    }
}

$actual = @($header) + @($entries)
if ($Update)
{
    $manifestContent = [string]::Join("`n", $actual) + "`n"
    [IO.File]::WriteAllText(
        $manifestPath,
        $manifestContent,
        [Text.UTF8Encoding]::new($false))
    Write-Host "Updated asset manifest: $manifestPath ($($entries.Count) files)"
    exit 0
}

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf))
{
    throw "Asset manifest is missing. Run Build/Validate-AssetManifest.ps1 -Update."
}

$expected = [IO.File]::ReadAllLines($manifestPath, [Text.Encoding]::UTF8)
if ($expected.Count -ne $actual.Count)
{
    throw "Asset manifest file count mismatch. Expected $($expected.Count - 1), found $($entries.Count). Run Build/Validate-AssetManifest.ps1 -Update after reviewing the asset change."
}

for ($index = 0; $index -lt $actual.Count; $index++)
{
    if (-not [string]::Equals($expected[$index], $actual[$index], [StringComparison]::Ordinal))
    {
        throw "Asset manifest mismatch at line $($index + 1). Run Build/Validate-AssetManifest.ps1 -Update after reviewing the asset change."
    }
}

Write-Host "Asset manifest verified: $($entries.Count) files"
