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

# PowerShell skips a CSV header beginning with '#'; preserve Luban's marker when writing.
function Read-LubanRows([string]$Path) {
    $csv = [IO.File]::ReadAllText($Path) -replace '\A##,', 'note,'
    $csv | ConvertFrom-Csv
}

function Write-LubanRows([string]$Path, [object[]]$Rows) {
    $lines = @($Rows | ConvertTo-Csv -NoTypeInformation)
    $lines[0] = $lines[0] -replace '\A"note",', '##,'
    [IO.File]::WriteAllLines($Path, $lines, [Text.UTF8Encoding]::new($false))
}

try {
    Copy-Item -LiteralPath (Join-Path $repo 'Config') -Destination $work -Recurse
    foreach ($file in @('Tools/Config/Generate.ps1', 'Tools/Config/ValidateBattleCatalog.ps1',
        'Tools/dependencies.lock.json', 'Tools/Config/VerifyAccessor/Program.cs',
        'Tools/Config/VerifyAccessor/VerifyAccessor.csproj',
        'Shared/com.astraloath.shared/Runtime/Battle/BattleStats.cs',
        'Shared/com.astraloath.shared/Runtime/Battle/HeroGrowthPreset.cs',
        'Shared/com.astraloath.shared/Runtime/Battle/HeroStatCalculator.cs',
        'Assets/Game/Modules/FoundationDemo/Res/Text/demo-resource.txt')) {
        $destination = Join-Path $work $file
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
        Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $destination
    }
    foreach ($hero in @('c1001', 'c1005', 'c1062')) {
        foreach ($slot in 1..3) {
            $icon = "Assets/Game/Modules/BattleScene/Res/Textures/Hud/Skills/sk_${hero}_${slot}.png"
            $destination = Join-Path $work $icon
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
            Copy-Item -LiteralPath (Join-Path $repo $icon) -Destination $destination
        }
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
    $heroTable = Join-Path $work 'Config/Tables/battle_heroes.csv'
    $heroRows = @(Read-LubanRows $heroTable)
    $changedHeroId = $heroRows[0].id
    $changedSpeed = [int]$heroRows[0].speed + 1
    $heroRows[0].speed = [string]$changedSpeed
    Write-LubanRows $heroTable $heroRows
    $validHeroes = Get-Content -LiteralPath $heroTable -Raw -Encoding utf8
    $presetTable = Join-Path $work 'Config/Tables/battle_growth_presets.csv'
    $presetRows = @(Read-LubanRows $presetTable)
    $changedPreset = @($presetRows | Where-Object id -CEQ $heroRows[0].growthPresetId)
    if ($changedPreset.Count -ne 1) { throw 'The battle fixture needs one matching growth preset.' }
    $changedPreset[0].speedMilli = [string]([int]$changedPreset[0].speedMilli + 1000)
    Write-LubanRows $presetTable $presetRows
    $awakeningTable = Join-Path $work 'Config/Tables/battle_awakening_steps.csv'
    $validAwakenings = Get-Content -LiteralPath $awakeningTable -Raw -Encoding utf8
    $skillTable = Join-Path $work 'Config/Tables/battle_skills.csv'
    $originalSkills = Get-Content -LiteralPath $skillTable -Raw -Encoding utf8
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
    $battlePath = Join-Path $work 'Assets/Game/Modules/BattleScene/Configs/battle-catalog.json'
    $battleText = Get-Content -LiteralPath $battlePath -Raw -Encoding utf8
    $battle = $battleText | ConvertFrom-Json
    if ($battle.schemaVersion -ne 2 -or
        @($battle.heroes | Where-Object id -CEQ $changedHeroId)[0].speed -ne $changedSpeed) {
        throw 'The BattleScene snapshot did not pick up the edited hero source row.'
    }
    foreach ($table in @(
        @{ Group = 'heroes'; File = 'battle_battleheroes.json' },
        @{ Group = 'growthPresets'; File = 'battle_battlegrowthpresets.json' },
        @{ Group = 'awakeningSteps'; File = 'battle_battleawakeningsteps.json' },
        @{ Group = 'skills'; File = 'battle_battleskills.json' },
        @{ Group = 'effects'; File = 'battle_battleskilleffects.json' },
        @{ Group = 'enhancements'; File = 'battle_battleskillenhancements.json' },
        @{ Group = 'messages'; File = 'battle_battlemessages.json' }
    )) {
        $rows = @(Get-Content -LiteralPath (Join-Path $work ('.artifacts/config/json/' + $table.File)) -Raw -Encoding utf8 | ConvertFrom-Json)
        if ($table.Group -eq 'messages') {
            $characterMessages = @(Get-Content -LiteralPath (Join-Path $work '.artifacts/config/json/characterlab_messages.json') -Raw -Encoding utf8 | ConvertFrom-Json)
            foreach ($hero in $battle.heroes) {
                $rows += @($characterMessages | Where-Object key -CEQ $hero.nameKey)
            }
        }
        $generated = ConvertTo-Json -InputObject $rows -Depth 20 -Compress
        $snapshot = ConvertTo-Json -InputObject @($battle.($table.Group)) -Depth 20 -Compress
        if ($generated -cne $snapshot) { throw "BattleScene '$($table.Group)' differs from the generated source table." }
    }
    & $pwsh -NoProfile -File $generate -Dotnet $dotnet -VerifyAccessors -CheckClient
    if ($LASTEXITCODE -ne 0) { throw 'Generated client snapshots failed the freshness check.' }

    foreach ($invalid in @('presentation', 'slot', 'coefficient')) {
        $skillRows = @(Read-LubanRows $skillTable)
        if ($invalid -eq 'presentation') {
            $skillRows[0].presentationId = 'missing_battle_presentation'
            $expectedError = 'Battle skill presentation'
        } elseif ($invalid -eq 'coefficient') {
            $skillRows[0].attackRateBp = ''
            $expectedError = 'Battle skill coefficient'
        } else {
            $sameHero = @($skillRows | Where-Object heroId -CEQ $skillRows[0].heroId)
            if ($sameHero.Count -lt 2) { throw 'The battle fixture needs two skills for the same hero.' }
            $sameHero[1].slot = $sameHero[0].slot
            $expectedError = 'Battle skill slot'
        }
        Write-LubanRows $skillTable $skillRows
        $invalidLog = & $pwsh -NoProfile -File $generate -Dotnet $dotnet -VerifyAccessors -SyncClient 2>&1
        if ($LASTEXITCODE -eq 0 -or ($invalidLog -join "`n") -notmatch [regex]::Escape($expectedError)) {
            throw "Invalid battle $invalid was not rejected by the expected validator: $($invalidLog -join "`n")"
        }
        if ((Get-Content -LiteralPath $battlePath -Raw -Encoding utf8) -cne $battleText) {
            throw "Rejected battle $invalid changed the published client snapshot."
        }
        Set-Content -LiteralPath $skillTable -Value $originalSkills -NoNewline -Encoding utf8
    }
    foreach ($invalid in @('growth-profile', 'growth-panel', 'awakening-rank')) {
        if ($invalid -eq 'awakening-rank') {
            $awakeningRows = @(Read-LubanRows $awakeningTable)
            $sameHero = @($awakeningRows | Where-Object heroId -CEQ $awakeningRows[0].heroId)
            $sameHero[1].rank = $sameHero[0].rank
            Write-LubanRows $awakeningTable $awakeningRows
            $expectedError = 'Battle awakening rank'
        } else {
            $heroRows = @(Read-LubanRows $heroTable)
            if ($invalid -eq 'growth-profile') {
                $otherPreset = @($presetRows | Where-Object id -CNE $heroRows[0].growthPresetId)[0]
                $heroRows[0].growthPresetId = $otherPreset.id
                $expectedError = 'Battle hero growth preset'
            } else {
                $heroRows[0].attack = [string]([int]$heroRows[0].attack + 1)
                $expectedError = 'Battle hero growth stats'
            }
            Write-LubanRows $heroTable $heroRows
        }
        $invalidLog = & $pwsh -NoProfile -File $generate -Dotnet $dotnet -SyncClient 2>&1
        if ($LASTEXITCODE -eq 0 -or ($invalidLog -join "`n") -notmatch [regex]::Escape($expectedError)) {
            throw "Invalid battle $invalid was not rejected by the expected validator: $($invalidLog -join "`n")"
        }
        if ((Get-Content -LiteralPath $battlePath -Raw -Encoding utf8) -cne $battleText) {
            throw "Rejected battle $invalid changed the published client snapshot."
        }
        Set-Content -LiteralPath $heroTable -Value $validHeroes -NoNewline -Encoding utf8
        Set-Content -LiteralPath $awakeningTable -Value $validAwakenings -NoNewline -Encoding utf8
    }
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
    Write-Host 'Config fixture passed generation without tar, edited demo and battle snapshots, binary accessors, invalid presentation/slot/empty coefficient/growth preset/growth panel/awakening rank/bounds rejection and extractor checksum rejection.'
} finally {
    $item = Get-Item -LiteralPath $work
    if ($item.Parent.FullName -ne (Join-Path $repo '.artifacts') -or
        $item.Name -notmatch '\Aconfig-test-[a-f0-9]{32}\z' -or
        ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Unexpected config fixture directory.'
    }
    Remove-Item -LiteralPath $work -Recurse -Force
}
