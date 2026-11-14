using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstralOath.Tests
{
    public sealed class BattleSkillTimelineNavigationTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static Type WindowType => Type.GetType(
            "AstralOath.Client.BattleScene.Editor.SkillTimelineWindow, AstralOath.Client.BattleScene.Editor", true);

        [Test]
        public void PointerAnchoredZoomKeepsTheTimeUnderTheMouse()
        {
            var method = WindowType.GetMethod("AnchoredScroll", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Zoom needs an explicit pointer/playhead anchor.");
            float before = 3f + 12f / 2f * .65f;
            float start = (float)method.Invoke(null, new object[] { 12f, 3f, 2f, 5f, .65f });
            Assert.That(start + 12f / 5f * .65f, Is.EqualTo(before).Within(.0001f));
            Assert.That((float)method.Invoke(null, new object[] { 12f, 8f, 5f, 1f, .65f }), Is.Zero);
        }

        [TestCase(12f, 5f, 6f)]
        [TestCase(12f, 12f, 12f)]
        [TestCase(1f, 0f, 1f)]
        public void FrameRangeIncludesTheSelectedClipAndTerminalMarkers(float duration, float from, float to)
        {
            var method = WindowType.GetMethod("FrameTimeRange", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "F must frame both clips and single-frame markers.");
            var range = (Vector2)method.Invoke(null, new object[] { duration, from, to });
            Assert.That(range.x, Is.InRange(0f, duration));
            Assert.That(range.y, Is.InRange(1f, 48f));
            Assert.That(range.x, Is.LessThanOrEqualTo(from + .0001f));
            Assert.That(range.x + duration / range.y, Is.GreaterThanOrEqualTo(to - .0001f));
        }

        [Test]
        public void ClickingTheVisibleMiddleOfALongClipDoesNotJumpTheTimeline()
        {
            WithWindow("c1008", 3, (type, window) =>
            {
                var preview = type.GetField("preview", Instance).GetValue(window);
                var cues = (IReadOnlyList<(float At, JObject Node, float Duration, JObject[] Modulators)>)
                    preview.GetType().GetProperty("Cues", Instance).GetValue(preview);
                var tail = cues.First(cue => cue.Node["guid"] == null && cue.Duration > 7f);
                type.GetField("includeAfterglow", Instance).SetValue(window, true);
                type.GetField("zoom", Instance).SetValue(window, 3f);
                type.GetField("scrollSeconds", Instance).SetValue(window, 5f);
                type.GetMethod("PickCue", Instance).Invoke(window, new object[] { tail.Node, tail.At, false });
                Assert.That((float)type.GetField("scrollSeconds", Instance).GetValue(window), Is.EqualTo(5f));
                Assert.That((float)type.GetField("zoom", Instance).GetValue(window), Is.EqualTo(3f));
                var patch = (JObject)type.GetField("patch", Instance).GetValue(window);
                Assert.That(((JObject)patch["nodes"]).Count, Is.Zero);
            });
        }

        [Test]
        public void RepeatedModelEventsKeepTheirOccurrenceAfterRebuildingThePreview()
        {
            WithWindow("c1008", 3, (type, window) =>
            {
                IReadOnlyList<(float At, JObject Node, float Duration, JObject[] Modulators)> ReadCues()
                {
                    var preview = type.GetField("preview", Instance).GetValue(window);
                    return (IReadOnlyList<(float At, JObject Node, float Duration, JObject[] Modulators)>)
                        preview.GetType().GetProperty("Cues", Instance).GetValue(preview);
                }
                // Replay the same authored animation from two graph nodes. Its model events have
                // identical payloads but different cue occurrences; no generated asset is modified.
                var source = (JObject)((JObject)type.GetField("source", Instance).GetValue(window)).DeepClone();
                ((JArray)source["source"]["entities"]).Add(new JObject { ["guid"] = 902, ["etty"] = "ANI", ["ani"] = "skill3" });
                ((JArray)source["source"]["connections"]).Add(new JObject
                {
                    ["from"] = (int)source["source"]["entrypoint"]["start"], ["to"] = 902, ["when"] = 1, ["delay"] = 500
                });
                var overrides = Type.GetType("AstralOath.Client.BattleScene.SkillTimelineOverrides, AstralOath.Client.BattleScene", true);
                var patch = overrides.GetMethod("NewPatch", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { source });
                type.GetField("source", Instance).SetValue(window, source);
                type.GetField("patch", Instance).SetValue(window, patch);
                type.GetField("rebuild", Instance).SetValue(window, true);
                type.GetMethod("UpdatePreview", Instance).Invoke(window, null);
                var duplicate = ReadCues().Where(cue => cue.Node["guid"] == null)
                    .GroupBy(cue => cue.Node.ToString()).First(group => group.Count() > 1).ToArray();
                var last = duplicate.Last();
                type.GetMethod("SelectCue", Instance).Invoke(window, new object[] { last.Node, last.At });
                type.GetField("flipped", Instance).SetValue(window, true);
                type.GetField("rebuild", Instance).SetValue(window, true);
                type.GetMethod("UpdatePreview", Instance).Invoke(window, null);
                Assert.That(type.GetField("preview", Instance).GetValue(window), Is.Not.Null,
                    (string)type.GetField("error", Instance).GetValue(window));
                var expected = ReadCues().Where(cue => cue.Node.ToString() == last.Node.ToString()).Last();
                Assert.That(type.GetField("selectedNode", Instance).GetValue(window), Is.SameAs(expected.Node));
            });
        }

        [TestCase("c1001", 1, false)]
        [TestCase("c1008", 3, true)]
        public void SelectionSurvivesScrubbingReplayingAndLooping(string hero, int slot, bool modelEvent)
        {
            WithWindow(hero, slot, (type, window) =>
            {
                object Read(string name) => type.GetField(name, Instance).GetValue(window);
                void Set(string name, object value) => type.GetField(name, Instance).SetValue(window, value);
                object Call(string name, params object[] args) => type.GetMethod(name, Instance).Invoke(window, args);
                var session = Read("preview");
                var sessionType = session.GetType();
                IReadOnlyList<(float At, JObject Node, float Duration, JObject[] Modulators)> Cues() =>
                    (IReadOnlyList<(float At, JObject Node, float Duration, JObject[] Modulators)>)
                        sessionType.GetProperty("Cues", Instance).GetValue(session);
                var selected = Cues().First(cue => modelEvent ? cue.Node["guid"] == null : (string)cue.Node["etty"] == "ANI");
                Call("PickCue", selected.Node, selected.At, false);
                string key = (string)Read("selectedCueKey");
                string beforePatch = ((JObject)Read("patch")).ToString();
                float duration = (float)type.GetProperty("TimelineDuration", Instance).GetValue(window);

                void AssertSelection(string step)
                {
                    Assert.That((string)Read("selectedCueKey"), Is.EqualTo(key), step);
                    Assert.That(Cues().Any(cue => ReferenceEquals(cue.Node, Read("selectedNode"))),
                        Is.True, step + ": highlighted selection must reference the current preview cues.");
                    Assert.DoesNotThrow(() => Call("FocusSelection"), step + ": F must still focus the selected clip.");
                    Assert.That(((JObject)Read("patch")).ToString(), Is.EqualTo(beforePatch), "Navigation must not edit timing.");
                }

                foreach (float fraction in new[] { .9f, .1f, .75f, 0f, .4f })
                {
                    Call("Seek", duration * fraction);
                    AssertSelection("scrub " + fraction);
                }
                Call("Seek", duration);
                Call("PlayPause");
                AssertSelection("replay from end");
                Call("Seek", duration);
                Set("loop", true);
                Set("playing", true);
                Set("lastUpdate", EditorApplication.timeSinceStartup - .1d);
                Call("UpdatePreview");
                AssertSelection("automatic loop");
                Assert.That((string)Read("error"), Is.Null);
            });
        }

        private static void WithWindow(string hero, int slot, Action<Type, EditorWindow> assertion)
        {
            var type = WindowType;
            var window = (EditorWindow)ScriptableObject.CreateInstance(type);
            try
            {
                var skills = (Array)type.GetField("skills", Instance).GetValue(window);
                var skill = skills.Cast<object>().Single(item =>
                    (string)item.GetType().GetField("Hero", Instance).GetValue(item) == hero &&
                    (int)item.GetType().GetField("Slot", Instance).GetValue(item) == slot);
                type.GetMethod("LoadSkill", Instance).Invoke(window, new[] { skill });
                type.GetMethod("UpdatePreview", Instance).Invoke(window, null);
                Assert.That(type.GetField("preview", Instance).GetValue(window), Is.Not.Null,
                    (string)type.GetField("error", Instance).GetValue(window));
                assertion(type, window);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void SelectingAnOffscreenCueRevealsItAndRebuildRetainsReadOnlyCueSelection()
        {
            var type = WindowType;
            var window = (EditorWindow)ScriptableObject.CreateInstance(type);
            try
            {
                object Read(string field) => type.GetField(field, Instance).GetValue(window);
                void Set(string field, object value) => type.GetField(field, Instance).SetValue(window, value);
                object Call(string name, params object[] args) => type.GetMethod(name, Instance).Invoke(window, args);
                var skills = (Array)Read("skills");
                var armin = skills.Cast<object>().Single(skill =>
                    (string)skill.GetType().GetField("Hero", Instance).GetValue(skill) == "c1008" &&
                    (int)skill.GetType().GetField("Slot", Instance).GetValue(skill) == 3);
                Call("LoadSkill", armin);
                Call("UpdatePreview");
                var session = Read("preview");
                Assert.That(session, Is.Not.Null, (string)Read("error"));
                var sessionType = session.GetType();
                float duration = (float)sessionType.GetProperty("Duration", Instance).GetValue(session);
                var cues = (IReadOnlyList<(float At, JObject Node, float Duration, JObject[] Modulators)>)
                    sessionType.GetProperty("Cues", Instance).GetValue(session);
                var late = cues.Last(cue => cue.At > duration / 4f);
                Set("zoom", 12f);
                Set("scrollSeconds", 0f);
                string beforePatch = ((JObject)Read("patch")).ToString();
                Call("SelectCue", late.Node, late.At);
                float wall = (float)sessionType.GetMethod("StageToWallTime", Instance).Invoke(session, new object[] { late.At });
                Assert.That((float)Read("scrollSeconds"), Is.LessThanOrEqualTo(wall));
                Assert.That((float)Read("scrollSeconds") + duration / (float)Read("zoom"),
                    Is.GreaterThanOrEqualTo(wall), "A selected offscreen cue must become visible.");
                Assert.That(((JObject)Read("patch")).ToString(), Is.EqualTo(beforePatch), "Selection never edits timing.");

                var modelCue = cues.First(cue => cue.Node["guid"] == null);
                Call("SelectCue", modelCue.Node, modelCue.At);
                Set("flipped", true);
                Set("rebuild", true);
                Call("UpdatePreview");
                session = Read("preview");
                Assert.That(session, Is.Not.Null, (string)Read("error"));
                cues = (IReadOnlyList<(float At, JObject Node, float Duration, JObject[] Modulators)>)
                    sessionType.GetProperty("Cues", Instance).GetValue(session);
                Assert.That(cues.Any(cue => ReferenceEquals(cue.Node, Read("selectedNode"))), Is.True,
                    "Rebuild must reselect the current model cue, not keep a stale JObject.");
            }
            finally { Object.DestroyImmediate(window); }
        }
    }
}
