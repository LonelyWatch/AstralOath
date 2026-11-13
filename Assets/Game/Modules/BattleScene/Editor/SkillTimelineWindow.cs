using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Client.BattleScene.Editor
{
    internal sealed class SkillTimelineWindow : EditorWindow
    {
        private const string LabCatalog = "Assets/Game/Modules/CharacterLab/Configs/catalog.json";
        private const string BattleCatalog = "Assets/Game/Modules/BattleScene/Configs/battle-catalog.json";
        private const float TrackLabel = 103f;
        private const float RowHeight = 24f;
        private const float RulerHeight = 26f;
        private static readonly string[] Tracks =
            { "动作 / 位移", "镜头 / 震动", "特效 / 命中", "全屏 / 场景", "条件 / 其他" };
        private static readonly Color[] TrackColors =
        {
            new Color(.34f,.73f,.59f), new Color(.40f,.67f,1f), new Color(1f,.69f,.38f),
            new Color(.78f,.55f,.96f), new Color(.61f,.69f,.79f)
        };

        [Serializable]
        private sealed class Draft : ScriptableObject { public string patch; }
        private sealed class Skill
        {
            internal string Hero, Name, Id, Asset, Target;
            internal int Slot;
        }

        private Skill[] skills = Array.Empty<Skill>();
        private string[] targetIds = Array.Empty<string>();
        private string[] targetLabels = Array.Empty<string>();
        private int targetIndex, targetMode;
        private Skill selected;
        private SkillTimelinePreviewSession preview;
        private JObject source, patch, selectedNode;
        private Draft draft;
        private string savedPatch, search = "", error;
        private bool playing, loop, snap = true, flipped, audio, rebuild, compareOriginal;
        private int fps = 30, selectedGuid = -1;
        private float speed = 1f, zoom = 1f, scrollSeconds;
        private Vector2 rosterScroll, detailScroll;
        private double lastUpdate;
        private float? dragStart, dragGhost;
        private Rect ruler;
        private Rect trackArea;

        [MenuItem("Tools/战斗场景/技能演出编辑器")]
        public static void Open() => GetWindow<SkillTimelineWindow>("技能演出");

        private void OnEnable()
        {
            minSize = new Vector2(900, 580);
            saveChangesMessage = "当前技能有未保存的演出修改。";
            EditorApplication.update += UpdatePreview;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            Undo.undoRedoPerformed += RestoreUndo;
            try
            {
                ReadCatalog();
                if (skills.Length > 0) LoadSkill(skills[0]);
            }
            catch (Exception problem) { error = problem.Message; }
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdatePreview;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            Undo.undoRedoPerformed -= RestoreUndo;
            DisposePreview();
            if (draft) DestroyImmediate(draft);
        }

        private void PlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) DisposePreview();
            if (state == PlayModeStateChange.EnteredEditMode) rebuild = selected != null;
        }

        private void ReadCatalog()
        {
            var paths = BattleSkillPresentationAssets.AddressMap();
            var lab = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(LabCatalog).text);
            var battle = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(BattleCatalog).text);
            var active = battle["skills"].ToLookup(row => (string)row["heroId"]);
            var targets = lab["characters"].OrderBy(hero => (string)hero["id"], StringComparer.Ordinal).ToArray();
            targetIds = targets.Select(hero => (string)hero["id"]).ToArray();
            targetLabels = targets.Select(hero => $"{hero["name"]} ({hero["id"]})").ToArray();
            skills = lab["characters"].SelectMany(hero =>
            {
                string id = (string)hero["id"];
                return hero["skills"].Select((row, index) =>
                {
                    string address = (string)row["graphAddress"];
                    var sourceAsset = BattleSkillPresentationAssets.Load<TextAsset>(address, paths);
                    string graph = (string)JObject.Parse(sourceAsset.text)["id"];
                    var authority = active[id].FirstOrDefault(item => (int)item["slot"] == index + 1);
                    return new Skill
                    {
                        Hero = id, Name = (string)hero["name"], Slot = index + 1, Id = graph,
                        Asset = AssetDatabase.GetAssetPath(sourceAsset), Target = (string)authority?["target"]
                    };
                });
            }).OrderBy(skill => skill.Hero, StringComparer.Ordinal).ThenBy(skill => skill.Slot).ToArray();
        }

        private void LoadSkill(Skill skill)
        {
            if (selected != null && Dirty &&
                !EditorUtility.DisplayDialog("未保存的演出", "切换技能会放弃当前草稿。", "放弃并切换", "继续编辑"))
                return;

            DisposePreview();
            selected = skill;
            selectedNode = null;
            selectedGuid = -1;
            error = null;
            zoom = 1f;
            scrollSeconds = 0f;
            source = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(skill.Asset).text);
            var catalog = ReadOverrides();
            patch = catalog[skill.Id] is JObject saved ? (JObject)saved.DeepClone() : SkillTimelineOverrides.NewPatch(source);
            savedPatch = patch.ToString(Formatting.None);
            if (draft) DestroyImmediate(draft);
            draft = CreateInstance<Draft>();
            draft.hideFlags = HideFlags.HideAndDontSave;
            draft.patch = savedPatch;
            rebuild = true;
        }

        private static JObject ReadOverrides()
        {
            var data = AssetDatabase.LoadAssetAtPath<TextAsset>(SkillTimelineOverrides.AssetPath);
            return data ? SkillTimelineOverrides.ReadCatalog(data.text) : new JObject();
        }

        private bool Dirty => draft && !string.Equals(draft.patch, savedPatch, StringComparison.Ordinal);

        public override void SaveChanges()
        {
            SavePatch();
            base.SaveChanges();
        }

        public override void DiscardChanges()
        {
            if (draft) draft.patch = savedPatch;
            base.DiscardChanges();
        }

        private void SavePatch()
        {
            SkillTimelineOverrides.Apply(source, patch);
            var graphs = ReadOverrides();
            if (((JObject)patch["nodes"]).Count == 0) graphs.Remove(selected.Id);
            else graphs[selected.Id] = patch.DeepClone();
            string path = Path.GetFullPath(SkillTimelineOverrides.AssetPath);
            string temp = path + ".writing";
            try
            {
                File.WriteAllText(temp, new JObject { ["version"] = 1, ["graphs"] = graphs }.ToString(Formatting.Indented) + "\n");
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            AssetDatabase.ImportAsset(SkillTimelineOverrides.AssetPath, ImportAssetOptions.ForceSynchronousImport);
            savedPatch = draft.patch;
        }

        private void RestoreUndo()
        {
            if (!draft || source == null) return;
            patch = JObject.Parse(draft.patch);
            rebuild = true;
            Repaint();
        }

        private void EditNode(Action<JObject> action)
        {
            if (selectedGuid < 0) return;
            var next = (JObject)patch.DeepClone();
            var nodes = (JObject)next["nodes"];
            string key = selectedGuid.ToString(CultureInfo.InvariantCulture);
            var node = nodes[key] as JObject ?? new JObject();
            action(node);
            nodes[key] = node;
            if (node.Count == 0) nodes.Remove(key);
            SkillTimelineOverrides.Apply(source, next);
            Undo.RecordObject(draft, "修改技能演出节点");
            draft.patch = next.ToString(Formatting.None);
            patch = next;
            rebuild = true;
        }

        private void SelectCue(JObject node, float at)
        {
            selectedNode = node;
            selectedGuid = (int?)node["guid"] ?? -1;
            SeekStage(at);
        }

        private void UpdatePreview()
        {
            if (selected == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            try
            {
                if (rebuild)
                {
                    float current = preview?.Time ?? 0f;
                    preview ??= new SkillTimelinePreviewSession();
                    var graph = compareOriginal ? source : SkillTimelineOverrides.Apply(source, patch);
                    bool all = targetMode is 2 or 4 || targetMode == 0 &&
                        (selected.Target is "all_allies" or "all_enemies" or "passive");
                    bool allies = targetMode is 3 or 4 || targetMode == 0 &&
                        (selected.Target is "one_ally" or "all_allies");
                    bool self = targetMode == 5 || targetMode == 0 && selected.Target == "self";
                    preview.LoadScenario(selected.Hero, selected.Id, graph.ToString(Formatting.None),
                        targetHeroId: targetIds.ElementAtOrDefault(targetIndex), casterOnRight: flipped,
                        targetAll: all, targetAllies: allies, targetSelf: self);
                    preview.Seek(Mathf.Min(current, preview.Duration));
                    if (selectedGuid >= 0)
                        selectedNode = preview.Cues.FirstOrDefault(item => (int?)item.Node["guid"] == selectedGuid).Node;
                    rebuild = false;
                    playing = false;
                    error = null;
                    preview.Render();
                    Repaint();
                }
                if (!playing || preview == null) return;
                double now = EditorApplication.timeSinceStartup;
                float delta = Mathf.Clamp((float)(now - lastUpdate), 0f, .12f);
                lastUpdate = now;
                float next = preview.Time + delta * speed;
                if (next >= preview.Duration)
                {
                    if (loop)
                    {
                        preview.Seek(0f);
                        if (audio && Mathf.Approximately(speed, 1f)) preview.StartAudio();
                        next = 0f;
                    }
                    else
                    {
                        playing = false;
                        preview.StopAudio();
                        next = preview.Duration;
                    }
                }
                preview.Seek(next);
                preview.Render();
                Repaint();
            }
            catch (Exception problem)
            {
                error = problem.GetBaseException().Message;
                rebuild = false;
                playing = false;
                DisposePreview();
                Repaint();
            }
        }

        private void DisposePreview()
        {
            playing = false;
            preview?.Dispose();
            preview = null;
        }

        private void PlayPause()
        {
            if (preview == null) return;
            playing = !playing;
            lastUpdate = EditorApplication.timeSinceStartup;
            if (!playing) preview.SetAudioPaused(true);
            else if (preview.Time <= .001f || preview.Time >= preview.Duration)
            {
                preview.Seek(0f);
                if (audio && Mathf.Approximately(speed, 1f)) preview.StartAudio();
            }
            else preview.SetAudioPaused(false);
        }

        private void Seek(float time)
        {
            if (preview == null) return;
            playing = false;
            preview.StopAudio();
            preview.Seek(time);
            preview.Render();
            Repaint();
        }

        private void SeekStage(float stage)
        {
            if (preview == null) return;
            Seek(preview.StageToWallTime(stage));
        }

        private void OnGUI()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("退出 Play 模式后使用独立预览。", MessageType.Info);
                return;
            }

            hasUnsavedChanges = Dirty;
            HandleKeys();
            DrawToolbar();
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawRoster();
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
                {
                    DrawViewport();
                    DrawTimeline();
                }
                DrawInspector();
            }
        }

        private void HandleKeys()
        {
            var input = Event.current;
            if (input.type != EventType.KeyDown || EditorGUIUtility.editingTextField) return;
            if (input.keyCode == KeyCode.Space) PlayPause();
            else if (input.keyCode == KeyCode.LeftArrow) Seek((preview?.Time ?? 0f) - 1f / fps);
            else if (input.keyCode == KeyCode.RightArrow) Seek((preview?.Time ?? 0f) + 1f / fps);
            else if (input.keyCode == KeyCode.Home) Seek(0f);
            else if (input.keyCode == KeyCode.End) Seek(preview?.Duration ?? 0f);
            else return;
            input.Use();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(selected == null ? "技能演出" : $"{selected.Name} / S{selected.Slot}  {selected.Id}", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                bool original = GUILayout.Toggle(compareOriginal, "对照原图", EditorStyles.toolbarButton);
                if (original != compareOriginal)
                {
                    compareOriginal = original;
                    rebuild = true;
                }
                if (GUILayout.Button("原始图", EditorStyles.toolbarButton) && selected != null)
                    EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<TextAsset>(selected.Asset));
                using (new EditorGUI.DisabledScope(!Dirty))
                    if (GUILayout.Button("保存演出", EditorStyles.toolbarButton))
                        try { SavePatch(); error = null; } catch (Exception problem) { error = problem.Message; }
                if (Dirty) GUILayout.Label("未保存", EditorStyles.miniLabel);
                if (preview == null && selected != null && patch?["nodes"] is JObject nodes && nodes.Count > 0 &&
                    GUILayout.Button("还原本技能", EditorStyles.toolbarButton) &&
                    EditorUtility.DisplayDialog("还原演出", "清除当前技能覆盖并恢复原图。", "还原", "取消"))
                {
                    patch = SkillTimelineOverrides.NewPatch(source);
                    draft.patch = patch.ToString(Formatting.None);
                    rebuild = true;
                }
            }
        }

        private void DrawRoster()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(165)))
            {
                search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
                rosterScroll = EditorGUILayout.BeginScrollView(rosterScroll);
                string hero = null;
                foreach (var skill in skills.Where(Matches))
                {
                    if (hero != skill.Hero)
                    {
                        hero = skill.Hero;
                        EditorGUILayout.LabelField(skill.Name + "  " + hero, EditorStyles.boldLabel);
                    }
                    using (new EditorGUI.DisabledScope(selected == skill))
                        if (GUILayout.Button($"S{skill.Slot}  {skill.Id}", EditorStyles.miniButton)) LoadSkill(skill);
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private bool Matches(Skill skill) => string.IsNullOrEmpty(search) ||
            skill.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
            skill.Hero.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
            skill.Id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

        private void DrawViewport()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("|◀", EditorStyles.toolbarButton)) Seek(0f);
                if (GUILayout.Button("−1 帧", EditorStyles.toolbarButton)) Seek((preview?.Time ?? 0f) - 1f / fps);
                if (GUILayout.Button(playing ? "暂停" : "播放", EditorStyles.toolbarButton)) PlayPause();
                if (GUILayout.Button("+1 帧", EditorStyles.toolbarButton)) Seek((preview?.Time ?? 0f) + 1f / fps);
                loop = GUILayout.Toggle(loop, "循环", EditorStyles.toolbarButton);
                bool nextAudio = GUILayout.Toggle(audio, "声音", EditorStyles.toolbarButton);
                if (audio != nextAudio && !nextAudio) preview?.StopAudio();
                audio = nextAudio;
                float newSpeed = EditorGUILayout.FloatField(speed, GUILayout.Width(37));
                if (!Mathf.Approximately(newSpeed, speed))
                {
                    speed = Mathf.Clamp(newSpeed, .1f, 3f);
                    preview?.StopAudio();
                }
                GUILayout.Label("×", EditorStyles.miniLabel);
                bool nextFlip = GUILayout.Toggle(flipped, "换边", EditorStyles.toolbarButton);
                if (nextFlip != flipped) { flipped = nextFlip; rebuild = true; }
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                targetMode = EditorGUILayout.Popup(targetMode, new[] { "按技能选目标", "单体敌人", "全体敌人", "单体友军", "全体友军", "自身" }, GUILayout.Width(100));
                if (targetLabels.Length > 0)
                    targetIndex = EditorGUILayout.Popup(targetIndex, targetLabels, GUILayout.Width(133));
                if (EditorGUI.EndChangeCheck()) rebuild = true;
                if (preview != null && GUILayout.Button("下一命中", EditorStyles.toolbarButton))
                {
                    var hits = preview.HitTimes;
                    float nextHit = hits.FirstOrDefault(hit => hit > preview.StageTime + .01f);
                    SeekStage(nextHit > 0f ? nextHit : hits.FirstOrDefault());
                }
            }
            var image = GUILayoutUtility.GetRect(220f, 220f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(image, new Color(.07f, .08f, .10f));
            if (preview?.Texture)
                GUI.DrawTexture(image, preview.Texture, ScaleMode.ScaleToFit, false);
        }

        private void DrawTimeline()
        {
            if (preview == null) return;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("帧", GUILayout.Width(16));
                EditorGUI.BeginChangeCheck();
                int frame = EditorGUILayout.DelayedIntField(Mathf.RoundToInt(preview.Time * fps), GUILayout.Width(44));
                if (EditorGUI.EndChangeCheck()) Seek(frame / (float)fps);
                GUILayout.Label($"播放 {preview.Time:F3}s   源 {preview.StageTime:F3}s", GUILayout.Width(148));
                fps = EditorGUILayout.IntPopup(fps, new[] { "24 fps", "30 fps", "60 fps" }, new[] { 24, 30, 60 }, GUILayout.Width(66));
                snap = GUILayout.Toggle(snap, "吸附", EditorStyles.toolbarButton);
                GUILayout.FlexibleSpace();
                GUILayout.Label("缩放");
                zoom = GUILayout.HorizontalSlider(zoom, 1f, 12f, GUILayout.Width(65));
            }
            trackArea = GUILayoutUtility.GetRect(220f, RulerHeight + RowHeight * Tracks.Length, GUILayout.ExpandWidth(true));
            ruler = new Rect(trackArea.x + TrackLabel, trackArea.y, Mathf.Max(1f, trackArea.width - TrackLabel), RulerHeight);
            float visible = Mathf.Max(.25f, preview.Duration / zoom);
            scrollSeconds = Mathf.Clamp(scrollSeconds, 0f, Mathf.Max(0f, preview.Duration - visible));
            DrawTrackGrid(visible);
            DrawCues(visible);
            TimelineInput(visible);
            if (dragGhost.HasValue)
            {
                float ghost = Mathf.Clamp(X(dragGhost.Value, visible), ruler.xMin, ruler.xMax);
                EditorGUI.DrawRect(new Rect(ghost, ruler.y, 2f, trackArea.height), Color.white);
            }
            if (zoom > 1f)
                scrollSeconds = GUILayout.HorizontalScrollbar(scrollSeconds, visible, 0f, Mathf.Max(visible, preview.Duration));
        }

        private void DrawTrackGrid(float visible)
        {
            EditorGUI.DrawRect(trackArea, new Color(.13f,.15f,.18f));
            for (int row = 0; row < Tracks.Length; row++)
            {
                var rect = new Rect(trackArea.x, ruler.yMax + row * RowHeight, trackArea.width, RowHeight);
                if ((row & 1) == 0) EditorGUI.DrawRect(rect, new Color(.17f,.19f,.22f));
                GUI.Label(new Rect(rect.x + 5, rect.y, TrackLabel - 6, RowHeight), Tracks[row], EditorStyles.miniLabel);
            }
            float spacing = visible >= 12f ? 2f : visible >= 5f ? 1f : visible >= 2f ? .5f : visible >= 1f ? .25f : .1f;
            for (float tick = Mathf.Ceil(scrollSeconds / spacing) * spacing; tick <= scrollSeconds + visible; tick += spacing)
            {
                float x = X(tick, visible);
                EditorGUI.DrawRect(new Rect(x, ruler.y, 1f, trackArea.height), new Color(.32f,.36f,.43f,.46f));
                GUI.Label(new Rect(x + 2, ruler.y, 43, 19), tick.ToString("0.##", CultureInfo.InvariantCulture), EditorStyles.miniLabel);
            }
        }

        private void DrawCues(float visible)
        {
            foreach (var cue in preview.Cues)
            {
                string kind = (string)cue.Node["etty"];
                int track = Track(kind);
                float start = preview.StageToWallTime(cue.At);
                float end = preview.StageToWallTime(cue.At + cue.Duration);
                if (kind == "CUTIN")
                {
                    var hold = preview.Cutins.FirstOrDefault(item => Mathf.Approximately(item.At, cue.At));
                    end = start + hold.Duration;
                }
                float left = X(start, visible);
                float right = X(end, visible);
                if (right < ruler.xMin || left > ruler.xMax) continue;
                var rect = new Rect(Mathf.Max(left, ruler.xMin), ruler.yMax + track * RowHeight + 4f,
                    Mathf.Min(Mathf.Max(right - left, 8f), ruler.xMax - Mathf.Max(left, ruler.xMin)), RowHeight - 8f);
                var color = ReferenceEquals(cue.Node, selectedNode) ? Color.white : TrackColors[track];
                EditorGUI.DrawRect(rect, color);
                if (rect.width >= 25f)
                    GUI.Label(rect, new GUIContent(kind, $"#{cue.Node["guid"]} {kind}  {cue.At:F3}s"), EditorStyles.centeredGreyMiniLabel);
                var input = Event.current;
                if (input.type == EventType.MouseDown && input.button == 0 && rect.Contains(input.mousePosition))
                {
                    SelectCue(cue.Node, cue.At);
                    if (!compareOriginal && selectedGuid >= 0) dragStart = cue.At;
                    dragMouseX = input.mousePosition.x;
                    input.Use();
                }
            }

            if (preview.Time >= scrollSeconds && preview.Time <= scrollSeconds + visible)
                EditorGUI.DrawRect(new Rect(X(preview.Time, visible), ruler.y, 2f, trackArea.height), new Color(1f,.87f,.51f));
        }

        private static int Track(string kind) => kind switch
        {
            "ANI" or "ANI_SCOPE" or "IDLE" or "MOVE" or "NODE_ANI" => 0,
            "CAM" or "ZOOM" or "SHAKE" => 1,
            "HIT" or "EFFECT" or "PPEFFECT" => 2,
            "CUTIN" or "DARK" or "BGHATCH" or "BGSHOW" or "LAYOUT" => 3,
            _ => 4
        };

        private float X(float time, float visible) => ruler.x + (time - scrollSeconds) / visible * ruler.width;

        private void TimelineInput(float visible)
        {
            var input = Event.current;
            if (input.type == EventType.ScrollWheel && trackArea.Contains(input.mousePosition))
            {
                if (input.shift) zoom = Mathf.Clamp(zoom * Mathf.Pow(1.1f, -input.delta.y), 1f, 12f);
                else scrollSeconds += input.delta.y * visible * .08f;
                input.Use();
                Repaint();
                return;
            }
            if (dragStart.HasValue && input.type == EventType.MouseDrag)
            {
                float sourceTime = DragStageTime(input.mousePosition.x, visible);
                dragGhost = preview.StageToWallTime(Milliseconds(sourceTime) / 1000f);
                input.Use();
                Repaint();
                return;
            }
            if (input.type == EventType.MouseUp)
            {
                if (dragStart.HasValue && selectedGuid >= 0 && input.button == 0)
                {
                    float sourceTime = DragStageTime(input.mousePosition.x, visible);
                    if (Mathf.Abs(input.mousePosition.x - dragMouseX) >= 4f)
                        TryEdit(() => EditNode(node => node["atMs"] = Milliseconds(sourceTime)));
                }
                dragStart = dragGhost = null;
            }
            if (ruler.Contains(input.mousePosition) && input.button == 0 &&
                (input.type == EventType.MouseDown || input.type == EventType.MouseDrag))
            {
                float time = scrollSeconds + (input.mousePosition.x - ruler.x) / ruler.width * visible;
                Seek(snap ? Mathf.Round(time * fps) / fps : time);
                input.Use();
            }
        }
        private float dragMouseX;

        private float DragStageTime(float mouseX, float visible)
        {
            float wallOffset = (mouseX - dragMouseX) / ruler.width * visible;
            float originalWall = preview.StageToWallTime(dragStart.Value);
            return preview.WallToStageTime(originalWall + wallOffset);
        }

        private int Milliseconds(float seconds)
        {
            float value = snap ? Mathf.Round(seconds * fps) / fps : seconds;
            return Mathf.RoundToInt(Mathf.Clamp(value * 1000f, 0f, SkillTimelineOverrides.MaximumMilliseconds));
        }

        private void DrawInspector()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(245)))
            {
                EditorGUILayout.LabelField("节点参数", EditorStyles.boldLabel);
                detailScroll = EditorGUILayout.BeginScrollView(detailScroll, GUILayout.ExpandHeight(true));
                if (preview != null && preview.Cues.Count > 0)
                {
                    var labels = new string[preview.Cues.Count + 1];
                    labels[0] = "定位节点…";
                    int current = 0;
                    for (int index = 0; index < preview.Cues.Count; index++)
                    {
                        var cue = preview.Cues[index];
                        labels[index + 1] = $"{cue.At:F3}s  {cue.Node["etty"]}  #{cue.Node["guid"]}";
                        if (ReferenceEquals(cue.Node, selectedNode)) current = index + 1;
                    }
                    int picked = EditorGUILayout.Popup(current, labels);
                    if (picked > 0 && picked != current)
                    {
                        var cue = preview.Cues[picked - 1];
                        SelectCue(cue.Node, cue.At);
                    }
                }
                using var readOnly = new EditorGUI.DisabledScope(compareOriginal);
                if (selectedNode != null && selectedGuid < 0)
                {
                    EditorGUILayout.LabelField("模型时间轴", EditorStyles.boldLabel);
                    EditorGUILayout.SelectableLabel((string)selectedNode["trackSource"], EditorStyles.wordWrappedMiniLabel,
                        GUILayout.Height(30));
                    foreach (var field in selectedNode.Properties().Where(field => field.Name != "trackSource"))
                        EditorGUILayout.LabelField(field.Name, field.Value.ToString(Formatting.None));
                }
                else if (selectedNode != null)
                {
                    var original = SkillTimelineOverrides.Nodes(source).First(node => (int?)node["guid"] == selectedGuid);
                    var edit = patch["nodes"]?[selectedGuid.ToString(CultureInfo.InvariantCulture)];
                    EditorGUILayout.LabelField($"#{selectedGuid}  {selectedNode["etty"]}");
                    float at = preview?.Cues.FirstOrDefault(item => (int?)item.Node["guid"] == selectedGuid).At ?? 0f;
                    EditorGUI.BeginChangeCheck();
                    int time = EditorGUILayout.DelayedIntField("源开始（ms）", (int?)edit?["atMs"] ?? Mathf.RoundToInt(at * 1000f));
                    if (EditorGUI.EndChangeCheck()) TryEdit(() => EditNode(node => node["atMs"] = time));
                    if (edit != null && GUILayout.Button("还原此节点")) TryEdit(() => EditNode(node => node.RemoveAll()));
                    foreach (var field in original.Properties().Where(field => field.Name != "guid" && field.Name != "etty"))
                        DrawField(field.Name, edit?["fields"]?[field.Name] ?? field.Value);
                }
                else EditorGUILayout.LabelField("点选轨道上的节点查看参数。", EditorStyles.wordWrappedMiniLabel);
                if (preview != null && preview.Diagnostics.Count > 0)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("演出诊断", EditorStyles.boldLabel);
                    foreach (string message in preview.Diagnostics)
                        EditorGUILayout.HelpBox(message, MessageType.Info);
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawField(string name, JToken value)
        {
            if (!SkillTimelineOverrides.EditableFields.Contains(name))
            {
                EditorGUILayout.LabelField(name, value.ToString(Formatting.None));
                return;
            }
            EditorGUI.BeginChangeCheck();
            JToken updated = value.Type switch
            {
                JTokenType.Integer => new JValue(EditorGUILayout.DelayedIntField(name, (int)value)),
                JTokenType.Float => new JValue(EditorGUILayout.DelayedFloatField(name, (float)value)),
                JTokenType.String => new JValue(EditorGUILayout.DelayedTextField(name, (string)value)),
                JTokenType.Boolean => new JValue(EditorGUILayout.Toggle(name, (bool)value)),
                _ => value
            };
            if (EditorGUI.EndChangeCheck()) TryEdit(() => EditNode(node =>
            {
                var fields = node["fields"] as JObject ?? new JObject();
                fields[name] = updated;
                node["fields"] = fields;
            }));
        }

        private void TryEdit(Action action)
        {
            try { action(); error = null; }
            catch (Exception problem) { error = problem.Message; }
        }
    }
}
