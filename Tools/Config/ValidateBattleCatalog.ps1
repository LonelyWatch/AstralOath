function Export-BattleCatalog {
    param([string]$Repo, [string]$JsonDir, $CharacterCatalog, [object[]]$CharacterMessages)

    $heroes = @(Get-Content -LiteralPath (Join-Path $JsonDir 'battle_battleheroes.json') -Raw -Encoding UTF8 | ConvertFrom-Json)
    $skills = @(Get-Content -LiteralPath (Join-Path $JsonDir 'battle_battleskills.json') -Raw -Encoding UTF8 | ConvertFrom-Json)
    $enhancements = @(Get-Content -LiteralPath (Join-Path $JsonDir 'battle_battleskillenhancements.json') -Raw -Encoding UTF8 | ConvertFrom-Json)
    $messages = @(Get-Content -LiteralPath (Join-Path $JsonDir 'battle_battlemessages.json') -Raw -Encoding UTF8 | ConvertFrom-Json)

    if ($heroes.Count -eq 0) { throw 'Battle catalog requires at least one hero.' }
    foreach ($message in $messages) {
        if ($message.key -cnotmatch '\Abattle\.[a-z][a-z0-9_]*(?:\.[a-z][a-z0-9_]*)+\z' -or
            [string]::IsNullOrWhiteSpace($message.zhCn)) {
            throw "Battle localization is invalid: $($message.key)."
        }
    }

    $heroNames = foreach ($hero in $heroes) {
        $id = $hero.id
        if ($id -cnotmatch '\Ac[0-9]{4}\z' -or $hero.element -cnotin @('fire','ice','wind','light','dark') -or
            $hero.heroClass -cnotin @('knight','warrior','thief','ranger','mage','soul_weaver') -or
            $hero.zodiac -cnotin @('aries','taurus','gemini','cancer','leo','virgo','libra','scorpio','sagittarius','capricorn','aquarius','pisces') -or
            $hero.variant -cnotmatch '\A[a-z][a-z0-9_]*\z' -or
            $hero.baseStars -gt $hero.stars -or $hero.awakenings -gt $hero.stars) {
            throw "Battle hero profile is invalid: $id."
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
        schemaVersion = 1
        heroes = $heroes
        skills = $skills
        enhancements = $enhancements
        messages = @($messages) + @($heroNames)
    }
    [IO.File]::WriteAllText((Join-Path $JsonDir 'battle-catalog.json'), ($runtime | ConvertTo-Json -Depth 20) + "`n",
        [Text.UTF8Encoding]::new($false))
}
