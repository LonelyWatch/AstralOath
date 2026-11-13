using System;
using System.IO;
using System.Reflection;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class BattleTurnIdleTests
    {
        private const BindingFlags NonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags Methods = NonPublic | BindingFlags.Public;
        private const string SourceRoot = "Assets/Game/Modules/CharacterPresentation/Res/Characters/";
        private static readonly Type ActorType = Type.GetType(
            "AstralOath.Client.BattleScene.BattlePreviewActor, AstralOath.Client.BattleScene", true);

        [TestCase("c1001", "ras")]
        [TestCase("c1002", "cecilia")]
        [TestCase("c1003", "rose")]
        [TestCase("c1004", "silk")]
        [TestCase("c1005", "mercedes")]
        [TestCase("c1006", "kise")]
        [TestCase("c1007", "wildred")]
        [TestCase("c1008", "arming")]
        [TestCase("c1009", "charlotte")]
        [TestCase("c1062", "angelica")]
        public void SourceTurnEntryPlaysOnceThenLoopsBattleIdle(string id, string model)
        {
            string root = SourceRoot + id;
            string json = File.ReadAllText($"{root}/Animations/model/{model}.spine.json");
            string atlas = File.ReadAllText($"{root}/Atlas/model/{model}.atlas.txt");
            string page = atlas.TrimStart().Split('\n')[0].TrimEnd('\r');

            WithActor(json, atlas, page, (actor, player) =>
            {
                Assert.That(player.CurrentAnimation, Is.EqualTo("idle"));
                Invoke(actor, "SetTurnReady", true);
                Assert.That(player.CurrentAnimation, Is.EqualTo("b_idle_ready"));
                Assert.That(player.Duration, Is.GreaterThan(0f));
                player.Tick(.11f);
                float readyTime = player.Time;
                Invoke(actor, "SetTurnReady", true);
                Assert.That(player.Time, Is.EqualTo(readyTime));

                player.Tick(player.Duration + .25f);
                Invoke(actor, "Update");
                Assert.That(player.CurrentAnimation, Is.EqualTo("b_idle"), "The ready clip must complete rather than wrap.");
                Invoke(actor, "SetTurnReady", true);
                Assert.That(player.CurrentAnimation, Is.EqualTo("b_idle"), "The same turn cannot re-arm the cue.");

                Invoke(actor, "PlayBattleAnimation", "attacked");
                Assert.That(player.CurrentAnimation, Is.EqualTo("attacked"));
                Invoke(actor, "PlayBattleIdle");
                Assert.That(player.CurrentAnimation, Is.EqualTo("b_idle"), "Hit recovery cannot repeat a consumed cue.");
                Invoke(actor, "SetTurnReady", false);

                Invoke(actor, "BeginPresentationControl");
                Invoke(actor, "SetTurnReady", true);
                Assert.That(player.CurrentAnimation, Is.EqualTo("idle"));
                Invoke(actor, "EndPresentationControl");
                Assert.That(player.CurrentAnimation, Is.EqualTo("b_idle_ready"), "A later turn receives a fresh cue.");

                Invoke(actor, "PlayBattleAnimation", "attacked");
                Invoke(actor, "PlayBattleIdle");
                Assert.That(player.CurrentAnimation, Is.EqualTo("b_idle"), "Interrupted entry does not replay.");
                Invoke(actor, "SetTurnReady", false);
                Invoke(actor, "SetTurnReady", true);
                Invoke(actor, "SetTurnReady", false);
                Assert.That(player.CurrentAnimation, Is.EqualTo("idle"), "An ended turn cannot keep the entry pose.");
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingOrStaticReadyClipKeepsWaitingLoopWithoutReset(bool staticReady)
        {
            var json = JObject.Parse(SkeletonPlayerTests.Json("2.1.25"));
            json["animations"]["idle"] = JObject.Parse("{\"bones\":{\"root\":{\"translate\":[{\"time\":0},{\"time\":1}]}}}");
            if (staticReady)
                json["animations"]["b_idle_ready"] = new JObject();

            WithActor(json.ToString(), SkeletonPlayerTests.Atlas, "page.png", (actor, player) =>
            {
                player.Tick(.11f);
                float waitingTime = player.Time;
                Invoke(actor, "SetTurnReady", true);
                Assert.That(player.CurrentAnimation, Is.EqualTo("idle"));
                Assert.That(player.Time, Is.EqualTo(waitingTime));
            });
        }

        private static void WithActor(string skeleton, string atlas, string pageName, Action<Component, ISkeletonPlayer> check)
        {
            var gameObject = new GameObject("Turn idle regression");
            gameObject.SetActive(false);
            var actor = gameObject.AddComponent(ActorType);
            var document = new TextAsset(skeleton);
            var atlasAsset = new TextAsset(atlas);
            var texture = new Texture2D(2, 2);
            try
            {
                Set("skeleton", document);
                Set("atlas", atlasAsset);
                Set("page", texture);
                Set("pageName", pageName);
                Invoke(actor, "OnEnable");
                check(actor, (ISkeletonPlayer)ActorType.GetField("player", NonPublic).GetValue(actor));
            }
            finally
            {
                Invoke(actor, "OnDisable");
                UnityEngine.Object.DestroyImmediate(gameObject);
                UnityEngine.Object.DestroyImmediate(document);
                UnityEngine.Object.DestroyImmediate(atlasAsset);
                UnityEngine.Object.DestroyImmediate(texture);
            }

            void Set(string field, object value) => ActorType.GetField(field, NonPublic).SetValue(actor, value);
        }

        private static void Invoke(Component actor, string method, params object[] args)
        {
            var member = ActorType.GetMethod(method, Methods);
            Assert.That(member, Is.Not.Null, method);
            member.Invoke(actor, args);
        }
    }
}
