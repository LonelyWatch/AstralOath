using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
namespace AstralOath.Tests
{
    public sealed class BattleDamageSequenceTests
    {
        [TestCase(2920,11)] [TestCase(2,7)] [TestCase(int.MaxValue,13)]
        public void VisualHitsPreserveTheExactAuthoritativeTotal(int total,int count)
        {
            var type=Type.GetType("AstralOath.Client.BattleScene.BattleDamageSequence, AstralOath.Client.BattleScene");
            Assert.That(type,Is.Not.Null,"Visual hit distribution must preserve authoritative totals.");
            var times=Enumerable.Range(0,count).Select(i=>.5f+i*.1f).ToArray();
            var rows=(Array)type.GetMethod("Build",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{total,times,.5f});
            Assert.That(rows.Length,Is.EqualTo(count));
            long sum=0;
            foreach(var row in rows)
            {
                int amount=(int)row.GetType().GetField("Amount").GetValue(row);
                Assert.That(amount,Is.GreaterThanOrEqualTo(0)); sum+=amount;
            }
            Assert.That(sum,Is.EqualTo(total));
        }
    }
}
