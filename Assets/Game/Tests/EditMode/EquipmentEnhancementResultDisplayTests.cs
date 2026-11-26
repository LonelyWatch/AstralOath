using System;
using System.Reflection;
using AstralOath.Client.Home;
using NUnit.Framework;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class EquipmentEnhancementResultDisplayTests
    {
        private static EquipmentInventoryItem Item(int main, int defense, int damage) => new EquipmentInventoryItem
        {
            mainStat = new EquipmentStatValue { kind = "healthPct", value = main }, setId = "critical",
            substats = new[] { new EquipmentStatValue { kind = "attack", value = 34 },
                new EquipmentStatValue { kind = "critChanceBp", value = 400 },
                new EquipmentStatValue { kind = "defensePct", value = defense },
                new EquipmentStatValue { kind = "critDamageBp", value = damage } }
        };

        private static object Open(EquipmentInventoryItem item)
        {
            var type = typeof(EquipmentEnhancement).Assembly.GetType("AstralOath.Client.Home.EquipmentEnhancementResultDisplay");
            Assert.That(type, Is.Not.Null, "The open enhancement page needs its own immutable comparison baseline.");
            return Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { item }, null);
        }

        private static int Delta(object display, string method, object item) => (int)display.GetType().GetMethod(method).Invoke(display, new[] { item });

        [Test]
        public void ConsecutiveReceiptsKeepThePageOpeningScoreAsTheirBaseline()
        {
            var display = Open(Item(1200, 500, 700));
            Assert.That(Delta(display, "ScoreDelta", Item(1900, 1000, 700)), Is.EqualTo(8));
            Assert.That(Delta(display, "ScoreDelta", Item(2600, 1000, 1200)), Is.EqualTo(16),
                "The original +6 result remains (+16) relative to opening score27, not (+8) relative to the previous receipt.");
        }

        [Test]
        public void PreviouslyEnhancedSubstatKeepsItsDeltaWhenAnotherSubstatChanges()
        {
            var display = Open(Item(1200, 500, 700));
            Assert.That(Delta(display, "SubstatDelta", new EquipmentStatValue { kind = "defensePct", value = 1000 }), Is.EqualTo(500));
            Assert.That(Delta(display, "SubstatDelta", new EquipmentStatValue { kind = "critDamageBp", value = 1200 }), Is.EqualTo(500));
            Assert.That(Delta(display, "SubstatDelta", new EquipmentStatValue { kind = "attack", value = 34 }), Is.Zero);
        }

        [Test]
        public void ReopeningThePageStartsANewComparisonRatherThanInventingHistoricalGains()
        {
            var display = Open(Item(2600, 1000, 1200));
            Assert.That(Delta(display, "ScoreDelta", Item(2600, 1000, 1200)), Is.Zero);
            Assert.That(Delta(display, "SubstatDelta", new EquipmentStatValue { kind = "defensePct", value = 1000 }), Is.Zero);
        }

        [Test]
        public void MissingCounterFieldsStayUnknownWhileExplicitZeroIsKnown()
        {
            var known = typeof(EquipmentStatValue).GetField("enhancementCountKnown");
            var count = typeof(EquipmentStatValue).GetField("enhancementCount");
            Assert.That(known, Is.Not.Null); Assert.That(count, Is.Not.Null);
            var old = JsonUtility.FromJson<EquipmentStatValue>("{\"kind\":\"defensePct\",\"value\":500}");
            var fresh = JsonUtility.FromJson<EquipmentStatValue>("{\"kind\":\"defensePct\",\"value\":500,\"enhancementCount\":0,\"enhancementCountKnown\":true}");
            Assert.That(known.GetValue(old), Is.EqualTo(false));
            Assert.That(known.GetValue(fresh), Is.EqualTo(true)); Assert.That(count.GetValue(fresh), Is.EqualTo(0));
        }

        [Test]
        public void OnlyKnownPositiveEnhancementCountsAddTheOrangeNameSuffix()
        {
            var display = Open(Item(1200, 500, 700));
            string Suffix(string json) => (string)display.GetType().GetMethod("CountSuffix").Invoke(null,
                new object[] { JsonUtility.FromJson<EquipmentStatValue>(json) });
            Assert.That(Suffix("{\"kind\":\"defensePct\",\"value\":1000}"), Is.Empty);
            Assert.That(Suffix("{\"kind\":\"defensePct\",\"value\":500,\"enhancementCount\":0,\"enhancementCountKnown\":true}"), Is.Empty);
            Assert.That(Suffix("{\"kind\":\"defensePct\",\"value\":1000,\"enhancementCount\":1,\"enhancementCountKnown\":true}"),
                Is.EqualTo(" <color=#FF7E00>(1)</color>"));
        }
    }
}
