#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location $repo
$staging = '.artifacts/content-smoke-' + [Guid]::NewGuid().ToString('N')
New-Item -ItemType Directory -Path "$staging/input/yooasset" -Force | Out-Null
Set-Content -Path "$staging/input/yooasset/demo.bytes" -Value 'foundation' -NoNewline
$tmpShader = 'Assets/TextMesh Pro/Shaders/TMP_SDF-Mobile.shader'
if (-not (Test-Path -LiteralPath $tmpShader -PathType Leaf)) { throw 'The Foundation TMP shader source fixture is missing.' }
$provenance = @{
    files = @(@{
        path = 'yooasset/demo.bytes'
        origin = 'project'
        sourcePath = $tmpShader
        sourceSha256 = (Get-FileHash -LiteralPath $tmpShader).Hash.ToLowerInvariant()
        dependencies = @()
    })
}
$provenance | ConvertTo-Json -Depth 8 | Set-Content "$staging/provenance.json"
& ./Tools/Content/Release.ps1 -Action NewDevKey -Key "$staging/dev"
& ./Tools/Content/Release.ps1 -Action Build -Key "$staging/dev/private.key" -PayloadRoot "$staging/input" -Provenance "$staging/provenance.json" -Release "$staging/release" -Version 1
& ./Tools/Content/Release.ps1 -Action Verify -Key "$staging/dev/public.pem" -Release "$staging/release"
Add-Content "$staging/release/yooasset/demo.bytes" 'tampered'
$rejected = $false
try { & ./Tools/Content/Release.ps1 -Action Verify -Key "$staging/dev/public.pem" -Release "$staging/release" } catch { $rejected = $true }
if (-not $rejected) { throw 'Tampered payload was accepted.' }

$provenance.files[0].dependencies = @('missing.bytes')
$provenance | ConvertTo-Json -Depth 8 | Set-Content "$staging/provenance.json"
$rejected = $false
try { & ./Tools/Content/Release.ps1 -Action Build -Key "$staging/dev/private.key" -PayloadRoot "$staging/input" -Provenance "$staging/provenance.json" -Release "$staging/bad-release" -Version 2 } catch { $rejected = $_.Exception.Message -like '*missing dependency*' }
if (-not $rejected) { throw 'Unresolved dependency was accepted.' }
Write-Host 'Content CLI round-trip, tamper and dependency checks passed.'

$sourceDirectory = 'Config/.release-smoke-' + [Guid]::NewGuid().ToString('N')
$unicodeSource = "$sourceDirectory/中文 资源.txt"
$sourceLink = "$sourceDirectory/linked"
$sourceLinkTarget = (Resolve-Path -LiteralPath "$staging/input/yooasset").Path
$null = New-Item -ItemType Directory -Path $sourceDirectory
try {
    Set-Content -LiteralPath $unicodeSource -Value 'local source fixture' -NoNewline
    $sourceEntry = $provenance.files[0]
    $sourceEntry.dependencies = @()
    $sourceEntry.sourcePath = $unicodeSource
    $sourceEntry.sourceSha256 = (Get-FileHash -LiteralPath $unicodeSource).Hash.ToLowerInvariant()
    $provenance | ConvertTo-Json -Depth 8 | Set-Content "$staging/provenance.json"
    & ./Tools/Content/Release.ps1 -Action Build -Key "$staging/dev/private.key" -PayloadRoot "$staging/input" -Provenance "$staging/provenance.json" -Release "$staging/unicode-release" -Version 3
    & ./Tools/Content/Release.ps1 -Action Verify -Key "$staging/dev/public.pem" -Release "$staging/unicode-release"

    $linkType = if ($IsWindows) { 'Junction' } else { 'SymbolicLink' }
    $null = New-Item -ItemType $linkType -Path $sourceLink -Target $sourceLinkTarget
    $cases = @(
        @{ name = 'traversal'; origin = 'project'; path = "$sourceDirectory/../Tables/demo_ui.csv"; file = 'Config/Tables/demo_ui.csv'; error = '*Invalid source path*' }
        @{ name = 'absolute'; origin = 'sevenepic'; path = (Resolve-Path -LiteralPath $tmpShader).Path; file = $tmpShader; error = '*Invalid source path*' }
        @{ name = 'drive-relative'; origin = 'sevenepic'; path = 'C:demo.bytes'; file = $tmpShader; error = '*Invalid source path*' }
        @{ name = 'unc'; origin = 'sevenepic'; path = '//server/share/demo.bytes'; file = $tmpShader; error = '*Invalid source path*' }
        @{ name = 'outside-project-source'; origin = 'project'; path = 'README.md'; file = 'README.md'; error = '*Project sources must be under Assets or Config*' }
        @{ name = 'linked-source'; origin = 'project'; path = "$sourceLink/demo.bytes"; file = "$sourceLinkTarget/demo.bytes"; error = '*reparse point*' }
    )
    foreach ($case in $cases) {
        $sourceEntry.origin = $case.origin
        $sourceEntry.sourcePath = $case.path
        $sourceEntry.sourceSha256 = (Get-FileHash -LiteralPath $case.file).Hash.ToLowerInvariant()
        $releasePath = "$staging/rejected-$($case.name)"
        $provenance | ConvertTo-Json -Depth 8 | Set-Content "$staging/provenance.json"
        $rejected = $false
        try {
            & ./Tools/Content/Release.ps1 -Action Build -Key "$staging/dev/private.key" -PayloadRoot "$staging/input" -Provenance "$staging/provenance.json" -Release $releasePath -Version 4
        } catch { $rejected = $_.Exception.Message -like $case.error }
        if (-not $rejected -or (Test-Path -LiteralPath $releasePath)) { throw "Unsafe source was not rejected as expected: $($case.name)." }
    }

    $sourceEntry.origin = 'project'
    $sourceEntry.sourcePath = $unicodeSource
    $sourceEntry.sourceSha256 = (Get-FileHash -LiteralPath $unicodeSource).Hash.ToLowerInvariant()
    $sourceEntry.path = 'yooasset/invalid payload.bytes'
    Copy-Item -LiteralPath "$staging/input/yooasset/demo.bytes" -Destination "$staging/input/$($sourceEntry.path)"
    $provenance | ConvertTo-Json -Depth 8 | Set-Content "$staging/provenance.json"
    $rejected = $false
    try {
        & ./Tools/Content/Release.ps1 -Action Build -Key "$staging/dev/private.key" -PayloadRoot "$staging/input" -Provenance "$staging/provenance.json" -Release "$staging/unsafe-payload" -Version 4
    } catch { $rejected = $_.Exception.Message -like '*Invalid relative path*' }
    if (-not $rejected -or (Test-Path -LiteralPath "$staging/unsafe-payload")) { throw 'A payload path with spaces was accepted.' }
} finally {
    if (Test-Path -LiteralPath $sourceLink) { Remove-Item -LiteralPath $sourceLink }
    if (Test-Path -LiteralPath $unicodeSource) { Remove-Item -LiteralPath $unicodeSource }
    Remove-Item -LiteralPath $sourceDirectory
}
Write-Host 'Local source paths allow spaces and CJK while unsafe sources and payload paths are rejected.'

$packageDir = "$staging/unity-package"
New-Item -ItemType Directory -Path $packageDir | Out-Null
Set-Content -Path "$packageDir/Foundation.version" -Value 'foundation-1' -NoNewline
Set-Content -Path "$packageDir/Foundation_foundation-1.bytes" -Value '123456789' -NoNewline
Set-Content -Path "$packageDir/Foundation_foundation-1.hash" -Value '2639f4cb' -NoNewline
Set-Content -Path "$packageDir/demo.bundle" -Value 'fixture bundle' -NoNewline
Set-Content -Path "$packageDir/OutputCache.manifest" -Value 'build-only fixture' -NoNewline
@{
    Summary = @{ BuildPackageName = 'Foundation'; BuildPackageVersion = 'foundation-1' }
    BundleInfos = @(@{
        BundleName = 'demo.bundle'
        FileName = 'demo.bundle'
        FileSize = (Get-Item "$packageDir/demo.bundle").Length
        FileHash = (Get-FileHash "$packageDir/demo.bundle" -Algorithm MD5).Hash
        DependBundles = @()
        BundleContents = @(@{ AssetPath = $tmpShader })
    })
} | ConvertTo-Json -Depth 8 | Set-Content "$packageDir/Foundation_foundation-1.report"
& ./Tools/Content/Package-Unity.ps1 -PackageDirectory $packageDir -Key "$staging/dev/private.key" -Release "$staging/unity-release" -Version 42
& ./Tools/Content/Release.ps1 -Action Verify -Key "$staging/dev/public.pem" -Release "$staging/unity-release" -SchemaVersion 2
$unityManifest = Get-Content -Raw "$staging/unity-release/manifest.json" | ConvertFrom-Json
if ($unityManifest.schemaVersion -ne 2) { throw 'Foundation UIView content must use schema 2.' }
if (-not (Test-Path "$staging/unity-release/yooasset/Foundation/demo.bundle") -or
    (Test-Path "$staging/unity-release/yooasset/Foundation/OutputCache.manifest")) {
    throw 'YooAsset bundle layout or allowlist differs from the build report.'
}
$key = Get-Content -Raw "$staging/dev/public.json" | ConvertFrom-Json
$rsa = [Security.Cryptography.RSA]::Create()
try {
    $parameters = [Security.Cryptography.RSAParameters]::new()
    $parameters.Modulus = [Convert]::FromBase64String($key.modulus)
    $parameters.Exponent = [Convert]::FromBase64String($key.exponent)
    $rsa.ImportParameters($parameters)
    $directory = (Resolve-Path "$staging/unity-release").Path
    if (-not $rsa.VerifyData([IO.File]::ReadAllBytes((Join-Path $directory 'manifest.json')),
        [IO.File]::ReadAllBytes((Join-Path $directory 'manifest.sig')),
        [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)) {
        throw 'Client-compatible public JSON did not verify release.'
    }
} finally { $rsa.Dispose() }
Write-Host 'YooAsset package wrapper layout and client public key passed with a report fixture.'


Add-Content "$packageDir/Foundation_foundation-1.bytes" 'tampered'
$rejected = $false
try {
    & ./Tools/Content/Package-Unity.ps1 -PackageDirectory $packageDir -Key "$staging/dev/private.key" -Release "$staging/bad-unity-release" -Version 43
} catch { $rejected = $_.Exception.Message -like '*manifest hash*' }
if (-not $rejected -or (Test-Path "$staging/bad-unity-release")) { throw 'Tampered YooAsset manifest was published.' }

[IO.File]::WriteAllBytes((Join-Path $packageDir 'Foundation_foundation-1.bytes'), [byte[]]::new(0))
Set-Content "$packageDir/Foundation_foundation-1.hash" '00000000' -NoNewline
$rejected = $false
try {
    & ./Tools/Content/Package-Unity.ps1 -PackageDirectory $packageDir -Key "$staging/dev/private.key" -Release "$staging/empty-unity-release" -Version 44
} catch { $rejected = $_.Exception.Message -like '*manifest is empty*' }
if (-not $rejected -or (Test-Path "$staging/empty-unity-release")) { throw 'Empty YooAsset manifest was published.' }
