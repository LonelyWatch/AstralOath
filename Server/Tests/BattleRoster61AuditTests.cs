using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleRoster61AuditTests
{
    private const string Basic = "audit.s1";
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };

    [Theory]
    [InlineData("c1079", BattleStatusKind.Unhealable)]
    [InlineData("c1080", BattleStatusKind.HitChanceDown)]
    public void HarmfulBasicEffectHonorsHitChanceAndResistance(string hero, BattleStatusKind expected)
    {
        var cast = Cast(Match(hero, foe: Buff("EvasionUp", 10000)), hero + ".s1", "foe");
        Assert.Equal(BattleHitKind.Miss, Assert.Single(cast.Events, e => e.Kind == BattleFlowEventKind.Damage).HitKind);
        Assert.Null(cast.Snapshot.GetUnit("foe").GetStatus(expected));
        Assert.Contains(cast.Events, e => e.EffectRejection == BattleEffectRejection.Missed && e.StatusKind == expected);
        var results = Enumerable.Range(1, 20).Select(seed => Cast(Match(hero, (ulong)seed, resistance: 30000), hero + ".s1", "foe")).ToArray();
        Assert.All(results, result => Assert.Null(result.Snapshot.GetUnit("foe").GetStatus(expected)));
        Assert.Contains(results, result => result.Events.Any(e => e.EffectRejection == BattleEffectRejection.Resisted));
        Assert.Contains(results, result => result.Events.All(e => e.EffectRejection != BattleEffectRejection.Resisted));
    }

    [Fact]
    public void DieneS1PushesEvenOnAnEvadedAttack()
    {
        var match = Match("c1076", foe: Buff("EvasionUp", 10000), actor: Buff("DefenseUp", 6000));
        var cast = Cast(match, "c1076.s1", "foe");
        Assert.Equal(BattleHitKind.Miss, Assert.Single(cast.Events, e => e.Kind == BattleFlowEventKind.Damage).HitKind);
        Assert.Equal(new[] {1000, 2000}, cast.Events.Where(e => e.Kind == BattleFlowEventKind.ReadinessChanged && e.TargetId=="hero").Select(e => e.Amount));
    }

    [Fact]
    public void DieneBarrierIsEnhancedMultiplicativelyAndHerDisabledCleanseStaysDisabled()
    {
        var match = Match("c1076", rank:6, ally: Buff("Unhealable", 0));
        var opening = match.Snapshot.GetUnit("ally").ReadinessBp;
        var cast = Cast(match, "c1076.s2");
        var unit = cast.Snapshot.GetUnit("ally");
        Assert.NotNull(unit.GetStatus(BattleStatusKind.Unhealable));
        Assert.Equal(30000, unit.GetStatus(BattleStatusKind.Barrier).Strength);
        Assert.Equal(2, unit.GetStatus(BattleStatusKind.AttackUp).RemainingTurns);
        Assert.Contains(cast.Events, e => e.Kind == BattleFlowEventKind.ReadinessChanged && e.TargetId=="ally" && e.Amount==opening+1500);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    public void DieneBurnShortensHerAlreadyCoolingSecondSkillAndKeepsTheCleanse(bool burn, int expectedCooldown)
    {
        var match = Match("c1076", ally: Buff("Unhealable",0));
        Cast(match,"c1076.s2");
        var prayer = Cast(match,"c1076.s3",burn:burn);
        Assert.Equal(expectedCooldown,prayer.Snapshot.GetUnit("hero").GetCooldown("c1076.s2"));
        Assert.Null(prayer.Snapshot.GetUnit("ally").GetStatus(BattleStatusKind.Unhealable));
        Assert.NotNull(prayer.Snapshot.GetUnit("ally").GetStatus(BattleStatusKind.PenetrationResistanceUp));
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void CermiaResetIsSlotSpecificAndNeverEnablesTheZeroRateShield(int awakening, bool attackUp)
    {
        var match = Match("c1079", awakening:awakening);
        Cast(match,"c1079.s3","foe");
        var momentum = Cast(match,"c1079.s2");
        Assert.Equal("hero",momentum.Snapshot.CurrentActorId);
        Assert.Equal(0,momentum.Snapshot.GetUnit("hero").GetCooldown("c1079.s3"));
        Assert.True(momentum.Snapshot.GetUnit("hero").GetCooldown("c1079.s2")>0);
        Assert.Equal(attackUp,momentum.Snapshot.GetUnit("hero").GetStatus(BattleStatusKind.GreaterAttackUp)!=null);
        Assert.Null(momentum.Snapshot.GetUnit("hero").GetStatus(BattleStatusKind.Barrier));
    }

    [Theory]
    [InlineData(false, 11500)]
    [InlineData(true, 16500)]
    public void CermiaBurnUsesSourceDamageAndLeavesASecondSkillOnCooldown(bool burn, int rate)
    {
        var match = Match("c1079");
        Cast(match,"c1079.s2");
        var before = match.Snapshot.GetUnit("hero");
        var foe = match.Snapshot.GetUnit("foe");
        var cast = Cast(match,"c1079.s3","foe",burn);
        var hit = Assert.Single(cast.Events, e => e.Kind==BattleFlowEventKind.Damage);
        Assert.Equal(EpicSevenCombatMath.Damage(before.Stats.Attack*1.75m,foe.Stats.Defense*.5m,before.MaximumHealth,rate,9000,0,0,0,hit.HitKind,before.Stats.CritDamageBp),hit.Amount);
        Assert.True(cast.Snapshot.GetUnit("hero").GetCooldown("c1079.s2")>0);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void PavelBuffPrecedesEveryAoEDamageAndSuppressesGuaranteedCounters(int awakening, bool grantsBuff)
    {
        var match = Match("c1080",awakening:awakening,foe:Buff("Counter",10000));
        Assert.Contains(Cast(match,"c1080.s1","foe").Events,e=>e.Origin==BattleActionOrigin.Counter);
        var cast = Cast(match,"c1080.s2",burn:true);
        Assert.DoesNotContain(cast.Events,e=>e.Origin==BattleActionOrigin.Counter);
        var events = cast.Events.ToList();
        int buff = events.FindIndex(e=>e.Kind==BattleFlowEventKind.StatusApplied && e.StatusKind==BattleStatusKind.AttackUp);
        Assert.Equal(grantsBuff,buff>=0);
        Assert.True(buff<events.FindIndex(e=>e.Kind==BattleFlowEventKind.Damage));
        Assert.Contains(events,e=>e.Kind==BattleFlowEventKind.ExtraTurnGranted);
        Assert.Equal(2,events.Count(e=>e.Kind==BattleFlowEventKind.Damage));
    }

    [Theory]
    [InlineData(1, 9000, 11000, 750)]
    [InlineData(2, 8000, 10000, 1125)]
    [InlineData(3, 16000, 10000, 1500)]
    public void PavelUsesHisCurrentSpeedAtTheSourceRate(int slot, int rate, int power, int ppm)
    {
        var match = Match("c1080",awakening:2,actor:Buff("SpeedUp",3000));
        var actor = match.Snapshot.GetUnit("hero");
        var foe = match.Snapshot.GetUnit("foe");
        var cast = Cast(match,"c1080.s"+slot,slot==2?null:"foe");
        var hit = Assert.Single(cast.Events,e=>e.Kind==BattleFlowEventKind.Damage && e.TargetId=="foe");
        decimal speed = decimal.Floor(actor.Stats.Speed * 1.30m);
        int expected = EpicSevenCombatMath.Damage(actor.Stats.Attack,foe.Stats.Defense,actor.MaximumHealth,rate,power,0,0,0,hit.HitKind,actor.Stats.CritDamageBp,
            damageMultiplier:1m+speed*ppm/1_000_000m);
        Assert.Equal(expected,hit.Amount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CeriseForcedAssistRequiresAnEligibleLivingAlly(bool disabled)
    {
        var match = Match("c1081",ally:disabled?Buff("Stun",0):null);
        Cast(match,"c1081.s3");
        var shot = Cast(match,"c1081.s1","foe");
        var helpers = shot.Events.Where(e=>e.Kind==BattleFlowEventKind.Action && e.Origin==BattleActionOrigin.DualAttack).ToArray();
        Assert.Equal(disabled?0:1, helpers.Length);
        if (!disabled) Assert.Equal("ally",helpers[0].SourceId);
    }

    [Theory]
    [InlineData(false, null, false)]
    [InlineData(true, null, true)]
    [InlineData(true, "Immunity", false)]
    [InlineData(true, "EvasionUp", false)]
    public void CeriseBurnIgnoresResistanceOnly(bool burn, string? protection, bool lands)
    {
        var match = Match("c1081", resistance:30000, foe:protection==null?null:Buff(protection,10000));
        var cast = Cast(match,"c1081.s2","foe",burn);
        var recipient = cast.Snapshot.GetUnit("foe");
        Assert.Equal(lands,recipient.GetStatus(BattleStatusKind.Stun)!=null);
        Assert.Equal(lands,recipient.GetStatus(BattleStatusKind.Unbuffable)!=null);
        Assert.Contains(cast.Events,e=>e.Kind==BattleFlowEventKind.ReadinessChanged && e.TargetId=="hero" && e.Amount==5000);
    }

    [Theory]
    [InlineData(2,false)]
    [InlineData(3,true)]
    public void CeriseS3ShortensEnemyBuffBeforeApplyingItsTwoDebuffs(int awakening, bool invincible)
    {
        for(ulong seed=1;seed<=32;seed++)
        {
            var match = Match("c1081",seed,awakening,rank:5,foe:Buff("Immunity",0,1),enemyElement:BattleElement.Wind);
            var cast = Cast(match,"c1081.s3");
            var target = cast.Snapshot.GetUnit("foe");
            Assert.DoesNotContain(cast.Events,e=>e.Kind==BattleFlowEventKind.Damage && e.HitKind==BattleHitKind.Miss);
            if(target.GetStatus(BattleStatusKind.Restrict)==null || target.GetStatus(BattleStatusKind.SpeedDown)==null)continue;
            var events=cast.Events.ToList();
            int remove=events.FindIndex(e=>e.Kind==BattleFlowEventKind.StatusRemoved && e.TargetId=="foe" && e.StatusKind==BattleStatusKind.Immunity);
            int apply=events.FindIndex(e=>e.Kind==BattleFlowEventKind.StatusApplied && e.TargetId=="foe" && e.StatusKind==BattleStatusKind.SpeedDown);
            Assert.True(remove>=0 && apply>remove);
            Assert.Equal(invincible,events.Any(e=>e.Kind==BattleFlowEventKind.StatusApplied && e.TargetId=="hero" && e.StatusKind==BattleStatusKind.Invincible));
            return;
        }
        Assert.Fail("No seed landed both debuffs after shortening a one-turn immunity.");
    }

    [Theory]
    [InlineData("c1076", 3, 20, false)]
    [InlineData("c1079", 3, 10, true)]
    [InlineData("c1080", 2, 20, false)]
    [InlineData("c1081", 2, 20, true)]
    public void BurnRequiresFullSoulAndACoolingSkillCannotSpendItAgain(string hero, int slot, int price, bool singleTarget)
    {
        string skill = hero + ".s" + slot;
        string? target = singleTarget ? "foe" : null;
        var empty = Match(hero, soul:price - 1);
        var before = empty.Snapshot;
        var insufficient = empty.Submit(new BattleSkillCommand(before.BattleId, before.RulesVersion, 1, "insufficient", "hero", skill, target, true));
        Assert.Equal(BattleCommandStatus.InvalidAction, insufficient.Status);
        Assert.Empty(insufficient.Events);
        Assert.Equal(before.LastCommandSequence, insufficient.Snapshot.LastCommandSequence);
        Assert.Equal(price - 1, insufficient.Snapshot.GetSoul(BattleSide.Ally));
        var match = Match(hero, soul:50);
        var first = Cast(match, skill, target, burn:true);
        Assert.True(first.Snapshot.GetUnit("hero").GetCooldown(skill)>0);
        var cooldown = match.Submit(new BattleSkillCommand(before.BattleId, before.RulesVersion, 2, "cooldown", "hero", skill, target, true));
        Assert.Equal(BattleCommandStatus.SkillOnCooldown, cooldown.Status);
        Assert.Empty(cooldown.Events);
        Assert.Equal(first.Snapshot.GetSoul(BattleSide.Ally), cooldown.Snapshot.GetSoul(BattleSide.Ally));
        Assert.Equal(first.Snapshot.LastCommandSequence, cooldown.Snapshot.LastCommandSequence);
    }

    private sealed record Opening(string Kind,int Strength,int Turns);
    private static Opening Buff(string kind,int strength,int turns=5)=>new(kind,strength,turns);

    private static BattleMatch Match(string id,ulong seed=17,int awakening=3,int rank=0,int resistance=0,int soul=50,
        Opening? actor=null,Opening? ally=null,Opening? foe=null,BattleElement enemyElement=BattleElement.Dark)
    {
        using var stream=typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        var data=JsonSerializer.Deserialize<BattleCatalogData>(stream,Options)!;
        var skills=data.skills.Where(s=>s.heroId==id).OrderBy(s=>s.slot).Select(s=>s.id).ToArray();
        var element=id switch { "c1079"=>BattleElement.Fire, "c1080"=>BattleElement.Wind, _=>BattleElement.Ice };
        var form=new BattleUnitForm("normal",id,skills);
        var hero=BattleUnit.CreateHero("hero",new HeroDefinition(id,id,element,"warrior","lion",form,
            new HeroGrowthPreset("audit",5,60000,300000,60000,120000),Enumerable.Range(1,6).Select(n=>new HeroAwakeningStep(n,0,0))),
            new BattleSlot(BattleSide.Ally,1),60,6,awakening);
        var additions=new List<BattleCatalogData.EffectRow>();
        var probeSkills=new List<BattleCatalogData.SkillRow>();
        BattleUnit Other(string name,BattleSide side,int slot,Opening? buff)
        {
            string key=Basic+name;
            probeSkills.Add(new BattleCatalogData.SkillRow {id=key,slot=1,target="one_enemy",attackRateBp=3000,powerBp=10000});
            additions.Add(new BattleCatalogData.EffectRow {id=key+".damage",skillId=key,order=1,phase="impact",operation="damage",recipient="targets",chanceBp=10000});
            AddOpening(key,2,buff);
            return BattleUnit.CreateMonster(name,new MonsterDefinition(name,name,enemyElement,new BattleUnitForm("normal",name,new[]{key})),
                new BattleSlot(side,slot),60,MonsterRank.Normal,new BattleStats(200,100000,300,1,effectResistanceBp:side==BattleSide.Enemy?resistance:0));
        }
        void AddOpening(string skill,int order,Opening? state)
        {
            if (state==null)return;
            additions.Add(new BattleCatalogData.EffectRow {id=skill+".opening",skillId=skill,order=order,phase="battle_start",operation="apply_status",
                recipient="self",status=state.Kind,chanceBp=10000,turns=state.Turns,value=state.Strength});
        }
        AddOpening(skills[0],data.effects.Count(e=>e.skillId==skills[0])+1,actor);
        var formation=new BattleFormation(new[]{hero,Other("ally",BattleSide.Ally,2,ally),Other("foe",BattleSide.Enemy,1,foe),Other("other",BattleSide.Enemy,2,null)});
        data.skills=data.skills.Concat(probeSkills).ToArray();
        data.effects=data.effects.Concat(additions).ToArray();
        return new BattleMatch("audit",BattlePreviewFactory.RulesVersion,seed,formation,new BattleSkillResolver(data,
            enhancementRank:(_,skill)=>Math.Min(rank,data.enhancements.Count(e=>e.skillId==skill)),randomDualChanceBp:0),
            new BattleFlowRules(initialAllySoul:soul,openingReadinessMaxBp:0));
    }

    private static BattleCommandResult Cast(BattleMatch match,string skill,string? target=null,bool burn=false)
    {
        var state=match.Snapshot;
        ulong sequence=state.LastCommandSequence+1;
        var result=match.Submit(new BattleSkillCommand(state.BattleId,state.RulesVersion,sequence,"audit"+sequence,state.CurrentActorId,skill,target,burn));
        Assert.Equal(BattleCommandStatus.Accepted,result.Status);
        return result;
    }
}
