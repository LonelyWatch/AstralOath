#requires -Version 5.1
[CmdletBinding()]
param([string]$ArchivePath, [uri]$Proxy)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$localTools = [IO.Path]::GetFullPath((Join-Path $repo '.tools'))
$destination = [IO.Path]::GetFullPath((Join-Path $localTools 'powershell'))
$executable = Join-Path $destination 'pwsh.exe'
$lock = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dependencies.lock.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$entries = @($lock.dependencies | Where-Object name -eq 'powershell')
if ($entries.Count -ne 1) { throw 'PowerShell dependency lock entry missing or duplicated.' }
$pinned = $entries[0]
if ($pinned.sha256 -notmatch '\A[0-9a-fA-F]{64}\z' -or
    $pinned.version -notmatch '\A7\.\d+\.\d+\z' -or
    [IO.Path]::GetFileName($pinned.file) -ne $pinned.file) {
    throw 'Invalid PowerShell dependency lock entry.'
}

function Assert-PowerShellVersion([string]$Path) {
    $actual = & $Path -NoLogo -NoProfile -NonInteractive -Command '$PSVersionTable.PSVersion.ToString()'
    if ($LASTEXITCODE -ne 0 -or $actual -ne $pinned.version) {
        throw "Expected PowerShell $($pinned.version), found '$actual' at '$Path'. Existing files were retained; inspect them before reinstalling."
    }
}

[void][IO.Directory]::CreateDirectory($localTools)
if ((Get-Item -LiteralPath $localTools -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw 'Expected a regular .tools directory.'
}
if (Test-Path -LiteralPath $destination) {
    $existing = Get-Item -LiteralPath $destination -Force
    if (-not $existing.PSIsContainer -or ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        -not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "Incomplete or invalid PowerShell installation at '$destination'. Existing files were retained; inspect them before reinstalling."
    }
    Assert-PowerShellVersion $executable
    Write-Output $executable
    return
}

if ($ArchivePath) {
    $archive = (Resolve-Path -LiteralPath $ArchivePath).Path
    if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) { throw 'PowerShell archive must be a file.' }
} else {
    $downloads = Join-Path $localTools 'downloads'
    [void][IO.Directory]::CreateDirectory($downloads)
    if ((Get-Item -LiteralPath $downloads -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'Expected a regular .tools/downloads directory.'
    }
    $archive = Join-Path $downloads $pinned.file
    if (-not (Test-Path -LiteralPath $archive)) {
        $partial = "$archive.$([Guid]::NewGuid().ToString('N')).part"
        $request = @{ Uri = $pinned.url; OutFile = $partial; UseBasicParsing = $true; TimeoutSec = 300 }
        if ($Proxy) { $request.Proxy = $Proxy }
        $previousProtocol = [Net.ServicePointManager]::SecurityProtocol
        try {
            [Net.ServicePointManager]::SecurityProtocol = $previousProtocol -bor [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest @request
        } finally {
            [Net.ServicePointManager]::SecurityProtocol = $previousProtocol
        }
        if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ne $pinned.sha256) {
            throw "PowerShell download checksum mismatch. Download retained at '$partial'."
        }
        Move-Item -LiteralPath $partial -Destination $archive
    }
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $pinned.sha256) {
    throw "PowerShell archive checksum mismatch: '$archive'."
}

$staging = [IO.Path]::GetFullPath((Join-Path $localTools ('powershell-install-' + [Guid]::NewGuid().ToString('N'))))
try {
    Expand-Archive -LiteralPath $archive -DestinationPath $staging
    $candidate = Join-Path $staging 'pwsh.exe'
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw 'PowerShell archive does not contain pwsh.exe.' }
    Assert-PowerShellVersion $candidate
    # Both absolute paths must remain direct children of this project's regular .tools directory.
    if ([IO.Path]::GetDirectoryName($staging) -ne $localTools -or
        [IO.Path]::GetDirectoryName($destination) -ne $localTools -or
        (Get-Item -LiteralPath $staging -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'Unexpected PowerShell installation path.'
    }
    [IO.Directory]::Move($staging, $destination)
} catch {
    throw "PowerShell installation failed; staging files were retained at '$staging'. $($_.Exception.Message)"
}
Write-Output $executable
