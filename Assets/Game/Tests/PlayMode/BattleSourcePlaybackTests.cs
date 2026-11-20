#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class BattleSourcePlaybackTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const double ActionTimeout = 30d;
        private static readonly Type PresentationType = typeof(BattleHudPreview).Assembly
            .GetType("AstralOath.Client.BattleScene.BattleSkillPresentation", true);
        private static readonly MethodInfo Begin = PresentationType.GetMethod("Begin");
        private static readonly MethodInfo Tick = PresentationType.GetMethod("Tick");
        private static readonly FieldInfo Facing = typeof(BattlePreviewActor).GetField("faceLeft", PrivateInstance);
        private Scene scene;
        private IDisposable playback;
        private Transform units;
        private Camera camera;
        private GameObject background;
        private TextAsset[] graphs;
        private BattleSkillPresentationAsset[] assets;
        private Action<bool> setCinematicVisible;
        private bool cinematicVisible;
        private string captureDirectory;
        private RenderTexture captureTarget;
        private const int CaptureWidth = 1920, CaptureHeight = 1080;

        [Flags]
        private enum Phase { None = 0, Approach = 1, Impact = 2, Wide = 4, Blur = 8, Cutin = 16, Hatch = 32 }

        [UnityTest]
        public IEnumerator AllNineSourceGraphsPlayInBothDirectionsAndRestoreTheScene()
        {
            var roots = default(GameObject[]);
            yield return LoadBattle(value => roots = value);

            var allies = units.Find("Allies").GetComponentsInChildren<BattlePreviewActor>();
            var enemies = units.Find("Enemies").GetComponentsInChildren<BattlePreviewActor>();
            var target = enemies[0];
            var secondEnemy = enemies[1];
            var ras = allies.Single(actor => actor.name == "Ras [c1001]");
            var mercedes = allies.Single(actor => actor.name == "Mercedes [c1005]");
            var allEnemyTargets = new[] { target }.Concat(enemies.Where(actor => actor != target)).ToArray();
            var allAllyTargets = new[] { ras }.Concat(allies.Where(actor => actor != ras)).ToArray();
            var roster = new[]
            {
                (Name: "Ras [c1001]", Graph: "ras"),
                (Name: "Mercedes [c1005]", Graph: "mercedes"),
                (Name: "Angelica [c1062]", Graph: "angelica")
            };
            var slots = allies.Concat(enemies).Select(actor => actor.transform.parent).Distinct().ToArray();
            var positions = slots.Select(slot => slot.position).ToArray();
            var expectedIds = roster.SelectMany(hero => Enumerable.Range(1, 3)
                .Select(skill => $"c_{hero.Graph}_skill_{skill:D2}")).ToArray();
            Assert.That(expectedIds, Is.SubsetOf(graphs.Select(graph => JsonUtility.FromJson<GraphId>(graph.text).id).ToArray()));
            Assert.That(assets, Is.Not.Empty);

            foreach (bool faceLeft in new[] { false, true })
            {
                for (int index = 0; index < slots.Length; index++)
                {
                    var position = positions[index];
                    if (faceLeft) position.x = -position.x;
                    slots[index].position = position;
                }
                foreach (var actor in allies) SetFacing(actor, faceLeft);
                foreach (var actor in enemies) SetFacing(actor, !faceLeft);
                yield return null;

                foreach (var hero in roster)
                {
                    var actor = allies.Single(item => item.name == hero.Name);
                    for (int skill = 1; skill <= 3; skill++)
                    {
                        string id = $"c_{hero.Graph}_skill_{skill:D2}";
                        string sample = $"{hero.Graph}-s{skill}-{(faceLeft ? "left" : "right")}";
                        Phase focus = id switch
                        {
                            "c_ras_skill_01" or "c_angelica_skill_01" => Phase.Approach | Phase.Impact,
                            "c_ras_skill_02" => Phase.Approach | Phase.Impact | Phase.Blur,
                            "c_ras_skill_03" => Phase.Hatch,
                            "c_mercedes_skill_03" => Phase.Wide | Phase.Hatch | Phase.Cutin,
                            "c_angelica_skill_03" => Phase.Blur | Phase.Cutin,
                            _ => Phase.None
                        };
                        var recipients = id switch
                        {
                            "c_ras_skill_03" or "c_mercedes_skill_02" or "c_mercedes_skill_03" => allEnemyTargets,
                            "c_mercedes_skill_01" => new[] { target, secondEnemy },
                            "c_angelica_skill_02" => new[] { ras, mercedes },
                            "c_angelica_skill_03" => allAllyTargets,
                            _ => new[] { target }
                        };
                        var primary = recipients[0];
                        yield return Play(id, sample, actor, primary, recipients, focus, Phase.None);
                        if (focus != Phase.None)
                        {
                            Phase interruptAt = focus.HasFlag(Phase.Approach) ? Phase.Approach :
                                focus.HasFlag(Phase.Wide) ? Phase.Wide :
                                focus.HasFlag(Phase.Hatch) ? Phase.Hatch : Phase.Blur;
                            yield return Play(id, sample + "-interrupted", actor, primary, recipients, Phase.None, interruptAt);
                        }
                    }
                }
            }
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator AddedHeroesCanEnterBattleAndPlayAllSourceGraphs()
        {
            var roots = default(GameObject[]);
            yield return LoadBattle(value => roots = value);
            var host = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            var catalog = JsonUtility.FromJson<BattleCatalogData>(HostField<TextAsset>(host, "battleCatalog").text);
            var separateLegacyFixture = new HashSet<string>(StringComparer.Ordinal) { "c1001", "c1005", "c1062" };
            string[] added = catalog.heroes.Select(hero => hero.id)
                .Where(id => !separateLegacyFixture.Contains(id)).ToArray();
            Assert.That(added, Does.Contain("c1010"));
            var cutinsByGraph = graphs.Select(graph => JObject.Parse(graph.text))
                .ToDictionary(graph => (string)graph["id"], graph => graph["assets"]
                    .Where(asset => (string)asset["type"] == "cutin").Select(asset => (string)asset["source"]).ToArray());
            var expectedPresentations = catalog.skills.SelectMany(skill => new[] { skill.presentationId, skill.speedUpPresentationId,
                skill.burnPresentationId, skill.counterPresentationId, skill.alternatePresentationId })
                .Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal);
            Assert.That(cutinsByGraph.Keys, Is.EquivalentTo(expectedPresentations));
            string requested = Environment.GetEnvironmentVariable("ASTRAL_BATTLE_HERO_ID");
            if (!string.IsNullOrWhiteSpace(requested))
            {
                requested = requested.Trim();
                var ids = requested.Split(',').Select(id => id.Trim()).ToArray();
                Assert.That(ids, Is.SubsetOf(added));
                added = ids;
            }
            var selection = typeof(BattleHudPreview).GetField("allyHeroIds", PrivateInstance);
            var map = HostField<BindingMap>(host, "hud");
            typeof(BattleHudPreview).GetField("enemyHeroIds", PrivateInstance).SetValue(host, BattleScenePreviewTests.SlowEnemyIds());
            host.enabled = true;
            foreach (string id in added)
            {
                selection.SetValue(host, Enumerable.Repeat(id, 4).ToArray());
                host.StartBattle();
                Assert.That(host.SimulationSnapshot.Units.Where(unit => unit.Slot.Side == BattleSide.Ally)
                    .Select(unit => unit.DefinitionId), Is.All.EqualTo(id));
                var actor = host.SimulationSnapshot.GetUnit(host.SimulationSnapshot.CurrentActorId);
                Assert.That(actor.DefinitionId, Is.EqualTo(id));
                if (id == "c1047")
                {
                    var ultimate = catalog.skills.Single(row => row.heroId == id && row.slot == 3);
                    Assert.That(actor.Resource.Kind, Is.EqualTo(BattleResourceKind.FightingSpirit));
                    Assert.That(actor.Resource.Amount, Is.EqualTo(40));
                    Assert.That(ultimate.resourceCost, Is.EqualTo(80));
                    Assert.That(map.Require<GameButton>("skill3").interactable, Is.False);
                }
                var skill = catalog.skills.Single(row => row.heroId == id && row.slot == 2);
                Assert.That(map.Require<GameButton>("skill2").interactable, Is.EqualTo(skill.target != "passive"));
                var basic = catalog.skills.Single(row => row.heroId == id && row.slot == 1);
                if (id == "c1067")
                {
                    Assert.That(actor.GetCooldown("c1067.s3"), Is.GreaterThan(0));
                    Assert.That(map.Require<GameButton>("skill3").interactable, Is.False);
                }
                if (!string.IsNullOrEmpty(basic.speedUpPresentationId))
                {
                    var sprites = HostField<Sprite[]>(host, "battleSprites");
                    var image = (Image)map.Require<GameButton>("skill1").targetGraphic;
                    Assert.That(sprites.Any(sprite => sprite && sprite.name == Path.GetFileNameWithoutExtension(basic.speedUpIconAsset)),
                        Is.True, "The alternate graphic must be available before the first action.");
                    Assert.That(image.sprite.name, Is.EqualTo(Path.GetFileNameWithoutExtension(basic.iconAsset)),
                        "A fresh turn without a speed buff must still show the ordinary attack.");
                }
                if (captureTarget)
                {
                    Assert.That(map.Require<Image>("activePortrait").sprite.name, Is.EqualTo(id + "_su"));
                    foreach (var equipped in catalog.skills.Where(row => row.heroId == id))
                    {
                        var button = map.Require<GameButton>("skill" + equipped.slot);
                        Assert.That(((Image)button.targetGraphic).sprite.name, Is.EqualTo(Path.GetFileNameWithoutExtension(equipped.iconAsset)));
                    }
                    File.WriteAllBytes(Path.Combine(captureDirectory, id + "-formation.png"), RenderFrame(null, null, null).Composite);
                }
                Assert.That(host.TrySelectBattleTarget("enemy-1"), Is.True, id + " S1 must submit through the real battle controller");
                TestContext.WriteLine($"{id}: formation and authoritative S1 command passed.");
                yield return null;
#if UNITY_EDITOR_WIN
                if (id == "c1019")
                {
                    Assert.That(host.StartedSkillSounds, Is.GreaterThan(0), "Ravi's original effect event must play in battle.");
                    Assert.That(host.StartedSkillVoices, Is.GreaterThan(0), "Ravi's selected voice bank must play in battle.");
                }
#endif
            }
            host.enabled = false;
            yield return null;

            var allies = units.Find("Allies").GetComponentsInChildren<BattlePreviewActor>();
            var enemies = units.Find("Enemies").GetComponentsInChildren<BattlePreviewActor>();
            var front = allies[0];
            foreach (string id in added)
            {
                typeof(BattleHudPreview).GetMethod("ConfigureActorModel", PrivateInstance).Invoke(host, new object[] { front, id });
                yield return null; // Finish deferred disposal of the prior skeleton before taking a renderer baseline.
                var renderer = front.GetComponentInChildren<MeshRenderer>();
                Assert.That(renderer && renderer.enabled && renderer.GetComponent<MeshFilter>().sharedMesh.vertexCount > 0, Is.True, id);
                foreach (var skill in catalog.skills.Where(row => row.heroId == id))
                {
                    if (string.IsNullOrEmpty(skill.presentationId))
                    {
                        Assert.That(skill.target, Is.EqualTo("passive"), skill.id + " must have an active source graph");
                        continue;
                    }
                    var protection = catalog.effects.Where(effect => effect.skillId == skill.id && effect.phase == "after_ally_damaged").ToArray();
                    var recipients = skill.target switch
                    {
                        "all_allies" => allies,
                        "one_ally" or "one_other_ally" or "one_ally_including_dead" => new[] { allies[1] },
                        "self" => new[] { front },
                        "all_enemies" => enemies,
                        "two_enemies" => enemies.Take(2).ToArray(),
                        "passive" when protection.Any(effect => effect.recipient == "allies") => allies,
                        "passive" when protection.Length > 0 && protection.All(effect => effect.recipient == "self") => new[] { front },
                        "passive" when protection.Length > 0 => new[] { allies[protection.Any(effect => effect.condition == "back_row_immobilized") ? 3 : 1] },
                        "passive" when catalog.effects.Any(effect => effect.skillId == skill.id && effect.operation == "damage" &&
                            (effect.phase == "after_ally_attack" || effect.phase == "counter_basic")) => enemies,
                        "passive" => id is "c1007" or "c1008" or "c1020" or "c1023" or "c1028" ? enemies : new[] { enemies[0] },
                        _ => new[] { enemies[0] }
                    };
                    var sourceCutins = cutinsByGraph[skill.presentationId];
                    Phase required = sourceCutins.Length > 0 ? Phase.Cutin : Phase.None;
                    yield return Play(skill.presentationId, id + "-s" + skill.slot, front, recipients[0], recipients,
                        required | Phase.Impact, Phase.None, sourceCutins);
                    TestContext.WriteLine($"{id} S{skill.slot}: {skill.presentationId} passed source playback and reset.");
                    if (!string.IsNullOrEmpty(skill.counterPresentationId))
                        yield return Play(skill.counterPresentationId, id + "-counter", front, enemies[0], enemies, Phase.None, Phase.None);
                    if (!string.IsNullOrEmpty(skill.alternatePresentationId))
                    {
                        string sample = id + "-s" + skill.slot + "-" + skill.alternatePresentationStatus;
                        var alternateTargets = catalog.effects.Any(effect => effect.skillId == skill.id &&
                            effect.operation == "aoe_if_status" && effect.status == skill.alternatePresentationStatus) ? enemies : recipients;
                        yield return Play(skill.alternatePresentationId, sample, front, alternateTargets[0], alternateTargets,
                            Phase.Impact, Phase.None, cutinsByGraph[skill.alternatePresentationId]);
                        TestContext.WriteLine($"{sample}: {skill.alternatePresentationId} passed source playback and reset.");
                    }
                    if (!string.IsNullOrEmpty(skill.burnPresentationId))
                    {
                        yield return Play(skill.burnPresentationId, id + "-s" + skill.slot + "-burn",
                            front, recipients[0], recipients, Phase.Impact, Phase.None, cutinsByGraph[skill.burnPresentationId]);
                        TestContext.WriteLine($"{id} S{skill.slot} Burn: {skill.burnPresentationId} passed source playback and reset.");
                    }
                    if (!string.IsNullOrEmpty(skill.speedUpPresentationId) &&
                        !catalog.skills.Any(row => row.heroId == id && row.presentationId == skill.speedUpPresentationId))
                        yield return Play(skill.speedUpPresentationId, id + "-s" + skill.slot + "-speed",
                            front, recipients[0], recipients, Phase.None, Phase.None);
                }
            }
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator IdolTransformationReachesTheRealTurnHudAndRestoresAnOrdinarySnapshot()
        {
            var roots = default(GameObject[]);
            yield return LoadBattle(value => roots = value);
            var host = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            typeof(BattleHudPreview).GetField("allyHeroIds", PrivateInstance).SetValue(host, Enumerable.Repeat("c1067", 4).ToArray());
            typeof(BattleHudPreview).GetField("enemyHeroIds", PrivateInstance).SetValue(host, BattleScenePreviewTests.SlowEnemyIds());
            host.enabled = true;
            host.StartBattle();
            var opening = host.SimulationSnapshot;
            var simulation = typeof(BattleHudPreview).GetField("simulation", PrivateInstance).GetValue(host);
            var type = simulation.GetType();
            var tick = type.GetMethod("Tick");
            var canAct = type.GetProperty("CanAct", PrivateInstance);
            var map = HostField<BindingMap>(host, "hud");
            string transformed = null;
            for (int step = 0; step < 5000; step++)
            {
                var snapshot = host.SimulationSnapshot;
                Assert.That(snapshot.Outcome, Is.EqualTo(BattleOutcome.InProgress));
                if ((bool)canAct.GetValue(simulation))
                {
                    var current = snapshot.GetUnit(snapshot.CurrentActorId);
                    if (current.GetStatus(BattleStatusKind.Idol) != null)
                    {
                        Assert.That(current.InstanceId, Is.EqualTo(transformed));
                        string Icon(int slot) => ((Image)map.Require<GameButton>("skill" + slot).targetGraphic).sprite.name;
                        Assert.That(Icon(1), Is.EqualTo("sk_c1067_4"));
                        Assert.That(Icon(2), Is.EqualTo("sk_c1067_5"));
                        var views = (IDictionary)type.GetField("views", PrivateInstance).GetValue(simulation);
                        var unit = views[current.InstanceId];
                        var actor = (BattlePreviewActor)unit.GetType().GetProperty("Actor").GetValue(unit);
                        var body = typeof(BattlePreviewActor).GetField("skeleton", PrivateInstance);
                        Assert.That(((TextAsset)body.GetValue(actor)).name, Is.EqualTo("tamarinne_t.spine"));
                        if (captureTarget)
                            File.WriteAllBytes(Path.Combine(captureDirectory, "c1067-idol-turn.png"), RenderFrame(null, null, null).Composite);
                        // Reconciliation also runs on a restored or resynchronized authoritative snapshot.
                        unit.GetType().GetMethod("RefreshStatuses").Invoke(unit, new object[] { opening.GetUnit(current.InstanceId) });
                        Assert.That(((TextAsset)body.GetValue(actor)).name, Is.EqualTo("tamarinne.spine"));
                        yield break;
                    }

                    int slot = current.GetCooldown("c1067.s3") == 0 && transformed == null ? 3 :
                        current.GetCooldown("c1067.s2") == 0 ? 2 : 1;
                    map.Require<GameButton>("skill" + slot).onClick.Invoke();
                    string target = slot == 1 ? snapshot.Units.First(unit => unit.IsAlive && unit.Slot.Side == BattleSide.Enemy).InstanceId : current.InstanceId;
                    Assert.That(host.TrySelectBattleTarget(target), Is.True, $"Real S{slot} command on turn {snapshot.TurnNumber}");
                    if (slot == 3)
                    {
                        transformed = current.InstanceId;
                        Assert.That(host.SimulationSnapshot.GetUnit(transformed).GetStatus(BattleStatusKind.Idol), Is.Not.Null);
                    }
                }
                tick.Invoke(simulation, new object[] { .2f });
                if (step % 12 == 0) yield return null;
            }
            Assert.Fail("A normal S1/S2 rotation must unlock the opening S3 and yield an idol turn.");
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator BellonaExtraAttackAndVioletCounterPlayTheirAcceptedGraphs()
        {
            var roots = default(GameObject[]);
            yield return LoadBattle(value => roots = value);
            var host = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            typeof(BattleHudPreview).GetField("allyHeroIds", PrivateInstance).SetValue(host, new[] { "c1071", "c1067", "c1067", "c1067" });
            host.enabled = true;
            var map = HostField<BindingMap>(host, "hud");
            foreach (var (origin, graph, enemies, file) in new[]
            {
                (BattleActionOrigin.Counter, "c_violetto_skill_01", Enumerable.Repeat("c1074", 4).ToArray(), "c1074-real-counter.png"),
                (BattleActionOrigin.ExtraAttack, "c_bellona_skill_03", BattleScenePreviewTests.SlowEnemyIds(), "c1071-real-extra.png")
            })
            {
                typeof(BattleHudPreview).GetField("enemyHeroIds", PrivateInstance).SetValue(host, enemies);
                host.StartBattle();
                var simulation = typeof(BattleHudPreview).GetField("simulation", PrivateInstance).GetValue(host);
                var type = simulation.GetType();
                var tick = type.GetMethod("Tick");
                var queue = (Queue<BattleFlowEvent>)type.GetField("playback", PrivateInstance).GetValue(simulation);
                var canAct = type.GetProperty("CanAct", PrivateInstance);
                BattleFlowEvent accepted = null;
                int fanTurns = 0;
                bool rendered = false;
                for (int step = 0; step < 5000 && !rendered; step++)
                {
                    var snapshot = host.SimulationSnapshot;
                    if (snapshot.Outcome != BattleOutcome.InProgress && queue.Count == 0 && accepted == null) break;
                    if ((bool)canAct.GetValue(simulation))
                    {
                        var current = snapshot.GetUnit(snapshot.CurrentActorId);
                        int slot = 1;
                        if (current.DefinitionId == "c1071")
                        {
                            slot = fanTurns == 0 ? 3 : fanTurns == 1 ? 2 : 1;
                            TestContext.WriteLine($"{origin} S{slot} Bellona turn {fanTurns + 1}, focus {current.Resource.Amount}");
                            fanTurns++;
                        }
                        else if (current.GetCooldown("c1067.s2") == 0) slot = 2;
                        string target = current.DefinitionId == "c1067" && slot == 2 ? current.InstanceId :
                            snapshot.Units.Where(unit => unit.IsAlive && unit.Slot.Side == BattleSide.Enemy)
                                .OrderByDescending(unit => unit.CurrentHealth).First().InstanceId;
                        map.Require<GameButton>("skill" + slot).onClick.Invoke();
                        Assert.That(host.TrySelectBattleTarget(target), Is.True);
                    }
                    accepted ??= queue.FirstOrDefault(item => item.Kind == BattleFlowEventKind.Action && item.Origin == origin && item.PresentationId == graph);
                    tick.Invoke(simulation, new object[] { .1f });
                    if (accepted != null && !queue.Contains(accepted))
                    {
                        var stage = type.GetField("skillPresentation", PrivateInstance).GetValue(simulation);
                        Assert.That(PresentationType.GetField("currentPresentationId", PrivateInstance).GetValue(stage), Is.EqualTo(graph));
                        if ((float)PresentationType.GetProperty("StageTime").GetValue(stage) >= .3f)
                        {
                            if (captureTarget) File.WriteAllBytes(Path.Combine(captureDirectory, file), RenderFrame(null, null, null).Composite);
                            TestContext.WriteLine(accepted.Origin + " " + accepted.SkillId + " " + accepted.PresentationId);
                            rendered = true;
                        }
                    }
                    if (step % 12 == 0) yield return null;
                }
                Assert.That(rendered, Is.True, $"{origin} must reach the normal renderer, Bellona acted {fanTurns} times.");
            }
        }

        [UnityTest]
        public IEnumerator LunaWideSkyReachesBothEdgesForEitherTeam()
        {
            var roots = default(GameObject[]);
            yield return LoadBattle(value => roots = value);
            var host = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            float previousAspect = camera.aspect;
            foreach (bool fromRight in new[] { false, true })
            foreach (int width in new[] { 960, 1260 })
            {
                const int height = 540;
                var target = RenderTexture.GetTemporary(width, height, 24);
                var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                try
                {
                    camera.targetTexture = target;
                    camera.aspect = (float)width / height;
                    var caster = units.Find(fromRight ? "Enemies" : "Allies").GetComponentsInChildren<BattlePreviewActor>()[0];
                    var recipients = units.Find(fromRight ? "Allies" : "Enemies").GetComponentsInChildren<BattlePreviewActor>();
                    typeof(BattleHudPreview).GetMethod("ConfigureActorModel", PrivateInstance).Invoke(host, new object[] { caster, "c1066" });
                    playback = (IDisposable)Activator.CreateInstance(PresentationType,
                        new object[] { units, graphs, assets, camera, setCinematicVisible, background });
                    Begin.Invoke(playback, new object[] { "c_lunabianca_skill_03", caster, recipients[0], new[] { recipients[0] } });
                    for (int i = 0; i < 60; i++) Tick.Invoke(playback, new object[] { 1f / 30f });
                    camera.Render();
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    pixels.Apply();
                    if (!string.IsNullOrEmpty(captureDirectory))
                        File.WriteAllBytes(Path.Combine(captureDirectory, $"c1066-sky-{(fromRight ? "right" : "left")}-{width}.png"), pixels.EncodeToPNG());
                    foreach (float x in new[] { .03f, .97f })
                    foreach (float y in new[] { .35f, .55f, .72f })
                    {
                        Color color = pixels.GetPixelBilinear(x, y);
                        Assert.That(color.b, Is.GreaterThan(.24f), $"The cloud artwork must reach ({x},{y}) at {width} × {height}, caster right={fromRight}.");
                    }
                }
                finally
                {
                    playback?.Dispose(); playback = null;
                    camera.targetTexture = previousTarget; camera.aspect = previousAspect;
                    RenderTexture.active = previousActive;
                    UnityEngine.Object.Destroy(pixels);
                    RenderTexture.ReleaseTemporary(target);
                }
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator KiseS3KeepsScreenEffectsOffTheTargetSlotsAndRestoresTheMap()
        {
            yield return LoadBattle(_ => { });
            var caster = units.Find("Enemies").GetComponentsInChildren<BattlePreviewActor>()
                .Single(actor => actor.name.EndsWith("[c1006]", StringComparison.Ordinal));
            var targets = units.Find("Allies").GetComponentsInChildren<BattlePreviewActor>();
            var shadow = caster.transform.parent.GetComponentInChildren<SpriteRenderer>().transform;
            var baseline = new SceneBaseline(camera, background, units, caster.transform, shadow);
            playback = (IDisposable)Activator.CreateInstance(PresentationType,
                new object[] { units, graphs, assets, camera, setCinematicVisible, background });
            var effects = units.Find("Battle Skill Effects");
            var stage = PresentationType.GetField("cinematic", PrivateInstance).GetValue(playback);
            var shakeOffset = stage.GetType().GetField("shakeOffset", PrivateInstance);
            var bodyProperty = typeof(BattlePreviewActor).GetProperty("PresentationRenderer", PrivateInstance);

            foreach (var target in new[] { targets[0], targets[targets.Length - 1] })
            {
                var timing = ((float Impact, float Duration))Begin.Invoke(playback,
                    new object[] { "c_kise_skill_03", caster, target, new[] { target } });
                float elapsed = 0f;
                bool spaceObserved = false;
                bool secondSceneObserved = false;
                bool mapHidden = false;
                bool mapRestored = false;
                int steps = 0;
                while (elapsed < timing.Duration + .05f && steps++ < 1200)
                {
                    Tick.Invoke(playback, new object[] { .02f });
                    elapsed = (float)PresentationType.GetProperty("StageTime").GetValue(playback);
                    mapHidden |= !background.activeSelf;
                    mapRestored |= mapHidden && background.activeSelf;
                    var space = effects.Find("Skill CFX kise_m_skill_03_space.cfx");
                    if (space && elapsed < 2f)
                    {
                        AssertScreenAnchor(space, target.name);
                        spaceObserved = true;
                    }
                    var secondScene = effects.Cast<Transform>().FirstOrDefault(group => group.Find("kise_m_skill_03_bg") &&
                        group.name == "Skill CFX kise_m_skill_03_bg_scene2.cfx");
                    if (secondScene && !background.activeSelf)
                    {
                        AssertScreenAnchor(secondScene, target.name);
                        if (!secondSceneObserved)
                        {
                            int backgroundOrder = secondScene.Find("kise_m_skill_03_bg").GetComponent<Renderer>().sortingOrder;
                            int foregroundOrder = effects.Cast<Transform>()
                                .Single(group => group.Find("kise_m_skill_03_scene2")).GetComponentInChildren<Renderer>().sortingOrder;
                            foreach (var participant in new[] { caster, target })
                            {
                                int bodyOrder = ((Renderer)bodyProperty.GetValue(participant)).sortingOrder;
                                Assert.That(backgroundOrder, Is.LessThan(bodyOrder));
                                Assert.That(foregroundOrder, Is.GreaterThan(bodyOrder));
                            }
                        }
                        secondSceneObserved = true;
                    }
                    yield return null;
                }
                Assert.That(spaceObserved && secondSceneObserved && mapHidden && mapRestored, Is.True, target.name);
                Assert.That(elapsed, Is.GreaterThanOrEqualTo(timing.Duration), target.name);
                baseline.AssertRestored(target.name);
                Assert.That(effects.childCount, Is.Zero, target.name);
            }

            void AssertScreenAnchor(Transform effect, string targetName)
            {
                Vector3 viewport = camera.WorldToViewportPoint(effect.position + (Vector3)shakeOffset.GetValue(stage));
                float authoredY = .5f + 216f * .01f / (2f * baseline.CameraSize);
                Assert.That(viewport.x, Is.EqualTo(.5f).Within(.001f), targetName);
                Assert.That(viewport.y, Is.EqualTo(authoredY).Within(.001f), targetName);
            }
        }

        [UnityTest]
        public IEnumerator CaptureKiseAndCeciliaS3SourceFrames()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASTRAL_BATTLE_CAPTURE_DIR")))
                Assert.Ignore("Set ASTRAL_BATTLE_CAPTURE_DIR to record source composition frames.");

            yield return LoadBattle(_ => { });
            playback = (IDisposable)Activator.CreateInstance(PresentationType,
                new object[] { units, graphs, assets, camera, setCinematicVisible, background });
            var effects = units.Find("Battle Skill Effects");
            var stage = PresentationType.GetField("cinematic", PrivateInstance).GetValue(playback);
            var allies = units.Find("Allies").GetComponentsInChildren<BattlePreviewActor>();
            var enemies = units.Find("Enemies").GetComponentsInChildren<BattlePreviewActor>();
            var kise = enemies.Single(actor => actor.name.EndsWith("[c1006]", StringComparison.Ordinal));
            var cecilia = allies.Single(actor => actor.name.EndsWith("[c1002]", StringComparison.Ordinal));

            var kiseTiming = ((float Impact, float Duration))Begin.Invoke(playback,
                new object[] { "c_kise_skill_03", kise, allies[0], new[] { allies[0] } });
            yield return CaptureUntil(kiseTiming.Duration, new[]
            {
                ("kise-s3-cinematic-space", "kise_m_skill_03_space", 0.65f),
                ("kise-s3-cinematic-scene1", "kise_m_skill_03_scene1", 0.25f),
                ("kise-s3-cinematic-scene2", "kise_m_skill_03_scene2", 0.56f)
            });
            WriteFrame("kise-s3-cinematic-restored");

            var ceciliaTiming = ((float Impact, float Duration))Begin.Invoke(playback,
                new object[] { "c_cecilia_skill_new_03", cecilia, enemies[0], enemies });
            yield return CaptureUntil(ceciliaTiming.Duration, new[]
            {
                ("cecilia-s3-lance-glow", "cecilia_skill_03_lanceaura2", 1.5333f)
            });

            IEnumerator CaptureUntil(float duration, (string File, string Primitive, float LocalTime)[] frames)
            {
                var starts = new Dictionary<string, float>(StringComparer.Ordinal);
                var captures = new HashSet<string>(StringComparer.Ordinal);
                float sourceTime = 0f;
                int steps = 0;
                while (sourceTime < duration + .05f && steps++ < 1800)
                {
                    Tick.Invoke(playback, new object[] { 1f / 120f });
                    sourceTime = (float)PresentationType.GetProperty("StageTime").GetValue(playback);
                    foreach (var frame in frames)
                    {
                        if (captures.Contains(frame.File)) continue;
                        if (!effects.Cast<Transform>().Any(group => group.Find(frame.Primitive))) continue;
                        if (!starts.TryGetValue(frame.Primitive, out float start))
                            starts.Add(frame.Primitive, start = sourceTime);
                        if (sourceTime - start < frame.LocalTime) continue;
                        WriteFrame(frame.File);
                        captures.Add(frame.File);
                    }
                    yield return null;
                }
                Assert.That(captures, Is.EquivalentTo(frames.Select(frame => frame.File)));
                Assert.That(sourceTime, Is.GreaterThanOrEqualTo(duration));
            }

            void WriteFrame(string name)
            {
                var frame = RenderFrame(stage, null, null);
                File.WriteAllBytes(Path.Combine(captureDirectory, name + ".png"), frame.Composite);
            }
        }

        private IEnumerator LoadBattle(Action<GameObject[]> loaded)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("The source playback sweep requires a graphics device.");

            scene = BattleScenePreviewTests.LoadPlayerOpeningScene(useDefaultRoster: true);
            yield return null;
            yield return null;

            var roots = scene.GetRootGameObjects();
            var host = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            host.AutoStartBattle = false;
            host.enabled = false; // Isolate direct source playback from the automatically started match.
            yield return null;
            graphs = HostField<TextAsset[]>(host, "battleGraphs");
            assets = HostField<BattleSkillPresentationAsset[]>(host, "skillAssets");
            background = HostField<GameObject>(host, "stageBackground");
            camera = host.GetComponent<Canvas>().worldCamera;
            units = roots.Single(root => root.name == "Units").transform;
            var map = HostField<BindingMap>(host, "hud");
            var hudObjects = new[] { map.transform.Find("Skills").gameObject, map.Require<GameButton>("soulBurn").gameObject };
            var hudVisibility = hudObjects.Select(item => item.activeSelf).ToArray();
            setCinematicVisible = visible =>
            {
                cinematicVisible = visible;
                for (int index = 0; index < hudObjects.Length; index++)
                    hudObjects[index].SetActive(!visible && hudVisibility[index]);
            };

            string output = Environment.GetEnvironmentVariable("ASTRAL_BATTLE_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(output))
            {
                captureDirectory = Path.Combine(output, "source-playback");
                Directory.CreateDirectory(captureDirectory);
                // Size the actual stage before playing any cue. Changing aspect only for readback
                // captures 4:3 DARK, CUTIN and map coverage inside an unrelated 16:9 projection.
                captureTarget = new RenderTexture(CaptureWidth, CaptureHeight, 24);
                Assert.That(captureTarget.Create(), Is.True);
                camera.targetTexture = captureTarget;
                var canvas = host.GetComponent<Canvas>();
                var scaler = host.GetComponent<CanvasScaler>();
                Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(CaptureWidth, CaptureHeight)));
                // The Game view can be smaller than the offscreen target. At the HUD reference
                // resolution one design unit is one pixel throughout layout and readback.
                scaler.enabled = false;
                canvas.scaleFactor = 1f;
                Canvas.ForceUpdateCanvases();
                var viewport = camera.GetComponent<BattlePreviewViewport>();
                typeof(BattlePreviewViewport).GetMethod("ApplyScreen", PrivateInstance).Invoke(viewport,
                    new object[] { new Vector2Int(CaptureWidth, CaptureHeight), new Rect(0, 0, CaptureWidth, CaptureHeight) });
                yield return null;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                var active = (RectTransform)map.transform.Find("Active Unit");
                var corners = new Vector3[4];
                active.GetWorldCorners(corners);
                float hudWidth = camera.WorldToScreenPoint(corners[2]).x - camera.WorldToScreenPoint(corners[0]).x;
                Assert.That(hudWidth, Is.EqualTo(CaptureWidth).Within(2f),
                    $"The HUD design width must fill one capture frame. Canvas {canvas.pixelRect}, group {active.rect} / {active.localScale}, camera {camera.pixelRect}.");
            }

            loaded(roots);
        }

        private IEnumerator Play(string id, string sample, BattlePreviewActor actor, BattlePreviewActor target,
            BattlePreviewActor[] recipients, Phase capturePhases, Phase interruptAt, string[] expectedCutins = null)
        {
            if (expectedCutins?.Length > 0) capturePhases |= Phase.Cutin;
            playback ??= (IDisposable)Activator.CreateInstance(PresentationType,
                new object[] { units, graphs, assets, camera, setCinematicVisible, background });
            var effects = units.Find("Battle Skill Effects");
            var stage = PresentationType.GetField("cinematic", PrivateInstance).GetValue(playback);
            var requestedSize = stage.GetType().GetField("requestedSize", PrivateInstance);
            var cutinRemaining = stage.GetType().GetProperty("CutinRemainingSeconds");
            var shadow = actor.transform.parent.GetComponentInChildren<SpriteRenderer>().transform;
            var baseline = new SceneBaseline(camera, background, units, actor.transform, shadow);
            var visualMeshes = new HashSet<Mesh>();
            var visualTargets = new HashSet<RenderTexture>();
            var frameCaptures = new List<string>();
            var bodyCaptures = new List<string>();
            var timing = ((float Impact, float Duration))Begin.Invoke(playback, new object[] { id, actor, target, recipients });
            var bodyDocument = typeof(BattlePreviewActor).GetField("skeleton", PrivateInstance);
            if (id.StartsWith("c_tamarinne_", StringComparison.Ordinal))
            {
                bool idol = id is "c_tamarinne_skill_01_1" or "c_tamarinne_skill_02_2";
                Assert.That(((TextAsset)bodyDocument.GetValue(actor)).name, Is.EqualTo(idol ? "tamarinne_t.spine" : "tamarinne.spine"),
                    id + " must start with the model that owns its skill animation.");
            }
            float fullDuration = (float)PresentationType.GetProperty("FullDuration", PrivateInstance).GetValue(playback);
            Assert.That(float.IsFinite(timing.Duration) && timing.Duration > 0f && timing.Duration < ActionTimeout, Is.True, sample);
            Assert.That(timing.Impact, Is.InRange(0f, timing.Duration), sample);
            if (id == "c_aming_skill_03")
            {
                Assert.That(timing.Duration, Is.EqualTo(6.0666f).Within(.015f), "The real action ends at the source jump_back.");
                Assert.That(fullDuration, Is.EqualTo(11.552f).Within(.015f), "Pellet drift remains available without blocking the next action.");
            }
            bool checkTargetsAtHits = id is "c_ras_skill_03" or "c_mercedes_skill_03" or "c_kise_skill_03";
            if (id == "c_aming_skill_02")
            {
                var pillarHits = ((IEnumerable<float>)PresentationType.GetProperty("HitTimes").GetValue(playback)).ToArray();
                Assert.That(pillarHits.Length, Is.EqualTo(7));
                Assert.That(pillarHits[0], Is.EqualTo(2.021f).Within(.001f));
                Assert.That(pillarHits[6], Is.EqualTo(3.143f).Within(.001f));
            }
            float[] checkedHits = checkTargetsAtHits || !string.IsNullOrEmpty(captureDirectory)
                ? ((IEnumerable<float>)PresentationType.GetProperty("HitTimes").GetValue(playback)).ToArray()
                : Array.Empty<float>();
            if (id == "c_mercedes_skill_03")
                Assert.That(checkedHits, Has.Length.EqualTo(8), "Mercedes keeps all eight source hit cues.");
            // The source LAYOUT runs for 200 ms before skill3, whose cutin event is at 1300 ms.
            const float mercedesSourceCutin = 1.5f;
            bool checkMercedesCutin = id == "c_mercedes_skill_03";
            float mercedesCutinAt = -1f;
            int nextCheckedHit = 0;
            int sourceReactionHits = 0;
            var referenceTimes = captureTarget && id == "c_strage_skill_03"
                ? new[] { .8f, 1.8f, 4.8f, 5.8f } : Array.Empty<float>();
            if (captureTarget && expectedCutins?.Length > 0)
            {
                // Non-damaging scenery, target lifts and the setup for a portrait can fall between all HITs.
                var phaseTimes = Enumerable.Range(1, Mathf.FloorToInt(timing.Duration * 2f)).Select(index => index * .5f);
                referenceTimes = referenceTimes.Concat(phaseTimes).Distinct().OrderBy(value => value).ToArray();
            }
            int referenceIndex = 0;
            var hatchReferences = captureTarget && id == "c_sez_gaien_skill_03" ? new HashSet<int> { 17, 18, 52 } : null;
            var layers = hatchReferences == null ? null : (IEnumerable)stage.GetType().GetField("layers", PrivateInstance).GetValue(stage);
            var cameraTrace = !string.IsNullOrEmpty(captureDirectory)
                ? new List<string> { "source_seconds,camera_x,camera_y,orthographic_size,actor_x,actor_y" } : null;
            double deadline = Time.realtimeSinceStartupAsDouble + ActionTimeout;
            float elapsed = 0f;
            float currentCutinLength = 0f;
            Transform currentCutin = null;
            var seenCutins = new List<string>();
            var capturedCutins = new HashSet<Transform>();
            Phase observed = Phase.None;
            do
            {
                Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), sample + " exceeded the real-time deadline at source " + elapsed + ", cut-in remaining " + cutinRemaining.GetValue(stage));
                Assert.That(IsFinite(camera.transform.position) && float.IsFinite(camera.orthographicSize) &&
                    camera.orthographicSize > 0f, Is.True, sample + " produced an invalid camera pose");
                Assert.That(IsFinite(actor.transform.position) && IsFinite(shadow.position), Is.True, sample);

                if (cameraTrace != null)
                    cameraTrace.Add(FormattableString.Invariant($"{elapsed:F6},{camera.transform.position.x:F6},{camera.transform.position.y:F6},{camera.orthographicSize:F6},{actor.transform.position.x:F6},{actor.transform.position.y:F6}"));
                var blur = camera.transform.Find("Battle post-effect camera");
                var postCamera = blur && blur.gameObject.activeInHierarchy ? blur.GetComponent<Camera>() : null;
                if (postCamera && postCamera.enabled && postCamera.targetTexture)
                {
                    visualTargets.Add(postCamera.targetTexture);
                    Observe(Phase.Blur);
                }
                var activeCutin = camera.transform.Cast<Transform>().FirstOrDefault(child => child.gameObject.activeInHierarchy &&
                    child.name.StartsWith("CUTIN ", StringComparison.Ordinal));
                if (activeCutin)
                {
                    if (activeCutin != currentCutin)
                    {
                        currentCutin = activeCutin;
                        currentCutinLength = (float)cutinRemaining.GetValue(stage);
                        seenCutins.Add(activeCutin.name.Substring("CUTIN ".Length));
                    }
                    if (checkMercedesCutin)
                    {
                        if (mercedesCutinAt < 0f)
                        {
                            mercedesCutinAt = elapsed;
                            Assert.That(elapsed, Is.EqualTo(mercedesSourceCutin).Within(.001f));
                            const string mercedesPortrait = "cut/fhd/sk_c1005_3.webp";
                            Assert.That(activeCutin.name, Is.EqualTo("CUTIN " + mercedesPortrait));
                            var portrait = assets.Single(asset => asset.source == mercedesPortrait);
                            Assert.That(activeCutin.GetComponent<MeshRenderer>().sharedMaterial.mainTexture, Is.SameAs(portrait.pages[0]));
                            Assert.That((float)cutinRemaining.GetValue(stage), Is.EqualTo(1.35f).Within(.02f));
                        }
                        Assert.That(elapsed, Is.EqualTo(mercedesCutinAt).Within(.001f), "Cut-in pauses the source animation clock.");
                    }
                    Observe(Phase.Cutin);
                    if (captureTarget && !capturedCutins.Contains(activeCutin) && (capturePhases & Phase.Cutin) != 0)
                    {
                        float remaining = (float)cutinRemaining.GetValue(stage);
                        if (remaining > 0f && remaining <= currentCutinLength * .5f)
                        {
                            string suffix = seenCutins.Count == 1 ? "-cutin-middle.png" : $"-cutin-{seenCutins.Count}-middle.png";
                            frameCaptures.Add(Path.Combine(captureDirectory, sample + suffix));
                            capturedCutins.Add(activeCutin);
                        }
                    }
                }
                var hatch = camera.transform.Find("BGHATCH");
                if (hatch && hatch.gameObject.activeInHierarchy &&
                    hatch.GetComponent<MeshFilter>().sharedMesh.colors[0].a >= .9f)
                    Observe(Phase.Hatch);
                if (!activeCutin && hatchReferences?.Count > 0)
                    foreach (var layer in layers)
                    {
                        var type = layer.GetType();
                        var node = (JObject)type.GetField("Node").GetValue(layer);
                        int guid = (int?)node?["guid"] ?? 0;
                        if ((string)node?["etty"] != "BGHATCH" || !hatchReferences.Contains(guid)) continue;
                        float start = (float)type.GetField("Start").GetValue(layer);
                        float duration = (float)type.GetField("Duration").GetValue(layer);
                        if (elapsed < start + duration * .25f) continue;
                        frameCaptures.Add(Path.Combine(captureDirectory, sample + "-hatch-" + guid + ".png"));
                        hatchReferences.Remove(guid);
                    }
                if ((float)requestedSize.GetValue(stage) > baseline.CameraSize + 0.1f)
                    Observe(Phase.Wide);
                if (elapsed < timing.Impact && Mathf.Abs(actor.transform.localPosition.x - baseline.ActorHome.x) > 0.25f &&
                    Mathf.Abs(actor.transform.position.x - target.transform.position.x) < Mathf.Abs(baseline.ActorWorldX - target.transform.position.x))
                    Observe(Phase.Approach);
                if (elapsed >= timing.Impact)
                    Observe(Phase.Impact);

                if (referenceIndex < referenceTimes.Length && elapsed + .00001f >= referenceTimes[referenceIndex])
                {
                    string frameName = FormattableString.Invariant($"{sample}-source-{referenceTimes[referenceIndex]:0.0}.png");
                    frameCaptures.Add(Path.Combine(captureDirectory, frameName));
                    referenceIndex++;
                }

                while (nextCheckedHit < checkedHits.Length && elapsed + .00001f >= checkedHits[nextCheckedHit])
                {
                    if (checkTargetsAtHits)
                    foreach (var recipient in recipients)
                    {
                        var body = (Renderer)typeof(BattlePreviewActor).GetProperty("PresentationRenderer", PrivateInstance).GetValue(recipient);
                        Assert.That(body && body.enabled && body.gameObject.activeInHierarchy, Is.True,
                            sample + " hit " + nextCheckedHit + " at " + elapsed + " must display accepted target " + recipient.name);
                    }
                    if (id == "c_kise_skill_03" && (bool)PresentationType.GetMethod("OwnsNodeAnimationPose", PrivateInstance)
                        .Invoke(playback, new object[] { target }))
                    {
                        var player = (AstralOath.Client.Skeletons.ISkeletonPlayer)typeof(BattlePreviewActor)
                            .GetField("player", PrivateInstance).GetValue(target);
                        Assert.That(player.CurrentAnimation, Is.EqualTo("flying_damage"));
                        Assert.That(player.Duration, Is.Zero);
                        sourceReactionHits++;
                    }
                    if (!string.IsNullOrEmpty(captureDirectory))
                    {
                        frameCaptures.Add(Path.Combine(captureDirectory, sample + "-hit-" + nextCheckedHit + ".png"));
                        if (nextCheckedHit == 0 || nextCheckedHit == checkedHits.Length - 1)
                            bodyCaptures.Add(Path.Combine(captureDirectory, sample + "-hit-" + nextCheckedHit + "-actors.png"));
                    }
                    nextCheckedHit++;
                }
                if (frameCaptures.Count > 0)
                {
                    bool blurred = postCamera && postCamera.enabled && postCamera.targetTexture;
                    var frame = RenderFrame(stage, blurred ? postCamera : null, visualTargets);
                    Assert.That(frame.Composite.Length, Is.GreaterThan(8), sample + " did not produce a PNG");
                    foreach (string path in frameCaptures)
                    {
                        File.WriteAllBytes(path, frame.Composite);
                        if (frame.World != null)
                            File.WriteAllBytes(Path.ChangeExtension(path, null) + "-world.png", frame.World);
                    }
                    frameCaptures.Clear();
                    if (bodyCaptures.Count > 0)
                    {
                        // Diagnostic companion: isolate actors from bright foreground CFX without
                        // changing the normal capture, source blend mode, or production sorting.
                        var renderers = effects.GetComponentsInChildren<Renderer>(true);
                        var enabled = renderers.Select(renderer => renderer.enabled).ToArray();
                        try
                        {
                            foreach (var renderer in renderers) renderer.enabled = false;
                            var bodies = RenderFrame(stage, blurred ? postCamera : null, visualTargets);
                            foreach (string path in bodyCaptures) File.WriteAllBytes(path, bodies.World ?? bodies.Composite);
                        }
                        finally
                        {
                            for (int index = 0; index < renderers.Length; index++) renderers[index].enabled = enabled[index];
                        }
                        bodyCaptures.Clear();
                    }
                }
                TrackMeshes(camera.transform, visualMeshes);
                TrackMeshes(effects, visualMeshes);
                if (interruptAt != Phase.None && (observed & interruptAt) != 0)
                {
                    playback.Dispose();
                    playback = null;
                    break;
                }

                yield return null;
                // PNG readback can stall a test frame long enough to skip a short portrait.
                // Advance capture runs on a 30 fps sample clock independent of disk/GPU latency.
                float delta = captureTarget ? 1f / 30f : Time.unscaledDeltaTime;
                float wallCutin = (float)cutinRemaining.GetValue(stage);
                if (wallCutin > 0f) delta = Mathf.Min(delta, wallCutin);
                else if (checkMercedesCutin && elapsed < mercedesSourceCutin)
                    delta = Mathf.Min(delta, mercedesSourceCutin - elapsed);
                else if (nextCheckedHit < checkedHits.Length && elapsed < checkedHits[nextCheckedHit])
                    delta = Mathf.Min(delta, checkedHits[nextCheckedHit] - elapsed);
                if (wallCutin <= 0f && referenceIndex < referenceTimes.Length && elapsed < referenceTimes[referenceIndex])
                    delta = Mathf.Min(delta, referenceTimes[referenceIndex] - elapsed);
                Tick.Invoke(playback, new object[] { delta });
                elapsed = (float)PresentationType.GetProperty("StageTime").GetValue(playback);
            }
            while (elapsed < fullDuration + 0.05f);

            if (cameraTrace != null) File.WriteAllLines(Path.Combine(captureDirectory, sample + "-camera.csv"), cameraTrace);
            if (id == "c_kise_skill_03")
                Assert.That(sourceReactionHits, Is.GreaterThan(0), "At least one Kise hit must use the actual source reaction.");
            if (interruptAt == Phase.None) Assert.That(nextCheckedHit, Is.EqualTo(checkedHits.Length), sample + " must inspect every source hit.");
            Assert.That((observed & capturePhases) == capturePhases, Is.True,
                $"{sample} observed {observed}; expected {capturePhases}.");
            if (hatchReferences != null) Assert.That(hatchReferences, Is.Empty);
            if (expectedCutins != null)
            {
                Assert.That(seenCutins, Is.EquivalentTo(expectedCutins), sample + " must play every source portrait.");
                if (captureTarget) Assert.That(capturedCutins.Count, Is.EqualTo(seenCutins.Count), sample + " must capture each portrait midpoint.");
            }
            Assert.That(interruptAt == Phase.None || playback == null, Is.True, sample + " never reached the interruption phase");
            yield return null;
            yield return null;
            baseline.AssertRestored(sample);
            if (id == "c_tamarinne_skill_03")
                Assert.That(((TextAsset)bodyDocument.GetValue(actor)).name, Is.EqualTo("tamarinne_t.spine"),
                    "The source MODEL_BODY must leave the transformed body visible after the portrait.");
            if (captureTarget && interruptAt == Phase.None && (sample.EndsWith("-s3", StringComparison.Ordinal) || expectedCutins?.Length > 0))
                File.WriteAllBytes(Path.Combine(captureDirectory, sample + "-restored.png"), RenderFrame(stage, null, visualTargets).Composite);
            Assert.That(cinematicVisible, Is.False, sample + " left the gameplay HUD hidden");
            Assert.That(!effects || effects.childCount == 0, Is.True, sample + " left effect objects in the scene");
            Assert.That(visualMeshes.All(mesh => !mesh), Is.True, sample + " leaked a transient mesh");
            Assert.That(visualTargets.All(texture => !texture), Is.True, sample + " leaked a post-effect render target");

            void Observe(Phase phase)
            {
                if ((observed & phase) != 0)
                    return;
                observed |= phase;
                if ((capturePhases & phase) == 0 || string.IsNullOrEmpty(captureDirectory))
                    return;
                frameCaptures.Add(Path.Combine(captureDirectory, sample + "-" + phase.ToString().ToLowerInvariant() + ".png"));
            }
        }

        private (byte[] Composite, byte[] World) RenderFrame(object stage, Camera postCamera, HashSet<RenderTexture> visualTargets)
        {
            Assert.That(camera.targetTexture, Is.SameAs(captureTarget));
            Assert.That(camera.aspect, Is.EqualTo((float)CaptureWidth / CaptureHeight).Within(.0001f));
            var image = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGB24, false);
            var previousActive = RenderTexture.active;
            object postEffect = null;
            MethodInfo refreshPostEffect = null;
            if (postCamera)
            {
                postEffect = stage.GetType().GetField("postEffects", PrivateInstance).GetValue(stage);
                refreshPostEffect = postEffect.GetType().GetMethod("Tick");
            }

            try
            {
                Canvas.ForceUpdateCanvases();
                byte[] world = null;
                if (postCamera)
                {
                    // Update the blur target and projection for this resolution before drawing either pass.
                    refreshPostEffect.Invoke(postEffect, new object[] { 0f });
                    var blurTarget = postCamera.targetTexture;
                    Assert.That(blurTarget && blurTarget.IsCreated() && blurTarget.width == CaptureWidth &&
                        blurTarget.height == CaptureHeight, Is.True, "The blur camera must draw the capture frame at the capture resolution.");
                    visualTargets.Add(blurTarget);
                    postCamera.Render();
                    world = ReadPng(blurTarget, image);
                }
                camera.Render();
                return (ReadPng(captureTarget, image), world);
            }
            finally
            {
                RenderTexture.active = previousActive;
                UnityEngine.Object.Destroy(image);
            }
        }

        private static byte[] ReadPng(RenderTexture source, Texture2D image)
        {
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = source;
                image.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
                image.Apply(false, false);
                return image.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        private static void TrackMeshes(Transform parent, HashSet<Mesh> output)
        {
            foreach (var filter in parent.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh && (mesh.name == "Skeleton dynamic mesh" || mesh.name == "Battle cinematic quad" ||
                    mesh.name == "Battle post-effect quad"))
                    output.Add(mesh);
            }
        }

        private static T HostField<T>(BattleHudPreview host, string name) =>
            (T)typeof(BattleHudPreview).GetField(name, PrivateInstance).GetValue(host);

        private static void SetFacing(BattlePreviewActor actor, bool faceLeft)
        {
            actor.enabled = false;
            Facing.SetValue(actor, faceLeft);
            actor.enabled = true;
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        [Serializable]
        private sealed class GraphId { public string id = ""; }

        private sealed class SceneBaseline
        {
            private readonly Camera camera;
            private readonly GameObject background;
            private readonly Transform actor;
            private readonly Transform shadow;
            private readonly Vector3 cameraPosition;
            private readonly Color cameraClear;
            private readonly Rect cameraRect;
            private readonly int cameraMask;
            private readonly RenderTexture cameraTarget;
            private readonly int cameraChildren;
            private readonly bool backgroundActive;
            private readonly Vector3 shadowHome;
            private readonly (Renderer Renderer, BattlePreviewActor Owner, bool Enabled, Vector3 Position, Quaternion Rotation, Vector3 Scale)[] visibility;
            private readonly (Transform Transform, Vector3 Position, Vector3 Scale)[] backdrop;

            internal SceneBaseline(Camera camera, GameObject background, Transform units, Transform actor, Transform shadow)
            {
                this.camera = camera;
                this.background = background;
                this.actor = actor;
                this.shadow = shadow;
                cameraPosition = camera.transform.position;
                CameraSize = camera.orthographicSize;
                cameraClear = camera.backgroundColor;
                cameraRect = camera.rect;
                cameraMask = camera.cullingMask;
                cameraTarget = camera.targetTexture;
                cameraChildren = camera.transform.childCount;
                backgroundActive = background.activeSelf;
                ActorHome = actor.localPosition;
                ActorWorldX = actor.position.x;
                shadowHome = shadow.localPosition;
                var bodyProperty = typeof(BattlePreviewActor).GetProperty("PresentationRenderer", PrivateInstance);
                var owners = units.GetComponentsInChildren<BattlePreviewActor>(true)
                    .Where(view => (Renderer)bodyProperty.GetValue(view)).ToDictionary(view => (Renderer)bodyProperty.GetValue(view));
                visibility = units.GetComponentsInChildren<Renderer>(true).Select(renderer =>
                    (renderer, owners.GetValueOrDefault(renderer), renderer.enabled, renderer.transform.localPosition,
                        renderer.transform.localRotation, renderer.transform.localScale)).ToArray();
                backdrop = background.GetComponentsInChildren<SpriteRenderer>(true).Select(item =>
                    (item.transform, item.transform.localPosition, item.transform.localScale)).ToArray();
            }

            internal float CameraSize { get; }
            internal Vector3 ActorHome { get; }
            internal float ActorWorldX { get; }

            internal void AssertRestored(string sample)
            {
                Assert.That(Vector3.Distance(camera.transform.position, cameraPosition), Is.LessThan(0.001f), sample);
                Assert.That(camera.orthographicSize, Is.EqualTo(CameraSize).Within(0.001f), sample);
                Assert.That(camera.backgroundColor, Is.EqualTo(cameraClear), sample);
                Assert.That(camera.rect, Is.EqualTo(cameraRect), sample);
                Assert.That(camera.cullingMask, Is.EqualTo(cameraMask), sample);
                Assert.That(camera.targetTexture, Is.EqualTo(cameraTarget), sample);
                Assert.That(camera.transform.childCount, Is.EqualTo(cameraChildren), sample + " left camera children behind");
                Assert.That(background.activeSelf, Is.EqualTo(backgroundActive), sample);
                Assert.That(Vector3.Distance(actor.localPosition, ActorHome), Is.LessThan(0.001f), sample);
                Assert.That(Vector3.Distance(shadow.localPosition, shadowHome), Is.LessThan(0.001f), sample);
                foreach (var item in visibility)
                {
                    var current = item.Owner ? (Renderer)typeof(BattlePreviewActor).GetProperty("PresentationRenderer", PrivateInstance).GetValue(item.Owner) : item.Renderer;
                    Assert.That(current && current.enabled == item.Enabled, Is.True, sample + " changed slot renderer visibility");
                    var body = current.transform;
                    Assert.That(Vector3.Distance(body.localPosition, item.Position), Is.LessThan(.001f), sample);
                    Assert.That(Vector3.Distance(body.localScale, item.Scale), Is.LessThan(.001f), sample);
                    Assert.That(Quaternion.Angle(body.localRotation, item.Rotation), Is.LessThan(.01f), sample);
                }
                foreach (var item in backdrop)
                {
                    Assert.That(Vector3.Distance(item.Transform.localPosition, item.Position), Is.LessThan(0.001f), sample);
                    Assert.That(Vector3.Distance(item.Transform.localScale, item.Scale), Is.LessThan(0.001f), sample);
                }
            }
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            playback?.Dispose();
            playback = null;
            if (captureTarget)
            {
                if (camera && camera.targetTexture == captureTarget) camera.targetTexture = null;
                captureTarget.Release();
                UnityEngine.Object.Destroy(captureTarget);
                captureTarget = null;
            }
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
        }
    }
}
#endif
