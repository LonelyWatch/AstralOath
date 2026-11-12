using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Tests
{
    public sealed class BattlePostEffectTests
    {
        private const string RasThirdSkill = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1001/Configs/stagept/c_ras_skill_03.graph.json";
        private IDisposable effect;

        [TearDown]
        public void Cleanup() => effect?.Dispose();

        [Test]
        public void SourceSineModulationSharesTheBlurWindow()
        {
            var graph = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(RasThirdSkill).text);
            var nodes = graph["source"]["entities"].OfType<JObject>().ToDictionary(node => (int)node["guid"]);
            var edge = graph["source"]["connections"].Single(item => (int)item["from"] == 33 && (int?)item["when"] == 2);
            var type = Type.GetType("AstralOath.Client.BattleScene.BattleSourcePostEffect, AstralOath.Client.BattleScene");
            Assert.That(type, Is.Not.Null, "Battle playback needs a source-driven post-effect lifetime.");

            effect = (IDisposable)Activator.CreateInstance(type, new object[] { null });
            Call("Play", nodes[33], new[] { nodes[(int)edge["to"]] });
            Assert.That(ActiveRange(), Is.EqualTo(150f).Within(.001f),
                "The hit blur starts with its source amplitude at phase .5.");
            Call("Tick", .15f);
            Assert.That(ActiveRange(), Is.EqualTo(135.355f).Within(.001f));
            Call("Tick", .15f);
            Assert.That(ActiveRange(), Is.Null, "The shader is released when the 300 ms source interval ends.");
        }

        [Test]
        public void StoppingOneBlurKeepsTheOtherSourceWindow()
        {
            var type = Type.GetType("AstralOath.Client.BattleScene.BattleSourcePostEffect, AstralOath.Client.BattleScene");
            Assert.That(type, Is.Not.Null);
            effect = (IDisposable)Activator.CreateInstance(type, new object[] { null });

            Call("Play", Blur("first", 40f), Array.Empty<JObject>());
            Call("Play", Blur("second", 70f), Array.Empty<JObject>());
            Assert.That(ActiveRange(), Is.EqualTo(70f));

            Call("Stop", "second");
            Assert.That(ActiveRange(), Is.EqualTo(40f));
            Call("Reset");
            Assert.That(ActiveRange(), Is.Null);
        }

        private float? ActiveRange() => (float?)effect.GetType()
            .GetProperty("ActiveRange", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(effect);

        private void Call(string method, params object[] args) => effect.GetType().GetMethod(method).Invoke(effect, args);

        private static JObject Blur(string id, float range) => new JObject
        {
            ["id"] = id,
            ["program"] = "sprite_blur",
            ["u_range"] = range,
            ["u_sample"] = 3,
            ["direction_y"] = 1,
            ["duration"] = 500
        };
    }
}
