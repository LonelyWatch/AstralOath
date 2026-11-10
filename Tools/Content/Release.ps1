#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('NewDevKey','Build','Verify')][string]$Action,
    [Parameter(Mandatory)][string]$Key,
    [string]$PayloadRoot,
    [string]$Provenance,
    [string]$Release,
    [long]$Version,
    [int]$ProtocolVersion = 1,
    [int]$SchemaVersion = 1
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'SourcePath.ps1')
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$original = 'D:/sevenepic'
$artifacts = Join-Path $repo '.artifacts'

function Assert-RelativePath([string]$Path) {
    if ($Path.Length -gt 240 -or $Path.Length -eq 0) { throw "Invalid relative path '$Path'." }
    foreach ($segment in $Path.Split('/')) {
        if ($segment -cnotmatch '\A[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}\z' -or $segment.EndsWith('.') -or
            $segment -match '\A(?:CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|\z)') {
            throw "Invalid relative path '$Path'."
        }
    }
}

function Assert-NoLinks([string]$Path) {
    $current = $Path
    while ($current) {
        $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "A symbolic link or reparse point is not allowed at '$current'."
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}

function Resolve-File([string]$Root, [string]$Path) {
    Assert-RelativePath $Path
    $file = Join-Path $Root $Path
    Assert-NoLinks $file
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing input file '$file'." }
    return $file
}

function Get-Hash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-NewArtifact([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path, (Get-Location).ProviderPath)
    $relative = [IO.Path]::GetRelativePath($artifacts, $full)
    if ([IO.Path]::IsPathRooted($relative) -or $relative -eq '.' -or $relative -eq '..' -or
        $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar)) {
        throw 'Output must be a child of the repository .artifacts directory.'
    }
    Assert-NoLinks $full
    if (Test-Path -LiteralPath $full) { throw "Output already exists at '$full'." }
    return $full
}

function Get-BoundedBytes([string]$Path, [int]$Limit) {
    $file = Get-Item -LiteralPath $Path
    if ($file.Length -gt $Limit) { throw "Metadata exceeded its size limit at '$Path'." }
    return ,([IO.File]::ReadAllBytes($file.FullName))
}

function Assert-PathSet($Paths) {
    $files = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $directories = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $Paths) {
        Assert-RelativePath $path
        if ($path -in 'manifest.json','manifest.sig') { throw "Reserved payload path '$path'." }
        if (-not $files.Add($path)) { throw "Duplicate payload path '$path'." }
        $directory = $path
        while (($separator = $directory.LastIndexOf('/')) -gt 0) {
            $directory = $directory.Substring(0, $separator)
            if ($directories.ContainsKey($directory) -and $directories[$directory] -cne $directory) {
                throw "Inconsistent directory casing for '$directory'."
            }
            $directories[$directory] = $directory
        }
    }
    foreach ($path in $files) {
        if ($directories.ContainsKey($path)) { throw "Payload path '$path' is also a directory." }
    }
}

function Read-Provenance([string]$InputFile) {
    $document = [Text.Encoding]::UTF8.GetString((Get-BoundedBytes $InputFile 1MB)) | ConvertFrom-Json -AsHashtable
    if ($document.files -isnot [array] -or $document.files.Count -lt 1 -or $document.files.Count -gt 4096) {
        throw 'Provenance requires between 1 and 4096 explicit payload files.'
    }
    Assert-PathSet $document.files.path
    $entries = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($entry in $document.files) {
        if ($entry.sourcePath -isnot [string]) { throw 'Source path must be a string.' }
        if ($entry.origin -ceq 'project') {
            $sourceRoot = $repo
            if ($entry.sourcePath -cnotmatch '\A(?:Assets|Config)/') { throw 'Project sources must be under Assets or Config.' }
        } elseif ($entry.origin -ceq 'sevenepic') {
            $sourceRoot = $original
        } else { throw "Unknown origin '$($entry.origin)'." }

        $source = Resolve-ContentSourceFile $sourceRoot $entry.sourcePath
        if ($entry.sourceSha256 -cnotmatch '\A[a-f0-9]{64}\z' -or (Get-Hash $source) -cne $entry.sourceSha256) {
            throw "Source hash mismatch for '$($entry.sourcePath)'."
        }
        if ($entry.dependencies -isnot [array]) { throw "Dependencies must be an array for '$($entry.path)'." }
        $entries.Add($entry.path, $entry.dependencies)
    }

    $complete = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($path in $entries.Keys) {
        foreach ($dependency in $entries[$path]) {
            if (-not $entries.ContainsKey($dependency)) { throw "Payload '$path' has missing dependency '$dependency'." }
        }
    }
    do {
        $lastCount = $complete.Count
        foreach ($path in $entries.Keys) {
            if ($complete.Contains($path)) { continue }
            $pending = @($entries[$path] | Where-Object { -not $complete.Contains($_) })
            if ($pending.Count -eq 0) { [void]$complete.Add($path) }
        }
    } while ($complete.Count -gt $lastCount)
    if ($complete.Count -ne $entries.Count) { throw 'Payload dependencies contain a cycle.' }
    return $document.files
}

$rsa = [Security.Cryptography.RSA]::Create()
try {
    if ($Action -eq 'NewDevKey') {
        $directory = Assert-NewArtifact $Key
        [void][IO.Directory]::CreateDirectory($directory)
        $rsa.KeySize = 3072
        [IO.File]::WriteAllText((Join-Path $directory 'private.key'), $rsa.ExportPkcs8PrivateKeyPem())
        [IO.File]::WriteAllText((Join-Path $directory 'public.pem'), $rsa.ExportSubjectPublicKeyInfoPem())
        $parameters = $rsa.ExportParameters($false)
        $publicKey = @{
            modulus = [Convert]::ToBase64String($parameters.Modulus)
            exponent = [Convert]::ToBase64String($parameters.Exponent)
        }
        [IO.File]::WriteAllText((Join-Path $directory 'public.json'), ($publicKey | ConvertTo-Json -Compress))
        return
    }

    if (-not $Release -or $ProtocolVersion -lt 1 -or $SchemaVersion -lt 1) {
        throw 'Release and positive protocol/schema versions are required.'
    }
    $rsa.ImportFromPem([IO.File]::ReadAllText((Resolve-Path -LiteralPath $Key).Path))
    if ($rsa.KeySize -lt 2048) { throw 'RSA key must be at least 2048 bits.' }
    $algorithm = [Security.Cryptography.HashAlgorithmName]::SHA256
    $padding = [Security.Cryptography.RSASignaturePadding]::Pkcs1
    if ($Action -eq 'Build') {
        if (-not $PayloadRoot -or -not $Provenance -or $Version -lt 1) {
            throw 'Build requires payload root, provenance and a positive release version.'
        }
        $destination = Assert-NewArtifact $Release
        $inputs = Read-Provenance $Provenance
        $root = (Resolve-Path -LiteralPath $PayloadRoot).Path
        $staging = $destination + '.pending-' + [Guid]::NewGuid().ToString('N')
        [void][IO.Directory]::CreateDirectory($staging)
        $total = 0L
        $entries = foreach ($entry in $inputs | Sort-Object -Property path) {
            $payload = Resolve-File $root $entry.path
            $length = (Get-Item -LiteralPath $payload).Length
            $total += $length
            if ($length -gt 8GB -or $total -gt 64GB) { throw 'Payload exceeds release size limits.' }
            $target = Join-Path $staging $entry.path
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
            [IO.File]::Copy($payload, $target)
            if ((Get-Item -LiteralPath $target).Length -ne $length) { throw "Payload changed during build: $payload" }
            [ordered]@{ path = $entry.path; size = $length; sha256 = Get-Hash $target }
        }
        $manifest = [ordered]@{
            version = $Version
            protocolVersion = $ProtocolVersion
            schemaVersion = $SchemaVersion
            files = @($entries)
        }
        $bytes = [Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Depth 8 -Compress))
        if ($bytes.Length -gt 1MB) { throw 'Manifest exceeds its size limit.' }
        [IO.File]::WriteAllBytes((Join-Path $staging 'manifest.json'), $bytes)
        [IO.File]::WriteAllBytes((Join-Path $staging 'manifest.sig'), $rsa.SignData($bytes, $algorithm, $padding))
        [IO.Directory]::Move($staging, $destination)
        Write-Host "Signed release $Version at $destination"
        return
    }

    $root = (Resolve-Path -LiteralPath $Release).Path
    $bytes = Get-BoundedBytes (Resolve-File $root 'manifest.json') 1MB
    $signature = Get-BoundedBytes (Resolve-File $root 'manifest.sig') ($rsa.KeySize / 8)
    if ($signature.Length -ne ($rsa.KeySize / 8) -or -not $rsa.VerifyData($bytes, $signature, $algorithm, $padding)) {
        throw 'Invalid content manifest signature.'
    }
    $manifest = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json -AsHashtable
    if ($manifest.version -isnot [long] -or $manifest.version -lt 1 -or $manifest.protocolVersion -ne $ProtocolVersion -or
        $manifest.schemaVersion -ne $SchemaVersion -or $manifest.files -isnot [array] -or
        $manifest.files.Count -lt 1 -or $manifest.files.Count -gt 4096) {
        throw 'Manifest header or compatibility versions are invalid.'
    }
    Assert-PathSet $manifest.files.path
    $total = 0L
    foreach ($entry in $manifest.files) {
        $payload = Resolve-File $root $entry.path
        $length = (Get-Item -LiteralPath $payload).Length
        $total += $length
        if ($entry.size -isnot [long] -or $entry.size -ne $length -or $length -gt 8GB -or $total -gt 64GB -or
            $entry.sha256 -cnotmatch '\A[a-f0-9]{64}\z' -or (Get-Hash $payload) -cne $entry.sha256) {
            throw "Payload hash or size mismatch for '$($entry.path)'."
        }
    }
    Write-Host "Verified release $($manifest.version)"
} finally {
    $rsa.Dispose()
}
