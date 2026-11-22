using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleRoster51To60Tests
{
    [Theory]
    [InlineData("c1060")]
    [InlineData("c1065")]
    [InlineData("c1066")]
    [InlineData("c1067")]
    [InlineData("c1069")]
    [InlineData("c1070")]
    [InlineData("c1071")]
    [InlineData("c1072")]
    [InlineData("c1073")]
    [InlineData("c1074")]
    public void OriginalSlotsSubmitThroughBattleAndEmitTheirOwnPresentation(string hero)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        var catalog = JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
        var skills = catalog.skills.Where(skill => skill.heroId == hero).OrderBy(skill => skill.slot).ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, skills.Select(skill => skill.slot));
        var preview = BattlePreviewFactory.Create("roster-preview", 1, catalog, false, Enumerable.Repeat(hero, 4).ToArray());
        var previewAi = new BattleAiPolicy(catalog);
        foreach (var passive in skills.Where(row => row.target == "passive"))
            Assert.False(previewAi.IsSkillAvailable(preview.Snapshot, passive.id));
        var basic = Cast(preview, previewAi.ChooseTarget(preview.Snapshot, hero + ".s1"));
        Assert.Contains(basic.Events, e => e.Origin == BattleActionOrigin.Primary && e.SkillId == hero + ".s1" &&
            !string.IsNullOrEmpty(e.PresentationId));

        foreach (var skill in skills.Where(skill => skill.target != "passive"))
        {
            var match = Encounter(hero);
            var state = match.Snapshot;
            var actor = state.CurrentActorId;
            Assert.Equal(hero, state.GetUnit(actor).DefinitionId);
            var ai = new BattleAiPolicy(catalog);
            for (var turn = 0; !ai.IsSkillAvailable(match.Snapshot, skill.id) && turn < 12; turn++)
            {
                Cast(match, ai.ChooseTarget(match.Snapshot, hero + ".s1"));
                AdvanceTo(match, actor, ai);
            }
            var result = Cast(match, ai.ChooseTarget(match.Snapshot, skill.id));
            var action = Assert.Single(result.Events, evt => evt.Kind == BattleFlowEventKind.Action &&
                evt.Origin == BattleActionOrigin.Primary && evt.SkillId == skill.id);
            if (skill.id == "c1066.s1")
                Assert.Contains(action.PresentationId, new[] { "c_lunabianca_skill_01_1", "c_lunabianca_skill_01_2", "c_lunabianca_skill_01_3" });
            else
                Assert.Equal(skill.presentationId, action.PresentationId);
        }
    }

    [Fact]
    public void KrauBurnTargetsBothEnemiesAndUsesTheHorseBurnGraph()
    {
        var match = Encounter("c1070");
        var result = Use(match, "c1070.s3", null, true);
        Assert.Contains(result.Events, e => e.PresentationId == "c_krau_skill_03_2");
        var hits = result.Events.Where(e => e.Kind == BattleFlowEventKind.Damage && e.SourceId == "hero").ToArray();
        Assert.Equal(new[] { "enemy1", "enemy2" }, hits.Select(e => e.TargetId));
        Assert.All(hits, e => Assert.Equal(BattleHitKind.Normal, e.HitKind));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void KrauBurnUsesTheSameStartingHealthLossForBothEnemiesWhenOneReflects(int reflector)
    {
        var match = Encounter("c1070", reflectingEnemy: reflector);
        var result = Use(match, "c1070.s3", null, true);
        var damage = result.Events.Where(e => e.Kind == BattleFlowEventKind.Damage).ToArray();
        Assert.Contains(damage, e => e.SourceId == "enemy" + reflector && e.TargetId == "hero" && e.Amount > 0);
        var first = Assert.Single(damage, e => e.SourceId == "hero" && e.TargetId == "enemy1");
        var second = Assert.Single(damage, e => e.SourceId == "hero" && e.TargetId == "enemy2");
        Assert.Equal(first.Amount, second.Amount);
    }

    [Fact]
    public void BellonaUsesNewFocusForHerFanAndSpendsItOnlyOnce()
    {
        var match = Encounter("c1071");
        Use(match, "c1071.s3", "enemy1");
        Use(match, "c1071.s2", "enemy1");
        Assert.Equal(4, match.Snapshot.GetUnit("hero").Resource.Amount);
        var fan = Use(match, "c1071.s1", "enemy1");
        Assert.Single(fan.Events, e => e.SkillId == "c1071.s3" && e.Origin == BattleActionOrigin.ExtraAttack);
        Assert.Equal(0, fan.Snapshot.GetUnit("hero").Resource.Amount);
    }

    [Fact]
    public void BellonaKeepsFullFocusWhenHerBasicAttackLeavesNoTargetForTheFan()
    {
        var match = Encounter("c1071", targetDamage: 99999, lastTarget: true);
        var result = Use(match, "c1071.s1", "enemy1");
        Assert.False(result.Snapshot.GetUnit("enemy1").IsAlive);
        Assert.DoesNotContain(result.Events, e => e.Origin == BattleActionOrigin.ExtraAttack);
        Assert.Equal(5, result.Snapshot.GetUnit("hero").Resource.Amount);
    }

    [Fact]
    public void SigretFollowsTheSameWoundedTargetAndDoesNotRepeatOnAHealthyTarget()
    {
        BattleCommandResult CastAt(int damage)
        {
            var match = Encounter("c1072", damage);
            return Use(match, "c1072.s2", "enemy1");
        }
        Assert.DoesNotContain(CastAt(0).Events, e => e.Origin == BattleActionOrigin.ExtraAttack);
        var follow = Assert.Single(CastAt(60000).Events, e => e.Origin == BattleActionOrigin.ExtraAttack && e.Kind == BattleFlowEventKind.Action);
        Assert.Equal("enemy1", follow.TargetId);
        Assert.Equal("c1072.s1", follow.SkillId);
    }

    [Fact]
    public void KawerikConsumesTenSpiritForExactlyOneAdditionalAttack()
    {
        var match = Encounter("c1073");
        var hero = match.Snapshot.GetUnit("hero");
        Assert.Equal(100, hero.Resource.Amount);
        Assert.NotNull(hero.GetStatus(BattleStatusKind.MagicBarrier));
        for (int i = 0; i < 10; i++)
        {
            var result = Use(match, "c1073.s1", "enemy1");
            Assert.Single(result.Events, e => e.Kind == BattleFlowEventKind.Action && e.Origin == BattleActionOrigin.ExtraAttack);
            Assert.Equal(90 - 10 * i, result.Snapshot.GetUnit("hero").Resource.Amount);
        }
        Assert.Null(match.Snapshot.GetUnit("hero").GetStatus(BattleStatusKind.MagicBarrier));
        Assert.DoesNotContain(Use(match, "c1073.s1", "enemy1").Events, e => e.Origin == BattleActionOrigin.ExtraAttack);
    }

    [Theory]
    [InlineData(0, 6)]
    [InlineData(3, 5)]
    public void VioletUsesFullFocusToResetButterflyCut(int skillRank, int cooldownReduction)
    {
        var match = Encounter("c1074", violetThirdRank: skillRank);
        var normalTurn = Use(match, "c1074.s2", "hero");
        Assert.DoesNotContain(normalTurn.Events, e => e.CooldownReduction > 0);
        Assert.NotNull(match.Snapshot.GetUnit("hero").GetStatus(BattleStatusKind.Perception));
        Use(match, "c1074.s1", "enemy1");
        Use(match, "c1074.s1", "enemy1");
        Assert.Equal(5, match.Snapshot.GetUnit("hero").Resource.Amount);
        var result = Use(match, "c1074.s3", "enemy1");
        var feedback = Assert.Single(result.Events, e => e.CooldownReduction > 0);
        Assert.Equal("hero", feedback.TargetId);
        Assert.Equal("c1074.s3", feedback.SkillId);
        Assert.Equal(cooldownReduction, feedback.CooldownReduction);
        Assert.Equal(0, feedback.Amount);
        Assert.Equal(0, result.Snapshot.GetUnit("hero").GetCooldown("c1074.s3"));
        Assert.Equal(0, result.Snapshot.GetUnit("hero").Resource.Amount);
    }

    [Fact]
    public void MagicBarrierNullifiesTheIncomingSkillAndConsumesItsCost()
    {
        var match = Encounter("c1073", incomingAttack: true);
        var result = Use(match, BattlePreviewFactory.SlimeSkillId, "hero");
        Assert.Equal(0, Assert.Single(result.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "hero").Amount);
        Assert.Equal(90, result.Snapshot.GetUnit("hero").Resource.Amount);
    }

    [Fact]
    public void KrauGuardsOtherAlliesWhileHisHorseIsReady()
    {
        var match = Encounter("c1070", incomingAttack: true);
        var result = Use(match, BattlePreviewFactory.SlimeSkillId, "teammate");
        var redirected = Assert.Single(result.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "hero").Amount;
        var received = Assert.Single(result.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "teammate").Amount;
        Assert.True(redirected > 0);
        Assert.InRange(Math.Abs((double)redirected / (redirected + received) - .25), 0, .02);
    }

    [Fact]
    public void KawerikRegainsHisFieldOnRevivalWhenHeStillHasTenSpirit()
    {
        var match = Encounter("c1073", reviveAtOpening: true);
        var revived = Use(match, "fixture.revive", "hero");
        Assert.Contains(revived.Events, e => e.Kind == BattleFlowEventKind.UnitRevived && e.TargetId == "hero");
        Assert.Equal(100, revived.Snapshot.GetUnit("hero").Resource.Amount);
        Assert.NotNull(revived.Snapshot.GetUnit("hero").GetStatus(BattleStatusKind.MagicBarrier));
    }

    private static BattleMatch Encounter(string heroId, int targetDamage = 0, bool incomingAttack = false, bool reviveAtOpening = false,
        bool lastTarget = false, int reflectingEnemy = 0, int violetThirdRank = 0)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        var data = JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
        var row = data.heroes.Single(entry => entry.id == heroId);
        var growth = data.growthPresets.Single(entry => entry.id == row.growthPresetId);
        var preset = new HeroGrowthPreset(growth.id, growth.nativeStars, growth.attackCoefficientMilli,
            growth.healthCoefficientMilli, growth.defenseCoefficientMilli, growth.speedMilli);
        if (lastTarget) row.resourceOpeningGain = 4;
        var form = new BattleUnitForm(row.variant, heroId,
            data.skills.Where(s => s.heroId == heroId).OrderBy(s => s.slot).Select(s => s.id));
        var definition = new HeroDefinition(heroId, row.nameKey, Enum.Parse<BattleElement>(row.element, true),
            row.heroClass, row.zodiac, form, preset,
            data.awakeningSteps.Where(s => s.heroId == heroId).Select(s => new HeroAwakeningStep(s.rank, s.attackFlat,
                s.healthFlat, s.attackPercentBp, s.healthPercentBp, s.defensePercentBp, s.speedFlat, s.critChanceBp,
                s.critDamageBp, s.effectivenessBp, s.effectResistanceBp)), row.GetResourceDefinition());
        var hero = BattleUnit.CreateHero("hero", definition, new BattleSlot(BattleSide.Ally, 1), 60, 6, 6);
        var enemy = new MonsterDefinition("training", "training", BattleElement.Dark,
            new BattleUnitForm("normal", "training", new[] { BattlePreviewFactory.SlimeSkillId }));
        var enemies = Enumerable.Range(1, lastTarget ? 1 : 2).Select(n => BattleUnit.CreateMonster("enemy" + n, enemy,
            new BattleSlot(BattleSide.Enemy, n), 60, MonsterRank.Normal, new BattleStats(100, 100000, 300, incomingAttack && n == 1 ? 500 : 1))).ToArray();
        enemies[0].ApplyDamage(targetDamage);
        if (reflectingEnemy != 0)
        {
            hero.ApplyDamage(800);
            const string id = "fixture.reflect";
            data.skills = data.skills.Append(new BattleCatalogData.SkillRow { id = id, slot = 2, target = "passive" }).ToArray();
            var reflect = data.effects.Single(e => e.skillId == "c1037.s2" && e.status == "Reflect");
            data.effects = data.effects.Append(new BattleCatalogData.EffectRow { id = id + ".1", skillId = id, order = 1,
                phase = "battle_start", operation = "apply_status", recipient = "self", status = reflect.status,
                chanceBp = 10000, turns = reflect.turns, value = reflect.value }).ToArray();
            var defender = new MonsterDefinition("mirror", "mirror", BattleElement.Dark,
                new BattleUnitForm("normal", "mirror", new[] { BattlePreviewFactory.SlimeSkillId, id }));
            enemies[reflectingEnemy - 1] = BattleUnit.CreateMonster("enemy" + reflectingEnemy, defender,
                new BattleSlot(BattleSide.Enemy, reflectingEnemy), 60, MonsterRank.Normal, new BattleStats(100, 100000, 300, 1));
        }
        var roster = new List<BattleUnit> { hero };
        if (incomingAttack)
            roster.Add(BattleUnit.CreateMonster("teammate", enemy, new BattleSlot(BattleSide.Ally, 2), 60,
                MonsterRank.Normal, new BattleStats(100, 10000, 300, 1)));
        if (reviveAtOpening)
        {
            hero.ApplyDamage(int.MaxValue);
            const string revive = "fixture.revive";
            data.skills = data.skills.Append(new BattleCatalogData.SkillRow { id = revive, slot = 1, target = "one_ally_including_dead" }).ToArray();
            data.effects = data.effects.Append(new BattleCatalogData.EffectRow { id = revive + ".1", skillId = revive,
                phase = "impact", operation = "revive", recipient = "targets", value = 5000, chanceBp = 10000, order = 1 }).ToArray();
            var ally = new MonsterDefinition("healer", "healer", BattleElement.Dark,
                new BattleUnitForm("normal", "healer", new[] { revive }));
            roster.Add(BattleUnit.CreateMonster("healer", ally, new BattleSlot(BattleSide.Ally, 2), 60,
                MonsterRank.Normal, new BattleStats(100, 10000, 300, 500)));
        }
        roster.AddRange(enemies);
        return new BattleMatch("roster", BattlePreviewFactory.RulesVersion, 1,
            new BattleFormation(roster), new BattleSkillResolver(data, includePreviewSlime: true,
                enhancementRank: (_, skill) => skill == "c1074.s3" ? violetThirdRank : 0, randomDualChanceBp: 0),
            new BattleFlowRules(initialAllySoul: 40));
    }

    private static BattleCommandResult Use(BattleMatch match, string skill, string? target, bool burn = false)
    {
        var state = match.Snapshot;
        var sequence = state.LastCommandSequence + 1;
        var result = match.Submit(new BattleSkillCommand(state.BattleId, state.RulesVersion, sequence,
            "command-" + sequence, state.CurrentActorId, skill, target, burn));
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        return result;
    }

    private static void AdvanceTo(BattleMatch match, string actor, BattleAiPolicy ai)
    {
        for (int turn = 0; turn < 20 && match.Snapshot.CurrentActorId != actor; turn++)
        {
            var state = match.Snapshot;
            var active = state.GetUnit(state.CurrentActorId);
            Cast(match, ai.ChooseTarget(state, active.Form.SkillIds[0]));
        }
        Assert.Equal(actor, match.Snapshot.CurrentActorId);
    }

    private static BattleCommandResult Cast(BattleMatch match, BattleAiChoice choice)
    {
        Assert.NotNull(choice);
        var state = match.Snapshot;
        var sequence = state.LastCommandSequence + 1;
        var result = match.Submit(new BattleSkillCommand(state.BattleId, state.RulesVersion, sequence,
            "cmd-" + sequence, state.CurrentActorId, choice.SkillId, choice.TargetId));
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        return result;
    }
}
