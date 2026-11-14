using System;
using System.IO;
using System.Reflection;
using AstralOath.Shared.Battle;
using NUnit.Framework;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class BattleInspectionContentTests
    {
        private const string Catalog = "Assets/Game/Modules/BattleScene/Configs/battle-catalog.json";

        [Test]
        public void EverySkillAndStatusHasReadableChineseInspectionCopy()
        {
            var catalog = JsonUtility.FromJson<BattleCatalogData>(File.ReadAllText(Catalog));
            Assert.That(catalog.skills, Is.Not.Empty);
            var type = Type.GetType("AstralOath.Client.BattleScene.BattleInspectionContent, AstralOath.Client.BattleScene", true);
            var content = Activator.CreateInstance(type, catalog);
            string Read(string method, object value) => (string)type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(content, new[] { value });
            foreach (var skill in catalog.skills)
            {
                string description = Read("SkillDescription", skill);
                Assert.That(description, Does.Match("[\\u4e00-\\u9fff]"), skill.id);
                string burn = Read("SkillBurnDescription", skill);
                if (skill.soulBurnCost > 0)
                    Assert.That(burn, Does.Match("[\\u4e00-\\u9fff]"), skill.id);
                else
                    Assert.That(burn, Is.Empty, skill.id);
            }
            foreach (BattleStatusKind kind in Enum.GetValues(typeof(BattleStatusKind)))
            {
                Assert.That(Read("StatusName", kind), Does.Match("[\\u4e00-\\u9fff]"), kind.ToString());
                Assert.That(Read("StatusDescription", kind), Does.Match("[\\u4e00-\\u9fff]"), kind.ToString());
            }

            (string Title, string Description, BattleStatusKind? Status) Auxiliary(string id) =>
                ((string Title, string Description, BattleStatusKind? Status))type.GetMethod("SkillAuxiliaryEffect", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(content, new object[] { Array.Find(catalog.skills, skill => skill.id == id) });
            Assert.That(Auxiliary("c1001.s1"), Is.EqualTo((string.Empty, string.Empty, (BattleStatusKind?)null)));
            Assert.That(Auxiliary("c1001.s2").Title, Is.EqualTo("夹攻"));
            var defense = Auxiliary("c1001.s3");
            Assert.That(defense.Title, Is.EqualTo(Read("StatusName", BattleStatusKind.DefenseUp)));
            Assert.That(defense.Description, Is.EqualTo("防御力提升60%。"));
            Assert.That(Auxiliary("c1005.s3").Description, Is.EqualTo("攻击力提升50%。"));
            Assert.That(defense.Description, Is.EqualTo(Read("StatusDescription", BattleStatusKind.DefenseUp)),
                "The secondary card shows the same original description as a held status icon.");

            var ras = Array.Find(catalog.skills, skill => skill.id == "c1001.s1");
            string enhancements = Read("SkillEnhancements", ras);
            Assert.That(enhancements, Does.StartWith("+1").And.Contain("5%").And.Contain("+5"));
            var silkPassive = Array.Find(catalog.skills, skill => skill.id == "c1004.s2");
            Assert.That(Read("SkillEnhancements", silkPassive), Is.Empty);

            Assert.That(Read("SkillDescription", ras), Is.EqualTo("挥剑攻击敌人，有75%的几率解除1个强化效果。根据自身最大生命值等比提升伤害量。"));
            var arminPassive = Array.Find(catalog.skills, skill => skill.id == "c1008.s2");
            Assert.That(Read("SkillDescription", arminPassive), Is.EqualTo(
                "受到攻击时，有<color=#FF9900>20%</color>的几率发动反击。\n我军人员受到单一攻击时，使其获得30%的<color=#FFFFFF>伤害降低</color>效果。"));
            var angelica = Array.Find(catalog.skills, skill => skill.id == "c1062.s3");
            Assert.That(Read("SkillBurnDescription", angelica), Is.EqualTo("此技能赋予的强化效果持续回合数\n增加2回合。"));
            var corvus = Array.Find(catalog.skills, skill => skill.id == "c1012.s3");
            Assert.That(Read("SkillBurnDescription", corvus), Is.EqualTo("生命值恢复量变为2倍。"));
            Assert.That(Read("StatusDescription", BattleStatusKind.Stealth), Is.EqualTo(
                "若存在其他我军人员，则不会被敌人指定为技能目标。受到全体攻击时，获得伤害降低效果，受到攻击造成的伤害时解除。"));
            Assert.That(Auxiliary("c1001.s2").Description, Is.EqualTo(
                "其他我军人员以基本技能发动攻击时，若自己处于可行动的状态下，也会使用基本技能攻击相同的目标。\n反击时不会触发。"));
        }
    }
}
