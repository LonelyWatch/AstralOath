#requires -Version 5.1
[CmdletBinding()]
param([string]$Dotnet, [switch]$VerifyAccessors, [switch]$SyncClient, [switch]$CheckClient)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repo 'Tools/ProjectEnvironment.ps1')
Initialize-AstralProjectEnvironment -RepositoryRoot $repo | Out-Null
$toolRoot = Join-Path $repo '.tools/luban-v5.1.0'
$output = Join-Path $repo '.artifacts/config'
$work = Join-Path $repo ('.artifacts/config-pending-' + [Guid]::NewGuid().ToString('N'))
$staging = Join-Path $work 'output'
$backup = Join-Path $work 'previous'
$published = $false
$version = 'v5.1.0'
if ($SyncClient -and $CheckClient) { throw 'Choose either -SyncClient or -CheckClient.' }
$client = Join-Path $repo 'Assets/Game/Modules'
$clientFiles = @{
    'demo_ui.json' = 'FoundationDemo/Configs/demo-config.json'
    'demo_localizationkeys.json' = 'FoundationDemo/Configs/demo-localization.json'
    'characterlab-catalog-runtime.json' = 'CharacterLab/Configs/catalog.json'
    'characterlab-localization-runtime.json' = 'CharacterLab/Configs/localization.json'
    'battle-catalog.json' = 'BattleScene/Configs/battle-catalog.json'
}
if (-not $Dotnet) {
    $local = Join-Path $repo '.tools/dotnet/dotnet.exe'
    $Dotnet = if (Test-Path -LiteralPath $local) { $local } else { 'dotnet' }
} elseif (Test-Path -LiteralPath $Dotnet -PathType Leaf) {
    $Dotnet = (Resolve-Path -LiteralPath $Dotnet).Path
}

function Get-PinnedFile([string]$Url, [string]$Path, [string]$Hash) {
    if (-not (Test-Path -LiteralPath $Path)) {
        $temporary = $Path + '.download'
        Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $temporary
        if ((Get-FileHash -LiteralPath $temporary).Hash -cne $Hash) { throw "Checksum mismatch for $Url" }
        Move-Item -LiteralPath $temporary -Destination $Path
    }
    if ((Get-FileHash -LiteralPath $Path).Hash -cne $Hash) { throw "Checksum mismatch for $Path" }
}

[void](New-Item -ItemType Directory -Path $toolRoot -Force)
$archive = Join-Path $toolRoot 'Luban.7z'
Get-PinnedFile "https://github.com/focus-creative-games/luban/releases/download/$version/Luban.7z" $archive 'BAC9A1B8D69CFEAA7EF1D8C67B34A007324D1CD158E0B14294521B3879BA5213'
# Windows Server 2022 tar lacks LZMA support. Use the same verified extractor on CI and developer PCs.
$dependencies = Get-Content -LiteralPath (Join-Path $repo 'Tools/dependencies.lock.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$extractor = @($dependencies.dependencies | Where-Object name -eq '7zip-reduced')
if ($extractor.Count -ne 1) { throw 'Expected exactly one pinned 7zip-reduced dependency.' }
$extractor = $extractor[0]
$extractorRoot = Join-Path $repo ".tools/7zip-$($extractor.version)"
[void][IO.Directory]::CreateDirectory($extractorRoot)
$sevenZip = Join-Path $extractorRoot $extractor.file
Get-PinnedFile $extractor.url $sevenZip $extractor.sha256.ToUpperInvariant()
$artifactRoot = [IO.Directory]::CreateDirectory((Join-Path $repo '.artifacts'))
if ($artifactRoot.Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw 'Expected a regular .artifacts directory.'
}
Push-Location $repo
try {
    [void][IO.Directory]::CreateDirectory($work)
    & $sevenZip x $archive "-o$work" -y -bso0 -bsp0
    $dll = Join-Path $work 'Luban/Luban.dll'
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw 'Luban extraction failed.' }
    & $Dotnet --roll-forward Major $dll --conf Config/luban.conf -t client -d json --strict -x "outputDataDir=$staging/json"
    if ($LASTEXITCODE -ne 0) { throw 'Luban JSON generation or validation failed.' }
    foreach ($asset in @(Get-Content -Raw -Encoding UTF8 (Join-Path $staging 'json/demo_assets.json') | ConvertFrom-Json)) {
        if ($asset.sourcePath -cnotmatch '\A(?:Assets|Config)/(?:[A-Za-z0-9_][A-Za-z0-9_.-]*/)*[A-Za-z0-9_][A-Za-z0-9_.-]*\z' -or
            -not (Test-Path -LiteralPath (Join-Path $repo $asset.sourcePath) -PathType Leaf)) {
            throw "Asset address '$($asset.address)' references an invalid project source."
        }
    }
    foreach ($entry in @(Get-Content -Raw -Encoding UTF8 (Join-Path $staging 'json/demo_localizationkeys.json') | ConvertFrom-Json)) {
        if ($entry.key -cnotmatch '\A[a-z][a-z0-9_]*(?:\.[a-z][a-z0-9_]*)+\z' -or [string]::IsNullOrWhiteSpace($entry.zhCn)) {
            throw "Invalid localization entry '$($entry.key)'."
        }
    }
    $labMessages = @(Get-Content -LiteralPath (Join-Path $staging 'json/characterlab_messages.json') -Raw -Encoding UTF8 | ConvertFrom-Json)
    $labRows = @(Get-Content -LiteralPath (Join-Path $staging 'json/characterlab_characters.json') -Raw -Encoding UTF8 | ConvertFrom-Json)
    $catalog = Get-Content -LiteralPath (Join-Path $repo 'Config/CharacterAssets/catalog.generated.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $labels = @{}
    foreach ($message in $labMessages) {
        if ($message.key -cnotmatch '\A[a-z][a-z0-9_]*(?:\.[a-z][a-z0-9_]*)+\z' -or [string]::IsNullOrWhiteSpace($message.zhCn)) { throw "Invalid CharacterLab localization key: $($message.key)" }
        $labels[$message.key] = $message.zhCn
    }
    if ($labRows.Count -ne @($catalog.characters).Count) { throw 'CharacterLab source table and imported catalog disagree. Run character import after changing the roster.' }
    $catalog.characters = @($labRows | Sort-Object sortOrder | ForEach-Object {
        $row = $_
        $match = @($catalog.characters | Where-Object id -CEQ $row.id)
        if ($match.Count -ne 1 -or -not $labels.ContainsKey($row.nameKey)) { throw "Missing imported character or label: $($row.id)" }
        if ([string]::IsNullOrWhiteSpace($row.battleSkin) -or [string]::IsNullOrWhiteSpace($row.portraitSkin)) { throw "CharacterLab requires explicit battleSkin and portraitSkin: $($row.id)" }
        $match[0] | Add-Member -NotePropertyName battleSkin -NotePropertyValue $row.battleSkin -Force
        $match[0] | Add-Member -NotePropertyName portraitSkin -NotePropertyValue $row.portraitSkin -Force
        $match[0].displayNameKey = $row.nameKey
        $match[0].name = $labels[$row.nameKey]
        $match[0]
    })
    [IO.File]::WriteAllText((Join-Path $staging 'json/characterlab-catalog-runtime.json'), ($catalog | ConvertTo-Json -Depth 80) + "`n", [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $staging 'json/characterlab-localization-runtime.json'), (@{rows=$labMessages} | ConvertTo-Json -Depth 10) + "`n", [Text.UTF8Encoding]::new($false))
    . (Join-Path $PSScriptRoot 'ValidateBattleCatalog.ps1')
    Export-BattleCatalog -Repo $repo -JsonDir (Join-Path $staging 'json') -CharacterCatalog $catalog -CharacterMessages $labMessages
    & $Dotnet --roll-forward Major $dll --conf Config/luban.conf -t client -c cs-bin -d bin --strict -x "outputCodeDir=$staging/csharp" -x "outputDataDir=$staging/bin"
    if ($LASTEXITCODE -ne 0) { throw 'Luban binary and C# generation failed.' }

    if ($VerifyAccessors -or $SyncClient -or $CheckClient) {
        $runtimeDir = Join-Path $toolRoot 'runtime'
        [void](New-Item -ItemType Directory -Path $runtimeDir -Force)
        $upstream = 'https://raw.githubusercontent.com/focus-creative-games/luban_examples/c52e273f1f50a263e9257dddb35c8e222d9777b8'
        $runtime = 'Projects/Csharp_DotNet_bin/LubanLib'
        $files = @{
            'BeanBase.cs' = '2B3FB576F2D2CB3B6C74C2FFD8C75C4F035AB42D857547B1A9C0D892D5C45201'
            'ByteBuf.cs' = 'C784561036714B73A23834969A3C3426CE32F0AA15690E47AD3AC4C56FE0F017'
            'ITypeId.cs' = 'EFFD56BE2EC82D7650B23172AD396341CF7248F7CA34DFEDA362F88A6C1BB477'
            'StringUtil.cs' = '513ED1B536CAC41CD1BBB0CFF42466F4F289255422B2E6149BE67097951A791D'
        }
        foreach ($file in $files.Keys) {
            Get-PinnedFile "$upstream/$runtime/$file" (Join-Path $runtimeDir $file) $files[$file]
        }
        Get-PinnedFile "$upstream/LICENSE" (Join-Path $runtimeDir 'LICENSE') '97C4A8162A1D5B28E06F9B4FF9EB5B054231D2BFF1E0A6A272F5E8D74FE39FC1'
        & $Dotnet run --project (Join-Path $PSScriptRoot 'VerifyAccessor') "-p:GeneratedSourceDirectory=$staging/csharp" -- (Join-Path $staging 'bin') (Join-Path $staging 'json')
        if ($LASTEXITCODE -ne 0) { throw 'Luban accessor verification failed.' }
    }
    $lockPath = Join-Path $repo '.artifacts/config.lock'
    $lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        if ($CheckClient) {
            foreach ($name in $clientFiles.Keys) {
                $snapshot = Join-Path $client $clientFiles[$name]
                $generated = Join-Path $staging "json/$name"
                if (-not (Test-Path -LiteralPath $snapshot -PathType Leaf) -or
                    ([IO.File]::ReadAllText($snapshot) -replace '\r\n', "`n") -cne
                    ([IO.File]::ReadAllText($generated) -replace '\r\n', "`n")) {
                    throw "Client snapshot '$snapshot' is stale. Regenerate with -VerifyAccessors -SyncClient."
                }
            }
        }
        if (Test-Path -LiteralPath $output) {
            $old = Get-Item -LiteralPath $output -Force
            if (-not $old.PSIsContainer -or ($old.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw 'Expected a regular config output directory.'
            }
            [IO.Directory]::Move($output, $backup)
        }
        try {
            [IO.Directory]::Move($staging, $output)
            $published = $true
        } catch {
            if (Test-Path -LiteralPath $backup) { [IO.Directory]::Move($backup, $output) }
            throw
        }
        if ($SyncClient) {
            [void][IO.Directory]::CreateDirectory($client)
            foreach ($name in $clientFiles.Keys) {
                $target = Join-Path $client $clientFiles[$name]
                [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
                [IO.File]::Copy((Join-Path $output "json/$name"), $target, $true)
            }
        }
    } finally {
        $lock.Dispose()
    }
} finally {
    try {
        if ((Test-Path -LiteralPath $work) -and ($published -or -not (Test-Path -LiteralPath $backup))) {
            $resolved = (Resolve-Path -LiteralPath $work).Path
            $parent = Get-Item -LiteralPath (Join-Path $repo '.artifacts') -Force
            $item = Get-Item -LiteralPath $resolved -Force
            if ([IO.Path]::GetDirectoryName($resolved) -ne $parent.FullName -or
                $item.Name -notmatch '\Aconfig-pending-[a-f0-9]{32}\z' -or
                ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
                ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw 'Unexpected Luban staging path.'
            }
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    } finally {
        Pop-Location
    }
}
