using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleRoster61To70Tests
{
    [Theory]
    [InlineData("c1076")][InlineData("c1079")][InlineData("c1080")][InlineData("c1081")][InlineData("c1082")]
    [InlineData("c1083")][InlineData("c1085")][InlineData("c1086")][InlineData("c1087")][InlineData("c1088")]
    public void AllEquippedActiveSkillsSubmitAndPresentTheirSourceGraph(string hero)
    {
        var data = Catalog();
        var slots = data.skills.Where(s => s.heroId == hero).OrderBy(s => s.slot).ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, slots.Select(s => s.slot));
        foreach (var skill in slots.Where(s => s.target != "passive"))
        {
            var match = Encounter(hero);
            var result = Use(match, skill.id, skill.target == "one_enemy" ? "enemy1" : null);
            Assert.Contains(result.Events, e => e.Origin == BattleActionOrigin.Primary &&
                e.Kind == BattleFlowEventKind.Action && e.PresentationId == skill.presentationId);
        }
    }

    [Fact]
    public void CeriseOnlyGuaranteesOneAssistWhileS3Cools()
    {
        var match = Encounter("c1081");
        Assert.DoesNotContain(Use(match,"c1081.s1","enemy1").Events,e=>e.Origin==BattleActionOrigin.DualAttack);
        Use(match,"c1081.s3");
        var result = Use(match,"c1081.s1","enemy1");
        Assert.Single(result.Events,e=>e.Kind==BattleFlowEventKind.Action && e.Origin==BattleActionOrigin.DualAttack);
    }

    [Fact]
    public void DieneAndLulucaUseEachTargetsHealthAndTheCastersAttackForTheirBarriers()
    {
        var diene = Use(Encounter("c1076"),"c1076.s2");
        foreach (var id in new[] { "hero", "ally" })
        {
            var unit = diene.Snapshot.GetUnit(id);
            Assert.Equal((int)Math.Round(unit.MaximumHealth * .2m, MidpointRounding.AwayFromZero),unit.GetStatus(BattleStatusKind.Barrier).Strength);
        }
        var luluca = Use(Encounter("c1082"),"c1082.s2");
        int attack = luluca.Snapshot.GetUnit("hero").Stats.Attack;
        Assert.Equal((int)Math.Round(attack * .4m, MidpointRounding.AwayFromZero),luluca.Snapshot.GetUnit("ally").GetStatus(BattleStatusKind.Barrier).Strength);
    }

    [Theory]
    [InlineData(false,1)][InlineData(true,2)]
    public void DienesMaxRankBasicAddsFifteenReadinessForEachEnabledSourceRow(bool buffed,int count)
    {
        var result=Use(Encounter("c1076",rank:7,actorOpeningStatus:buffed?"AttackUp":null),"c1076.s1","enemy1");
        var values=result.Events.Where(e=>e.Kind==BattleFlowEventKind.ReadinessChanged && e.TargetId=="hero").Select(e=>e.Amount).ToArray();
        Assert.Equal(Enumerable.Range(1,count).Select(i=>1500*i),values);
    }

    [Fact]
    public void VivianBurnOnlyRaisesTheFirstStormOfTheChain()
    {
        int[] Cast(bool burn)
        {
            var cast=Use(Encounter("c1088",enemyHealth:new[]{1,500000}),"c1088.s2",burn:burn);
            return cast.Events.Where(e=>e.Kind==BattleFlowEventKind.Damage && e.TargetId=="enemy2").Select(e=>e.Amount).ToArray();
        }
        var normal=Cast(false);
        var burned=Cast(true);
        Assert.Equal(2,normal.Length);
        Assert.Equal(2,burned.Length);
        Assert.True(burned[0]>normal[0]);
        Assert.Equal(normal[1],burned[1]);
    }

    [Fact]
    public void LulucaKeepsThreeAttackIncreasesAcrossSuccessfulCasts()
    {
        var match = Encounter("c1082");
        for (int i=1; i<=4; i++)
        {
            while (match.Snapshot.GetUnit("hero").GetCooldown("c1082.s3")>0) Use(match,"c1082.s1","enemy1");
            var result=Use(match,"c1082.s3");
            var growth=result.Snapshot.GetUnit("hero").Statuses.Single(status=>status.Kind.ToString()=="AttackGrowth");
            Assert.Equal(Math.Min(3,i)*2000,growth.Strength);
        }
    }

    [Theory]
    [InlineData(2,false)][InlineData(3,true)]
    public void CermiaAndFuriousGrantTheirAwakenedEffectsOnlyAtThirdAwakening(int awakening, bool awakened)
    {
        var cermia=Encounter("c1079",awakening);
        Use(cermia,"c1079.s3","enemy1");
        var reset=Use(cermia,"c1079.s2");
        Assert.Equal(0,reset.Snapshot.GetUnit("hero").GetCooldown("c1079.s3"));
        Assert.Equal(awakened,reset.Snapshot.GetUnit("hero").GetStatus(BattleStatusKind.GreaterAttackUp)!=null);
        Assert.Contains(reset.Events,e=>e.Kind==BattleFlowEventKind.ExtraTurnGranted);
        var furious=Use(Encounter("c1087",awakening),"c1087.s2");
        Assert.Equal(awakened,furious.Events.Any(e=>e.Kind==BattleFlowEventKind.ExtraTurnGranted));
        Assert.NotNull(furious.Snapshot.GetUnit("ally").GetStatus(BattleStatusKind.CriticalChanceUp));
        var prayer=Use(Encounter("c1076",awakening),"c1076.s3",burn:true);
        Assert.Equal(awakened,prayer.Snapshot.GetUnit("ally").GetStatus(BattleStatusKind.PenetrationResistanceUp)!=null);
        Assert.Equal(2,prayer.Snapshot.GetUnit("hero").GetCooldown("c1076.s3"));
    }

    [Theory]
    [InlineData(MonsterRank.Normal,false)][InlineData(MonsterRank.Elite,true)][InlineData(MonsterRank.Boss,true)]
    public void PavelIgnoresKrausGuardAgainstOrdinaryTargets(MonsterRank rank,bool shared)
    {
        var match=Encounter("c1080",guard:true,enemyRank:rank);
        var attack=Use(match,"c1080.s3","enemy1");
        Assert.Equal(shared,attack.Events.Any(e=>e.Kind==BattleFlowEventKind.Damage && e.TargetId=="enemy2"));
    }

    [Fact]
    public void KawazuCleansesOnlyAfterBeingAttackedAndUsesHisPassiveCooldown()
    {
        var match=Encounter("c1085",incoming:true);
        var other=Use(match,"probe.s1","ally");
        Assert.DoesNotContain(other.Events,e=>e.Origin==BattleActionOrigin.Passive);
        var afflicted=Use(match,"probe.s1","hero");
        Assert.Contains(afflicted.Events,e=>e.Origin==BattleActionOrigin.Passive && e.PresentationId=="c_kawazu_skill_02");
        var unit=afflicted.Snapshot.GetUnit("hero");
        Assert.Null(unit.GetStatus(BattleStatusKind.Unhealable));
        Assert.NotNull(unit.GetStatus(BattleStatusKind.AttackUp));
        Assert.Equal(3,unit.GetCooldown("c1085.s2"));
    }

    [Fact]
    public void KawanasFireAllyReceivesBothFieldEffectsWhileAnotherElementOnlyGetsAttack()
    {
        var match=Encounter("c1086");
        var field=Use(match,"c1086.s2");
        Assert.NotNull(field.Snapshot.GetUnit("hero").GetStatus(BattleStatusKind.SkillNullifier));
        Assert.Null(field.Snapshot.GetUnit("ally").GetStatus(BattleStatusKind.SkillNullifier));
        Assert.NotNull(field.Snapshot.GetUnit("ally").GetStatus(BattleStatusKind.AttackUp));
        var stun=Use(match,"c1086.s3","enemy1");
        Assert.Contains(stun.Events,e=>e.Kind==BattleFlowEventKind.ReadinessChanged && e.TargetId=="hero");
        Assert.DoesNotContain(stun.Events,e=>e.Kind==BattleFlowEventKind.ReadinessChanged && e.TargetId=="ally");
    }

    [Fact]
    public void VivianStopsOnASurvivingWaveAndHasOnlyTwoReducedFollowUps()
    {
        var intact=Use(Encounter("c1088",enemyHealth:new[]{30000,30000,30000,30000}),"c1088.s2");
        Assert.DoesNotContain(intact.Events,e=>e.Kind==BattleFlowEventKind.Action && e.Origin==BattleActionOrigin.ExtraAttack);
        int survivesFirst=1+intact.Events.Single(e=>e.Kind==BattleFlowEventKind.Damage && e.TargetId=="enemy2").Amount;
        var newest=Encounter("c1088",enemyHealth:new[]{1,survivesFirst,30000,30000});
        var storm=Use(newest,"c1088.s2");
        var onLast=storm.Events.Where(e=>e.Kind==BattleFlowEventKind.Damage && e.TargetId=="enemy4").ToArray();
        Assert.Equal(3,onLast.Length);
        Assert.True(onLast[0].Amount>onLast[1].Amount && onLast[1].Amount>onLast[2].Amount);
        Assert.Equal(2,storm.Events.Count(e=>e.Kind==BattleFlowEventKind.Action && e.Origin==BattleActionOrigin.ExtraAttack));
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void PenetrationResistanceReducesCermiasHalfPenetrationToAQuarter(bool protectedTarget)
    {
        var match=Encounter("c1079",openingStatus:protectedTarget?"PenetrationResistanceUp":null);
        var attacker=match.Snapshot.GetUnit("hero");
        var target=match.Snapshot.GetUnit("enemy1");
        var result=Use(match,"c1079.s3","enemy1");
        var hit=Assert.Single(result.Events,e=>e.Kind==BattleFlowEventKind.Damage);
        var expected=EpicSevenCombatMath.Damage(attacker.Stats.Attack,target.Stats.Defense*(protectedTarget?.75m:.5m),
            attacker.MaximumHealth,11500,9000,0,0,0,hit.HitKind,attacker.Stats.CritDamageBp);
        Assert.Equal(expected,hit.Amount);
    }

    [Fact]
    public void ZenoOnlyGainsRageAndDefenseWhenAHeroIsFrostbittenAtHisTurnStart()
    {
        var monsters=Encounter("c1083");
        Use(monsters,"c1083.s1","enemy1",burn:true);
        Assert.Null(monsters.Snapshot.GetUnit("enemy1").GetStatus(BattleStatusKind.Frostbite));
        Assert.Null(monsters.Snapshot.GetUnit("hero").GetStatus(BattleStatusKind.Rage));
        for (ulong seed=1;seed<=8;seed++)
        {
            var match=Encounter("c1083",seed:seed,heroTarget:true);
            var cast=Use(match,"c1083.s1","enemy1",burn:true);
            if (cast.Snapshot.GetUnit("enemy1").GetStatus(BattleStatusKind.Frostbite)==null) continue;
            var hero=cast.Snapshot.GetUnit("hero");
            Assert.Equal(2000,hero.GetStatus(BattleStatusKind.Rage).Strength);
            Assert.Equal(6000,hero.GetStatus(BattleStatusKind.DefenseUp).Strength);
            return;
        }
        Assert.Fail("No landed Frostbite in the fixed set of normal attacks.");
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void ZenoRageBypassesResistanceAndRuptureHurtsOnlyOnTheNextAttack(bool enraged)
    {
        var match=Encounter("c1083",heroTarget:true,enemyResistance:20000,actorOpeningStatus:enraged?"Rage":null,
            openingStatus:"AttackUp");
        var spell=Use(match,"c1083.s3");
        var target=spell.Snapshot.GetUnit("enemy1");
        Assert.Equal(enraged,target.GetStatus(BattleStatusKind.Rupture)!=null);
        Assert.Equal(!enraged,target.GetStatus(BattleStatusKind.AttackUp)!=null);
        Assert.Null(spell.Snapshot.GetUnit("enemy2").GetStatus(BattleStatusKind.Rupture));
        Assert.Single(spell.Events,e=>e.Kind==BattleFlowEventKind.Damage && e.TargetId=="enemy1");
        var next=Use(match,"c1083.s1","enemy1");
        var additions=next.Events.Where(e=>e.Kind==BattleFlowEventKind.Damage && e.TargetId=="enemy1" && e.HitKind==BattleHitKind.None).ToArray();
        if (enraged) Assert.Equal((int)Math.Round(target.MaximumHealth*.12m,MidpointRounding.AwayFromZero),Assert.Single(additions).Amount);
        else Assert.Empty(additions);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void ZenosStatusConditionCanResolveAfterAnEvadedAttackButRageStillControlsResistance(bool enraged)
    {
        for (ulong seed=1;seed<=32;seed++)
        {
            var match=Encounter("c1083",seed:seed,heroTarget:true,openingStatus:"EvasionUp",
                actorOpeningStatus:enraged?"Rage":null,enemyResistance:20000);
            var attack=Use(match,"c1083.s3");
            var hit=attack.Events.Single(e=>e.Kind==BattleFlowEventKind.Damage && e.TargetId=="enemy1");
            if (hit.HitKind!=BattleHitKind.Miss) continue;
            var target=attack.Snapshot.GetUnit("enemy1");
            Assert.Equal(enraged,target.GetStatus(BattleStatusKind.Rupture)!=null);
            Assert.Equal(enraged,target.GetStatus(BattleStatusKind.Unbuffable)!=null);
            Assert.Equal(!enraged,target.GetStatus(BattleStatusKind.EvasionUp)!=null);
            Assert.DoesNotContain(attack.Events,e=>e.Kind==BattleFlowEventKind.EffectRejected &&
                e.TargetId=="enemy1" && e.EffectRejection==BattleEffectRejection.Missed);
            return;
        }
        Assert.Fail("No evaded Zeno S3 in the fixed seeds.");
    }

    [Fact]
    public void ZenosEvasionAuraUsesTheEnhancedFiftyPercentAndBenefitsOtherAttackers()
    {
        int misses=0;
        for (ulong seed=1;seed<=16;seed++)
        {
            foreach (int rank in new[]{0,5})
            {
                var match=Encounter("c1083",seed:seed,openingStatus:"EvasionUp",rank:rank,allyAttacksFirst:true);
                var attack=Use(match,"probe.s1","enemy1");
                var kind=attack.Events.First(e=>e.Kind==BattleFlowEventKind.Damage).HitKind;
                if (rank==5) Assert.NotEqual(BattleHitKind.Miss,kind);
                else if (kind==BattleHitKind.Miss) misses++;
            }
        }
        Assert.True(misses>0);
    }

    [Fact]
    public void FrostbiteBlocksBothDamageReductionAndSharingOnTheAffectedHero()
    {
        var normal=Use(Encounter("c1070",guard:true,heroTarget:true,reduceIncoming:true),"c1070.s3","enemy1");
        Assert.Contains(normal.Events,e=>e.Kind==BattleFlowEventKind.Damage && e.TargetId=="enemy2");
        int reduced=normal.Events.Where(e=>e.Kind==BattleFlowEventKind.Damage).Sum(e=>e.Amount);
        for (ulong seed=1;seed<=8;seed++)
        {
            var frozen=Encounter("c1070",seed:seed,guard:true,heroTarget:true,reduceIncoming:true,openingStatus:"Frostbite");
            if (frozen.Snapshot.GetUnit("enemy1").GetStatus(BattleStatusKind.Frostbite)==null) continue;
            var burst=Use(frozen,"c1070.s3","enemy1");
            int full=Assert.Single(burst.Events,e=>e.Kind==BattleFlowEventKind.Damage).Amount;
            Assert.InRange((double)reduced/full,.695,.705);
            return;
        }
        Assert.Fail("Frostbite did not land.");
    }

    [Fact]
    public void RuptureTriggersOnceForTwoDamageRowsInTheSameAttack()
    {
        for (ulong seed=1;seed<=8;seed++)
        {
            var match=Encounter("c1082",seed:seed,heroTarget:true,openingStatus:"Rupture",duplicateDamage:true);
            if (match.Snapshot.GetUnit("enemy1").GetStatus(BattleStatusKind.Rupture)==null) continue;
            var attack=Use(match,"c1082.s1","enemy1");
            Assert.Equal(2,attack.Events.Count(e=>e.Kind==BattleFlowEventKind.Damage && e.HitKind!=BattleHitKind.None));
            Assert.Single(attack.Events,e=>e.Kind==BattleFlowEventKind.Damage && e.HitKind==BattleHitKind.None);
            Assert.Equal(BattleHitKind.None,attack.Events.Last(e=>e.Kind==BattleFlowEventKind.Damage).HitKind);
            return;
        }
        Assert.Fail("Rupture did not land.");
    }

    [Fact]
    public void KawazuFlameBreathRollsAndOnlyItsSuccessUsesTheSecondGraphAndReadiness()
    {
        int activations=0;
        for (ulong seed=1;seed<=16;seed++)
        {
            var result=Use(Encounter("c1085",seed:seed),"c1085.s1","enemy1");
            bool triggered=result.Events.Any(e=>e.Origin==BattleActionOrigin.ExtraAttack && e.PresentationId=="c_kawazu_skill_02");
            Assert.Equal(triggered,result.Events.Any(e=>e.Kind==BattleFlowEventKind.ReadinessChanged && e.TargetId=="hero"));
            if (triggered) activations++;
        }
        Assert.InRange(activations,1,15);
    }

    [Theory]
    [InlineData(2,false)][InlineData(3,true)]
    public void KawanasAttackScaledDamageChecksTheTargetAfterTheTwoBleedRolls(int awakening,bool extra)
    {
        for (ulong seed=1;seed<=8;seed++)
        {
            var result=Use(Encounter("c1086",awakening,seed),"c1086.s1","enemy1");
            var bleeds=result.Snapshot.GetUnit("enemy1").Statuses.Count(s=>s.Kind==BattleStatusKind.Bleed);
            bool additional=result.Events.Any(e=>e.Kind==BattleFlowEventKind.Damage && e.HitKind==BattleHitKind.None);
            Assert.Equal(extra && bleeds>0,additional);
        }
    }

    private static BattleCatalogData Catalog()
    {
        using var stream=typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        return JsonSerializer.Deserialize<BattleCatalogData>(stream,new JsonSerializerOptions { IncludeFields=true })!;
    }

    private static BattleMatch Encounter(string kit, int awakening=3, ulong seed=17, bool incoming=false,
        bool guard=false, MonsterRank enemyRank=MonsterRank.Normal, int[]? enemyHealth=null, bool heroTarget=false,
        string? openingStatus=null, int enemyResistance=0, int rank=0, string? actorOpeningStatus=null, bool allyAttacksFirst=false,
        bool reduceIncoming=false, bool duplicateDamage=false)
    {
        var data=Catalog();
        var ids=data.skills.Where(s=>s.heroId==kit).OrderBy(s=>s.slot).Select(s=>s.id).ToArray();
        Assert.NotEmpty(ids);
        var form=new BattleUnitForm("normal",kit,ids);
        var actor=BattleUnit.CreateHero("hero",new HeroDefinition(kit,kit,BattleElement.Fire,"warrior","lion",form,
            new HeroGrowthPreset("fixture",5,60000,300000,60000,120000),
            Enumerable.Range(1,6).Select(n=>new HeroAwakeningStep(n,0,0))),new BattleSlot(BattleSide.Ally,1),60,6,awakening);
        const string basic="probe.s1";
        data.skills=data.skills.Append(new BattleCatalogData.SkillRow { id=basic,slot=1,target="one_enemy",attackRateBp=5000,powerBp=10000 }).ToArray();
        var rows=new List<BattleCatalogData.EffectRow>
        {
            new() { id=basic+".1",skillId=basic,order=1,phase="impact",operation="damage",recipient="targets",chanceBp=10000 }
        };
        if (actorOpeningStatus!=null)
        {
            var opener=ids[0];
            rows.Add(new() { id=opener+".fixture",skillId=opener,order=data.effects.Count(e=>e.skillId==opener)+1,
                phase="battle_start",operation="apply_status",recipient="self",status=actorOpeningStatus,chanceBp=10000,turns=4,value=2000 });
        }
        if (incoming)
        {
            rows.Add(new() { id=basic+".2",skillId=basic,order=2,phase="impact",operation="ignore_resistance",recipient="self",chanceBp=10000,value=1 });
            rows.Add(new() { id=basic+".3",skillId=basic,order=3,phase="after_hit",operation="apply_status",recipient="targets",
                status="Unhealable",chanceBp=10000,turns=2 });
        }
        var defenderSkills=new List<string> { basic };
        if (duplicateDamage)
        {
            var first=data.effects.Single(e=>e.skillId==ids[0] && e.operation=="damage");
            foreach (var e in data.effects.Where(e=>e.skillId==ids[0] && e.order>first.order)) e.order++;
            rows.Add(new() { id=first.id+".repeat",skillId=ids[0],order=first.order+1,phase=first.phase,
                operation="damage",recipient="targets",chanceBp=10000 });
        }
        if (openingStatus!=null || reduceIncoming)
        {
            const string setup="probe.setup";
            defenderSkills.Add(setup);
            data.skills=data.skills.Append(new BattleCatalogData.SkillRow { id=setup,slot=2,target="passive" }).ToArray();
            if (openingStatus!=null)
                rows.Add(new() { id=setup+".1",skillId=setup,order=1,phase="battle_start",operation="apply_status",
                    recipient="self",status=openingStatus,turns=9,value=openingStatus=="Rupture"?1200:5000,chanceBp=10000 });
            if (reduceIncoming)
                rows.Add(new() { id=setup+".guard",skillId=setup,order=openingStatus==null?1:2,phase="damage_guard",
                    operation="damage_reduction",recipient="self",value=3000,chanceBp=10000 });
        }
        data.effects=data.effects.Concat(rows).ToArray();
        BattleUnit Other(string id, BattleSide side, int slot, int hp)
        {
            var skills=side==BattleSide.Enemy && slot==1 ? defenderSkills.ToArray() : new[]{ basic };
            if (side==BattleSide.Enemy && slot==2 && guard) skills=new[]{"c1070.s1","c1070.s2","c1070.s3"};
            var otherForm=new BattleUnitForm("normal",id,skills);
            var position=new BattleSlot(side,slot);
            if (side==BattleSide.Enemy && slot==1 && heroTarget)
                return BattleUnit.CreateHero(id,new HeroDefinition(id,id,BattleElement.Dark,"warrior","lion",otherForm,
                    new HeroGrowthPreset("defender",5,60000,900000,60000,1000),
                    Enumerable.Range(1,6).Select(n=>new HeroAwakeningStep(n,0,0,effectResistanceBp:n==1?enemyResistance:0))),position,60,6,6);
            return BattleUnit.CreateMonster(id,new MonsterDefinition(id,id,BattleElement.Dark,otherForm),
                position,60,enemyRank,new BattleStats(200,hp,300,
                    (incoming && side==BattleSide.Enemy && slot==1) || (allyAttacksFirst && side==BattleSide.Ally) ? 500 : 1));
        }
        var units=new List<BattleUnit> { actor,Other("ally",BattleSide.Ally,2,500000) };
        var health=enemyHealth??new[]{500000,500000};
        for (int i=0; i<health.Length; i++) units.Add(Other("enemy"+(i+1),BattleSide.Enemy,i+1,health[i]));
        var formation=new BattleFormation(units);
        return new BattleMatch("roster",BattlePreviewFactory.RulesVersion,seed,formation,
            new BattleSkillResolver(data,enhancementRank: (_,skill)=>Math.Min(rank,data.enhancements.Count(e=>e.skillId==skill)),
                randomDualChanceBp:0),new BattleFlowRules(initialAllySoul:50,openingReadinessMaxBp:0));
    }

    private static BattleCommandResult Use(BattleMatch match,string skill,string? target=null,bool burn=false)
    {
        var state=match.Snapshot;
        var seq=state.LastCommandSequence+1;
        var result=match.Submit(new BattleSkillCommand(state.BattleId,state.RulesVersion,seq,"cmd"+seq,state.CurrentActorId,skill,target,burn));
        Assert.Equal(BattleCommandStatus.Accepted,result.Status);
        return result;
    }
}
