using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AstralOath.Client.Skeletons;
using AstralOath.Client.Tests;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace AstralOath.Tests
{
    public sealed class BattleCfxAnimationSelectionTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Module = "AstralOath.Client.BattleScene";
        private GameObject root, group;
        private IDisposable playback;
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private Type runtime;

        [TearDown]
        public void Cleanup()
        {
            // Runtime visuals normally retire in PlayMode. Remove this EditMode fixture's group first.
            if (group) UnityEngine.Object.DestroyImmediate(group);
            playback?.Dispose(); playback = null;
            if (root) UnityEngine.Object.DestroyImmediate(root);
            foreach (var item in owned) if (item) UnityEngine.Object.DestroyImmediate(item);
            owned.Clear();
        }

        [Test]
        public void RealAngelicaAlternatePrimitiveUsesItsOwnDuration()
        {
            const string prefix = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1062/";
            var effect = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(prefix +
                "Effects/effect/angelica_skill_03_target.cfx.json").text);
            var part = (JObject)effect["primitive"][4];
            Assert.That((string)part["ani"], Is.EqualTo("animation2"));
            Create((string)part["source"], AssetDatabase.LoadAssetAtPath<TextAsset>(prefix +
                "Animations/effect/angelica_sk3eff_target_b.spine.json"));
            Assert.That(Duration(part), Is.EqualTo(2.6333f).Within(.0002f),
                "The source fifth primitive selects animation2, not the shorter animation clip.");
        }

        [TestCase("2.1.25")]
        [TestCase("3.8.99")]
        public void ExplicitAnimationControlsPoseAndLifetime(string version)
        {
            CreateSynthetic(version);
            var part = JObject.Parse("{ 'format':'spine','source':'fixture','ani':'animation2' }");
            Spawn(part);
            runtime.GetMethod("Tick").Invoke(playback, new object[] { .5f });
            Assert.That(group.GetComponentInChildren<MeshFilter>().sharedMesh.bounds.center.x,
                Is.EqualTo(10f).Within(.001f), "ani must select the requested native runtime animation.");
            Assert.That(Duration(part), Is.EqualTo(2f).Within(.001f));
        }

        [TestCase("2.1.25")]
        [TestCase("3.8.99")]
        public void ExplicitZeroDurationPoseRendersForItsDeclaredLifetime(string version)
        {
            CreateSynthetic(version);
            var part = JObject.Parse("{ 'format':'spine','source':'fixture','ani':'static','lifeTime':800 }");
            Assert.That(Duration(part), Is.EqualTo(.8f).Within(.001f),
                "A declared static clip exists even when its natural duration is zero.");
            Spawn(part);
            Assert.That(group.GetComponentInChildren<MeshFilter>().sharedMesh.bounds.center.x,
                Is.EqualTo(25f).Within(.001f));
            runtime.GetMethod("Tick").Invoke(playback, new object[] { .79f });
            var mesh = group.GetComponentInChildren<MeshFilter>();
            Assert.That(mesh, Is.Not.Null, "The explicit lifetime keeps the static visual alive.");
            Assert.That(mesh.sharedMesh.bounds.center.x, Is.EqualTo(25f).Within(.001f));
        }

        [TestCase("2.1.25")]
        [TestCase("3.8.99")]
        public void DelayedActionCanSwitchToAZeroDurationPose(string version)
        {
            CreateSynthetic(version);
            var part = JObject.Parse("{ 'format':'spine','source':'fixture','lifeTime':800,'actions':[{'action':'static','delay':300}] }");
            Spawn(part);
            runtime.GetMethod("Tick").Invoke(playback, new object[] { .2f });
            Assert.That(group.GetComponentInChildren<MeshFilter>().sharedMesh.bounds.center.x,
                Is.EqualTo(2f).Within(.001f), "The initial clip remains active before the declared delay.");
            runtime.GetMethod("Tick").Invoke(playback, new object[] { .11f });
            Assert.That(group.GetComponentInChildren<MeshFilter>().sharedMesh.bounds.center.x,
                Is.EqualTo(25f).Within(.001f), "The action must select a real zero-duration pose at its delay.");
            runtime.GetMethod("Tick").Invoke(playback, new object[] { .4f });
            Assert.That(group.GetComponentInChildren<MeshFilter>().sharedMesh.bounds.center.x,
                Is.EqualTo(25f).Within(.001f));
            Assert.That(Duration(part), Is.EqualTo(.8f).Within(.001f));
        }

        [Test]
        public void ExplicitMissingAnimationIsDiagnosedAndNeverFallsBack()
        {
            CreateSynthetic("2.1.25");
            var part = JObject.Parse("{ 'format':'spine','source':'fixture','ani':'missing' }");
            LogAssert.Expect(LogType.Warning, "fixture_skill: Unknown CFX animation 'missing' in 'effect/fixture.scsp'.");
            Assert.That(Duration(part), Is.Zero);
            var failure = Assert.Throws<TargetInvocationException>(() => Spawn(part));
            Assert.That(failure.InnerException, Is.TypeOf<FormatException>());
            Assert.That(failure.InnerException.Message, Does.Contain("missing"));
            Assert.That(group.GetComponentInChildren<MeshFilter>(), Is.Null);
        }

        [Test]
        public void DelayedActionKeepsItsSeparateAnimationSwitchAndLifetimeOverride()
        {
            CreateSynthetic("2.1.25");
            var part = JObject.Parse("{ 'format':'spine','source':'fixture','actions':[{'action':'animation2','delay':300}] }");
            Assert.That(Duration(part), Is.EqualTo(2.3f).Within(.001f));
            Spawn(part);
            runtime.GetMethod("Tick").Invoke(playback, new object[] { .5f });
            Assert.That(group.GetComponentInChildren<MeshFilter>().sharedMesh.bounds.center.x,
                Is.EqualTo(4f).Within(.001f), "actions remain delayed transitions from the selected initial clip.");
            part["lifeTime"] = 420;
            Assert.That(Duration(part), Is.EqualTo(.42f).Within(.001f));
        }

        [Test]
        public void OpacityActionFadesTheExistingSpineClipOnTheEffectClock()
        {
            CreateSynthetic("2.1.25");
            const string path = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1066/Effects/effect/lunabianca_skill_03_sky.cfx.json";
            var source = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(path).text);
            var primitive = ((JArray)source["primitive"]).OfType<JObject>()
                .Single(row => (string)row["source"] == "lunabianca_skill_03_cloudscroll_f");
            var part = new JObject { ["source"] = "fixture", ["format"] = "spine", ["actions"] = primitive["actions"].DeepClone() };
            Assert.That(Duration(part), Is.EqualTo(1.9f).Within(.001f));
            Spawn(part);
            var renderer = group.GetComponentInChildren<MeshRenderer>();
            var block = new MaterialPropertyBlock();
            runtime.GetMethod("Tick").Invoke(playback, new object[] { 1.75f });
            renderer.GetPropertyBlock(block);
            Assert.That(block.GetFloat("_PresentationOpacity"), Is.EqualTo(1f).Within(.001f));
            runtime.GetMethod("Tick").Invoke(playback, new object[] { .1f });
            renderer.GetPropertyBlock(block);
            Assert.That(block.GetFloat("_PresentationOpacity"), Is.EqualTo(.5f).Within(.001f));
        }

        [Test]
        public void UnmappedActionDoesNotInventAnAnimationOrExtendDuration()
        {
            CreateSynthetic("2.1.25");
            var part = JObject.Parse("{ 'format':'spine','source':'fixture','actions':[{'action':'Fire','delay':10000}] }");
            LogAssert.Expect(LogType.Warning, "fixture_skill: Unmapped CFX action 'Fire' in 'effect/fixture.scsp'.");
            Assert.That(Duration(part), Is.EqualTo(1f).Within(.001f));
        }

        [TestCase(1.6f)]
        [TestCase(.8f)]
        [TestCase(.5f)]
        public void ClarissaS2RepeatedLayersSampleTheirAuthoredLocalFrame(float speed)
        {
            const string prefix = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1028/";
            var cfx = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(prefix +
                "Effects/effect/clarisa_skill_02.cfx.json").text);
            var part = cfx["primitive"].OfType<JObject>().Single(row =>
                (string)row["source"] == "clarisa_sk2_eff_b" && (float?)row["timeScale"] == speed);
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(prefix + "Animations/effect/clarisa_sk2_eff_b.spine.json");
            var atlas = AssetDatabase.LoadAssetAtPath<TextAsset>(prefix + "Atlas/effect/clarisa_sk2_eff_b.atlas.txt");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "Textures/effect/clarisa_sk2eff.png");
            const string pageName = "clarisa_sk2eff.sct";
            Create((string)part["source"], json, atlas, texture, pageName);
            using var reference = new SkeletonPlayerFactory().Create(new SkeletonPlayerSource(json.text, atlas.text,
                new Dictionary<string, Texture2D> { [pageName] = texture }, true, 1f), root.transform);
            reference.Play("animation", false);
            Spawn(part);
            const float elapsed = .2f;
            reference.Tick(elapsed * speed);
            runtime.GetMethod("Tick").Invoke(playback, new object[] { elapsed });
            var expected = reference.Root.GetComponent<MeshFilter>().sharedMesh.vertices;
            Assert.That(expected, Is.Not.Empty);
            Assert.That(group.GetComponentInChildren<MeshFilter>().sharedMesh.vertices, Is.EqualTo(expected),
                "At one stage instant, the visible geometry must match the source rig at its local clip time.");
            Assert.That(Duration(part), Is.EqualTo(reference.Duration / speed).Within(.0001f));
        }

        private void CreateSynthetic(string version)
        {
            var json = JObject.Parse(SkeletonPlayerTests.Json(version));
            json["animations"] = JObject.Parse(@"{
                'animation':{'bones':{'root':{'translate':[{'time':0},{'time':1,'x':10}]}}},
                'animation2':{'bones':{'root':{'translate':[{'time':0},{'time':2,'x':40}]}}},
                'static':{'bones':{'root':{'translate':[{'time':0,'x':25}]}}}} ");
            var document = new TextAsset(json.ToString()); owned.Add(document);
            Create("fixture", document);
        }

        [TestCase(false, true, 1)]
        [TestCase(true, true, 1)]
        [TestCase(true, false, -1)]
        public void KenLetteringKeepsItsSourceOrientationOnEitherTeam(bool enemy, bool sourceFlag, int expectedSign)
        {
            PrepareKenLettering(enemy, sourceFlag, out var node, out var actor);
            runtime.GetMethod("Effect", Private).Invoke(playback, new object[] { node, actor, Array.CreateInstance(actor.GetType(), 0) });
            runtime.GetMethod("Tick").Invoke(playback, new object[] { 1.2f });
            var mesh = root.GetComponentsInChildren<MeshFilter>().Single(item => item.name == "ken_nbg_tx");
            Assert.That(mesh.sharedMesh.vertexCount, Is.GreaterThan(0));
            Assert.That(Math.Sign(mesh.transform.TransformVector(Vector3.right).x), Is.EqualTo(expectedSign),
                "Ken's authored lettering must keep the same reading direction when the caster is on the right.");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void KenLetteringProjectionDuringSourceZoomPulse(bool confirmedLayer)
        {
            var cameraObject = new GameObject("Ken pulse camera");
            owned.Add(cameraObject);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5.4f;
            camera.aspect = 16f / 9f;
            camera.transform.position = Vector3.back * 10f;
            PrepareKenLettering(false, true, out var node, out var actor, camera);
            if (!confirmedLayer) node["location"] = 1;
            actor.transform.position = new Vector3(2.8f, -2f);
            actor.transform.localScale = Vector3.one * .6f;
            var cinematic = runtime.GetField("cinematic", Private).GetValue(playback);
            var stage = cinematic.GetType();
            stage.GetMethod("Begin").Invoke(cinematic, new object[] { actor.transform, new[] { actor.transform }, false });
            var graph = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1047/Configs/stagept/c_ken_skill_03.graph.json").text);
            var nodes = graph["source"]["entities"].OfType<JObject>().ToDictionary(n => (int)n["guid"]);
            stage.GetMethod("Play").Invoke(cinematic, new object[] { nodes[19] });
            stage.GetMethod("Play").Invoke(cinematic, new object[] { nodes[20] });
            runtime.GetMethod("Tick").Invoke(playback, new object[] { .2f });
            runtime.GetMethod("Effect", Private).Invoke(playback, new object[] { node, actor, Array.CreateInstance(actor.GetType(), 0) });
            runtime.GetMethod("Tick").Invoke(playback, new object[] { 1.2f });
            var mesh = root.GetComponentsInChildren<MeshFilter>().Single(item => item.name == "ken_nbg_tx");
            var vertex = Array.FindIndex(mesh.sharedMesh.colors32, c => c.a > 200);
            Assert.That(vertex, Is.GreaterThanOrEqualTo(0));
            Vector2 Project() => camera.WorldToViewportPoint(mesh.transform.TransformPoint(mesh.sharedMesh.vertices[vertex]));
            float Width() => Mathf.Abs(camera.WorldToViewportPoint(mesh.transform.TransformPoint(Vector3.right * 100f)).x -
                camera.WorldToViewportPoint(mesh.transform.position).x);
            float originalWidth = Width();
            var originalVertex = Project();
            Assert.That(camera.orthographicSize, Is.EqualTo(9f).Within(.001f));
            float referenceSize = confirmedLayer ? 5.4f : camera.orthographicSize;
            Assert.That(originalWidth, Is.EqualTo(100f * (float)node["scale"] * .6f /
                (2f * referenceSize * camera.aspect)).Within(.0001f));
            foreach (int id in new[] { 22, 23 })
            {
                stage.GetMethod("Play").Invoke(cinematic, new object[] { nodes[id] });
                runtime.GetMethod("Tick").Invoke(playback, new object[] { .05f });
                Assert.That(camera.orthographicSize, Is.EqualTo(5.4f / (id == 22 ? .65f : .6f)).Within(.001f));
                float expectedRatio = !confirmedLayer && id == 22 ? .65f / .6f : 1f;
                Assert.That(Width() / originalWidth, Is.EqualTo(expectedRatio).Within(.0001f));
                if (confirmedLayer)
                    Assert.That(Vector2.Distance(Project(), originalVertex), Is.LessThan(.0002f));
            }
        }

        private void PrepareKenLettering(bool enemy, bool sourceFlag, out JObject node, out Component actor,
            Camera camera = null)
        {
            const string prefix = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1047/";
            const string source = "ken_nbg_tx";
            var graph = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(prefix +
                "Configs/stagept/c_ken_skill_03.graph.json").text);
            node = graph["source"]["entities"].OfType<JObject>().Single(n => (int)n["guid"] == 18);
            Assert.That((bool)node["disableAutoFlipX"], Is.True);
            if (!sourceFlag) node.Remove("disableAutoFlipX");
            Create(source, AssetDatabase.LoadAssetAtPath<TextAsset>(prefix + "Animations/effect/" + source + ".spine.json"),
                AssetDatabase.LoadAssetAtPath<TextAsset>(prefix + "Atlas/effect/" + source + ".atlas.txt"),
                AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "Textures/effect/ken_nbgeff.png"), "ken_nbgeff.sct",
                camera: camera, importScale: .01f);
            var documents = (Dictionary<string, JObject>)runtime.GetProperty("documents", Private).GetValue(playback);
            documents.Add("effect/ken_skill_03_tx.cfx", JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(prefix +
                "Effects/effect/ken_skill_03_tx.cfx.json").text));
            var actorType = Type.GetType(Module + ".BattlePreviewActor, " + Module, true);
            var actorObject = new GameObject("Actor");
            actorObject.transform.SetParent(root.transform, false);
            actor = actorObject.AddComponent(actorType);
            actorType.GetField("faceLeft", Private).SetValue(actor, enemy);
            runtime.GetField("caster", Private).SetValue(playback, actor);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SchnielDetoxDrawsOneGroundRingAtHisFormation(bool enemy)
        {
            const string prefix = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1060/";
            const string source = "schniel_skeff_sk2_3_ground_glow";
            var graph = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(prefix +
                "Configs/stagept/c_schniel_skill_02.graph.json").text);
            var node = graph["source"]["entities"].OfType<JObject>().Single(n => (int)n["guid"] == 9);
            Assert.That((int)node["location"], Is.EqualTo(32));
            var cfx = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(prefix +
                "Effects/effect/schniel_skeff_sk2_3.cfx.json").text);
            Assert.That(cfx["primitive"].Count(), Is.EqualTo(9));
            var ground = cfx["primitive"].Single(n => (string)n["source"] == source);
            cfx["primitive"] = new JArray(ground.DeepClone());
            Create(source, AssetDatabase.LoadAssetAtPath<TextAsset>(prefix + "Animations/effect/" + source + ".spine.json"),
                AssetDatabase.LoadAssetAtPath<TextAsset>(prefix + "Atlas/effect/" + source + ".atlas.txt"),
                AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "Textures/effect/" + source + ".png"), source + ".sct",
                importScale:.01f);
            ((Dictionary<string, JObject>)runtime.GetProperty("documents", Private).GetValue(playback))
                .Add("effect/schniel_skeff_sk2_3.cfx", cfx);
            var actors = Type.GetType(Module + ".BattlePreviewActor, " + Module, true);
            float facing = enemy ? -1f : 1f;
            Component Actor(float x, float y, bool otherTeam)
            {
                var go = new GameObject("Actor");
                go.transform.SetParent(root.transform,false);
                go.transform.position = new Vector3(x*facing,y,0f);
                var unit = go.AddComponent(actors);
                actors.GetField("faceLeft",Private).SetValue(unit, otherTeam != enemy);
                return unit;
            }
            var caster = Actor(-2,-3,false);
            var groupMembers = new[] { caster, Actor(-4,-1,false), Actor(-6,1,false), Actor(-8,-1,false) };
            var opponent = Actor(4,0,true);
            var inactive = Actor(20,0,false);
            inactive.gameObject.SetActive(false);
            var defeated = Actor(30,0,false);
            actors.GetField("defeatPose",Private).SetValue(defeated,true);
            var formation = Array.CreateInstance(actors, 7);
            for (int i = 0; i < groupMembers.Length; i++) formation.SetValue(groupMembers[i],i);
            formation.SetValue(opponent,4);
            formation.SetValue(inactive,5);
            formation.SetValue(defeated,6);
            runtime.GetField("participants",Private).SetValue(playback,formation);
            runtime.GetField("caster",Private).SetValue(playback,caster);
            var recipients = Array.CreateInstance(actors,1);
            recipients.SetValue(caster,0);
            runtime.GetMethod("Effect",Private).Invoke(playback,new object[]{node,caster,recipients});
            runtime.GetMethod("Tick").Invoke(playback,new object[]{.666f});
            var rings = root.GetComponentsInChildren<MeshFilter>().Where(m => m.name == source).ToArray();
            Assert.That(rings.Length,Is.EqualTo(1));
            Assert.That(rings[0].sharedMesh.colors32.Any(c => c.a>0),Is.True);
            Assert.That(rings[0].transform.parent.position.x,Is.EqualTo(-5f*facing).Within(.001f));
            Assert.That(rings[0].transform.parent.position.y,Is.EqualTo(-1.7f).Within(.001f));
        }

        [Test]
        public void TenebriaDomeDrawsTheSameVisibleSourceVerticesInFourQuadrants()
        {
            const string prefix = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1050/";
            const string source = "tenebria_skill_03_up_doom_L";
            var cfx = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(prefix + "Effects/effect/tenebria_skill_03.cfx.json").text);
            var parts = cfx["primitive"].OfType<JObject>().Where(part => (string)part["source"] == source).ToArray();
            Assert.That(parts.Length, Is.EqualTo(4));
            Create(source, AssetDatabase.LoadAssetAtPath<TextAsset>(prefix + "Animations/effect/" + source + ".spine.json"),
                AssetDatabase.LoadAssetAtPath<TextAsset>(prefix + "Atlas/effect/" + source + ".atlas.txt"),
                AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "Textures/effect/" + source + ".png"), source + ".sct");
            var meshes = new List<MeshFilter>();
            for (int index = 0; index < parts.Length; index++)
            {
                var parsed = (JObject)runtime.GetMethod("ReadPrimitive", Private).Invoke(playback, new object[] { parts[index] });
                Assert.That(parsed, Is.Not.Null, "Missing source layer " + index);
                string label = "dome" + index;
                runtime.GetMethod("Spawn", Private).Invoke(playback, new object[] { "effect/" + label, parsed,
                    (Func<Vector3>)(() => Vector3.zero), null, 1f, Vector2.zero, 1f, false, null, false, false, false });
                meshes.Add(root.GetComponentsInChildren<Transform>().Single(item => item.name == "Skill CFX " + label).GetComponentInChildren<MeshFilter>());
            }
            runtime.GetMethod("Tick").Invoke(playback, new object[] { 2.55f });
            var mesh = meshes[0].sharedMesh;
            var colors = mesh.colors32;
            var vertices = mesh.vertices;
            int vertex = Array.FindIndex(colors, color => color.a > 0);
            Assert.That(vertex, Is.GreaterThanOrEqualTo(0), "The test frame must contain a visible, off-axis source vertex.");
            Vector3 p = meshes[0].transform.TransformPoint(vertices[vertex]);
            Assert.That(Mathf.Min(Mathf.Abs(p.x), Mathf.Abs(p.y)), Is.GreaterThan(1f));
            var signs = new[] { Vector2.one, new Vector2(-1, 1), -Vector2.one, new Vector2(1, -1) };
            for (int i = 0; i < meshes.Count; i++)
            {
                Vector3 world = meshes[i].transform.TransformPoint(meshes[i].sharedMesh.vertices[vertex]);
                Assert.That(world.x, Is.EqualTo(signs[i].x * p.x).Within(.001f));
                Assert.That(world.y, Is.EqualTo(signs[i].y * p.y).Within(.001f));
            }
            var projected = RenderLayerCenters(meshes);
            Assert.That(Mathf.Min(Mathf.Abs(projected[0].x), Mathf.Abs(projected[0].y)), Is.GreaterThan(2f));
            for (int i = 1; i < projected.Length; i++)
            {
                Vector2 expected = Vector2.Scale(projected[0], signs[i]);
                Assert.That(Vector2.Distance(projected[i], expected), Is.LessThan(2f),
                    "Each source layer must produce mirrored visible pixels, including the single-axis windings.");
            }
        }

        private Vector2[] RenderLayerCenters(List<MeshFilter> meshes)
        {
            const int pixels = 256, layer = 29;
            var renders = meshes.Select(mesh => mesh.GetComponent<MeshRenderer>()).ToArray();
            float extent = 1f;
            foreach (var item in renders)
            {
                item.gameObject.layer = layer;
                extent = Mathf.Max(extent, Mathf.Abs(item.bounds.min.x), Mathf.Abs(item.bounds.max.x),
                    Mathf.Abs(item.bounds.min.y), Mathf.Abs(item.bounds.max.y));
                item.enabled = false;
            }
            var cameraObject = new GameObject("CFX frame"); cameraObject.transform.SetParent(root.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -20);
            camera.orthographic = true; camera.orthographicSize = 1.05f * extent; camera.aspect = 1f;
            camera.cullingMask = 1 << layer; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var target = new RenderTexture(pixels, pixels, 24);
            var image = new Texture2D(pixels, pixels, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var centers = new Vector2[renders.Length];
            try
            {
                camera.targetTexture = target;
                for (int i = 0; i < renders.Length; i++)
                {
                    renders[i].enabled = true;
                    camera.Render(); RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, pixels, pixels), 0, 0);
                    var colors = image.GetPixels32();
                    Vector2 sum = Vector2.zero;
                    int count = 0;
                    for (int j = 0; j < colors.Length; j++)
                    {
                        if (colors[j].r + colors[j].g + colors[j].b < 30) continue;
                        sum += new Vector2(j % pixels, j / pixels); count++;
                    }
                    Assert.That(count, Is.GreaterThan(15), "Source layer " + i + " must contribute visible pixels.");
                    centers[i] = sum / count - Vector2.one * ((pixels - 1f) / 2f);
                    renders[i].enabled = false;
                }
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target);
            }
            return centers;
        }

        [Test]
        public void SezS3BurstAdvancesTenLocalFramesPerStageFrame()
        {
            const string prefix = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1038/Effects/effect/";
            var cfx = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(prefix + "sez_gaien_skill_03_target.cfx.json").text);
            var parts = cfx["primitive"].OfType<JObject>().Where(part => (string)part["format"] == "particle").ToArray();
            Assert.That(parts.Length, Is.EqualTo(5));
            Assert.That(parts.All(part => (float)part["timeScale"] == 10f), Is.True);
            string source = (string)parts[0]["source"];
            var definition = AssetDatabase.LoadAssetAtPath<TextAsset>(prefix + source + ".particle.json");
            Create(source, definition, particle: true);
            var texture = new Texture2D(2, 2); owned.Add(texture);
            var textures = (Dictionary<string, Texture2D>)runtime.GetProperty("textures", Private).GetValue(playback);
            textures.Add("particle/pati_01.sct", texture);
            var particleRuntime = runtime.Assembly.GetType(Module + ".BattleHudParticles", true);
            var referenceRoot = new GameObject("Source burst"); referenceRoot.transform.SetParent(root.transform);
            using var reference = (IDisposable)Activator.CreateInstance(particleRuntime, definition, textures,
                referenceRoot.transform, Vector2.zero, .01f, 55);
            particleRuntime.GetMethod("Play").Invoke(reference, null);
            runtime.GetMethod("SpawnParticles", Private).Invoke(playback,
                new object[] { source, parts[0], group.transform, 1f, 55, (Action)(() => { }), false });
            particleRuntime.GetMethod("Tick").Invoke(reference, new object[] { .01f * 10f });
            runtime.GetMethod("Tick").Invoke(playback, new object[] { .01f });
            var expected = referenceRoot.GetComponentInChildren<MeshFilter>().sharedMesh.vertices;
            Assert.That(expected, Is.Not.Empty);
            Assert.That(group.GetComponentInChildren<MeshFilter>().sharedMesh.vertices, Is.EqualTo(expected));
            Assert.That(Duration(parts[0]), Is.EqualTo(.04f).Within(.0001f),
                "The burst's 0.2 ± 0.2s local lifespan occupies at most 40ms of stage time.");
            parts[0]["lifeTime"] = 150;
            Assert.That(Duration(parts[0]), Is.EqualTo(.15f).Within(.0001f));
        }

        [Test]
        public void HasteHitUsesOneAndAHalfTimesLocalSpineTime()
        {
            const string prefix = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1039/";
            var cfx = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(prefix + "Effects/effect/haste_hit.cfx.json").text);
            var part = (JObject)cfx["primitive"][0];
            Assert.That((float)part["timeScale"], Is.EqualTo(1.5f));
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(prefix + "Animations/effect/hiteff_dark.spine.json");
            Create((string)part["source"], json);
            var unscaled = (JObject)part.DeepClone();
            unscaled.Remove("timeScale");
            Assert.That(Duration(unscaled), Is.GreaterThan(0f));
            Assert.That(Duration(part), Is.EqualTo(Duration(unscaled) / 1.5f).Within(.0001f));
        }

        private void Create(string source, TextAsset document, TextAsset atlas = null, Texture2D texture = null,
            string pageName = "page.png", bool particle = false, Camera camera = null, float importScale = 1f)
        {
            runtime = Type.GetType(Module + ".BattleSkillPresentation, " + Module, true);
            var assetType = Type.GetType(Module + ".BattleSkillPresentationAsset, " + Module, true);
            root = new GameObject("CFX selection fixture");
            group = new GameObject("CFX group"); group.transform.SetParent(root.transform, false);
            if (!atlas) { atlas = new TextAsset(SkeletonPlayerTests.Atlas); owned.Add(atlas); }
            if (!texture) { texture = new Texture2D(16, 16); owned.Add(texture); }
            var asset = Activator.CreateInstance(assetType);
            assetType.GetField("source").SetValue(asset, "effect/" + source + (particle ? ".particle" : ".scsp"));
            assetType.GetField("document").SetValue(asset, document);
            assetType.GetField("atlas").SetValue(asset, atlas);
            assetType.GetField("pageNames").SetValue(asset, new[] { pageName });
            assetType.GetField("pages").SetValue(asset, new[] { texture });
            assetType.GetField("scale").SetValue(asset, importScale);
            var assets = Array.CreateInstance(assetType, 1); assets.SetValue(asset, 0);
            playback = (IDisposable)Activator.CreateInstance(runtime,
                new object[] { root.transform, Array.Empty<TextAsset>(), assets, camera, null, null });
            runtime.GetField("currentPresentationId", Private).SetValue(playback, "fixture_skill");
        }

        private float Duration(JObject part) => (float)runtime.GetMethod("PrimitiveDuration", Private).Invoke(playback, new object[] { part });
        private void Spawn(JObject part) => runtime.GetMethod("SpawnSpine", Private).Invoke(playback,
            new object[] { (string)part["source"], part, group.transform, 1f, 55, 1f, (Action)(() => { }) });
    }
}
