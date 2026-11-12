function Export-BattleCatalog {
    param([string]$Repo, [string]$JsonDir, $CharacterCatalog, [object[]]$CharacterMessages)

    $heroes = @(Get-Content -LiteralPath (Join-Path $JsonDir 'battle_battleheroes.json') -Raw -Encoding UTF8 | ConvertFrom-Json)
    $growthPresets = @(Get-Content -LiteralPath (Join-Path $JsonDir 'battle_battlegrowthpresets.json') -Raw -Encoding UTF8 | ConvertFrom-Json)
    $awakeningSteps = @(Get-Content -LiteralPath (Join-Path $JsonDir 'battle_battleawakeningsteps.json') -Raw -Encoding UTF8 | ConvertFrom-Json)
    $skills = @(Get-Content -LiteralPath (Join-Path $JsonDir 'battle_battleskills.json') -Raw -Encoding UTF8 | ConvertFrom-Json)
    $effects = @(Get-Content -LiteralPath (Join-Path $JsonDir 'battle_battleskilleffects.json') -Raw -Encoding UTF8 | ConvertFrom-Json)
    $enhancements = @(Get-Content -LiteralPath (Join-Path $JsonDir 'battle_battleskillenhancements.json') -Raw -Encoding UTF8 | ConvertFrom-Json)
    $messages = @(Get-Content -LiteralPath (Join-Path $JsonDir 'battle_battlemessages.json') -Raw -Encoding UTF8 | ConvertFrom-Json)

    if ($heroes.Count -eq 0) { throw 'Battle catalog requires at least one hero.' }
    foreach ($message in $messages) {
        if ($message.key -cnotmatch '\Abattle\.[a-z][a-z0-9_]*(?:\.[a-z][a-z0-9_]*)+\z' -or
            [string]::IsNullOrWhiteSpace($message.zhCn)) {
            throw "Battle localization is invalid: $($message.key)."
        }
    }

    $heroClasses = @('knight','warrior','thief','ranger','mage','soul_weaver')
    $zodiacs = @('aries','taurus','gemini','cancer','leo','virgo','libra','scorpio','sagittarius','capricorn','aquarius','pisces')
    foreach ($preset in $growthPresets) {
        if ($preset.id -cnotmatch '\A[a-z][a-z0-9_]*\z' -or
            $preset.heroClass -cnotin $heroClasses -or $preset.zodiac -cnotin $zodiacs) {
            throw "Battle growth preset is invalid: $($preset.id)."
        }
    }

    $heroNames = foreach ($hero in $heroes) {
        $id = $hero.id
        if ($id -cnotmatch '\Ac[0-9]{4}\z' -or $hero.element -cnotin @('fire','ice','wind','light','dark') -or
            $hero.heroClass -cnotin $heroClasses -or
            $hero.zodiac -cnotin $zodiacs -or
            $hero.variant -cnotmatch '\A[a-z][a-z0-9_]*\z' -or
            $hero.baseStars -gt $hero.stars -or $hero.awakenings -gt $hero.stars) {
            throw "Battle hero profile is invalid: $id."
        }
        $preset = @($growthPresets | Where-Object id -CEQ $hero.growthPresetId)
        if ($preset.Count -ne 1 -or $preset[0].nativeStars -ne $hero.baseStars -or
            $preset[0].heroClass -cne $hero.heroClass -or $preset[0].zodiac -cne $hero.zodiac) {
            throw "Battle hero growth preset does not match native stars, class and zodiac for $id."
        }
        $ranks = @($awakeningSteps | Where-Object heroId -CEQ $id | Select-Object -ExpandProperty rank | Sort-Object)
        if (($ranks -join ',') -cne '1,2,3,4,5,6') {
            throw "Battle awakening rank set must be 1 through 6 exactly once for $id."
        }
        $imported = @($CharacterCatalog.characters | Where-Object id -CEQ $id)
        if ($imported.Count -ne 1 -or $imported[0].displayNameKey -cne $hero.nameKey) {
            throw "Battle hero must match an imported character and its name key: $id."
        }
        $ownedSkills = @($skills | Where-Object heroId -CEQ $id)
        $slots = @($ownedSkills | Select-Object -ExpandProperty slot | Sort-Object)
        if (($slots -join ',') -cne '1,2,3') { throw "Battle skill slot set must be S1/S2/S3 exactly once for $id." }
        foreach ($skill in $ownedSkills) {
            $slot = [int]$skill.slot
            $key = "$id.s$slot"
            if ($skill.id -cne $key) { throw "Battle skill slot ID must be $key." }
            if (@($imported[0].skills).Count -lt $slot -or $imported[0].skills[$slot - 1].id -cne $skill.presentationId) {
                throw "Battle skill presentation does not match the imported graph for $key."
            }
            $icon = "Assets/Game/Modules/BattleScene/Res/Textures/Hud/Skills/sk_${id}_$slot.png"
            if ($skill.iconAsset -cne $icon -or -not (Test-Path -LiteralPath (Join-Path $Repo $icon) -PathType Leaf)) {
                throw "Battle skill icon does not match the imported asset for $key."
            }
            if ($skill.nameKey -cne "battle.skill.$key.name" -or $skill.descriptionKey -cne "battle.skill.$key.description" -or
                $skill.target -cnotin @('one_enemy','two_enemies','all_enemies','one_ally','all_allies')) {
                throw "Battle skill text or targeting is invalid for $key."
            }
            $burn = $skill.soulBurnKind
            $turns = $skill.soulBurnTurns
            if ($burn -cnotin @('none','reduce_cooldown','increase_damage','extend_granted_buffs') -or
                (($skill.soulBurnCost -eq 0) -ne ($burn -ceq 'none')) -or
                (($turns -gt 0) -ne ($burn -cin @('reduce_cooldown','extend_granted_buffs'))) -or
                ($burn -ceq 'reduce_cooldown' -and $turns -gt $skill.cooldownTurns) -or
                ($null -ne $skill.burnAttackRateBp -and $burn -cne 'increase_damage')) {
                throw "Battle skill soul burn is invalid for $key."
            }
            foreach ($property in @('attackRateBp','powerBp','casterHealthDamageBp','casterHealthHealBp','casterHealthBarrierBp','burnAttackRateBp')) {
                $coefficient = $skill.$property
                if ($null -ne $coefficient -and ($coefficient -lt 1 -or $coefficient -gt 100000)) {
                    throw "Battle skill coefficient $property is invalid for $key."
                }
            }
        }
        $CharacterMessages | Where-Object key -CEQ $hero.nameKey
    }

    foreach ($skill in $skills) {
        $program = @($effects | Where-Object skillId -CEQ $skill.id | Sort-Object order)
        if ($program.Count -eq 0) { throw "Battle skill effect program is missing for $($skill.id)." }
        $stages = @('impact','after_hit','after_attack')
        $lastPhase = 0
        $hasDamage = $false
        for ($index = 0; $index -lt $program.Count; $index++) {
            $effect = $program[$index]
            $order = $index + 1
            $phase = [array]::IndexOf($stages, $effect.phase)
            $usesStatus = $effect.operation -cin @('apply_status','repeat_if_status')
            if ($effect.order -ne $order -or $effect.id -cne "$($skill.id).$order" -or $phase -lt $lastPhase -or
                $effect.operation -cnotin @('damage','heal','dispel','apply_status','readiness','dual_attack','repeat_if_status') -or
                $effect.recipient -cnotin @('targets','self','allies','target_and_lowest_ally') -or
                ($usesStatus -ne ($effect.status -cne 'none')) -or
                $effect.status -cnotin @('none','AttackUp','DefenseUp','Immunity','Barrier','Stun') -or
                (($effect.turns -gt 0) -ne ($effect.operation -ceq 'apply_status')) -or
                ($effect.phase -ceq 'after_hit' -and -not $hasDamage) -or
                ($effect.operation -ceq 'damage' -and ($null -eq $skill.attackRateBp -or $null -eq $skill.powerBp)) -or
                ($effect.operation -ceq 'heal' -and $null -eq $skill.casterHealthHealBp) -or
                ($effect.status -ceq 'Barrier' -and $null -eq $skill.casterHealthBarrierBp)) {
                throw "Battle skill effect sequence or arguments are invalid for $($effect.id)."
            }
            $lastPhase = $phase
            $hasDamage = $hasDamage -or $effect.operation -ceq 'damage'
        }
        $steps = @($enhancements | Where-Object skillId -CEQ $skill.id | Sort-Object step)
        if ($steps.Count -eq 0) { throw "Battle skill enhancement steps missing for $($skill.id)." }
        $cooldown = [int]$skill.cooldownTurns
        for ($index = 0; $index -lt $steps.Count; $index++) {
            $row = $steps[$index]
            $step = $index + 1
            if ($row.step -ne $step -or $row.id -cne "$($skill.id).e$step" -or
                $row.kind -cnotin @('damage_bp','healing_bp','effect_chance_bp','cooldown_turns')) {
                throw "Battle skill enhancement sequence is invalid for $($skill.id)."
            }
            if ($row.kind -ceq 'cooldown_turns') {
                $cooldown -= $row.value
                if ($cooldown -lt 0) { throw "Battle skill enhancement reduces cooldown below zero for $($skill.id)." }
            }
        }
    }

    $runtime = [ordered]@{
        schemaVersion = 2
        heroes = $heroes
        growthPresets = $growthPresets
        awakeningSteps = $awakeningSteps
        skills = $skills
        effects = $effects
        enhancements = $enhancements
        messages = @($messages) + @($heroNames)
    }
    [IO.File]::WriteAllText((Join-Path $JsonDir 'battle-catalog.json'), ($runtime | ConvertTo-Json -Depth 20) + "`n",
        [Text.UTF8Encoding]::new($false))
}
