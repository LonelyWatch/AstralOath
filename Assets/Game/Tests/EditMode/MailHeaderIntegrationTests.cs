using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class MailHeaderIntegrationTests
    {
        [TestCase("HomeWindow")]
        [TestCase("HeroInventory")]
        [TestCase("HomeMoreMenu")]
        [TestCase("SummonWindow")]
        public void ExistingPagesUseTheSameClickableMailEntry(string page)
        {
            string module = page == "SummonWindow" ? "Summon" : "Home";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/" + module + "/Res/Prefab/" + page + ".prefab");
            Assert.That(prefab, Is.Not.Null);
            int count = 0;
            foreach (var map in prefab.GetComponentsInChildren<BindingMap>(true))
            {
                if (map.name != "MailEntry") continue;
                Assert.That(map.Require<GameButton>("button"), Is.Not.Null);
                Assert.That(map.Require<GameText>("count"), Is.Not.Null);
                Assert.That(map.Require<GameObject>("counter"), Is.Not.Null);
                count++;
            }
            Assert.That(count, Is.GreaterThan(0), "既有页面信封必须复用可点击的邮件入口及真实摘要角标。");
        }
    }
}
