# 英雄等级面板与战斗单位

## 成长公式与样本

同职业、同星座、同初始星级可以优先复用模板，比较时还要对齐当前星级和觉醒。社区计算器的样本中，3★ 天秤骑士拉斯与雅洛薇 1 级零觉醒的攻／血／防／速同为 **53／263／68／95**。4★ 巨蟹精灵师安洁莉卡、绊爱与流星雅卡泰丝同为 **40／253／82／98**。英雄显式引用模板，同组可以录入独立系数，觉醒节点按英雄维护。[计算器及其数据](https://gacha-data.com/epic-seven/stat_calc/)

设等级为 `L`、当前星级为 `G`，成长系数为 `A/H/D/S`。1 级面板也用下式计算。1–6★ 等级上限依次为 15／20／30／40／50／60。[社区计算器源码](https://gacha-data.com/assets/calc.js?v=b256a749) · [早期社区实现](https://github.com/RaphaelDDL/e7db-api/blob/3e7ed97538936f9fc9f9341bdaf6228808828aa6/src/utils/Utility.js#L123)

```text
星级倍率 = 1 + (G - 1) × 0.075
攻击 = floor(A × (1 + L / 6) × 星级倍率)
生命 = floor(H × (1 + L / 3) × 星级倍率)
防御 = floor(D × (1 + L / 8) × 星级倍率)
速度 = floor(S)
```

同星级内攻、血、防的取整前每级增量固定。4★ 梅赛德丝零觉醒 Lv1、Lv2、Lv3 攻击为 90、102、115，显示分别加 12、13。升星会在同等级上改变攻、血、防，速度与等级和星级无关。攻击和生命按计算器设最小值 1。[社区计算器源码](https://gacha-data.com/assets/calc.js?v=b256a749)

觉醒把已解锁节点的同属性百分比与固定值分别求和。攻、血、防以 `floor(已取整基础值 × (1 + 百分比总和)) + 固定值总和` 计算。速度及副属性加节点值。基础暴击 15%、暴伤 150%、效果命中和抗性 0%。例如满档梅赛德丝攻击先取整为 952，再应用 9% 和固定 +150，得到 1187。装备、神器、刻印、技能觉醒与转职树各自处理。[拉斯觉醒](https://epic7tools.dev/heroes/ras/) · [梅赛德丝觉醒](https://epic7x.com/character/mercedes/) · [安洁莉卡觉醒](https://epic7tools.dev/heroes/angelica/)

三人 Lv60 六星六觉醒攻／血／防／速与公开面板一致。

- 拉斯 `c1001` 为 758／5826／672／95。[面板](https://epic7x.com/character/ras/)
- 梅赛德丝 `c1005` 为 1187／4491／627／101。[面板](https://epic7x.com/character/mercedes/)
- 安洁莉卡 `c1062` 为 576／5700／743／98。[面板](https://epic7x.com/character/angelica/)

浮点取整仍需原作面板复验。梅赛德丝 5★ Lv47 零觉醒生命按社区计算器运算顺序为 3054，精确小数计算得 3055。当前回归沿用 3054。本地原始表 `class_stat_preset.db`、`character_player*.db` 和 `zodiac_stone_2.db` 已定位，数值待解码。本轮核对来源为公开数据和计算器。

## 配表与使用

1. 在 `Config/Tables/battle_growth_presets.csv` 复用或填写成长模板。职业、星座与初始星级须与英雄一致。系数以千分整数保存，例如 40.2 写 40200。
2. 在 `battle_heroes.csv` 指定 `growthPresetId`。等级、星级、觉醒和八项面板列供导表验收。运行时从模板与觉醒计算。
3. 在 `battle_awakening_steps.csv` 为每个 `heroId` 填 1–6 阶节点。百分比字段使用万分比，10000 为 100%。
4. 在仓库根执行 `& ./.tools/powershell/pwsh.exe -NoProfile -File Tools/Config/Generate.ps1 -SyncClient`。同步或检查客户端时会自动读回二进制、检查引用，并用共享公式校验锚点。

```csharp
BattleStats panel = HeroStatCalculator.Calculate(
    hero.GrowthPreset, level, stars, awakenings, hero.AwakeningSteps);
BattleUnit ally = BattleUnit.CreateHero("ally-1", hero,
    new BattleSlot(BattleSide.Ally, 1), level, stars, awakenings);
BattleUnit boss = BattleUnit.CreateMonster("enemy-1", monster,
    new BattleSlot(BattleSide.Enemy, 1), encounterLevel, MonsterRank.Boss, encounterStats);
var formation = new BattleFormation(new[] { ally, boss });
```

## 怪物与形态

本地 `character_monster_*`、`cs_monster_*` 分成多组表，数值未解码。暂由遭遇明确配置等级、面板和类别，能表达同怪物在不同关卡的强度。原作 `stagept/c_tamarinne_skill_03.stg` 通过 `MODEL_BODY` 切换演出模型，支持将形态与单位身份分开建模。状态继承和 Boss 阶段随技能或遭遇规则验收。[战斗单位决策](../architecture/adr/010-battle-units.md)

本轮实现共享计算、单位创建与初始站位校验。第三步接入服务端对局和客户端显示。BattleScene 当前继续使用作者预设的站位与 HUD 数值。
