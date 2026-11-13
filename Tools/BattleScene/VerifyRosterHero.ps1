#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^c1[0-9]{3}$')][string]$HeroId,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z][A-Za-z0-9]+Tests$')][string]$AuthorityFixture,
    [ValidatePattern('^Unity[A-Za-z0-9_-]*$')][string]$ProjectName = 'UnityBattleScene'
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $repo 'Tools/ProjectEnvironment.ps1')
Initialize-AstralProjectEnvironment -RepositoryRoot $repo | Out-Null
$engine = Join-Path $repo 'Tools/Invoke-Unity.ps1'
$dotnet = Join-Path $repo '.tools/dotnet/dotnet.exe'
$catalog = Join-Path $repo 'Assets/Game/Modules/BattleScene/Configs/battle-catalog.json'
$reports = Join-Path $repo ".artifacts/roster-data/$HeroId"
$previousHero = $env:ASTRAL_BATTLE_HERO_ID

Push-Location $repo
try {
    $data = Get-Content -LiteralPath $catalog -Raw | ConvertFrom-Json
    $heroSkills = @($data.skills | Where-Object heroId -CEQ $HeroId | Sort-Object slot)
    if ($heroSkills.Count -ne 3 -or -not ($data.heroes.id -ccontains $HeroId)) {
        throw "Expected a registered hero with three source slots: $HeroId"
    }

    New-Item -Path $reports -ItemType Directory -Force | Out-Null
    $arguments = @('test', 'Server/Tests', '--no-restore', '--verbosity', 'minimal',
        '--filter', "FullyQualifiedName~$AuthorityFixture", '--logger', 'trx;LogFileName=authority.trx',
        '--results-directory', $reports)
    & $dotnet @arguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    [xml]$authority = Get-Content -LiteralPath (Join-Path $reports 'authority.trx') -Raw
    $count = $authority.TestRun.ResultSummary.Counters
    if ([int]$count.passed -le 0 -or [int]$count.failed -ne 0) {
        throw "No passing tests found for $AuthorityFixture."
    }

    $env:ASTRAL_BATTLE_HERO_ID = $HeroId
    & $engine -Mode PlayMode -Graphics -ProjectName $ProjectName -TestFilter AddedHeroesCanEnterBattleAndPlayAllSourceGraphs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $unity = Join-Path $repo '.artifacts/unity'
    Copy-Item -LiteralPath (Join-Path $unity 'PlayMode.xml') -Destination (Join-Path $reports 'playmode.xml')
    Copy-Item -LiteralPath (Join-Path $unity 'PlayMode.log') -Destination (Join-Path $reports 'playmode.log')
    [xml]$result = Get-Content -LiteralPath (Join-Path $reports 'playmode.xml') -Raw
    $test = $result.SelectSingleNode('//test-case[@name="AddedHeroesCanEnterBattleAndPlayAllSourceGraphs"]')
    $output = if ($test) { $test.InnerText } else { '' }
    foreach ($skill in $heroSkills) {
        $line = "$HeroId S$($skill.slot): $($skill.presentationId) passed source playback and reset."
        if (-not $output.Contains($line, [StringComparison]::Ordinal)) { throw "No per-skill evidence: $line" }
    }
    "Verified $HeroId authority ($($count.passed) cases) and three Unity skill playbacks. Reports: $reports"
}
finally {
    Pop-Location
    $env:ASTRAL_BATTLE_HERO_ID = $previousHero
}
