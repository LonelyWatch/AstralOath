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
        if ($hero.resourceKind -cnotin @('none','focus','fighting_spirit') -or
            (($hero.resourceKind -ceq 'none') -ne ($hero.resourceMaximum -eq 0)) -or
            $hero.resourceTurnGain -gt $hero.resourceMaximum -or $hero.resourceAttackedSpend -gt $hero.resourceMaximum) {
            throw "Battle hero personal resource is invalid for $id."
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
                $skill.target -cnotin @('one_enemy','two_enemies','all_enemies','one_ally','all_allies','passive')) {
                throw "Battle skill text or targeting is invalid for $key."
            }
            if ($skill.resourceCost -gt $hero.resourceMaximum -or
                $skill.resourceGain -gt $hero.resourceMaximum -or
                $skill.resourceGainWhenBuffed -gt $hero.resourceMaximum -or
                ($skill.resourceGainWhenBuffed -gt 0 -and $skill.resourceGainWhenBuffed -lt $skill.resourceGain) -or
                ($skill.target -ceq 'passive' -and ($skill.resourceCost -gt 0 -or $skill.resourceGain -gt 0 -or
                    $skill.resourceGainWhenBuffed -gt 0))) {
                throw "Battle skill personal resource is invalid for $key."
            }
            $burn = $skill.soulBurnKind
            $turns = $skill.soulBurnTurns
            if ($burn -cnotin @('none','reduce_cooldown','increase_damage','extend_granted_buffs','extra_turn','refill_resource') -or
                (($skill.soulBurnCost -eq 0) -ne ($burn -ceq 'none')) -or
                (($turns -gt 0) -ne ($burn -cin @('reduce_cooldown','extend_granted_buffs'))) -or
                ($burn -ceq 'refill_resource' -and ($skill.resourceCost -le 0 -or $hero.resourceMaximum -eq 0)) -or
                ($burn -ceq 'reduce_cooldown' -and $turns -gt $skill.cooldownTurns) -or
                ($null -ne $skill.burnAttackRateBp -and $burn -cne 'increase_damage')) {
                throw "Battle skill soul burn is invalid for $key."
            }
            foreach ($property in @('attackRateBp','powerBp','casterHealthDamageBp','casterHealthHealBp','casterHealthBarrierBp','burnAttackRateBp',
                'casterDefenseBarrierBp','casterAttackBarrierBp','casterDefenseDamageBp','casterSpeedDamagePpm','burnSpeedDamagePpm',
                'casterHealthRatioDamageBp','damageAgainstBuffBp','defensePenetrationBp','stealthPenetrationBp','highestDefensePenetrationBp')) {
                $coefficient = $skill.$property
                $limit = if ($property.EndsWith('PenetrationBp', [StringComparison]::OrdinalIgnoreCase)) { 10000 } else { 100000 }
                if ($null -ne $coefficient -and ($coefficient -lt 1 -or $coefficient -gt $limit)) {
                    throw "Battle skill coefficient $property is invalid for $key."
                }
            }
            if (($null -ne $skill.burnSpeedDamagePpm -and $burn -cne 'increase_damage') -or
                ([int]$skill.defensePenetrationBp + [int]$skill.stealthPenetrationBp + [int]$skill.highestDefensePenetrationBp -gt 10000) -or
                (($skill.aoeWhenBuffed -or $skill.aoeOutsideTurn) -and $skill.target -cne 'one_enemy')) {
                throw "Battle skill conditional modifiers are invalid for $key."
            }
        }
        $CharacterMessages | Where-Object key -CEQ $hero.nameKey
    }

    $beneficialStatuses = @('AttackUp','DefenseUp','Immunity','Barrier','Counter','SpeedUp','EvasionUp','CriticalResistanceUp','Stealth')
    $harmfulStatuses = @('Stun','SpeedDown','AttackDown','DefenseDown','Unhealable','Silence','Provoke')
    $validStatuses = @('none') + $beneficialStatuses + $harmfulStatuses
    foreach ($skill in $skills) {
        $program = @($effects | Where-Object skillId -CEQ $skill.id | Sort-Object order)
        $passive = $skill.target -ceq 'passive'
        $triggersAttack = @($program | Where-Object phase -CEQ 'after_ally_attack').Count -gt 0
        if ($program.Count -eq 0 -or ($passive -and
            ($skill.soulGain -ne 0 -or $skill.soulBurnCost -ne 0 -or ($skill.cooldownTurns -gt 0 -and -not $triggersAttack)))) {
            throw "Battle skill effect program or passive activation is invalid for $($skill.id)."
        }
        $activeStages = @('impact','after_hit','after_attack')
        $passiveOperations = @{
            impact = @('damage_bonus')
            on_attacked = @('cleanse','readiness','counterattack')
            damage_guard = @('damage_reduction')
            after_ally_attack = @('trigger','damage','apply_status','readiness')
            battle_start = @('apply_status')
            turn_end = @('apply_status')
        }
        $activeOperations = @('damage','heal','dispel','cleanse','apply_status','readiness','reduce_readiness',
            'extend_cooldown','reduce_cooldown','grant_extra_turn','dual_attack','repeat_if_status','damage_bonus','critical_damage_bonus','defense_penetration')
        $lastPhase = 0
        $hasDamage = $false
        for ($index = 0; $index -lt $program.Count; $index++) {
            $effect = $program[$index]
            $order = $index + 1
            $phase = [array]::IndexOf($activeStages, $effect.phase)
            $operation = $effect.operation
            $usesStatus = $operation -cin @('apply_status','repeat_if_status')
            $isModifier = $operation -cin @('damage_bonus','critical_damage_bonus','defense_penetration')
            $usesResourceThreshold = $isModifier -and $effect.condition -ceq 'caster_has_resource'
            $validPhase = if ($passive) {
                $effect.phase -cin $passiveOperations.Keys -and $operation -cin $passiveOperations[$effect.phase]
            } else {
                $phase -ge $lastPhase -and $operation -cin $activeOperations -and (-not $isModifier -or $effect.phase -ceq 'impact')
            }
            if ($effect.order -ne $order -or $effect.id -cne "$($skill.id).$order" -or -not $validPhase -or
                $effect.recipient -cnotin @('targets','self','allies','target_and_lowest_ally') -or
                ($usesStatus -ne ($effect.status -cne 'none')) -or
                $effect.status -cnotin $validStatuses -or
                $effect.condition -cnotin @('none','caster_health_below_half','caster_health_at_least_half',
                    'caster_health_above_half','caster_health_at_most_half','caster_has_resource','caster_buffed','recipient_buffed','recipient_debuffed_after_skill_awakening','recipient_health_at_most_half','single_target_attack',
                    'target_hit_critical','attack_critical','attack_critical_after_skill_awakening') -or
                $effect.chanceBonusStatus -cnotin @('none','AttackDown') -or
                (($effect.chanceBonusStatus -ceq 'none') -ne ($effect.chanceBonusBp -eq 0)) -or
                ($operation -cin @('cleanse','dispel','reduce_readiness','extend_cooldown') -and $effect.value -le 0) -or
                ($operation -ceq 'extend_cooldown' -and $effect.value -gt 4) -or
                (($effect.turns -gt 0) -ne ($operation -ceq 'apply_status' -or $usesResourceThreshold)) -or
                ($effect.phase -ceq 'after_hit' -and -not $hasDamage) -or
                ($operation -ceq 'damage' -and ($null -eq $skill.attackRateBp -or $null -eq $skill.powerBp)) -or
                ($operation -ceq 'heal' -and $null -eq $skill.casterHealthHealBp) -or
                ($effect.status -ceq 'Barrier' -and
                    @(@($skill.casterHealthBarrierBp,$skill.casterDefenseBarrierBp,$skill.casterAttackBarrierBp) |
                        Where-Object { $null -ne $_ }).Count -ne 1)) {
                throw "Battle skill effect sequence or arguments are invalid for $($effect.id)."
            }
            $lifecycle = $effect.phase -cin @('battle_start','turn_end')
            if (($isModifier -and ($effect.recipient -cne 'self' -or $effect.chanceBp -ne 10000 -or
                    $effect.value -lt 1 -or $effect.value -gt 10000 -or
                    ($effect.condition -cnotin @('caster_buffed','caster_has_resource') -and
                        -not ($operation -ceq 'damage_bonus' -and $effect.condition -ceq 'recipient_debuffed_after_skill_awakening') -and
                        -not ($operation -ceq 'critical_damage_bonus' -and $effect.condition -ceq 'none')))) -or
                ($effect.condition -ceq 'target_hit_critical' -and
                    ($effect.phase -cne 'after_hit' -or $effect.recipient -cne 'targets')) -or
                ($effect.condition -cin @('attack_critical','attack_critical_after_skill_awakening') -and
                    ($effect.phase -cne 'after_attack' -or $effect.recipient -cne 'self' -or -not $hasDamage)) -or
                ($operation -ceq 'reduce_cooldown' -and ($effect.phase -cne 'after_attack' -or
                    $effect.recipient -cne 'self' -or $effect.value -lt 1 -or $effect.value -gt 4 -or $effect.chanceBp -ne 10000)) -or
                ($operation -ceq 'grant_extra_turn' -and ($effect.phase -cne 'after_attack' -or
                    $effect.recipient -cne 'self' -or $effect.value -ne 0 -or $effect.chanceBp -ne 10000)) -or
                ($operation -ceq 'damage_reduction' -and ($effect.recipient -cnotin @('self','allies') -or
                    $effect.chanceBp -ne 10000 -or $effect.value -lt 1 -or $effect.value -gt 10000 -or
                    $effect.condition -cnotin @('none','recipient_buffed','single_target_attack'))) -or
                ($operation -ceq 'counterattack' -and ($effect.recipient -cne 'self' -or $effect.value -ne 0)) -or
                ($operation -ceq 'trigger' -and ($order -ne 1 -or $effect.recipient -cne 'targets' -or
                    $effect.chanceBp -ne 10000 -or $effect.value -ne 0 -or $effect.chanceBonusBp -ne 0 -or
                    $effect.condition -cnotin @('none','recipient_health_at_most_half'))) -or
                ($lifecycle -and ($effect.chanceBp -ne 10000 -or $effect.status -cnotin $beneficialStatuses -or
                    $effect.chanceBonusBp -ne 0)) -or
                (($lifecycle -or $effect.phase -ceq 'on_attacked') -and $effect.recipient -cne 'self') -or
                ($triggersAttack -and $operation -ceq 'damage' -and $effect.recipient -cne 'targets')) {
                throw "Battle triggered effect arguments are invalid for $($effect.id)."
            }
            $lastPhase = $phase
            $hasDamage = $hasDamage -or $operation -ceq 'damage'
        }
        if ($triggersAttack -and ($program[0].operation -cne 'trigger' -or -not $hasDamage)) {
            throw "Passive attack requires a trigger and a damage effect for $($skill.id)."
        }
        $steps = @($enhancements | Where-Object skillId -CEQ $skill.id | Sort-Object step)
        if (-not $passive -and $steps.Count -eq 0) { throw "Battle skill enhancement steps missing for $($skill.id)." }
        $cooldown = [int]$skill.cooldownTurns
        for ($index = 0; $index -lt $steps.Count; $index++) {
            $row = $steps[$index]
            $step = $index + 1
            if ($row.step -ne $step -or $row.id -cne "$($skill.id).e$step" -or
                $row.kind -cnotin @('damage_bp','healing_bp','effect_chance_bp','cooldown_turns','readiness_bp','soul_gain','counter_chance_bp','barrier_bp')) {
                throw "Battle skill enhancement sequence is invalid for $($skill.id)."
            }
            if (($row.kind -ceq 'counter_chance_bp' -and -not @($program | Where-Object operation -CEQ 'counterattack').Count) -or
                ($row.kind -ceq 'barrier_bp' -and -not @($program | Where-Object status -CEQ 'Barrier').Count)) {
                throw "Battle enhancement has no matching effect for $($row.id)."
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
