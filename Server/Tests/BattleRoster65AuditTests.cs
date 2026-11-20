using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleRoster65AuditTests
{
    [Fact]
    public void ZenosEvasionAuraEndsWhenHeDiesAndReturnsAfterRevivalBeforeHisNextTurn()
    {
        var data = Catalog();
        AddSkill(data, "audit.lethal", 2, "one_enemy", new[] { Effect("fixed_damage", "targets", value: 500000) });
        var dodge = Effect("apply_status", "self", "EvasionUp", 4, 5000);
        dodge.phase = "battle_start";
        AddSkill(data, "audit.dodge", 3, "passive", new[] { dodge });
        BattleHitKind AlliedHit(ulong seed, bool kill, bool revive)
        {
            var battle = Match(data, seed, true, Hero(data, "z", "c1083", BattleSide.Ally, 1, 20),
                Hero(data, "r", "c1022", BattleSide.Ally, 2, 290), Probe("helper", BattleSide.Ally, 3, 280, "audit.hit"),
                Probe("enemy", BattleSide.Enemy, 1, 300, "audit.hit", "audit.lethal", "audit.dodge"));
            var incoming = Use(battle, "enemy", kill ? "audit.lethal" : "audit.hit", "z");
            Assert.Equal(!kill, incoming.Snapshot.GetUnit("z").IsAlive);
            if (revive) Use(battle, "r", "c1022.s3", "z");
            else Use(battle, "r", "c1022.s1", "enemy");
            var hit = Use(battle, "helper", "audit.hit", "enemy");
            return Assert.Single(hit.Events, e => e.Kind == BattleFlowEventKind.Damage && e.SourceId == "helper").HitKind;
        }
        for (ulong seed = 1; seed <= 16; seed++)
        {
            if (AlliedHit(seed, true, false) != BattleHitKind.Miss) continue;
            Assert.NotEqual(BattleHitKind.Miss, AlliedHit(seed, false, false));
            Assert.NotEqual(BattleHitKind.Miss, AlliedHit(seed, true, true));
            return;
        }
        Assert.Fail("The control with Zeno defeated did not evade in the deterministic sample.");
    }

    [Fact]
    public void ZenosSingleDispelInstructionRollsOnceForEachRecipientBeforeApplyingDebuffs()
    {
        var data = Catalog();
        OpeningStatus(data, "c1082.s1", "AttackUp", 5000);
        OpeningStatus(data, "c1082.s1", "SpeedUp", 3000);
        bool recipientsDiffer = false;
        for (ulong seed = 1; seed <= 16; seed++)
        {
            var battle = Match(data, seed, Hero(data, "z", "c1083", BattleSide.Ally, 1, 200),
                Hero(data, "a", "c1082", BattleSide.Enemy, 1, 20, resistance: 5000),
                Hero(data, "b", "c1082", BattleSide.Enemy, 2, 20, resistance: 5000));
            var cast = Use(battle, "z", "c1083.s3");
            int Removed(string id)
            {
                var removed = cast.Events.Where(e => e.Kind == BattleFlowEventKind.StatusRemoved && e.TargetId == id).ToArray();
                Assert.True(removed.Length is 0 or 2, $"Seed {seed}, target {id}: removed {removed.Length} of two removable buffs.");
                var status = cast.Events.FirstOrDefault(e => e.Kind == BattleFlowEventKind.StatusApplied && e.TargetId == id);
                if (removed.Length != 0 && status != null) Assert.True(removed.Last().Sequence < status.Sequence);
                return removed.Length;
            }
            recipientsDiffer |= Removed("a") != Removed("b");
        }
        Assert.True(recipientsDiffer, "Recipients should use independent resistance checks.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(375000)]
    public void LulucasBasicUsesTheTargetsMissingFractionBeforeDealingDamage(int healthLost)
    {
        var data = Catalog();
        var caster = Hero(data, "l", "c1082", BattleSide.Ally, 1, 200);
        var target = Probe("target", BattleSide.Enemy, 1, 1, "audit.hit");
        target.ApplyDamage(healthLost);
        var result = Use(Match(data, 4, caster, target), "l", "c1082.s1", "target");
        var hit = Assert.Single(result.Events, e => e.Kind == BattleFlowEventKind.Damage);
        decimal multiplier = 1m + .2m * healthLost / target.Stats.MaxHealth;
        Assert.Equal(EpicSevenCombatMath.Damage(caster.Stats.Attack, target.Stats.Defense, caster.Stats.MaxHealth,
            10000, 10000, 0, 0, 0, hit.HitKind, caster.Stats.CritDamageBp, damageMultiplier: multiplier), hit.Amount);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void LulucasBarrierIncludesHerAwakenedAttackGrowthAndLosesItAfterRevival(int awakening)
    {
        var data = Catalog();
        AddSkill(data, "audit.lethal", 2, "one_enemy", new[] { Effect("fixed_damage", "targets", value: 500000) });
        var luluca = Hero(data, "l", "c1082", BattleSide.Ally, 1, 200, awakening: awakening);
        var battle = Match(data, 4, true, luluca, Hero(data, "r", "c1022", BattleSide.Ally, 2, 80),
            Probe("attacker", BattleSide.Enemy, 1, 90, "audit.hit", "audit.lethal"));
        var first = Use(battle, "l", "c1082.s3");
        Assert.Equal(awakening >= 3, first.Snapshot.GetUnit("l").GetStatus(BattleStatusKind.AttackGrowth) != null);
        var shield = Use(battle, "l", "c1082.s2", burn: true);
        int baseAttack = luluca.Stats.Attack;
        int Barrier(decimal growth) => (int)Math.Round(baseAttack * growth * .4m * 1.25m, MidpointRounding.AwayFromZero);
        foreach (var target in new[] { "l", "r" })
        {
            var buff = shield.Snapshot.GetUnit(target).GetStatus(BattleStatusKind.Barrier);
            Assert.Equal(("l", 2, Barrier(awakening >= 3 ? 1.2m : 1m)), (buff.SourceId, buff.RemainingTurns, buff.Strength));
        }
        Assert.Equal(0, shield.Snapshot.GetUnit("l").GetCooldown("c1082.s2"));
        var death = Use(battle, "attacker", "audit.lethal", "l");
        Assert.Contains(death.Events, e => e.Kind == BattleFlowEventKind.UnitDefeated && e.TargetId == "l");
        var revived = Use(battle, "r", "c1022.s3", "l");
        Assert.Contains(revived.Events, e => e.Kind == BattleFlowEventKind.UnitRevived && e.SourceId == "r" && e.TargetId == "l");
        Assert.Null(revived.Snapshot.GetUnit("l").GetStatus(BattleStatusKind.AttackGrowth));
        var second = Use(battle, "l", "c1082.s2");
        Assert.Equal(Barrier(1m), second.Snapshot.GetUnit("l").Barrier);
    }

    [Fact]
    public void FrostbiteStopsKrausSharingButLeavesASurvivingBarrierOnTheNextAttack()
    {
        var data = Catalog();
        for (ulong seed = 1; seed <= 16; seed++)
        {
            var battle = Match(data, seed, Hero(data, "z", "c1083", BattleSide.Ally, 1, 200),
                Hero(data, "target", "c1082", BattleSide.Enemy, 1, 220, attackCoefficient: 600000),
                Hero(data, "krau", "c1070", BattleSide.Enemy, 2, 20));
            Use(battle, "target", "c1082.s2");
            var first = Use(battle, "z", "c1083.s1", "target", true);
            if (first.Snapshot.GetUnit("target").GetStatus(BattleStatusKind.Frostbite) == null) continue;
            Assert.Contains(first.Events, e => e.Kind == BattleFlowEventKind.BarrierAbsorbed && e.TargetId == "krau" && e.Amount > 0);
            Assert.Contains(first.Events, e => e.Kind == BattleFlowEventKind.ExtraTurnGranted && e.TargetId == "z");
            var before = first.Snapshot.GetUnit("target");
            var second = Use(battle, "z", "c1083.s1", "target");
            var absorbed = Assert.Single(second.Events, e => e.Kind == BattleFlowEventKind.BarrierAbsorbed);
            Assert.Equal(("z", "target"), (absorbed.SourceId, absorbed.TargetId));
            Assert.True(absorbed.Amount > 0);
            var after = second.Snapshot.GetUnit("target");
            Assert.Equal(before.Barrier - absorbed.Amount, after.Barrier);
            Assert.Equal(before.CurrentHealth, after.CurrentHealth);
            Assert.DoesNotContain(second.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "krau");
            return;
        }
        Assert.Fail("No successful Frostbite application in the deterministic sample.");
    }

    [Fact]
    public void RuptureDoesNotStrikeAfterLethalPrimaryDamageOrCarryAcrossRuelesRevival()
    {
        var data = Catalog();
        OpeningStatus(data, "c1083.s2", "Rage", 2000);
        AddSkill(data, "audit.lethal", 2, "one_enemy", new[] { Effect("damage", "targets") }, 100000000);
        var battle = Match(data, 4, Hero(data, "z", "c1083", BattleSide.Ally, 1, 200),
            Probe("helper", BattleSide.Ally, 2, 180, "audit.hit", "audit.lethal"),
            Hero(data, "target", "c1082", BattleSide.Enemy, 1, 20), Hero(data, "r", "c1022", BattleSide.Enemy, 2, 170));
        var cast = Use(battle, "z", "c1083.s3");
        var rupture = cast.Snapshot.GetUnit("target").GetStatus(BattleStatusKind.Rupture);
        Assert.Equal(("z", 2, 1200), (rupture.SourceId, rupture.RemainingTurns, rupture.Strength));
        Assert.Single(cast.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "target");
        var lethal = Use(battle, "helper", "audit.lethal", "target");
        Assert.Single(lethal.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "target");
        Assert.Contains(lethal.Events, e => e.Kind == BattleFlowEventKind.UnitDefeated && e.TargetId == "target");
        var revived = Use(battle, "r", "c1022.s3", "target");
        Assert.Contains(revived.Events, e => e.Kind == BattleFlowEventKind.StatusRemoved && e.StatusKind == BattleStatusKind.Rupture && e.TargetId == "target");
        Assert.Null(revived.Snapshot.GetUnit("target").GetStatus(BattleStatusKind.Rupture));
        var after = Use(battle, "z", "c1083.s1", "target");
        Assert.Single(after.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "target");
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    public void KhawazusTwoDebuffsTickOnTheTargetsOwnTurn(int awakening, int turns)
    {
        var data = Catalog();
        for (ulong seed = 1; seed <= 16; seed++)
        {
            var battle = Match(data, seed, Hero(data, "k", "c1085", BattleSide.Ally, 1, 200, awakening: awakening),
                Probe("target", BattleSide.Enemy, 1, 190, "audit.hit"));
            var cast = Use(battle, "k", "c1085.s3", "target");
            var target = cast.Snapshot.GetUnit("target");
            var debuffs = new[] { BattleStatusKind.Unhealable, BattleStatusKind.DefenseDown };
            if (debuffs.Any(kind => target.GetStatus(kind) == null)) continue;
            foreach (var kind in debuffs)
                Assert.Equal(("k", turns), (target.GetStatus(kind).SourceId, target.GetStatus(kind).RemainingTurns));
            var response = Use(battle, "target", "audit.hit", "k");
            foreach (var kind in debuffs)
                Assert.Equal(turns - 1, response.Snapshot.GetUnit("target").GetStatus(kind)?.RemainingTurns ?? 0);
            return;
        }
        Assert.Fail("Neither debuff pair landed in the deterministic sample.");
    }

    [Theory]
    [InlineData("c1083.s1", 10000, false)]
    [InlineData("c1083.s1", 10000, true)]
    [InlineData("c1083.s3", 16000, false)]
    [InlineData("c1083.s3", 16000, true)]
    public void ZenosDefenseDamageUsesHisTeamsAuriusAndDefenseIncrease(string skill, int defenseRatio, bool defenseUp)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.ArtifactCatalog")!;
        var artifacts = JsonSerializer.Deserialize<ArtifactCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
        var aurius = new ArtifactCatalog(artifacts).Templates.Single(a => a.Effect == "aurius");
        foreach (var artifact in new ArtifactTemplate?[] { null, aurius })
        {
            var data = Catalog();
            if (defenseUp)
            {
                OpeningStatus(data, "c1083.s2", "DefenseUp", 6000);
            }
            var zeno = Hero(data, "z", "c1083", BattleSide.Ally, 1, 200);
            var holder = Hero(data, "holder", "c1070", BattleSide.Ally, 2, 1, artifact);
            var target = Probe("target", BattleSide.Enemy, 1, 1, "audit.hit");
            var result = Use(Match(data, 4, zeno, holder, target), "z", skill, skill == "c1083.s1" ? "target" : null);
            var hit = Assert.Single(result.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "target");
            var defenseFactor = 1m + (defenseUp ? .6m : 0m) + (artifact == null ? 0m : .1m);
            var expected = EpicSevenCombatMath.Damage(zeno.Stats.Attack, target.Stats.Defense, zeno.Stats.MaxHealth,
                10000, 10000, 0, 0, 0, hit.HitKind, zeno.Stats.CritDamageBp,
                additionalBaseDamage: zeno.Stats.Defense * defenseFactor * defenseRatio / 10000m);
            Assert.Equal(expected, hit.Amount);
        }
    }

    [Fact]
    public void KhawazusFireBreathUsesItsOwnActionButGivesOnlyReadiness()
    {
        var data = Catalog();
        bool fired = false, skipped = false;
        for (ulong seed = 1; seed <= 32 && !(fired && skipped); seed++)
        {
            var battle = Match(data, seed, Hero(data, "k", "c1085", BattleSide.Ally, 1, 200),
                Probe("target", BattleSide.Enemy, 1, 1, "audit.hit"));
            var result = Use(battle, "k", "c1085.s1", "target");
            var actions = result.Events.Where(e => e.Kind == BattleFlowEventKind.Action).ToArray();
            Assert.Equal("c_kawazu_skill_01", actions[0].PresentationId);
            Assert.Single(result.Events, e => e.Kind == BattleFlowEventKind.Damage);
            Assert.Single(result.Events, e => e.Kind == BattleFlowEventKind.SoulChanged);
            Assert.Equal(51, result.Snapshot.AllySoul);
            var changes = result.Events.Where(e => e.Kind == BattleFlowEventKind.ReadinessChanged).ToArray();
            if (actions.Length == 1)
            {
                skipped = true;
                Assert.Empty(changes);
                continue;
            }
            fired = true;
            Assert.Equal(2, actions.Length);
            Assert.Equal(("k", "k", "c1085.s1", "c_kawazu_skill_02", BattleActionOrigin.ExtraAttack),
                (actions[1].SourceId, actions[1].TargetId, actions[1].SkillId, actions[1].PresentationId, actions[1].Origin));
            var change = Assert.Single(changes);
            Assert.Equal(("k", "k", 2000), (change.SourceId, change.TargetId, change.Amount));
            Assert.True(actions[1].Sequence < change.Sequence);
        }
        Assert.True(fired && skipped);
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 2)]
    public void KhawazusPassiveRemovesAtMostNinetyNineDebuffsAndCannotRepeatDuringCooldown(bool enhanced, int cooldown)
    {
        var data = Catalog();
        AddSkill(data, "audit.mark", 2, "one_enemy",
            new[] { Effect("ignore_resistance", "self", value: 1) }
                .Concat(Enumerable.Range(0, 50).Select(_ => Effect("apply_status", "targets", "Bleed", 3, 1000))).ToArray());
        var battle = Match(data, 3, enhanced, Hero(data, "k", "c1085", BattleSide.Ally, 1, 100),
            Probe("attacker", BattleSide.Enemy, 1, 900, "audit.hit", "audit.mark"));
        Use(battle, "attacker", "audit.mark", "k");
        var preparation = Use(battle, "attacker", "audit.mark", "k");
        var before = preparation.Snapshot.GetUnit("k").Statuses.Where(s => s.Kind == BattleStatusKind.Bleed).ToArray();
        Assert.Equal(100, before.Length);
        Assert.DoesNotContain(preparation.Events, e => e.Origin == BattleActionOrigin.Passive);
        var reaction = Use(battle, "attacker", "audit.hit", "k");
        var passive = Assert.Single(reaction.Events, e => e.Kind == BattleFlowEventKind.Action && e.Origin == BattleActionOrigin.Passive);
        Assert.Equal(("k", "k", "c1085.s2", "c_kawazu_skill_02"), (passive.SourceId, passive.TargetId, passive.SkillId, passive.PresentationId));
        Assert.Equal(99, reaction.Events.Count(e => e.Kind == BattleFlowEventKind.StatusRemoved && e.StatusKind == BattleStatusKind.Bleed));
        var unit = reaction.Snapshot.GetUnit("k");
        Assert.Equal(before.Last().InstanceId, Assert.Single(unit.Statuses, s => s.Kind == BattleStatusKind.Bleed).InstanceId);
        Assert.Equal(cooldown, unit.GetCooldown("c1085.s2"));
        Assert.Equal(2, unit.GetStatus(BattleStatusKind.AttackUp).RemainingTurns);
        Assert.DoesNotContain(Use(battle, "attacker", "audit.hit", "k").Events, e => e.Origin == BattleActionOrigin.Passive);
    }

    private static BattleCatalogData Catalog()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        var data = JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
        AddSkill(data, "audit.hit", 1, "one_enemy", new[] { Effect("damage", "targets") }, 100);
        return data;
    }

    private static BattleUnit Hero(BattleCatalogData data, string id, string kit, BattleSide side, int slot, int speed,
        ArtifactTemplate? artifact = null, int awakening = 3, int attackCoefficient = 60000, int resistance = 0)
    {
        var skills = data.skills.Where(s => s.heroId == kit).OrderBy(s => s.slot).Select(s => s.id);
        var definition = new HeroDefinition(kit, kit, BattleElement.Dark, artifact?.HeroClass ?? "warrior", "lion", new BattleUnitForm("normal", kit, skills),
            new HeroGrowthPreset("fixture", 5, attackCoefficient, 900000, 60000, speed * 1000),
            Enumerable.Range(1, 6).Select(n => new HeroAwakeningStep(n, 0, 0, effectResistanceBp: n == 1 ? resistance : 0)));
        return BattleUnit.CreateHero(id, definition, new BattleSlot(side, slot), 60, 6, awakening, artifact);
    }

    private static BattleUnit Probe(string id, BattleSide side, int slot, int speed, params string[] skills)
    {
        var definition = new MonsterDefinition(id, id, BattleElement.Dark, new BattleUnitForm("normal", id, skills));
        return BattleUnit.CreateMonster(id, definition, new BattleSlot(side, slot), 60, MonsterRank.Normal,
            new BattleStats(200, 500000, 300, speed));
    }

    private static BattleCatalogData.EffectRow Effect(string operation, string recipient, string status = "none", int turns = 0, int value = 0) =>
        new() { operation = operation, recipient = recipient, status = status, turns = turns, value = value,
            phase = "impact", chanceBp = 10000, condition = "none" };

    private static void OpeningStatus(BattleCatalogData data, string skill, string status, int value)
    {
        var opening = Effect("apply_status", "self", status, 2, value);
        opening.phase = "battle_start";
        opening.skillId = skill;
        opening.id = skill + ".audit." + status;
        opening.order = data.effects.Count(e => e.skillId == skill) + 1;
        data.effects = data.effects.Append(opening).ToArray();
    }

    private static void AddSkill(BattleCatalogData data, string id, int slot, string target, BattleCatalogData.EffectRow[] effects, int? rate = null)
    {
        data.skills = data.skills.Append(new BattleCatalogData.SkillRow { id = id, slot = slot, target = target, attackRateBp = rate, powerBp = 10000 }).ToArray();
        for (int i = 0; i < effects.Length; i++)
        {
            effects[i].skillId = id;
            effects[i].id = id + "." + (i + 1);
            effects[i].order = i + 1;
        }
        data.effects = data.effects.Concat(effects).ToArray();
    }

    private static BattleMatch Match(BattleCatalogData data, ulong seed, params BattleUnit[] units) => Match(data, seed, false, units);

    private static BattleMatch Match(BattleCatalogData data, ulong seed, bool enhanced, params BattleUnit[] units) =>
        new("audit", BattlePreviewFactory.RulesVersion, seed, new BattleFormation(units),
            new BattleSkillResolver(data, enhancementRank: (_, skill) => enhanced ? data.enhancements.Count(e => e.skillId == skill) : 0,
                randomDualChanceBp: 0), new BattleFlowRules(initialAllySoul: 50, openingReadinessMaxBp: 0));

    private static BattleCommandResult Use(BattleMatch battle, string actor, string skill, string? target = null, bool burn = false)
    {
        var state = battle.Snapshot;
        Assert.Equal(actor, state.CurrentActorId);
        var sequence = state.LastCommandSequence + 1;
        var result = battle.Submit(new BattleSkillCommand(state.BattleId, state.RulesVersion, sequence, "c" + sequence, actor, skill, target, burn));
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        return result;
    }
}
