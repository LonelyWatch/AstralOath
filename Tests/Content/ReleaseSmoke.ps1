#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location $repo
$staging = '.artifacts/content-smoke-' + [Guid]::NewGuid().ToString('N')
New-Item -ItemType Directory -Path "$staging/input/yooasset" -Force | Out-Null
Set-Content -Path "$staging/input/yooasset/demo.bytes" -Value 'foundation' -NoNewline
$source = 'Config/Tables/demo_ui.csv'
$provenance = @{
    files = @(@{
        path = 'yooasset/demo.bytes'
        origin = 'project'
        sourcePath = $source
        sourceSha256 = (Get-FileHash $source).Hash.ToLowerInvariant()
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
        BundleContents = @(@{ AssetPath = 'Assets/Game/Modules/FoundationDemo/Res/Text/demo-resource.txt' })
    })
} | ConvertTo-Json -Depth 8 | Set-Content "$packageDir/Foundation_foundation-1.report"
& ./Tools/Content/Package-Unity.ps1 -PackageDirectory $packageDir -Key "$staging/dev/private.key" -Release "$staging/unity-release" -Version 42
& ./Tools/Content/Release.ps1 -Action Verify -Key "$staging/dev/public.pem" -Release "$staging/unity-release"
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
