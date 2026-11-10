[CmdletBinding()]
param([string]$UnityPackage)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $UnityPackage) {
    foreach ($base in @($repo, (Join-Path $repo '.artifacts/UnityProject'))) {
        $candidate = Join-Path $base 'Library/PackageCache/com.unity.textmeshpro@3.0.7/Package Resources/TMP Essential Resources.unitypackage'
        if (Test-Path -LiteralPath $candidate) { $UnityPackage = $candidate; break }
    }
}
if (-not $UnityPackage -or -not (Test-Path -LiteralPath $UnityPackage)) { throw 'Resolve the pinned TMP 3.0.7 Unity package first (Tools/Invoke-Unity.ps1 -Mode Compile).' }
$extract = Join-Path $repo '.artifacts/tmp-essential-3.0.7'
New-Item -ItemType Directory -Force $extract | Out-Null
& tar -xf $UnityPackage -C $extract
if ($LASTEXITCODE -ne 0) { throw 'TMP archive extraction failed' }
$count = 0
foreach ($entry in Get-ChildItem -LiteralPath $extract -Directory) {
    $nameFile = Join-Path $entry.FullName 'pathname'
    if (-not (Test-Path -LiteralPath $nameFile)) { continue }
    $relative = (Get-Content -LiteralPath $nameFile -Raw).Trim()
    $target = [IO.Path]::GetFullPath((Join-Path $repo $relative))
    if (-not $target.StartsWith((Join-Path $repo 'Assets') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Unexpected TMP destination: $relative" }
    $asset = Join-Path $entry.FullName 'asset'
    if (Test-Path -LiteralPath $asset) {
        New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
        if (-not (Test-Path -LiteralPath $target)) { Copy-Item -LiteralPath $asset -Destination $target; $count++ }
    } else { New-Item -ItemType Directory -Force $target | Out-Null }
    $meta = Join-Path $entry.FullName 'asset.meta'
    if ((Test-Path -LiteralPath $meta) -and -not (Test-Path -LiteralPath "$target.meta")) { Copy-Item -LiteralPath $meta -Destination "$target.meta" }
}
Write-Output "Imported $count missing TMP essential assets, preserving upstream GUIDs and existing files."
