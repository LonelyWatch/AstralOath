#requires -Version 7
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$work = Join-Path $repo ('.artifacts/config-test-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($work)
$work = (Resolve-Path -LiteralPath $work).Path
$pwsh = Join-Path $PSHOME 'pwsh.exe'
if (-not (Test-Path -LiteralPath $pwsh)) { $pwsh = Join-Path $PSHOME 'pwsh' }
$localDotnet = Join-Path $repo '.tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }

try {
    Copy-Item -LiteralPath (Join-Path $repo 'Config') -Destination $work -Recurse
    foreach ($file in @('Tools/Config/Generate.ps1', 'Tools/dependencies.lock.json', 'Tools/Config/VerifyAccessor/Program.cs',
        'Tools/Config/VerifyAccessor/VerifyAccessor.csproj',
        'Assets/Game/Modules/FoundationDemo/Res/Text/demo-resource.txt')) {
        $destination = Join-Path $work $file
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
        Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $destination
    }
    $cachedTools = Join-Path $repo '.tools/luban-v5.1.0'
    if (Test-Path -LiteralPath $cachedTools) {
        $fixtureTools = [IO.Directory]::CreateDirectory((Join-Path $work '.tools')).FullName
        Copy-Item -LiteralPath $cachedTools -Destination $fixtureTools -Recurse
    }
    $dependencies = Get-Content -LiteralPath (Join-Path $repo 'Tools/dependencies.lock.json') -Raw -Encoding utf8 | ConvertFrom-Json
    $extractor = @($dependencies.dependencies | Where-Object name -eq '7zip-reduced')[0]
    $cachedExtractor = Join-Path $repo ".tools/7zip-$($extractor.version)"
    if (Test-Path -LiteralPath $cachedExtractor) {
        $fixtureTools = [IO.Directory]::CreateDirectory((Join-Path $work '.tools')).FullName
        Copy-Item -LiteralPath $cachedExtractor -Destination $fixtureTools -Recurse
    }
    $ui = Join-Path $work 'Config/Tables/demo_ui.csv'
    $locale = Join-Path $work 'Config/Tables/demo_localization.csv'
    $original = Get-Content -LiteralPath $ui -Raw -Encoding utf8
    $changed = $original -replace '(?m)^,1,[^\r\n]+', ',1,foundation.fixture_title,foundation.message,10,foundation/demo-config'
    if ($changed -ceq $original) { throw 'The configuration fixture has no row 1.' }
    Set-Content -LiteralPath $ui -Value $changed -NoNewline -Encoding utf8
    $messages = (Get-Content -LiteralPath $locale -Raw -Encoding utf8) -replace
        '(?m)^,foundation\.message,[^\r\n]+', ',foundation.message,变更后的描述'
    Set-Content -LiteralPath $locale -Value ($messages.TrimEnd() + "`n,foundation.fixture_title,测试文案`n") -NoNewline -Encoding utf8
    $generate = Join-Path $work 'Tools/Config/Generate.ps1'
    # Windows Server 2022 tar cannot decode the pinned Luban LZMA archive.
    # Mask tar so this case also catches that dependency on newer developer PCs.
    $withoutTar = Join-Path $work 'GenerateWithoutTar.ps1'
    Set-Content -LiteralPath $withoutTar -Encoding utf8 -Value @'
param([string]$Generate, [string]$Dotnet)
$ErrorActionPreference = 'Stop'
function global:tar { throw 'System tar is unavailable: LZMA codec is unsupported.' }
& $Generate -Dotnet $Dotnet -VerifyAccessors -SyncClient
'@
    & $pwsh -NoProfile -File $withoutTar -Generate $generate -Dotnet $dotnet
    if ($LASTEXITCODE -ne 0) { throw 'Valid configuration failed generation without system tar.' }

    $client = Join-Path $work 'Assets/Game/Modules/FoundationDemo/Configs'
    $uiSnapshot = Get-Content -LiteralPath (Join-Path $client 'demo-config.json') -Raw | ConvertFrom-Json
    $localeSnapshot = Get-Content -LiteralPath (Join-Path $client 'demo-localization.json') -Raw | ConvertFrom-Json
    if ($uiSnapshot[0].displaySeconds -ne 10 -or $uiSnapshot[0].titleKey -cne 'foundation.fixture_title' -or
        ($localeSnapshot | Where-Object key -CEQ 'foundation.fixture_title').zhCn -cne '测试文案' -or
        ($localeSnapshot | Where-Object key -CEQ 'foundation.message').zhCn -cne '变更后的描述') {
        throw 'The client snapshots did not pick up the generated data.'
    }
    & $pwsh -NoProfile -File $generate -Dotnet $dotnet -VerifyAccessors -CheckClient
    if ($LASTEXITCODE -ne 0) { throw 'Generated client snapshots failed the freshness check.' }

    foreach ($invalid in @('0', '31')) {
        Set-Content -LiteralPath $ui -Value ($changed.Replace(',10,', ",$invalid,")) -NoNewline -Encoding utf8
        & $pwsh -NoProfile -File $generate -Dotnet $dotnet -VerifyAccessors -SyncClient
        if ($LASTEXITCODE -eq 0) { throw "Invalid displaySeconds=$invalid passed generation." }
    }
    $fixtureExtractor = Join-Path $work ".tools/7zip-$($extractor.version)/$($extractor.file)"
    [IO.File]::AppendAllText($fixtureExtractor, 'tampered')
    $corruptLog = & $pwsh -NoProfile -File $generate -Dotnet $dotnet -VerifyAccessors -SyncClient 2>&1
    if ($LASTEXITCODE -eq 0 -or ($corruptLog -join "`n") -notmatch 'Checksum mismatch') {
        throw 'A corrupted extractor was not rejected before execution.'
    }
    Write-Host 'Config fixture passed generation without tar, valid edits, client snapshots, invalid bounds and extractor checksum rejection.'
} finally {
    $item = Get-Item -LiteralPath $work
    if ($item.Parent.FullName -ne (Join-Path $repo '.artifacts') -or
        $item.Name -notmatch '\Aconfig-test-[a-f0-9]{32}\z' -or
        ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Unexpected config fixture directory.'
    }
    Remove-Item -LiteralPath $work -Recurse -Force
}
