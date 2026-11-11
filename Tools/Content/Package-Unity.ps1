#requires -Version 7.0
[CmdletBinding(DefaultParameterSetName = 'Legacy')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Legacy')][string]$PackageDirectory,
    [Parameter(Mandatory, ParameterSetName = 'BuildOutput')][string]$BuildOutput,
    [string]$Key = $env:ASTRAL_SIGNING_KEY,
    [Parameter(Mandatory)][string]$Release,
    [Parameter(Mandatory, ParameterSetName = 'Legacy')][long]$Version,
    [int]$ProtocolVersion = 1,
    [int]$SchemaVersion = 2
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'SourcePath.ps1')
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$snapshot = $null
if (-not $Key) { throw 'Provide a signing key via -Key or ASTRAL_SIGNING_KEY.' }
if ($PSCmdlet.ParameterSetName -eq 'BuildOutput') {
    $output = (Resolve-Path -LiteralPath $BuildOutput).Path
    $receiptFile = Resolve-ContentSourceFile $output 'package.complete.json'
    if ((Get-Item -LiteralPath $receiptFile).Length -gt 4096) { throw 'Oversized build receipt.' }
    $receipt = Get-Content -LiteralPath $receiptFile -Raw | ConvertFrom-Json -AsHashtable
    if ($receipt.contentVersion -isnot [long] -or $receipt.contentVersion -lt 1 -or
        $receipt.packageDirectory -cnotmatch '\Abundles/(StandaloneWindows64|Android)/Foundation/foundation-[1-9][0-9]*\z') {
        throw 'Invalid build receipt.'
    }
    if ($PSBoundParameters.ContainsKey('SchemaVersion') -and $SchemaVersion -ne 3) {
        throw 'Completed HybridCLR builds require schema 3.'
    }
    $SchemaVersion = 3
    $Version = $receipt.contentVersion
    $PackageDirectory = Join-Path $output $receipt.packageDirectory
    $snapshot = Join-Path $output 'sources'
} elseif ($SchemaVersion -ge 3) {
    throw 'Schema 3 releases require -BuildOutput and a completed HybridCLR build receipt.'
}
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$prefix = 'yooasset/Foundation/'
$buildSource = 'Assets/Game/Editor/FoundationBuilder.cs'

function Get-PackageFile([string]$Name) {
    if ($Name -cnotmatch '\A[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}\z' -or $Name.EndsWith('.')) {
        throw "Unsafe YooAsset file name '$Name'."
    }
    $file = Get-Item -LiteralPath (Join-Path $package $Name)
    if ($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Expected a regular YooAsset file: $Name"
    }
    return $file
}

function New-Provenance([string]$Name, [string]$Source, [string[]]$Dependencies) {
    if ($Source -cnotmatch '\A(?:Assets|Config)/') {
        throw "Expected a project asset source: $Source"
    }
    # Generated code assets are archived with this build. Use the versioned assembly list as the project input.
    $sourceFile = Resolve-ContentSourceFile $repo $Source
    return @{
        path = $prefix + $Name
        origin = 'project'
        sourcePath = $Source
        sourceSha256 = (Get-FileHash -LiteralPath $sourceFile).Hash.ToLowerInvariant()
        dependencies = @($Dependencies | ForEach-Object { $prefix + $_ })
    }
}

$versionFile = Get-PackageFile 'Foundation.version'
if ($versionFile.Length -gt 128) { throw 'YooAsset version marker exceeded its size limit.' }
$packageVersion = (Get-Content -LiteralPath $versionFile.FullName -Raw).Trim()
if ($packageVersion -cnotmatch '\A[A-Za-z0-9_][A-Za-z0-9_.-]{0,63}\z') { throw 'Invalid YooAsset package version.' }
if ($snapshot -and $packageVersion -cne "foundation-$Version") { throw 'Build receipt and YooAsset versions disagree.' }
$manifestName = "Foundation_$packageVersion.bytes"
$hashName = "Foundation_$packageVersion.hash"
$null = Get-PackageFile $manifestName
$null = Get-PackageFile $hashName
$reportFile = Get-PackageFile "Foundation_$packageVersion.report"
if ($reportFile.Length -gt 8MB) { throw 'Foundation build report exceeded its size limit.' }
$report = Get-Content -LiteralPath $reportFile.FullName -Raw | ConvertFrom-Json
if ($report.Summary.BuildPackageName -cne 'Foundation' -or $report.Summary.BuildPackageVersion -cne $packageVersion -or
    @($report.BundleInfos).Count -lt 1 -or @($report.BundleInfos).Count -gt 4093) {
    throw 'The build report does not describe this Foundation package.'
}

$bundles = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
foreach ($bundle in $report.BundleInfos) {
    $bundles.Add($bundle.BundleName, $bundle.FileName)
}
$entries = @(
    New-Provenance 'Foundation.version' $buildSource @($hashName, $manifestName)
    New-Provenance $hashName $buildSource @()
    New-Provenance $manifestName $buildSource @($bundles.Values)
    foreach ($bundle in $report.BundleInfos) {
        $file = Get-PackageFile $bundle.FileName
        if ($file.Length -ne $bundle.FileSize -or (Get-FileHash -LiteralPath $file.FullName -Algorithm MD5).Hash -ne $bundle.FileHash) {
            throw "Bundle disagrees with the YooAsset build report: $($bundle.FileName)"
        }
        $dependencies = foreach ($dependency in $bundle.DependBundles) {
            if (-not $bundles.ContainsKey($dependency)) { throw "Unknown YooAsset dependency '$dependency'." }
            $bundles[$dependency]
        }
        $sources = @($bundle.BundleContents | ForEach-Object { $_.AssetPath })
        if ($sources.Count -eq 0) { throw "YooAsset bundle has no recorded source: $($bundle.FileName)" }
        $primary = $sources[0]
        if ($snapshot -and $primary.StartsWith('Assets/Game/HotUpdate/Generated/', [StringComparison]::Ordinal)) {
            $primary = 'Config/HotUpdate/assemblies.json'
        }
        $entry = New-Provenance $bundle.FileName $primary @($dependencies)
        $entry.sourceAssets = @($sources | ForEach-Object {
            $sourceRoot = if ($snapshot -and $_.StartsWith('Assets/Game/HotUpdate/Generated/', [StringComparison]::Ordinal)) { $snapshot } else { $repo }
            $sourceFile = Resolve-ContentSourceFile $sourceRoot $_
            @{ path = $_; sha256 = (Get-FileHash -LiteralPath $sourceFile).Hash.ToLowerInvariant() }
        })
        $entry
    }
)

$work = Join-Path $repo ('.artifacts/unity-content-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $work 'payload'
$destination = Join-Path $payload $prefix
[void][IO.Directory]::CreateDirectory($destination)
foreach ($entry in $entries) {
    $file = Get-PackageFile ($entry.path.Substring($prefix.Length))
    [IO.File]::Copy($file.FullName, (Join-Path $payload $entry.path))
}
$stagedManifest = Get-Item -LiteralPath (Join-Path $destination $manifestName)
if ($stagedManifest.Length -eq 0) { throw 'YooAsset binary manifest is empty.' }
$stagedHash = Get-Item -LiteralPath (Join-Path $destination $hashName)
if ($stagedHash.Length -gt 64) { throw 'YooAsset manifest hash marker is oversized.' }
$expectedHash = Get-Content -LiteralPath $stagedHash.FullName -Raw
if ($expectedHash -cmatch '\A[a-f0-9]{8}\z') {
    if (-not ('AstralOathYooCrc32' -as [type])) {
        Add-Type -TypeDefinition @"
using System;
using System.IO;

public static class AstralOathYooCrc32
{
    public static string Compute(string path)
    {
        uint crc = uint.MaxValue;
        using (var stream = File.OpenRead(path))
        {
            int value;
            while ((value = stream.ReadByte()) != -1)
            {
                crc ^= (byte)value;
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
            }
        }
        return BitConverter.ToString(BitConverter.GetBytes(~crc)).Replace("-", "").ToLowerInvariant();
    }
}
"@
    }
    $actualHash = [AstralOathYooCrc32]::Compute($stagedManifest.FullName)
} elseif ($expectedHash -cmatch '\A[a-f0-9]{32}\z') {
    $actualHash = (Get-FileHash -LiteralPath $stagedManifest.FullName -Algorithm MD5).Hash.ToLowerInvariant()
} else {
    throw 'YooAsset manifest hash marker is invalid.'
}
if ($actualHash -cne $expectedHash) { throw 'YooAsset manifest hash does not match the staged binary.' }

$provenance = Join-Path $work 'provenance.json'
@{ files = $entries } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $provenance -Encoding utf8
& (Join-Path $PSScriptRoot 'Release.ps1') -Action Build -Key $Key -PayloadRoot $payload -Provenance $provenance -Release $Release -Version $Version -ProtocolVersion $ProtocolVersion -SchemaVersion $SchemaVersion
Write-Host "Unity build provenance: $provenance"
