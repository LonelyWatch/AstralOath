[CmdletBinding()]
param([uri]$Proxy, [string]$ArchiveDirectory)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$lock = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json
$downloads = Join-Path $repo '.tools/downloads'
New-Item -ItemType Directory -Force $downloads | Out-Null
$stage = Join-Path $repo ('.tools/hybridclr/source-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $stage | Out-Null
foreach ($name in @('HybridCLR-runtime','HybridCLR-il2cpp')) {
    $entries = @($lock.dependencies | Where-Object name -eq $name)
    if ($entries.Count -ne 1) { throw "Dependency lock entry missing or duplicated: $name" }
    $entry = $entries[0]
    $archive = Join-Path $downloads $entry.file
    if ($ArchiveDirectory -and (Test-Path -LiteralPath (Join-Path $ArchiveDirectory $entry.file))) {
        $archive = Join-Path $ArchiveDirectory $entry.file
    }
    if (-not (Test-Path -LiteralPath $archive)) {
        $partial = "$archive.part-$([Guid]::NewGuid().ToString('N'))"
        $request = @{ Uri = $entry.url; OutFile = $partial }
        if ($Proxy) { $request.Proxy = $Proxy }
        Invoke-WebRequest @request
        if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ine $entry.sha256) {
            throw "Download checksum mismatch: $name"
        }
        Move-Item -LiteralPath $partial -Destination $archive
    }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ine $entry.sha256) {
        throw "Cached archive checksum mismatch: $name"
    }
    Expand-Archive -LiteralPath $archive -DestinationPath $stage
}
$runtime = @($lock.dependencies | Where-Object name -eq 'HybridCLR-runtime')[0]
$il2cpp = @($lock.dependencies | Where-Object name -eq 'HybridCLR-il2cpp')[0]
$source = Join-Path $stage ("hybridclr-$($runtime.commit)/hybridclr")
$destination = Join-Path $stage ("il2cpp_plus-$($il2cpp.commit)/libil2cpp")
if (-not (Test-Path -LiteralPath $source -PathType Container) -or
    -not (Test-Path -LiteralPath $destination -PathType Container)) { throw 'Unexpected official source archive layout.' }
Copy-Item -LiteralPath $source -Destination (Join-Path $destination 'hybridclr') -Recurse
# The only output is the source directory consumed by InstallerController.InstallFromLocal.
# Installation is project-local and is performed by Tools or Invoke-Unity.
Write-Output ([IO.Path]::GetFullPath($destination))
