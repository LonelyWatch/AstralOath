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
            Assert.That(defense.Description, Does.Contain("60%"));
            Assert.That(Auxiliary("c1005.s3").Description, Does.Contain("50%"));
            string Describe(BattleStatusKind kind, int strength) =>
                (string)type.GetMethod("StatusDescriptionWithStrength", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(content, new object[] { kind, strength });
            Assert.That(Describe(BattleStatusKind.DefenseUp, 7500), Is.EqualTo("防御力提升75%。"));
            Assert.That(Describe(BattleStatusKind.DefenseUp, 0), Is.EqualTo(Read("StatusDescription", BattleStatusKind.DefenseUp)));
            Assert.That(Describe(BattleStatusKind.Barrier, 6000), Is.EqualTo(Read("StatusDescription", BattleStatusKind.Barrier)));

            var ras = Array.Find(catalog.skills, skill => skill.id == "c1001.s1");
            string enhancements = Read("SkillEnhancements", ras);
            Assert.That(enhancements, Does.StartWith("+1").And.Contain("5%").And.Contain("+5"));
            var silkPassive = Array.Find(catalog.skills, skill => skill.id == "c1004.s2");
            Assert.That(Read("SkillEnhancements", silkPassive), Is.Empty);

            var armin = Array.Find(catalog.skills, skill => skill.id == "c1008.s3");
            Assert.That(Read("SkillBurnDescription", armin), Does.Contain("1"));
            var angelica = Array.Find(catalog.skills, skill => skill.id == "c1062.s3");
            Assert.That(Read("SkillBurnDescription", angelica), Does.Contain("2"));
            Assert.That(Read("SkillDescription", angelica), Does.Not.Contain("燃魂"));
            Assert.That(Read("StatusDescription", BattleStatusKind.Stealth), Does.Contain("范围攻击"));
            Assert.That(Read("StatusDescription", BattleStatusKind.Provoke), Does.Contain("基本技能"));
        }
    }
}
