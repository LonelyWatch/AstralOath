#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AstralOath.Client.Arena;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.FoundationDemo;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class ArenaTowerPageTests
    {
        [Test]
        public void InterruptedChallengesKeepTheirIdAcrossReopenUntilResolved()
        {
            var server = new Uri("http://tower-fixture.invalid");
            string player = Guid.NewGuid().ToString("N");
            var team = new[] { "hero-a", "hero-b" };
            var first = TowerChallengeRequest.Resume(server, player, 1, team);
            var retry = TowerChallengeRequest.Resume(server, player, 1, team);
            var otherTeam = TowerChallengeRequest.Resume(server, player, 1, team.Reverse().ToArray());
            var otherStage = TowerChallengeRequest.Resume(server, player, 2, team);
            var otherPlayer = TowerChallengeRequest.Resume(server, player + "-second", 1, team);
            try
            {
                Assert.That(retry.Id, Is.EqualTo(first.Id));
                Assert.That(new[]{first.Id,otherTeam.Id,otherStage.Id,otherPlayer.Id}.Distinct().Count(),Is.EqualTo(4));
                retry.Complete();
                var fresh = TowerChallengeRequest.Resume(server, player, 1, team);
                try
                {
                    Assert.That(fresh.Id, Is.Not.EqualTo(first.Id));
                    first.Complete();
                    Assert.That(TowerChallengeRequest.Resume(server, player, 1, team).Id, Is.EqualTo(fresh.Id));
                }
                finally { fresh.Complete(); }
                Assert.That(TowerChallengeRequest.IsRejected("tower_stage_locked"), Is.True);
                Assert.That(TowerChallengeRequest.IsRejected("request_timeout"), Is.False);
                Assert.That(TowerChallengeRequest.IsRejected("database_unavailable"), Is.False);
            }
            finally
            {
                first.Complete();
                otherTeam.Complete();
                otherStage.Complete();
                otherPlayer.Complete();
            }
        }

        private sealed class Assets : IAssetService
        {
            public Task<T> LoadAsync<T>(string address, BindingScope scope) where T : Object
            {
                string file = address == ArenaTower.Address ? "Res/Prefab/ArenaTower.prefab" : "Configs/arena-zh-hans.json";
                return Task.FromResult(AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Modules/Arena/" + file));
            }
        }

        [UnityTest]
        public IEnumerator ScrollsToCurrentStageAndRoutesClearTeamAndOwnedFormationIndependently()
        {
            var run = Exercise();
            while (!run.IsCompleted) yield return null;
            run.GetAwaiter().GetResult();
        }

        private static async Task Exercise()
        {
            const string module = "Assets/Game/Modules/Arena/";
            var art = AssetDatabase.LoadAssetAtPath<ArenaDraftArt>(module + "Configs/ArenaDraftArt.asset");
            Assert.That(art, Is.Not.Null);
            var table = JsonUtility.FromJson<TowerPageDto>(File.ReadAllText(FindTowerTable()));
            table.highestCleared = 54; table.currentStage = 55; table.rankId = "master";
            foreach (var stage in table.stages) { stage.cleared = stage.stage < 55; stage.unlocked = stage.stage <= 55; }
            var heroes = new[] { "c1001", "c1005", "c1062" }.Select((id, i) => new TowerHeroDto {
                instanceId = "owned-" + i, heroId = id, level = 1, stars = i == 0 ? 3 : 4, slot = i + 1 }).ToArray();
            var canvas = new GameObject("Arena tower fixture", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var cameraObject = new GameObject("Arena tower capture camera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            var size = ArenaReferenceCapture.Size(1920, 900);
            var target = RenderTexture.GetTemporary(size.x, size.y, 24);
            camera.targetTexture = target;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = ~(1 << 30);
            var layer = canvas.GetComponent<Canvas>();
            layer.renderMode = RenderMode.ScreenSpaceCamera;
            layer.worldCamera = camera; layer.planeDistance = 1;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920,1080); scaler.matchWidthOrHeight=1;
            int queried = 0, launched = 0, rooms = 0, shops = 0;
            string notice = null;
            string[] formation = null;
            try
            {
                using var life = new BindingScope(default);
                using var windows = new WindowCoordinator(life.Token);
                var registry = new UIViewRegistry(); registry.Register<ArenaTower>();
                var assets = new Assets();
                var view = await registry.CreateAsync<ArenaTower>(assets, canvas.transform, life);
                var locale = await ArenaLabels.LoadAsync(assets, life, "zh-Hans", "ja");
                var binder = new ArenaTowerBinder(view,locale,windows,
                    _ => Task.FromResult(table),
                    (stage, _) => { queried=stage; return Task.FromResult(new TowerTeamDto {
                        stage=stage, heroes=heroes, defenderHeroIds=new[] {"c1001","c1005","c1062","c1002"} }); },
                    _ => Task.FromResult(heroes),
                    (stage, ids, _) => {launched=stage; formation=ids; return Task.CompletedTask;},
                    id => art.Heroes.First(h => h.Id == id).Face,
                    () => Task.CompletedTask,
                    () => {rooms++; return Task.CompletedTask;},
                    () => {shops++; return Task.CompletedTask;}, s => notice=s, draftArt:art);
                await windows.ShowPageAsync(binder.OpenAsync);
                var page=view.Root.GetComponent<BindingMap>();
                var list=page.Require<ScrollRect>("list");
                var rows=list.content.Cast<Transform>().Where(t=>t.gameObject.activeSelf).Select(t=>t.GetComponent<BindingMap>()).ToArray();
                Assert.That(table.stages.Length, Is.EqualTo(100));
                Assert.That(rows.Length,Is.InRange(5,7),"Reuse the same small set of populated opponent plates.");
                BindingMap StageRow(int stage) => rows.Single(r => r.Require<GameText>("label").text == string.Format(locale.Resolve("tower.stage"),stage));
                Assert.That(StageRow(55).Require<GameObject>("selected").activeSelf,Is.True);
                Canvas.ForceUpdateCanvases();
                var rt=(RectTransform)StageRow(55).transform;
                var center=list.viewport.InverseTransformPoint(rt.TransformPoint(rt.rect.center));
                Assert.That(list.viewport.rect.Contains(center), Is.True, "Current stage must be in the viewport.");
                Assert.That(Mathf.Abs(list.viewport.rect.center.y-center.y), Is.LessThan(2));
                var visible = rows.Where(row => {
                    var box = (RectTransform)row.transform;
                    var point = list.viewport.InverseTransformPoint(box.TransformPoint(box.rect.center));
                    return list.viewport.rect.Contains(point);
                }).ToArray();
                Assert.That(visible.Length,Is.EqualTo(3), "The source shows three complete opponent plates.");
                Assert.That(visible,Does.Contain(StageRow(55)));
                Assert.That(rt.rect.height,Is.EqualTo(162).Within(1));
                var badge=StageRow(55).Require<Image>("portrait0").transform.parent.GetComponent<BindingMap>();
                Assert.That(badge.Require<Image>("tens").sprite,Is.Not.Null);
                Assert.That(badge.Require<Image>("tens").isActiveAndEnabled,Is.True);
                Assert.That(badge.Require<Image>("role").enabled,Is.True);
                Assert.That(StageRow(56).Require<GameButton>("choose").interactable, Is.False);
                Assert.That(StageRow(55).Require<GameButton>("inspect").interactable, Is.False);
                var entryOffset=list.verticalNormalizedPosition;
                list.verticalNormalizedPosition=0;
                list.onValueChanged.Invoke(list.normalizedPosition);
                Assert.That(StageRow(100).Require<GameButton>("choose").interactable,Is.False);
                list.verticalNormalizedPosition=1;
                list.onValueChanged.Invoke(list.normalizedPosition);
                Assert.That(StageRow(1).Require<GameButton>("inspect").interactable,Is.True);
                list.verticalNormalizedPosition=entryOffset;
                list.onValueChanged.Invoke(list.normalizedPosition);
                Assert.That(StageRow(55).Require<GameObject>("selected").activeSelf,Is.True);
                await Capture("01-tower-stage-55");

                StageRow(54).Require<GameButton>("inspect").onClick.Invoke();
                var overlay=page.Require<GameObject>("overlay").GetComponent<BindingMap>();
                Assert.That(queried,Is.EqualTo(54));
                Assert.That(overlay.Require<GameObject>("history").activeInHierarchy,Is.True);
                Assert.That(overlay.Require<Image>("historyAlly0").enabled,Is.True);
                Assert.That(overlay.Require<Image>("historyAlly3").enabled,Is.False);
                Assert.That(overlay.Require<Image>("historyEnemy3").enabled,Is.True);
                Assert.That(overlay.Require<Image>("historyEnemy0").sprite,Is.SameAs(art.Heroes.First(h=>h.Id=="c1001").Face));
                await Capture("02-tower-clear-team");
                overlay.Require<GameButton>("historyBack").onClick.Invoke();
                page.Require<GameButton>("challenge").onClick.Invoke();
                Assert.That(overlay.Require<GameObject>("scene").activeInHierarchy,Is.True);
                var choices=overlay.Require<ScrollRect>("list").content.Cast<Transform>().Where(t=>t.gameObject.activeSelf).ToArray();
                Assert.That(choices.Length, Is.EqualTo(3));
                var query=overlay.Require<TMP_InputField>("query");
                query.text="unavailable hero id";
                Assert.That(choices.Count(t=>t.gameObject.activeSelf),Is.Zero);
                query.text="";
                Assert.That(choices.Count(t=>t.gameObject.activeSelf),Is.EqualTo(3));
                choices[1].GetComponent<BindingMap>().Require<GameButton>("choose").onClick.Invoke();
                Assert.That(choices[1].GetComponent<BindingMap>().Require<GameObject>("selected").activeSelf, Is.False);
                await Capture("03-tower-formation");
                overlay.Require<GameButton>("confirm").onClick.Invoke();
                Assert.That(launched, Is.EqualTo(55));
                Assert.That(formation,Is.EqualTo(new[]{"owned-0","owned-2"}));
                page.Require<GameButton>("simulation").onClick.Invoke();
                page.Require<GameButton>("shop").onClick.Invoke();
                page.Require<GameButton>("record").onClick.Invoke();
                Assert.That((rooms,shops,notice), Is.EqualTo((1,1,locale.Resolve("tower.record"))));
            }
            finally
            {
                camera.targetTexture = null;
                Object.DestroyImmediate(canvas); Object.DestroyImmediate(cameraObject);
                RenderTexture.ReleaseTemporary(target);
            }

            async Task Capture(string name)
            {
                string folder=Environment.GetEnvironmentVariable("ASTRAL_TOWER_CAPTURE_DIR");
                if (string.IsNullOrEmpty(folder) || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
                Directory.CreateDirectory(folder);
                await Task.Yield();
                Canvas.ForceUpdateCanvases();
                foreach (var model in Object.FindObjectsOfType<Camera>().Where(c=>c!=camera && c.targetTexture)) model.Render();
                camera.Render();
                ArenaReferenceCapture.Measure(folder, name, canvas.transform, camera, target);
                var original=RenderTexture.active;
                var frame=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                try
                {
                    RenderTexture.active=target;
                    frame.ReadPixels(new Rect(0,0,target.width,target.height),0,0); frame.Apply();
                    File.WriteAllBytes(Path.Combine(folder,name+".png"),frame.EncodeToPNG());
                }
                finally {RenderTexture.active=original;Object.DestroyImmediate(frame);}
            }
        }

        private static string FindTowerTable()
        {
            for (var dir = Directory.GetParent(Application.dataPath); dir != null; dir = dir.Parent)
            {
                var file = Path.Combine(dir.FullName, "Config", "ArenaTower", "catalog.json");
                if (File.Exists(file)) return file;
            }
            throw new FileNotFoundException("The controlled tower page test requires Config/ArenaTower/catalog.json.");
        }
    }
}
#endif
