function Confirm-BattleOriginalText {
    param([string]$Repo, [object[]]$Messages, [object[]]$Skills, [string[]]$Statuses)

    $original = Get-Content -LiteralPath (Join-Path $Repo 'Config/BattleText/original-zhs.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($original.schemaVersion -ne 1 -or $original.locale -cne 'zh-CN') { throw 'Invalid battle original-text source.' }
    $entries = @{}
    $text = @{}
    foreach ($message in $Messages) { $text.Add($message.key, [string]$message.zhCn) }
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        foreach ($property in $original.entries.PSObject.Properties) {
            $key = $property.Name
            $entries.Add($key, $property.Value)
            if (-not $text.ContainsKey($key)) { throw "Missing original battle text: $key." }
            $bytes = [Text.Encoding]::UTF8.GetBytes(($text[$key] -replace "`r`n", "`n"))
            $hash = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant()
            if ($hash -cne $property.Value.displaySha256) {
                throw "Original battle text has changed: $key. Run Tools/Config/Import-BattleDescriptions.py --sync."
            }
        }
    } finally { $sha.Dispose() }

    $expected = @('battle.inspect.dual_attack.name', 'battle.inspect.dual_attack.description')
    foreach ($skill in $Skills) {
        $expected += $skill.nameKey, $skill.descriptionKey
        if ($skill.soulBurnCost -gt 0) { $expected += "battle.skill.$($skill.id).burn.description" }
        if ($skill.speedUpNameKey) { $expected += $skill.speedUpNameKey, $skill.speedUpDescriptionKey }
    }
    foreach ($status in $Statuses) {
        $prefix = 'battle.status.' + $status.ToLowerInvariant()
        $expected += "$prefix.name", "$prefix.description"
    }
    foreach ($key in $expected) {
        if (-not $entries.ContainsKey($key)) { throw "Missing verified original battle text: $key." }
    }
}

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
            $hero.resourceTurnGain -gt $hero.resourceMaximum -or $hero.resourceAttackedSpend -gt $hero.resourceMaximum -or
            $hero.resourceOpeningGain -gt $hero.resourceMaximum -or $hero.resourceAttackedGain -gt $hero.resourceMaximum) {
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
            $iconDir = 'Assets/Game/Modules/BattleScene/Res/Textures/Hud/Skills'
            $iconNames = @("sk_${id}_$slot.png")
            if ($skill.target -ceq 'passive') { $iconNames += "pa_${id}_$slot.png" }
            if ($skill.iconAsset -cnotin @($iconNames | ForEach-Object { "$iconDir/$_" }) -or
                -not (Test-Path -LiteralPath (Join-Path $Repo $skill.iconAsset) -PathType Leaf)) {
                throw "Battle skill icon does not match the imported asset for $key."
            }
            $hasVariant = -not [string]::IsNullOrWhiteSpace($skill.speedUpPresentationId)
            if ($hasVariant) {
                if ($skill.target -ceq 'passive' -or $skill.speedUpPresentationId -ceq $skill.presentationId -or
                    $skill.speedUpPresentationId -cnotin $imported[0].skills.id -or
                    $skill.speedUpIconAsset -cnotmatch ('\A' + [regex]::Escape($iconDir) + '/sk_' + $id + '_[0-9]+\.png\z') -or
                    -not (Test-Path -LiteralPath (Join-Path $Repo $skill.speedUpIconAsset) -PathType Leaf) -or
                    $null -eq $skill.speedUpAttackRateBp -or $null -eq $skill.speedUpPowerBp -or
                    [string]::IsNullOrWhiteSpace($skill.speedUpNameKey) -or [string]::IsNullOrWhiteSpace($skill.speedUpDescriptionKey) -or
                    $skill.speedUpNameKey -cnotin $messages.key -or $skill.speedUpDescriptionKey -cnotin $messages.key) {
                    throw "Battle speed-up variant references or coefficients are invalid for $key."
                }
            } elseif ($skill.speedUpIconAsset -or $skill.speedUpNameKey -or $skill.speedUpDescriptionKey -or
                $null -ne $skill.speedUpAttackRateBp -or $null -ne $skill.speedUpPowerBp) {
                throw "Battle speed-up variant requires a presentation for $key."
            }
            if ($skill.nameKey -cne "battle.skill.$key.name" -or $skill.descriptionKey -cne "battle.skill.$key.description" -or
                $skill.target -cnotin @('one_enemy','two_enemies','all_enemies','one_ally','one_other_ally','one_ally_including_dead','all_allies','self','passive')) {
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
            if ($skill.burnPresentationId -and ($skill.soulBurnCost -le 0 -or
                $skill.burnPresentationId -ceq $skill.presentationId -or $skill.burnPresentationId -cnotin $imported[0].skills.id)) {
                throw "Battle Soul Burn presentation is invalid for $key."
            }
            $burn = $skill.soulBurnKind
            $turns = $skill.soulBurnTurns
            if ($burn -cnotin @('none','reduce_cooldown','increase_damage','increase_damage_no_dual','increase_healing','increase_healing_no_dual','extend_granted_buffs','extra_turn','refill_resource','ignore_resistance') -or
                (($skill.soulBurnCost -eq 0) -ne ($burn -ceq 'none')) -or
                (($turns -gt 0) -ne ($burn -cin @('reduce_cooldown','extend_granted_buffs'))) -or
                ($burn -ceq 'refill_resource' -and ($skill.resourceCost -le 0 -or $hero.resourceMaximum -eq 0)) -or
                ($burn -ceq 'reduce_cooldown' -and $turns -gt $skill.cooldownTurns) -or
                ($null -ne $skill.burnAttackRateBp -and $burn -cnotin @('increase_damage','increase_damage_no_dual')) -or
                ($burn -ceq 'increase_damage_no_dual' -and ($null -eq $skill.burnAttackRateBp -or $skill.slot -ne 1))) {
                throw "Battle skill soul burn is invalid for $key."
            }
            foreach ($property in @('attackRateBp','powerBp','casterHealthDamageBp','casterHealthHealBp','casterHealthBarrierBp','burnAttackRateBp',
                'casterDefenseBarrierBp','casterAttackBarrierBp','casterDefenseDamageBp','casterSpeedDamagePpm','burnSpeedDamagePpm',
                'casterHealthRatioDamageBp','damageAgainstBuffBp','defensePenetrationBp','stealthPenetrationBp','highestDefensePenetrationBp',
                'speedUpAttackRateBp','speedUpPowerBp')) {
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

    $beneficialStatuses = @('AttackUp','DefenseUp','Immunity','Barrier','Counter','SpeedUp','EvasionUp','CriticalResistanceUp','Stealth','Indomitable',
        'GreaterSpeedUp','CriticalDamageUp','EffectivenessUp','Invincible','CriticalChanceUp','ContinuousHealing',
        'Immortality','GreaterAttackUp','DamageLimit','SpiritlordProtection','Rage')
    $harmfulStatuses = @('Stun','SpeedDown','AttackDown','DefenseDown','Unhealable','Silence','Provoke','Poison','Burn','Bleed','HitChanceDown','Unbuffable')
    $validStatuses = @('none') + $beneficialStatuses + $harmfulStatuses
    Confirm-BattleOriginalText -Repo $Repo -Messages $messages -Skills $skills -Statuses ($beneficialStatuses + $harmfulStatuses)
    foreach ($skill in $skills) {
        $program = @($effects | Where-Object skillId -CEQ $skill.id | Sort-Object order)
        $passive = $skill.target -ceq 'passive'
        $triggersAttack = @($program | Where-Object phase -CEQ 'after_ally_attack').Count -gt 0
        if ($program.Count -eq 0 -or ($passive -and
            ($skill.soulGain -ne 0 -or $skill.soulBurnCost -ne 0 -or ($skill.cooldownTurns -gt 0 -and -not $triggersAttack -and -not @($program | Where-Object phase -CEQ 'on_lethal_damage').Count)))) {
            throw "Battle skill effect program or passive activation is invalid for $($skill.id)."
        }
        $activeStages = @('before_attack','impact','after_hit','after_attack','after_ally_skill','battle_start','after_enemy_defeated')
        $passiveOperations = @{
            impact = @('damage_bonus')
            on_attacked = @('cleanse','readiness','readiness_bonus','counterattack','cleanse_once_on_critical')
            damage_guard = @('damage_reduction')
            after_ally_attack = @('trigger','damage','apply_status','readiness')
            after_caster_critical = @('apply_status','random_buff','random_buff_option','readiness')
            after_caster_kill = @('trigger','apply_status','damage','grant_soul')
            on_lethal_damage = @('life_floor','apply_status','reset_cooldown','heal_target_health')
            battle_start = @('apply_status','opening_attack_from_health')
            turn_end = @('apply_status')
        }
        $activeOperations = @('damage','heal','lifesteal','injury','heal_target_health','target_health_barrier','dispel','cleanse','cleanse_all','apply_status','readiness','reduce_readiness','reduce_readiness_double','transfer_debuff',
            'extend_cooldown','reduce_cooldown','reset_cooldown','grant_extra_turn','dual_attack','repeat_if_status','damage_bonus','damage_bonus_per_debuff','critical_damage_bonus','defense_penetration','attack_rate_bonus','defense_rate_bonus','health_rate_bonus','burn_health_rate_bonus','burn_healing_rate_bonus','elemental_advantage',
            'target_health_rate_bonus','resource_gain','resource_spend_all','ignore_resistance','presentation_from_slot','trigger_skill',
            'trigger_skill_no_soul','triggered_attack_rate_override','reduce_own_cooldown','revive','extend_buffs','cooldown_immune',
            'dispel_all','burn_effect_chance_bonus','attack_rate_override','caster_missing_health_damage_bonus',
            'damage_bonus_per_missing_target','readiness_fixed')
        $lastPhase = 0
        $hasDamage = $false
        for ($index = 0; $index -lt $program.Count; $index++) {
            $effect = $program[$index]
            $order = $index + 1
            $phase = [array]::IndexOf($activeStages, $effect.phase)
            $operation = $effect.operation
            $usesStatus = $operation -cin @('apply_status','repeat_if_status','random_buff_option')
            $isModifier = $operation -cin @('damage_bonus','damage_bonus_per_debuff','critical_damage_bonus','defense_penetration','attack_rate_bonus','defense_rate_bonus','health_rate_bonus','burn_health_rate_bonus','burn_healing_rate_bonus','elemental_advantage')
            $usesResourceThreshold = $isModifier -and $effect.condition -ceq 'caster_has_resource'
            $validPhase = if ($passive) {
                $effect.phase -cin $passiveOperations.Keys -and $operation -cin $passiveOperations[$effect.phase]
            } else {
                $phase -ge $lastPhase -and $operation -cin $activeOperations -and (-not $isModifier -or $effect.phase -ceq 'impact')
            }
            if ($effect.order -ne $order -or $effect.id -cne "$($skill.id).$order" -or -not $validPhase -or
                $effect.recipient -cnotin @('targets','self','allies','other_allies','target_and_lowest_ally') -or
                ($usesStatus -ne ($effect.status -cne 'none')) -or
                $effect.status -cnotin $validStatuses -or
                $effect.condition -cnotin @('none','caster_health_below_half','caster_health_at_least_half',
                    'caster_health_above_half','caster_health_at_most_half','caster_has_resource','caster_buffed','recipient_buffed','recipient_debuffed_after_skill_awakening','recipient_health_at_most_half','single_target_attack',
                    'target_hit_critical','attack_critical','attack_critical_after_skill_awakening','caster_indomitable',
                    'caster_not_indomitable','caster_skill_awakened','caster_skill_not_awakened','target_readiness_drained','caster_health_below_three_quarters',
                    'caster_speed_buffed','caster_speed_buffed_after_skill_awakening','defeated_target','enemy_debuffed','resource_full','resource_not_full','resource_full_after_awakening',
                    'caster_buffed_non_dual','caster_buffed_non_dual_burn','caster_at_least_three_buffs','caster_fewer_than_three_buffs',
                    'recipient_bleeding','caster_enraged','soul_burn','no_soul_burn','basic_or_ultimate_kill') -or
                $effect.chanceBonusStatus -cnotin @('none','AttackDown') -or
                (($effect.chanceBonusStatus -ceq 'none') -ne ($effect.chanceBonusBp -eq 0)) -or
                ($operation -cin @('cleanse','dispel','reduce_readiness','reduce_readiness_double','transfer_debuff','extend_cooldown') -and $effect.value -le 0) -or
                ($operation -cin @('extend_cooldown','transfer_debuff') -and $effect.value -gt 4) -or
                ($operation -cin @('reduce_readiness_double','transfer_debuff') -and
                    ($effect.phase -cne 'after_hit' -or $effect.recipient -cne 'targets')) -or
                ($effect.condition -ceq 'target_readiness_drained' -and
                    ($effect.phase -cne 'after_hit' -or $effect.recipient -cne 'targets')) -or
                (($effect.turns -gt 0) -ne ($operation -cin @('apply_status','target_health_barrier','random_buff_option') -or $usesResourceThreshold)) -or
                ($effect.phase -ceq 'after_hit' -and -not $hasDamage) -or
                ($operation -ceq 'damage' -and ($null -eq $skill.attackRateBp -or $null -eq $skill.powerBp)) -or
                ($operation -ceq 'heal' -and $null -eq $skill.casterHealthHealBp) -or
                ($operation -cin @('heal_target_health','target_health_barrier') -and
                    ($effect.value -lt 1 -or $effect.value -gt 10000 -or $effect.chanceBp -ne 10000)) -or
                ($effect.status -ceq 'Barrier' -and
                    @(@($skill.casterHealthBarrierBp,$skill.casterDefenseBarrierBp,$skill.casterAttackBarrierBp) |
                        Where-Object { $null -ne $_ }).Count -ne 1)) {
                throw "Battle skill effect sequence or arguments are invalid for $($effect.id)."
            }
            $lifecycle = $effect.phase -cin @('battle_start','turn_end')
            if (($isModifier -and ($effect.recipient -cne 'self' -or $effect.chanceBp -ne 10000 -or
                    $effect.value -lt 1 -or $effect.value -gt 10000 -or
                    ($effect.condition -cnotin @('caster_buffed','caster_has_resource') -and
                        -not ($operation -ceq 'damage_bonus' -and $effect.condition -cin @('recipient_debuffed_after_skill_awakening','caster_enraged')) -and
                        -not ($operation -cin @('critical_damage_bonus','damage_bonus_per_debuff','burn_healing_rate_bonus') -and $effect.condition -ceq 'none') -and
                        -not ($operation -ceq 'elemental_advantage' -and $effect.condition -ceq 'caster_speed_buffed_after_skill_awakening') -and
                        -not ($operation -cin @('attack_rate_bonus','defense_rate_bonus') -and
                            $effect.condition -cin @('caster_indomitable','caster_health_below_three_quarters','resource_full')) -and
                        -not ($operation -cin @('health_rate_bonus','burn_health_rate_bonus') -and
                            $effect.condition -cin @('none','caster_health_below_three_quarters'))))) -or
                ($operation -ceq 'burn_health_rate_bonus' -and $skill.soulBurnKind -cne 'increase_damage') -or
                ($operation -ceq 'burn_healing_rate_bonus' -and $skill.soulBurnKind -cnotin @('increase_healing','increase_healing_no_dual')) -or
                ($operation -ceq 'lifesteal' -and ($effect.phase -cne 'after_attack' -or $effect.recipient -cne 'self' -or
                    $effect.chanceBp -ne 10000 -or $effect.value -lt 1 -or $effect.value -gt 10000 -or -not $hasDamage)) -or
                ($operation -ceq 'injury' -and ($effect.phase -cne 'after_hit' -or $effect.recipient -cne 'targets' -or
                    $effect.chanceBp -ne 10000 -or $effect.value -lt 1 -or $effect.value -gt 5000)) -or
                ($operation -ceq 'opening_attack_from_health' -and ($effect.condition -cne 'none' -or
                    $effect.value -lt 1 -or $effect.value -gt 10000)) -or
                ($operation -ceq 'cleanse_once_on_critical' -and ($effect.chanceBp -ne 10000 -or
                    $effect.value -ne 1 -or $effect.condition -cne 'none' -or $effect.chanceBonusBp -ne 0)) -or
                ($operation -ceq 'cleanse_all' -and ($effect.value -ne 0 -or $effect.chanceBp -ne 10000)) -or
                ($operation -ceq 'readiness_bonus' -and ($effect.chanceBp -ne 10000 -or $effect.value -le 0)) -or
                ($effect.condition -ceq 'target_hit_critical' -and
                    ($effect.phase -cne 'after_hit' -or $effect.recipient -cne 'targets')) -or
                ($effect.condition -cin @('attack_critical','attack_critical_after_skill_awakening') -and
                    ($effect.phase -cne 'after_attack' -or $effect.recipient -cne 'self' -or -not $hasDamage)) -or
                ($operation -ceq 'reduce_cooldown' -and ($effect.phase -cnotin @('impact','after_attack') -or
                    $effect.recipient -cnotin @('self','targets','allies','other_allies') -or $effect.value -lt 1 -or $effect.value -gt 99 -or $effect.chanceBp -ne 10000)) -or
                ($operation -ceq 'grant_extra_turn' -and ($effect.phase -cne 'after_attack' -or
                    $effect.recipient -cne 'self' -or $effect.value -ne 0 -or $effect.chanceBp -ne 10000)) -or
                ($operation -ceq 'reset_cooldown' -and ($effect.phase -cnotin @('after_attack','on_lethal_damage') -or
                    $effect.recipient -cne 'self' -or $effect.chanceBp -ne 10000 -or
                    @($skills | Where-Object { $_.heroId -ceq $skill.heroId -and $_.slot -eq $effect.value -and
                        ($_.id -cne $skill.id -or $effect.condition -ceq 'defeated_target') -and $_.cooldownTurns -gt 0 }).Count -ne 1)) -or
                ($effect.phase -ceq 'after_ally_skill' -and ($passive -or $skill.cooldownTurns -le 0 -or
                    $operation -cne 'readiness' -or $effect.recipient -cne 'self' -or $effect.chanceBp -ne 10000 -or
                    $effect.value -le 0 -or $effect.condition -cnotin @('none','enemy_debuffed'))) -or
                ($effect.condition -ceq 'enemy_debuffed' -and $effect.phase -cne 'after_ally_skill') -or
                ($effect.condition -ceq 'defeated_target' -and
                    ($operation -cnotin @('grant_extra_turn','reset_cooldown','extend_buffs') -or -not $hasDamage)) -or
                ($effect.status -ceq 'Poison' -and $effect.value -ne 500) -or
                ($effect.condition -ceq 'caster_speed_buffed' -and
                    ($effect.phase -cne 'after_hit' -or -not $skill.speedUpPresentationId)) -or
                ($effect.phase -ceq 'after_caster_critical' -and
                    ($effect.recipient -cnotin @('self','other_allies') -or $effect.condition -cne 'none' -or $effect.chanceBonusBp -ne 0 -or
                    ($operation -cnotin @('random_buff','readiness') -and ($effect.chanceBp -ne 10000 -or $effect.status -cnotin $beneficialStatuses)))) -or
                ($operation -ceq 'damage_reduction' -and ($effect.recipient -cnotin @('self','allies') -or
                    $effect.chanceBp -ne 10000 -or $effect.value -lt 1 -or $effect.value -gt 10000 -or
                    $effect.condition -cnotin @('none','recipient_buffed','single_target_attack'))) -or
                ($operation -ceq 'counterattack' -and ($effect.recipient -cne 'self' -or $effect.value -ne 0)) -or
                ($operation -ceq 'trigger' -and $effect.phase -ceq 'after_ally_attack' -and ($order -ne 1 -or $effect.recipient -cne 'targets' -or
                    $effect.chanceBp -ne 10000 -or $effect.value -ne 0 -or $effect.chanceBonusBp -ne 0 -or
                    $effect.condition -cnotin @('none','recipient_health_at_most_half'))) -or
                ($lifecycle -and ($effect.chanceBp -ne 10000 -or ($operation -ceq 'apply_status' -and $effect.status -cnotin $beneficialStatuses) -or
                    $effect.chanceBonusBp -ne 0)) -or
                (($lifecycle -or $effect.phase -ceq 'on_attacked') -and $effect.recipient -cne 'self') -or
                ($triggersAttack -and $operation -ceq 'damage' -and $effect.recipient -cne 'targets')) {
                throw "Battle triggered effect arguments are invalid for $($effect.id)."
            }
            $lastPhase = $phase
            $hasDamage = $hasDamage -or $operation -ceq 'damage'
        }
        if ($skill.soulBurnKind -cin @('increase_healing','increase_healing_no_dual') -and
            @($program | Where-Object { $_.operation -cin @('heal','heal_target_health') }).Count -eq 0) {
            throw "Battle healing Soul Burn requires a healing effect for $($skill.id)."
        }
        if ($triggersAttack -and ($program[0].operation -cne 'trigger' -or -not $hasDamage)) {
            throw "Passive attack requires a trigger and a damage effect for $($skill.id)."
        }
        $randomBuffs = @($program | Where-Object operation -CEQ 'random_buff')
        $randomBuffOptions = @($program | Where-Object operation -CEQ 'random_buff_option')
        if ($randomBuffs.Count -gt 1 -or (($randomBuffs.Count -eq 1) -ne ($randomBuffOptions.Count -gt 0)) -or
            @($randomBuffOptions.status | Sort-Object -Unique).Count -ne $randomBuffOptions.Count) {
            throw "Battle critical passive has an invalid random buff pool for $($skill.id)."
        }
        $steps = @($enhancements | Where-Object skillId -CEQ $skill.id | Sort-Object step)
        if (-not $passive -and $steps.Count -eq 0) { throw "Battle skill enhancement steps missing for $($skill.id)." }
        $cooldown = [int]$skill.cooldownTurns
        $resourceCost = [int]$skill.resourceCost
        for ($index = 0; $index -lt $steps.Count; $index++) {
            $row = $steps[$index]
            $step = $index + 1
            if ($row.step -ne $step -or $row.id -cne "$($skill.id).e$step" -or
                $row.kind -cnotin @('damage_bp','healing_bp','effect_chance_bp','cooldown_turns','readiness_bp','soul_gain','counter_chance_bp','barrier_bp','resource_cost')) {
                throw "Battle skill enhancement sequence is invalid for $($skill.id)."
            }
            if (($row.kind -ceq 'counter_chance_bp' -and -not @($program | Where-Object operation -CEQ 'counterattack').Count) -or
                ($row.kind -ceq 'barrier_bp' -and -not @($program | Where-Object { $_.status -ceq 'Barrier' -or $_.operation -ceq 'target_health_barrier' }).Count)) {
                throw "Battle enhancement has no matching effect for $($row.id)."
            }
            if ($row.kind -ceq 'resource_cost') {
                $resourceCost -= $row.value
                if ($row.value -le 0 -or $resourceCost -lt 0) { throw "Battle skill enhancement has invalid resource cost for $($skill.id)." }
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
