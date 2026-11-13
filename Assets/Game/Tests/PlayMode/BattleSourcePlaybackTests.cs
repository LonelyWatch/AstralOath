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
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

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
        public IEnumerator AddedHeroesCanEnterBattleAndPlayAllSourceGraphs()
        {
            var roots = default(GameObject[]);
            yield return LoadBattle(value => roots = value);
            var host = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            var catalog = JsonUtility.FromJson<BattleCatalogData>(HostField<TextAsset>(host, "battleCatalog").text);
            string[] added = { "c1002", "c1003", "c1004", "c1006", "c1007", "c1008", "c1009" };
            Assert.That(catalog.heroes.Select(hero => hero.id), Is.EqualTo(added.Concat(new[] { "c1001", "c1005", "c1062" }).OrderBy(id => id, StringComparer.Ordinal)));
            Assert.That(graphs.Length, Is.EqualTo(30));
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
                var skill = catalog.skills.Single(row => row.heroId == id && row.slot == 2);
                Assert.That(map.Require<GameButton>("skill2").interactable, Is.EqualTo(skill.target != "passive"));
                Assert.That(host.TrySelectBattleTarget("enemy-1"), Is.True, id + " S1 must submit through the real battle controller");
                yield return null;
            }
            host.enabled = false;
            yield return null;

            var allies = units.Find("Allies").GetComponentsInChildren<BattlePreviewActor>();
            var enemies = units.Find("Enemies").GetComponentsInChildren<BattlePreviewActor>();
            var front = allies[0];
            var models = (Array)typeof(BattleHudPreview).GetField("heroModels", PrivateInstance).GetValue(host);
            foreach (string id in added)
            {
                var model = models.Cast<object>().Single(value => (string)value.GetType().GetField("id").GetValue(value) == id);
                var type = model.GetType();
                typeof(BattlePreviewActor).GetMethod("UseModel", PrivateInstance).Invoke(front, new[]
                {
                    type.GetField("skeleton").GetValue(model), type.GetField("skin").GetValue(model), id
                });
                yield return null; // Finish deferred disposal of the prior skeleton before taking a renderer baseline.
                var renderer = front.GetComponentInChildren<MeshRenderer>();
                Assert.That(renderer && renderer.enabled && renderer.GetComponent<MeshFilter>().sharedMesh.vertexCount > 0, Is.True, id);
                foreach (var skill in catalog.skills.Where(row => row.heroId == id))
                {
                    var recipients = skill.target switch
                    {
                        "all_allies" => allies,
                        "one_ally" => new[] { allies[1] },
                        "all_enemies" => enemies,
                        "two_enemies" => enemies.Take(2).ToArray(),
                        "passive" => id == "c1008" || id == "c1007" ? enemies : new[] { enemies[0] },
                        _ => new[] { enemies[0] }
                    };
                    yield return Play(skill.presentationId, id + "-s" + skill.slot, front, recipients[0], recipients, Phase.None, Phase.None);
                }
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
            }

            loaded(roots);
        }

        private IEnumerator Play(string id, string sample, BattlePreviewActor actor, BattlePreviewActor target,
            BattlePreviewActor[] recipients, Phase capturePhases, Phase interruptAt)
        {
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
            Assert.That(float.IsFinite(timing.Duration) && timing.Duration > 0f && timing.Duration < ActionTimeout, Is.True, sample);
            Assert.That(timing.Impact, Is.InRange(0f, timing.Duration), sample);
            bool checkTargetsAtHits = id is "c_ras_skill_03" or "c_mercedes_skill_03" or "c_kise_skill_03";
            if (id == "c_aming_skill_02")
            {
                var pillarHits = ((IEnumerable<float>)PresentationType.GetProperty("HitTimes").GetValue(playback)).ToArray();
                Assert.That(pillarHits.Length, Is.EqualTo(7));
                Assert.That(pillarHits[0], Is.EqualTo(2.021f).Within(.001f));
                Assert.That(pillarHits[6], Is.EqualTo(3.143f).Within(.001f));
            }
            float[] checkedHits = checkTargetsAtHits
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
            var cameraTrace = !string.IsNullOrEmpty(captureDirectory) && (id == "c_ras_skill_01" || id == "c_angelica_skill_01")
                ? new List<string> { "source_seconds,camera_x,camera_y,orthographic_size,actor_x,actor_y" } : null;
            double deadline = Time.realtimeSinceStartupAsDouble + ActionTimeout;
            float elapsed = 0f;
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
                }
                var hatch = camera.transform.Find("BGHATCH");
                if (hatch && hatch.gameObject.activeInHierarchy &&
                    hatch.GetComponent<MeshFilter>().sharedMesh.colors[0].a >= .9f)
                    Observe(Phase.Hatch);
                if ((float)requestedSize.GetValue(stage) > baseline.CameraSize + 0.1f)
                    Observe(Phase.Wide);
                if (elapsed < timing.Impact && Mathf.Abs(actor.transform.localPosition.x - baseline.ActorHome.x) > 0.25f &&
                    Mathf.Abs(actor.transform.position.x - target.transform.position.x) < Mathf.Abs(baseline.ActorWorldX - target.transform.position.x))
                    Observe(Phase.Approach);
                if (elapsed >= timing.Impact)
                    Observe(Phase.Impact);

                while (nextCheckedHit < checkedHits.Length && elapsed + .00001f >= checkedHits[nextCheckedHit])
                {
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
                float delta = Time.unscaledDeltaTime;
                float wallCutin = (float)cutinRemaining.GetValue(stage);
                if (wallCutin > 0f) delta = Mathf.Min(delta, wallCutin);
                else if (checkMercedesCutin && elapsed < mercedesSourceCutin)
                    delta = Mathf.Min(delta, mercedesSourceCutin - elapsed);
                else if (nextCheckedHit < checkedHits.Length && elapsed < checkedHits[nextCheckedHit])
                    delta = Mathf.Min(delta, checkedHits[nextCheckedHit] - elapsed);
                Tick.Invoke(playback, new object[] { delta });
                elapsed = (float)PresentationType.GetProperty("StageTime").GetValue(playback);
            }
            while (elapsed < timing.Duration + 0.05f);

            if (cameraTrace != null) File.WriteAllLines(Path.Combine(captureDirectory, sample + "-camera.csv"), cameraTrace);
            if (id == "c_kise_skill_03")
                Assert.That(sourceReactionHits, Is.GreaterThan(0), "At least one Kise hit must use the actual source reaction.");
            if (interruptAt == Phase.None) Assert.That(nextCheckedHit, Is.EqualTo(checkedHits.Length), sample + " must inspect every source hit.");
            Assert.That((observed & capturePhases) == capturePhases, Is.True,
                $"{sample} observed {observed}; expected {capturePhases}.");
            Assert.That(interruptAt == Phase.None || playback == null, Is.True, sample + " never reached the interruption phase");
            yield return null;
            yield return null;
            baseline.AssertRestored(sample);
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
            const int Width = 1920;
            const int Height = 1080;
            var target = RenderTexture.GetTemporary(Width, Height, 24);
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var previousTarget = camera.targetTexture;
            var previousRect = camera.rect;
            var previousAspect = camera.aspect;
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
                camera.targetTexture = target;
                camera.rect = new Rect(0f, 0f, 1f, 1f);
                camera.aspect = (float)Width / Height;
                Canvas.ForceUpdateCanvases();
                byte[] world = null;
                if (postCamera)
                {
                    // Update the blur target and projection for this resolution before drawing either pass.
                    refreshPostEffect.Invoke(postEffect, new object[] { 0f });
                    var blurTarget = postCamera.targetTexture;
                    Assert.That(blurTarget && blurTarget.IsCreated() && blurTarget.width == Width &&
                        blurTarget.height == Height, Is.True, "The blur camera must draw the capture frame at the capture resolution.");
                    visualTargets.Add(blurTarget);
                    postCamera.Render();
                    world = ReadPng(blurTarget, image);
                }
                camera.Render();
                return (ReadPng(target, image), world);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.rect = previousRect;
                camera.aspect = previousAspect;
                try
                {
                    if (postCamera)
                    {
                        refreshPostEffect.Invoke(postEffect, new object[] { 0f });
                        if (postCamera.targetTexture)
                            visualTargets.Add(postCamera.targetTexture);
                    }
                }
                finally
                {
                    RenderTexture.active = previousActive;
                    RenderTexture.ReleaseTemporary(target);
                    UnityEngine.Object.Destroy(image);
                }
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
            private readonly (Renderer Renderer, bool Enabled, Vector3 Position, Quaternion Rotation, Vector3 Scale)[] visibility;
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
                visibility = units.GetComponentsInChildren<Renderer>(true).Select(renderer =>
                    (renderer, renderer.enabled, renderer.transform.localPosition, renderer.transform.localRotation, renderer.transform.localScale)).ToArray();
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
                    Assert.That(item.Renderer.enabled, Is.EqualTo(item.Enabled), sample + " changed slot renderer visibility");
                    var body = item.Renderer.transform;
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
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
        }
    }
}
#endif
