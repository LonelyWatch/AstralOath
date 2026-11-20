using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleRoster51FirstTests
{
    [Fact]
    public void MaxHitBurnSelectsTheLongestGraphAndReducesThreeCooldownTurns()
    {
        var catalog = new BattleCatalogData
        {
            schemaVersion = 2, enhancements = Array.Empty<BattleCatalogData.EnhancementRow>(),
            skills = new[] {
                new BattleCatalogData.SkillRow { id="luna.s1", heroId="luna", slot=1, presentationId="short",
                    alternatePresentationId="middle", alternatePresentationStatus="RandomMiddleRoll", burnPresentationId="long",
                    soulBurnCost=10, soulBurnKind="maximum_hits", target="one_enemy", attackRateBp=7000, powerBp=9500 },
                new BattleCatalogData.SkillRow { id="luna.s3", heroId="luna", slot=3, target="self", cooldownTurns=10 }
            },
            effects = new[] {
                Row("luna.s1", 1, "impact", "damage", "targets"),
                Row("luna.s1", 2, "impact", "weighted_attack", "self", 7000, 1, 2000),
                Row("luna.s1", 3, "impact", "weighted_attack", "self", 14000, 2, 6000),
                Row("luna.s1", 4, "impact", "weighted_attack", "self", 21000, 3, 2000),
                Row("luna.s3", 1, "impact", "readiness_fixed", "self", 100)
            }
        };
        var match = Match(catalog, "luna");
        Submit(match, "luna.s3");
        int previous = match.Snapshot.GetUnit("hero").GetCooldown("luna.s3");
        var result = Submit(match, "luna.s1", "enemy1", burn:true);
        Assert.Contains(result.Events, e => e.Kind == BattleFlowEventKind.Action && e.PresentationId == "long");
        Assert.Contains(result.Events, e => e.Kind == BattleFlowEventKind.CooldownChanged && e.SkillId == "luna.s3" && e.Amount == previous - 3);
    }

    [Theory]
    [InlineData("c1060")]
    [InlineData("c1065")]
    [InlineData("c1066")]
    [InlineData("c1067")]
    [InlineData("c1069")]
    public void EveryFirstSlotRunsAsAPlayerCommand(string hero)
    {
        var match = Match(Catalog(), hero);
        var result = Submit(match, hero + ".s1", "enemy1");
        Assert.Contains(result.Events, e => e.Kind == BattleFlowEventKind.Action && e.SkillId == hero + ".s1");
        Assert.Contains(result.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "enemy1");
    }

    [Theory]
    [InlineData(2, false, 1500)]
    [InlineData(3, false, 1500)]
    [InlineData(3, true, 1900)]
    public void SchnielDealsFixedDamageEvenOnAMissAndShieldsEachAlly(int awakening, bool burn, int barrierRate)
    {
        var match = Match(Catalog(), "c1060", awakening:awakening, enemyEvades:true);
        int hp = match.Snapshot.GetUnit("hero").MaximumHealth;
        var cast = Submit(match, "c1060.s3", burn:burn);
        foreach (string id in new[] {"enemy1","enemy2"})
        {
            Assert.Contains(cast.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == id && e.HitKind == BattleHitKind.Miss);
            Assert.Contains(cast.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == id && e.Amount == 5000);
        }
        foreach (string id in new[] {"hero","ally"})
        {
            var target = cast.Snapshot.GetUnit(id);
            Assert.Equal((int)decimal.Round(hp * barrierRate / 10000m, 0, MidpointRounding.AwayFromZero),
                target.GetStatus(BattleStatusKind.Barrier).Strength);
            Assert.Equal(awakening >= 3, target.GetStatus(BattleStatusKind.DefenseUp) != null);
        }
    }

    [Theory]
    [InlineData("Poison", true)]
    [InlineData("Venom", true)]
    [InlineData("Bleed", false)]
    public void SchnielDetoxRespondsOnlyToEnemySkillsLeavingPoison(string debuff, bool responds)
    {
        var match = Match(Catalog(), "c1060", enemySpeed:180, enemyStatus:debuff);
        var cast = Submit(match, "test.basic", "hero");
        Assert.Equal(responds, cast.Events.Any(e => e.Kind == BattleFlowEventKind.Action && e.Origin == BattleActionOrigin.Passive &&
            e.SkillId == "c1060.s2"));
        if (responds)
        {
            foreach (string id in new[] {"hero","ally"})
            {
                var target = cast.Snapshot.GetUnit(id);
                Assert.NotNull(target.GetStatus(BattleStatusKind.Immunity));
                Assert.NotNull(target.GetStatus(BattleStatusKind.Indomitable));
            }
            Assert.Null(cast.Snapshot.GetUnit("hero").GetStatus(Enum.Parse<BattleStatusKind>(debuff)));
            Assert.Equal(2,cast.Snapshot.GetUnit("hero").GetCooldown("c1060.s2"));
        }
    }

    [Fact]
    public void SurinNeedsAnExistingBleedForHerFirstSkillAndCountsStacksForHerThird()
    {
        var match = Match(Catalog(), "c1065", seed:10);
        var empty = Submit(match, "c1065.s1", "enemy1");
        Assert.DoesNotContain(empty.Events, e => e.Kind == BattleFlowEventKind.StatusApplied && e.StatusKind == BattleStatusKind.Bleed);
        var second = Submit(match, "c1065.s2", "enemy1", true);
        Assert.Contains(second.Events,e => e.Kind == BattleFlowEventKind.ExtraTurnGranted);
        int firstCount = second.Snapshot.GetUnit("enemy1").Statuses.Count(s => s.Kind == BattleStatusKind.Bleed);
        Assert.InRange(firstCount,1,4);
        var first = Submit(match,"c1065.s1","enemy1");
        int count = first.Snapshot.GetUnit("enemy1").Statuses.Count(s => s.Kind == BattleStatusKind.Bleed);
        Assert.True(count > firstCount);
        var burst = Submit(match,"c1065.s3","enemy1");
        Assert.Contains(burst.Events,e => e.Kind == BattleFlowEventKind.ReadinessChanged && e.TargetId=="hero" && e.Amount==Math.Min(5,count)*1500);
        Assert.Contains(burst.Events,e => e.Kind == BattleFlowEventKind.StatusDurationChanged && e.StatusKind == BattleStatusKind.Bleed && e.Status.RemainingTurns==3);
    }

    [Fact]
    public void LunaRollsOneBranchForBothPresentationAndCooldown()
    {
        var variants = new HashSet<int>();
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var match = Match(Catalog(), "c1066",seed);
            Submit(match, "c1066.s3","enemy1");
            var attacker = match.Snapshot.GetUnit("hero");
            var target = match.Snapshot.GetUnit("enemy1");
            int before = attacker.GetCooldown("c1066.s3");
            var result = Submit(match,"c1066.s1","enemy1");
            var action = Assert.Single(result.Events,e => e.Kind==BattleFlowEventKind.Action);
            int tier = int.Parse(action.PresentationId.Substring(action.PresentationId.Length-1));
            variants.Add(tier);
            Assert.Contains(result.Events,e => e.Kind==BattleFlowEventKind.CooldownChanged && e.SkillId=="c1066.s3" && e.Amount==before-tier);
            var hit = Assert.Single(result.Events,e => e.Kind==BattleFlowEventKind.Damage);
            decimal defense = target.Stats.Defense * (target.GetStatus(BattleStatusKind.DefenseDown) == null ? 1m : .3m);
            Assert.Equal(EpicSevenCombatMath.Damage(attacker.Stats.Attack * 1.2m, defense, attacker.MaximumHealth,
                7000*tier,9500,0,0,0,hit.HitKind,attacker.Stats.CritDamageBp),hit.Amount);
        }
        Assert.Equal(new[]{1,2,3},variants.OrderBy(v=>v).ToArray());
    }

    [Theory]
    [InlineData(2,false)]
    [InlineData(3,true)]
    public void TamarinneOpensOnCooldownThenChangesCommandsForThreeOwnerTurns(int awakening,bool extra)
    {
        var match = Match(Catalog(), "c1067", awakening:awakening, rank:1);
        Assert.Equal(8,match.Snapshot.GetUnit("hero").GetCooldown("c1067.s3"));
        while (match.Snapshot.GetUnit("hero").GetCooldown("c1067.s3")>0)
        {
            var normal = Submit(match,"c1067.s1","enemy1");
            Assert.Contains(normal.Events,e=>e.Kind==BattleFlowEventKind.Action && e.PresentationId=="c_tamarinne_skill_01");
            Assert.Contains(normal.Events,e=>e.Kind==BattleFlowEventKind.Healing && e.TargetId=="ally");
        }
        var show = Submit(match,"c1067.s3");
        Assert.Equal(extra,show.Events.Any(e=>e.Kind==BattleFlowEventKind.ExtraTurnGranted));
        Assert.Equal(3,show.Snapshot.GetUnit("hero").GetStatus(BattleStatusKind.Idol).RemainingTurns);
        var song = Submit(match,"c1067.s2");
        Assert.Contains(song.Events,e=>e.Kind==BattleFlowEventKind.Action && e.PresentationId=="c_tamarinne_skill_02_2");
        Assert.Contains(song.Events,e=>e.Kind==BattleFlowEventKind.ReadinessChanged && e.TargetId=="hero" && e.Amount==3200);
        Assert.NotNull(song.Snapshot.GetUnit("ally").GetStatus(BattleStatusKind.AttackUp));
        var encore = Submit(match,"c1067.s1");
        Assert.Contains(encore.Events,e=>e.Kind==BattleFlowEventKind.Action && e.PresentationId=="c_tamarinne_skill_01_1");
        Assert.Contains(encore.Events,e=>e.Kind==BattleFlowEventKind.Action && e.Origin==BattleActionOrigin.DualAttack && e.SourceId=="ally");
        Assert.Contains(encore.Events,e=>e.Kind==BattleFlowEventKind.Damage && e.TargetId=="enemy2");
        Submit(match,"c1067.s1");
        Assert.Null(match.Snapshot.GetUnit("hero").GetStatus(BattleStatusKind.Idol));
        Assert.DoesNotContain(Submit(match,"c1067.s1","enemy1").Events,e=>e.Kind==BattleFlowEventKind.Damage && e.TargetId=="enemy2");
    }

    [Fact]
    public void LudwigGainsInvincibilityBeforeTheStrongerAwakenedPenetration()
    {
        int Damage(int awakening)
        {
            var match=Match(Catalog(),"c1069",awakening:awakening);
            var buff=Submit(match,"c1069.s2","enemy1",true);
            Assert.NotNull(buff.Snapshot.GetUnit("hero").GetStatus(BattleStatusKind.Invincible));
            return Assert.Single(Submit(match,"c1069.s3").Events,e=>e.Kind==BattleFlowEventKind.Damage && e.TargetId=="enemy2").Amount;
        }
        Assert.True(Damage(3)>Damage(2));
    }

    [Fact]
    public void FixedDamageFinisherPublishesBothDefeatsToTeammatePassives()
    {
        var match=Match(Catalog(),"c1060",enemyHealth:3900,allyTracksDefeats:true);
        var cast=Submit(match,"c1060.s3");
        Assert.Equal(2,cast.Events.Count(e=>e.Kind==BattleFlowEventKind.UnitDefeated));
        Assert.Equal(3,cast.Snapshot.GetUnit("ally").GetCooldown("test.allywatch"));
    }

    [Fact]
    public void VenomInjuresOnlyARealHeroByTheAmountLostOnTheirTurn()
    {
        var match=Match(Catalog(),"c1069",enemySpeed:180,enemyStatus:"Venom");
        var first=Submit(match,"test.basic","ally");
        Assert.Null(first.Snapshot.GetUnit("ally").GetStatus(BattleStatusKind.Venom));
        var applied=Submit(match,"test.basic","hero");
        var tick=Assert.Single(applied.Events,e=>e.Kind==BattleFlowEventKind.Damage && e.StatusKind==BattleStatusKind.Venom);
        Assert.True(tick.Amount>0);
        Assert.Equal(tick.Amount,applied.Snapshot.GetUnit("hero").InjuryAmount);
    }

    [Theory]
    [InlineData(2,5)]
    [InlineData(3,10)]
    public void OnlyTheAwakenedLinkedLunaUltimateAwardsBonusOnKill(int awakening,int souls)
    {
        var match=Match(Catalog(),"c1066",enemyHealth:1,awakening:awakening);
        var state=match.Snapshot;
        var forbidden=match.Submit(new BattleSkillCommand(state.BattleId,state.RulesVersion,1,"burn",state.CurrentActorId,
            "c1066.s3","enemy1",true));
        Assert.NotEqual(BattleCommandStatus.Accepted,forbidden.Status);
        var cast=Submit(match,"c1066.s3","enemy1");
        Assert.Equal(50+souls,cast.Snapshot.GetSoul(BattleSide.Ally));
    }

    private static BattleCatalogData.EffectRow Row(string skill, int order, string phase, string op, string recipient,
        int value = 0, int turns = 0, int chance = 10000, string status = "none") =>
        new() { id=skill+"."+order, skillId=skill, order=order, phase=phase, operation=op, recipient=recipient,
            value=value, turns=turns, status=status, chanceBp=chance, condition="none" };

    private static BattleCatalogData Catalog()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        return JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions {IncludeFields=true})!;
    }

    private static BattleMatch Match(BattleCatalogData catalog, string kit, ulong seed = 17, int enemySpeed = 1,
        int enemyHealth = 50000, int awakening = 3, int rank = 0, bool enemyEvades = false, string? enemyStatus = null,
        bool allyTracksDefeats = false)
    {
        var kitSkills = catalog.skills.Where(s => s.heroId == kit).OrderBy(s => s.slot).Select(s => s.id).ToArray();
        Assert.NotEmpty(kitSkills);
        var form = new BattleUnitForm("normal", kit, kitSkills);
        var actor = BattleUnit.CreateHero("hero", new HeroDefinition(kit, kit, BattleElement.Dark, "warrior", "lion", form,
            new HeroGrowthPreset("fixture", 5, 60000, 300000, 60000, 120000),
            Enumerable.Range(1,6).Select(n => new HeroAwakeningStep(n,0,0))),
            new BattleSlot(BattleSide.Ally, 1), 60, 6, awakening);
        var basic = new BattleCatalogData.SkillRow { id="test.basic",heroId="test",slot=1,target="one_enemy",attackRateBp=5000,powerBp=10000 };
        catalog.skills = catalog.skills.Append(basic).ToArray();
        catalog.effects = catalog.effects.Append(Row(basic.id, 1, "impact", "damage", "targets")).ToArray();
        if (enemyStatus != null)
            catalog.effects = catalog.effects.Concat(new[]{Row(basic.id,2,"impact","ignore_resistance","targets",1),
                Row(basic.id,3,"after_hit","apply_status","targets",1000,2,status:enemyStatus)}).ToArray();
        var enemyIds = new List<string>{basic.id};
        var allyIds = new List<string>{basic.id};
        if (allyTracksDefeats)
        {
            const string id = "test.allywatch";
            allyIds.Add(id);
            catalog.skills = catalog.skills.Append(new BattleCatalogData.SkillRow{id=id,slot=3,heroId="test",target="self",cooldownTurns=5}).ToArray();
            catalog.effects = catalog.effects.Concat(new[]{Row(id,1,"battle_start","opening_max_cooldown","self",5),
                Row(id,2,"after_enemy_defeated","reduce_own_cooldown","self",1)}).ToArray();
        }
        if (enemyEvades)
        {
            const string id = "test.evade";
            enemyIds.Add(id);
            catalog.skills = catalog.skills.Append(new BattleCatalogData.SkillRow{id=id,slot=2,heroId="test",target="passive"}).ToArray();
            catalog.effects = catalog.effects.Append(Row(id,1,"battle_start","apply_status","self",10000,4,status:"EvasionUp")).ToArray();
        }
        BattleUnit Other(string id, BattleSide side, int slot, int speed, int health) => BattleUnit.CreateMonster(id,
            new MonsterDefinition(id,id,BattleElement.Dark,new BattleUnitForm("normal",id,side==BattleSide.Enemy?enemyIds:allyIds)),
            new BattleSlot(side,slot),60,MonsterRank.Normal,new BattleStats(200,health,300,speed));
        var ally = Other("ally", BattleSide.Ally, 2, 1, 24000);
        ally.ApplyDamage(7000);
        return new BattleMatch("source", BattlePreviewFactory.RulesVersion, seed, new BattleFormation(new[] {
            actor, ally, Other("enemy1", BattleSide.Enemy, 1, enemySpeed, enemyHealth),
            Other("enemy2", BattleSide.Enemy, 2, enemySpeed, enemyHealth) }),
            new BattleSkillResolver(catalog, enhancementRank: (unit,skill) => unit == "hero" ?
                Math.Min(rank, catalog.enhancements.Count(row => row.skillId == skill)) : 0, randomDualChanceBp:0),
            new BattleFlowRules(initialAllySoul: 50, openingReadinessMaxBp: 0));
    }

    private static BattleCommandResult Submit(BattleMatch match, string skill, string? target = null, bool burn = false)
    {
        var state = match.Snapshot;
        ulong seq = state.LastCommandSequence+1;
        var result = match.Submit(new BattleSkillCommand(state.BattleId,state.RulesVersion,seq,"command"+seq,
            state.CurrentActorId,skill,target,burn));
        Assert.Equal(BattleCommandStatus.Accepted,result.Status);
        return result;
    }
}
