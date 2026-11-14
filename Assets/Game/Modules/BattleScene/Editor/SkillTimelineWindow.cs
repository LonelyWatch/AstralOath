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
        private const float LaneHeight = 25f;
        private const float MaximumZoom = 48f;
        private static readonly Color SelectionColor = new Color(.3f, .78f, 1f);
        [SerializeField] private float rosterWidth = 238f, inspectorWidth = 300f, timelineHeight = 320f;
        private int resizingPanel, dragControl;
        private bool revealSelectedLane, panningTimeline, scrubbingTimeline;
        private float panMouseX, panStart, timelineViewportHeight;
        private string selectedCueKey;
        private object selectionCueCollection;
        private int selectedOccurrence;
        private GUIStyle clipLabel, selectedClipLabel, cardLabel;
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
            internal string Hero, Name, Id, Asset, Target, SlotLabel;
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
        private bool playing, loop, snap = true, flipped, audio, rebuild, compareOriginal, includeAfterglow;
        private float TimelineDuration => preview == null ? 0f : includeAfterglow ? preview.Duration : preview.MainDuration;
        private int fps = 30, selectedGuid = -1;
        private float speed = 1f, zoom = 1f, scrollSeconds;
        private Vector2 rosterScroll, detailScroll, timelineScroll;
        private double lastUpdate;
        private float? dragStart, dragGhost;
        private Rect ruler;
        private Rect trackArea;

        [MenuItem("Tools/战斗场景/技能演出编辑器")]
        public static void Open() => GetWindow<SkillTimelineWindow>("技能演出");

        private void OnEnable()
        {
            minSize = new Vector2(1040, 660);
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
            var variant = battle["skills"].Where(row => !string.IsNullOrEmpty((string)row["speedUpPresentationId"]))
                .ToDictionary(row => ((string)row["heroId"], (string)row["speedUpPresentationId"]));
            var burnVariants = battle["skills"].Where(row => !string.IsNullOrEmpty((string)row["burnPresentationId"]))
                .ToDictionary(row => ((string)row["heroId"], (string)row["burnPresentationId"]));
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
                    bool speedUp = variant.TryGetValue((id, graph), out var speedParent);
                    bool burn = burnVariants.TryGetValue((id, graph), out var burnParent);
                    var parent = speedParent ?? burnParent ?? authority;
                    return new Skill
                    {
                        Hero = id, Name = (string)hero["name"], Slot = index + 1, Id = graph,
                        SlotLabel = burn ? $"S{parent["slot"]} · 燃魂" : speedUp ? $"S{parent["slot"]} · 提速" : $"S{index + 1}",
                        Asset = AssetDatabase.GetAssetPath(sourceAsset), Target = (string)parent?["target"]
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
            selectedCueKey = null;
            error = null;
            includeAfterglow = false;
            zoom = 1f;
            scrollSeconds = 0f;
            timelineScroll = Vector2.zero;
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

        private void SelectCue(JObject node, float at) => PickCue(node, at, true);

        private void PickCue(JObject node, float at, bool reveal)
        {
            selectedNode = node;
            selectedGuid = (int?)node["guid"] ?? -1;
            selectedCueKey = CueKey(node);
            selectionCueCollection = preview.Cues;
            selectedOccurrence = preview.Cues.TakeWhile(item => !ReferenceEquals(item.Node, node)).Count(item => CueKey(item.Node) == selectedCueKey);
            detailScroll = Vector2.zero;
            GUI.FocusControl(null);
            if (reveal) RevealTime(preview.StageToWallTime(at));
            revealSelectedLane = true;
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
                        (selected.Target is "one_ally" or "one_other_ally" or "one_ally_including_dead" or "all_allies");
                    bool self = targetMode == 5 || targetMode == 0 && selected.Target == "self";
                    bool two = targetMode == 6 || targetMode == 0 && selected.Target == "two_enemies";
                    preview.LoadScenario(selected.Hero, selected.Id, graph.ToString(Formatting.None),
                        targetHeroId: targetIds.ElementAtOrDefault(targetIndex), casterOnRight: flipped,
                        targetAll: all, targetAllies: allies, targetSelf: self, targetTwo: two);
                    MovePreviewTime(Mathf.Min(current, TimelineDuration));
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
                if (next >= TimelineDuration)
                {
                    if (loop)
                    {
                        MovePreviewTime(0f);
                        if (audio && Mathf.Approximately(speed, 1f)) preview.StartAudio();
                        next = 0f;
                    }
                    else
                    {
                        playing = false;
                        preview.StopAudio();
                        next = TimelineDuration;
                    }
                }
                MovePreviewTime(next);
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
            selectionCueCollection = null;
        }

        private void PlayPause()
        {
            if (preview == null) return;
            playing = !playing;
            lastUpdate = EditorApplication.timeSinceStartup;
            if (!playing) preview.SetAudioPaused(true);
            else if (preview.Time <= .001f || preview.Time >= TimelineDuration)
            {
                MovePreviewTime(0f);
                if (audio && Mathf.Approximately(speed, 1f)) preview.StartAudio();
            }
            else preview.SetAudioPaused(false);
        }

        private void Seek(float time)
        {
            if (preview == null) return;
            playing = false;
            preview.StopAudio();
            MovePreviewTime(Mathf.Clamp(time, 0f, TimelineDuration));
            preview.Render();
            Repaint();
        }

        private void MovePreviewTime(float time)
        {
            preview.Seek(time);
            // Rewind rebuilds the playback graph, including the JObject instances used by the GUI.
            // Rebind once per cue collection, not every displayed frame, so selection is independent
            // of playback lifetime and repeated model events retain their own occurrence.
            if (ReferenceEquals(selectionCueCollection, preview.Cues)) return;
            selectionCueCollection = preview.Cues;
            if (selectedCueKey == null) return;
            selectedNode = preview.Cues.Where(item => CueKey(item.Node) == selectedCueKey)
                .Skip(selectedOccurrence).FirstOrDefault().Node;
            if (selectedNode == null)
            {
                selectedGuid = -1;
                selectedCueKey = null;
                selectedOccurrence = 0;
            }
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
            EnsureStyles();
            dragControl = GUIUtility.GetControlID("SkillTimeline.Pointer".GetHashCode(), FocusType.Passive);
            if (Event.current.type == EventType.Ignore) ReleasePointer();
            HandleKeys();
            DrawToolbar();
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            float usable = Mathf.Max(1000f, position.width) - 16f;
            float left = Mathf.Clamp(rosterWidth, 210f, Mathf.Min(360f, usable - 700f));
            float right = Mathf.Clamp(inspectorWidth, 260f, Mathf.Min(420f, usable - left - 400f));
            timelineHeight = Mathf.Clamp(timelineHeight, 250f, Mathf.Max(250f, position.height - 315f));
            using (new EditorGUILayout.VerticalScope())
            {
                GUILayout.Space(6f);
                using (new EditorGUILayout.HorizontalScope(GUILayout.Height(Mathf.Max(235f,
                           position.height - timelineHeight - (string.IsNullOrEmpty(error) ? 60f : 100f)))))
                {
                    GUILayout.Space(8f);
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(left)))
                        DrawRoster();
                    DrawSplitter(1, false);
                    using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
                        DrawViewport();
                    DrawSplitter(2, false);
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(right)))
                        DrawInspector();
                    GUILayout.Space(8f);
                }
                DrawSplitter(3, true);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Height(timelineHeight)))
                    DrawTimeline();
                GUILayout.Label("空格 播放/暂停    ← → 逐帧    F 聚焦选中    A 显示全部    C 定位播放头    Shift+滚轮 缩放    中键拖动 平移",
                    EditorStyles.miniLabel);
            }
        }

        private void OnLostFocus() => ReleasePointer();

        private void CapturePointer() => GUIUtility.hotControl = dragControl;

        private void ReleasePointer()
        {
            if (GUIUtility.hotControl == dragControl) GUIUtility.hotControl = 0;
            resizingPanel = 0; panningTimeline = scrubbingTimeline = false; dragStart = dragGhost = null;
        }

        private void EnsureStyles()
        {
            if (clipLabel != null) return;
            clipLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, padding = new RectOffset(5, 3, 0, 0)
            };
            clipLabel.normal.textColor = new Color(.08f, .10f, .14f);
            selectedClipLabel = new GUIStyle(clipLabel) { fontStyle = FontStyle.Bold };
            selectedClipLabel.normal.textColor = Color.white;
            cardLabel = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, padding = new RectOffset(12, 6, 0, 0)
            };
        }

        private void DrawSplitter(int index, bool horizontal)
        {
            Rect rect = horizontal
                ? GUILayoutUtility.GetRect(1f, 8f, GUILayout.ExpandWidth(true))
                : GUILayoutUtility.GetRect(8f, 1f, GUILayout.Width(8f), GUILayout.ExpandHeight(true));
            EditorGUIUtility.AddCursorRect(rect, horizontal ? MouseCursor.ResizeVertical : MouseCursor.ResizeHorizontal);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(horizontal ? new Rect(rect.center.x - 20f, rect.y + 3f, 40f, 2f)
                    : new Rect(rect.x + 3f, rect.center.y - 20f, 2f, 40f), new Color(.34f, .39f, .44f));
            var input = Event.current;
            if (input.type == EventType.MouseDown && input.button == 0 && rect.Contains(input.mousePosition))
            { resizingPanel = index; CapturePointer(); input.Use(); }
            if (resizingPanel != index) return;
            if (input.type == EventType.MouseDrag)
            {
                if (index == 1) rosterWidth = Mathf.Clamp(rosterWidth + input.delta.x, 210f, 360f);
                if (index == 2) inspectorWidth = Mathf.Clamp(inspectorWidth - input.delta.x, 260f, 420f);
                if (index == 3) timelineHeight -= input.delta.y;
                input.Use(); Repaint();
            }
            if (input.type == EventType.MouseUp && input.button == 0) { ReleasePointer(); input.Use(); }
        }

        private void HandleKeys()
        {
            var input = Event.current;
            if (input.type != EventType.KeyDown || EditorGUIUtility.editingTextField ||
                input.control || input.command || input.alt) return;
            if (input.keyCode == KeyCode.Space) PlayPause();
            else if (input.keyCode == KeyCode.LeftArrow) Seek((preview?.Time ?? 0f) - 1f / fps);
            else if (input.keyCode == KeyCode.RightArrow) Seek((preview?.Time ?? 0f) + 1f / fps);
            else if (input.keyCode == KeyCode.Home) Seek(0f);
            else if (input.keyCode == KeyCode.End) Seek(TimelineDuration);
            else if (input.keyCode == KeyCode.F) FocusSelection();
            else if (input.keyCode == KeyCode.A) FitTimeline();
            else if (input.keyCode == KeyCode.C) CenterPlayhead();
            else return;
            input.Use();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(selected == null ? "技能演出" : $"{selected.Name} / {selected.SlotLabel}",
                    EditorStyles.boldLabel, GUILayout.MinWidth(110f));
                if (selected != null) GUILayout.Label(selected.Id, EditorStyles.miniLabel, GUILayout.MinWidth(0f));
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
                if (selected != null && !compareOriginal && patch?["nodes"] is JObject nodes && nodes.Count > 0 &&
                    GUILayout.Button("还原本技能", EditorStyles.toolbarButton) &&
                    EditorUtility.DisplayDialog("还原演出", "清除当前技能覆盖并恢复原图。", "还原", "取消"))
                {
                    Undo.RecordObject(draft, "还原技能演出");
                    patch = SkillTimelineOverrides.NewPatch(source);
                    draft.patch = patch.ToString(Formatting.None);
                    rebuild = true;
                }
            }
        }

        private void DrawRoster()
        {
            GUILayout.Label("角色与技能", EditorStyles.boldLabel);
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            GUILayout.Space(5f);
            rosterScroll = EditorGUILayout.BeginScrollView(rosterScroll, false, true);
            string hero = null;
            foreach (var skill in skills.Where(Matches))
            {
                if (hero != skill.Hero)
                {
                    if (hero != null) GUILayout.Space(8f);
                    hero = skill.Hero;
                    EditorGUILayout.LabelField(skill.Name + "  " + hero, EditorStyles.boldLabel);
                }
                var rect = GUILayoutUtility.GetRect(1f, 36f, GUILayout.ExpandWidth(true));
                bool active = selected == skill;
                EditorGUI.DrawRect(rect, active ? new Color(.13f, .32f, .46f) : new Color(.22f, .24f, .27f));
                if (active) EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), SelectionColor);
                if (GUI.Button(rect, new GUIContent("", skill.Id), GUIStyle.none) && !active) LoadSkill(skill);
                GUI.Label(new Rect(rect.x, rect.y + 1f, rect.width, 18f),
                    (active ? "●  " : "") + skill.SlotLabel + (active ? "    当前演出" : ""), cardLabel);
                GUI.Label(new Rect(rect.x + 12f, rect.y + 18f, rect.width - 20f, 16f),
                    new GUIContent(skill.Id, skill.Asset), EditorStyles.miniLabel);
                GUILayout.Space(3f);
            }
            EditorGUILayout.EndScrollView();
        }

        private bool Matches(Skill skill) => string.IsNullOrEmpty(search) ||
            skill.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
            skill.Hero.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
            skill.Id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

        private void DrawViewport()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                targetMode = EditorGUILayout.Popup(targetMode, new[] { "按技能选目标", "单体敌人", "全体敌人", "单体友军", "全体友军", "自身", "两名敌人" }, GUILayout.Width(114));
                if (targetLabels.Length > 0)
                    targetIndex = EditorGUILayout.Popup(targetIndex, targetLabels, GUILayout.MinWidth(100), GUILayout.MaxWidth(180));
                bool nextFlip = GUILayout.Toggle(flipped, "换边", EditorStyles.toolbarButton, GUILayout.Width(42f));
                if (nextFlip != flipped) { flipped = nextFlip; rebuild = true; }
                if (EditorGUI.EndChangeCheck()) rebuild = true;
                if (preview != null && GUILayout.Button("下一命中", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                {
                    var hits = preview.HitTimes;
                    float nextHit = hits.FirstOrDefault(hit => hit > preview.StageTime + .01f);
                    SeekStage(nextHit > 0f ? nextHit : hits.FirstOrDefault());
                }
            }
            GUILayout.Space(5f);
            var image = GUILayoutUtility.GetRect(80f, 80f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(image, new Color(.07f, .08f, .10f));
            if (preview?.Texture)
                GUI.DrawTexture(image, preview.Texture, ScaleMode.ScaleToFit, false);
        }

        private void DrawTimeline()
        {
            if (preview == null) { GUILayout.Label("正在准备演出预览…"); return; }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(new GUIContent("|◀", "回到开头（Home）"), EditorStyles.toolbarButton, GUILayout.Width(34f))) Seek(0f);
                if (GUILayout.Button("−1 帧", EditorStyles.toolbarButton, GUILayout.Width(50f))) Seek((preview?.Time ?? 0f) - 1f / fps);
                if (GUILayout.Button(playing ? "暂停" : "播放", EditorStyles.toolbarButton, GUILayout.Width(50f))) PlayPause();
                if (GUILayout.Button("+1 帧", EditorStyles.toolbarButton, GUILayout.Width(50f))) Seek((preview?.Time ?? 0f) + 1f / fps);
                loop = GUILayout.Toggle(loop, "循环", EditorStyles.toolbarButton, GUILayout.Width(42f));
                bool nextAudio = GUILayout.Toggle(audio, "声音", EditorStyles.toolbarButton, GUILayout.Width(42f));
                if (audio != nextAudio && !nextAudio) preview?.StopAudio();
                audio = nextAudio;
                float newSpeed = EditorGUILayout.FloatField(speed, GUILayout.Width(37));
                if (!Mathf.Approximately(newSpeed, speed))
                {
                    speed = Mathf.Clamp(newSpeed, .1f, 3f);
                    preview?.StopAudio();
                }
                GUILayout.Label("×", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label("空格 播放/暂停", EditorStyles.miniLabel);
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("时间轴", EditorStyles.boldLabel, GUILayout.Width(50f));
                if (GUILayout.Button(new GUIContent("显示全部  A", "完整显示技能，不改变播放头"), EditorStyles.toolbarButton, GUILayout.Width(90f))) FitTimeline();
                using (new EditorGUI.DisabledScope(selectedNode == null))
                    if (GUILayout.Button(new GUIContent("聚焦选中  F", "双击片段也可聚焦；同时定位到所在轨道"), EditorStyles.toolbarButton, GUILayout.Width(90f))) FocusSelection();
                if (GUILayout.Button("播放头  C", EditorStyles.toolbarButton, GUILayout.Width(78f))) CenterPlayhead();
                GUILayout.Space(12f);
                GUILayout.Label("帧", GUILayout.Width(16));
                EditorGUI.BeginChangeCheck();
                int frame = EditorGUILayout.DelayedIntField(Mathf.RoundToInt(preview.Time * fps), GUILayout.Width(54));
                if (EditorGUI.EndChangeCheck()) { Seek(frame / (float)fps); RevealTime(preview.Time); }
                GUILayout.Label($"播放 {preview.Time:F3} / {TimelineDuration:F3}s   源 {preview.StageTime:F3}s",
                    GUILayout.Width(240f));
                GUILayout.FlexibleSpace();
                bool tails = GUILayout.Toggle(includeAfterglow, new GUIContent("含余辉", "查看模型附加特效的完整余辉范围；不修改原数据或实战时序"),
                    EditorStyles.toolbarButton, GUILayout.Width(70f));
                if (tails != includeAfterglow)
                {
                    includeAfterglow = tails; FitTimeline();
                    if (preview.Time > TimelineDuration) Seek(TimelineDuration);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(SelectionSummary(), selectedNode != null ? EditorStyles.boldLabel : EditorStyles.miniLabel,
                    GUILayout.MinWidth(0));
                GUILayout.FlexibleSpace();
                fps = EditorGUILayout.IntPopup(fps, new[] { "24 fps", "30 fps", "60 fps" }, new[] { 24, 30, 60 }, GUILayout.Width(68));
                snap = GUILayout.Toggle(snap, "吸附", EditorStyles.toolbarButton, GUILayout.Width(40f));
                GUILayout.Label("缩放", GUILayout.Width(30f));
                float nextZoom = GUILayout.HorizontalSlider(zoom, 1f, MaximumZoom, GUILayout.Width(85));
                if (!Mathf.Approximately(nextZoom, zoom)) ZoomTimeline(nextZoom, .5f);
            }
            float visible = Mathf.Max(.01f, TimelineDuration / zoom);
            scrollSeconds = Mathf.Clamp(scrollSeconds, 0f, Mathf.Max(0f, TimelineDuration - visible));
            var rows = PlaceCues(visible, Mathf.Max(120f, ruler.width));
            float height = rows.Sum(row => row.Height);
            timelineViewportHeight = Mathf.Max(100f, timelineHeight - 120f);
            if (revealSelectedLane)
            {
                float top = 0f;
                foreach (var row in rows)
                {
                    foreach (var clip in row.Clips)
                        if (ReferenceEquals(clip.Node, selectedNode))
                        {
                            float y = top + 3f + clip.Lane * LaneHeight;
                            if (y < timelineScroll.y || y + LaneHeight > timelineScroll.y + timelineViewportHeight)
                                timelineScroll.y = Mathf.Clamp(y - timelineViewportHeight * .4f, 0f, Mathf.Max(0f, height - timelineViewportHeight));
                        }
                    top += row.Height;
                }
                revealSelectedLane = false;
            }
            Rect header = GUILayoutUtility.GetRect(220f, RulerHeight, GUILayout.ExpandWidth(true));
            bool scrollbar = height > timelineViewportHeight;
            var headerRuler = new Rect(header.x + TrackLabel, header.y,
                Mathf.Max(1f, header.width - TrackLabel - 12f - (scrollbar ? 16f : 0f)), RulerHeight);
            DrawRuler(header, headerRuler, visible);
            timelineScroll = EditorGUILayout.BeginScrollView(timelineScroll, false, scrollbar,
                GUILayout.Height(timelineViewportHeight));
            trackArea = GUILayoutUtility.GetRect(220f, height, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Layout)
                ruler = new Rect(trackArea.x + TrackLabel, trackArea.y, Mathf.Max(1f, trackArea.width - TrackLabel - 12f), RulerHeight);
            DrawTrackGrid(rows, visible);
            if (includeAfterglow && preview.MainDuration < preview.Duration - .01f &&
                preview.MainDuration < scrollSeconds + visible)
            {
                float x = Mathf.Max(ruler.x, X(preview.MainDuration, visible));
                EditorGUI.DrawRect(new Rect(x, ruler.y, ruler.xMax - x, trackArea.height), new Color(.24f, .22f, .15f, .5f));
                GUI.Label(new Rect(x + 4f, ruler.y + 11f, 130f, 16f), "附加特效余辉", EditorStyles.miniLabel);
            }
            DrawCues(rows, visible);
            TimelineInput(visible);
            if (dragGhost.HasValue)
            {
                float ghost = Mathf.Clamp(X(dragGhost.Value, visible), ruler.xMin, ruler.xMax);
                EditorGUI.DrawRect(new Rect(ghost, ruler.y, 2f, trackArea.height), Color.white);
            }
            EditorGUILayout.EndScrollView();
            scrollSeconds = GUILayout.HorizontalScrollbar(scrollSeconds, visible, 0f, Mathf.Max(visible, TimelineDuration));
        }

        private string SelectionSummary()
        {
            if (selectedNode == null || preview == null) return "单击片段查看参数，双击或按 F 聚焦";
            var cue = preview.Cues.FirstOrDefault(item => ReferenceEquals(item.Node, selectedNode));
            string id = selectedGuid >= 0 ? "#" + selectedGuid : "模型事件（只读）";
            return $"已选：{id} {selectedNode["etty"]}    {Mathf.RoundToInt(preview.StageToWallTime(cue.At) * fps)} 帧 / 源 {cue.At:F3}s";
        }

        private static string CueKey(JObject node) => node["guid"] != null
            ? "node:" + node["guid"] : "model:" + node.ToString(Formatting.None);

        private static Vector2 FrameTimeRange(float duration, float from, float to)
        {
            duration = Mathf.Max(.01f, duration);
            from = Mathf.Clamp(from, 0, duration);
            to = Mathf.Clamp(to, from, duration);
            float padding = Mathf.Max(.12f, (to - from) * .18f);
            float span = Mathf.Clamp(to - from + padding * 2f, duration / MaximumZoom, duration);
            return new Vector2(Mathf.Clamp((from + to - span) * .5f, 0f, duration - span), duration / span);
        }

        private static float AnchoredScroll(float duration, float start, float oldZoom, float newZoom, float anchor)
        {
            float span = duration / Mathf.Max(1f, newZoom);
            return Mathf.Clamp(start + duration / Mathf.Max(1f, oldZoom) * anchor - span * anchor,
                0f, Mathf.Max(0f, duration - span));
        }

        private void ZoomTimeline(float value, float anchor)
        {
            if (preview == null) return;
            value = Mathf.Clamp(value, 1f, MaximumZoom);
            scrollSeconds = AnchoredScroll(TimelineDuration, scrollSeconds, zoom, value, Mathf.Clamp01(anchor));
            zoom = value;
            Repaint();
        }

        private void FitTimeline()
        {
            zoom = 1f; scrollSeconds = 0f; timelineScroll = Vector2.zero;
            GUI.FocusControl(null); Repaint();
        }

        private void FocusSelection()
        {
            if (preview == null || selectedNode == null) return;
            var cue = preview.Cues.First(item => ReferenceEquals(item.Node, selectedNode));
            float start = preview.StageToWallTime(cue.At);
            float end = (string)cue.Node["etty"] == "CUTIN"
                ? start + preview.Cutins.FirstOrDefault(item => Mathf.Approximately(item.At, cue.At)).Duration
                : preview.StageToWallTime(cue.At + cue.Duration);
            if (end > preview.MainDuration + .001f) includeAfterglow = true;
            var view = FrameTimeRange(TimelineDuration, start, end);
            scrollSeconds = view.x; zoom = view.y; revealSelectedLane = true;
            GUI.FocusControl(null); Repaint();
        }

        private void CenterPlayhead()
        {
            if (preview == null) return;
            scrollSeconds = Mathf.Clamp(preview.Time - TimelineDuration / zoom * .5f,
                0f, Mathf.Max(0f, TimelineDuration - TimelineDuration / zoom));
            GUI.FocusControl(null); Repaint();
        }

        private void RevealTime(float time)
        {
            if (preview == null) return;
            if (time > preview.MainDuration + .001f) includeAfterglow = true;
            float span = TimelineDuration / zoom;
            if (time < scrollSeconds || time > scrollSeconds + span)
                scrollSeconds = Mathf.Clamp(time - span * .3f, 0f, Mathf.Max(0f, TimelineDuration - span));
        }

        private sealed class TrackRow
        {
            internal readonly List<float> Ends = new();
            internal readonly List<(JObject Node, float At, float Start, float End, int Lane)> Clips = new();
            internal float Height => Mathf.Max(1, Ends.Count) * LaneHeight + 4f;
        }

        private TrackRow[] PlaceCues(float visible, float rulerWidth)
        {
            var rows = Tracks.Select(_ => new TrackRow()).ToArray();
            float minimumDuration = 8f / rulerWidth * visible;
            var placements = preview.Cues.Select(cue =>
            {
                string kind = (string)cue.Node["etty"];
                float start = preview.StageToWallTime(cue.At);
                float end = kind == "CUTIN"
                    ? start + preview.Cutins.FirstOrDefault(item => Mathf.Approximately(item.At, cue.At)).Duration
                    : preview.StageToWallTime(cue.At + cue.Duration);
                return (cue.Node, cue.At, Track: Track(kind), Start: start, End: Mathf.Max(start + minimumDuration, end));
            });
            foreach (var clip in placements.OrderBy(clip => clip.Start))
            {
                var row = rows[clip.Track];
                int lane = AssignLane(row.Ends, clip.Start, clip.End);
                row.Clips.Add((clip.Node, clip.At, clip.Start, clip.End, lane));
            }
            return rows;
        }

        private static int AssignLane(List<float> laneEnds, float left, float right)
        {
            for (int lane = 0; lane < laneEnds.Count; lane++)
            {
                if (laneEnds[lane] > left) continue;
                laneEnds[lane] = right;
                return lane;
            }
            laneEnds.Add(right);
            return laneEnds.Count - 1;
        }

        private void DrawTrackGrid(TrackRow[] rows, float visible)
        {
            EditorGUI.DrawRect(trackArea, new Color(.13f, .15f, .18f));
            float y = trackArea.y;
            for (int index = 0; index < rows.Length; index++)
            {
                var rect = new Rect(trackArea.x, y, trackArea.width, rows[index].Height);
                if ((index & 1) == 0) EditorGUI.DrawRect(rect, new Color(.17f, .19f, .22f));
                GUI.Label(new Rect(rect.x + 5, rect.y + 2, TrackLabel - 6, LaneHeight), Tracks[index], EditorStyles.miniLabel);
                y += rect.height;
            }
            int frameStep = Mathf.Max(1, Mathf.CeilToInt(48f / ruler.width * visible * fps));
            int first = Mathf.CeilToInt(scrollSeconds * fps / frameStep) * frameStep;
            for (int frame = first; frame / (float)fps <= scrollSeconds + visible; frame += frameStep)
                EditorGUI.DrawRect(new Rect(X(frame / (float)fps, visible), trackArea.y, 1f, trackArea.height),
                    new Color(.32f, .36f, .43f, .46f));
        }

        private void DrawRuler(Rect header, Rect axis, float visible)
        {
            EditorGUI.DrawRect(header, new Color(.11f, .13f, .16f));
            GUI.Label(new Rect(header.x + 5f, header.y + 4f, TrackLabel - 5f, 18f), "轨道 / 帧", EditorStyles.miniLabel);
            int frameStep = Mathf.Max(1, Mathf.CeilToInt(48f / axis.width * visible * fps));
            int first = Mathf.CeilToInt(scrollSeconds * fps / frameStep) * frameStep;
            for (int frame = first; frame / (float)fps <= scrollSeconds + visible; frame += frameStep)
            {
                float time = frame / (float)fps;
                float x = axis.x + (time - scrollSeconds) / visible * axis.width;
                EditorGUI.DrawRect(new Rect(x, axis.yMax - 6f, 1f, 6f), Color.gray);
                GUI.Label(new Rect(x + 2, axis.y, 46f, 19f),
                    new GUIContent(frame + "f", time.ToString("0.000", CultureInfo.InvariantCulture) + " s"), EditorStyles.miniLabel);
            }
            if (preview.Time >= scrollSeconds && preview.Time <= scrollSeconds + visible)
            {
                float x = axis.x + (preview.Time - scrollSeconds) / visible * axis.width;
                EditorGUI.DrawRect(new Rect(x - 2f, axis.y, 4f, axis.height), new Color(1f, .87f, .51f));
            }
            var input = Event.current;
            if (input.type == EventType.MouseDown && input.button == 0 && axis.Contains(input.mousePosition) &&
                !dragStart.HasValue && resizingPanel == 0 && !panningTimeline)
            {
                scrubbingTimeline = true; CapturePointer(); GUI.FocusControl(null);
            }
            if (scrubbingTimeline && (input.type == EventType.MouseDown || input.type == EventType.MouseDrag))
            {
                float time = scrollSeconds + Mathf.Clamp01((input.mousePosition.x - axis.x) / axis.width) * visible;
                Seek(snap ? Mathf.Round(time * fps) / fps : time);
                input.Use();
            }
            if (scrubbingTimeline && input.type == EventType.MouseUp && input.button == 0)
            { ReleasePointer(); input.Use(); }
        }

        private void DrawCues(TrackRow[] rows, float visible)
        {
            float y = trackArea.y;
            for (int index = 0; index < rows.Length; index++)
            {
                foreach (var clip in rows[index].Clips)
                {
                    if (clip.End < scrollSeconds || clip.Start > scrollSeconds + visible) continue;
                    string kind = (string)clip.Node["etty"];
                    float left = Mathf.Clamp(X(clip.Start, visible), ruler.xMin, ruler.xMax - 8f);
                    float right = Mathf.Min(X(clip.End, visible), ruler.xMax);
                    var rect = new Rect(left, y + 3f + clip.Lane * LaneHeight, right - left, LaneHeight - 3f);
                    bool active = ReferenceEquals(clip.Node, selectedNode);
                    EditorGUI.DrawRect(rect, active ? new Color(.08f, .27f, .40f) : TrackColors[index]);
                    if (active)
                    {
                        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), SelectionColor);
                        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), SelectionColor);
                        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), SelectionColor);
                        EditorGUI.DrawRect(new Rect(rect.xMax - 2f, rect.y, 2f, rect.height), SelectionColor);
                    }
                    string id = clip.Node["guid"] != null ? "#" + clip.Node["guid"] : "模型";
                    string detail = $"{id} {kind}  播放 {Mathf.RoundToInt(preview.StageToWallTime(clip.At) * fps)} 帧 / 源 {clip.At:F3}s";
                    GUI.Label(rect, new GUIContent(rect.width >= 38f ? id + " " + kind : "", detail),
                        active ? selectedClipLabel : clipLabel);
                    var input = Event.current;
                    if (input.type == EventType.MouseDown && input.button == 0 && rect.Contains(input.mousePosition))
                    {
                        PickCue(clip.Node, clip.At, false);
                        if (input.clickCount == 2) { dragStart = dragGhost = null; FocusSelection(); }
                        else if (!compareOriginal && selectedGuid >= 0) { dragStart = clip.At; CapturePointer(); }
                        dragMouseX = input.mousePosition.x;
                        input.Use();
                    }
                }
                y += rows[index].Height;
            }
            if (preview.Time >= scrollSeconds && preview.Time <= scrollSeconds + visible)
                EditorGUI.DrawRect(new Rect(X(preview.Time, visible), ruler.y, 2f, trackArea.height), new Color(1f, .87f, .51f));
        }

        private static int Track(string kind) => kind switch
        {
            "ANI" or "ANI_SCOPE" or "IDLE" or "MOVE" or "NODE_ANI" => 0,
            "CAM" or "ZOOM" or "SHAKE" => 1,
            "HIT" or "SHOT" or "EFFECT" or "PPEFFECT" => 2,
            "CUTIN" or "DARK" or "BGHATCH" or "BGSHOW" or "LAYOUT" => 3,
            _ => 4
        };

        private float X(float time, float visible) => ruler.x + (time - scrollSeconds) / visible * ruler.width;

        private void TimelineInput(float visible)
        {
            var input = Event.current;
            if (input.type == EventType.ScrollWheel && !dragStart.HasValue && !scrubbingTimeline && (input.shift || input.alt) && trackArea.Contains(input.mousePosition))
            {
                if (input.shift) ZoomTimeline(zoom * Mathf.Pow(1.1f, -input.delta.y), (input.mousePosition.x - ruler.x) / ruler.width);
                else scrollSeconds += input.delta.y * visible * .08f;
                input.Use();
                Repaint();
                return;
            }
            if (input.type == EventType.MouseDown && input.button == 2 && trackArea.Contains(input.mousePosition))
            {
                panningTimeline = true; CapturePointer(); panMouseX = input.mousePosition.x; panStart = scrollSeconds;
                GUI.FocusControl(null); input.Use();
            }
            if (panningTimeline && input.type == EventType.MouseDrag)
            {
                scrollSeconds = Mathf.Clamp(panStart - (input.mousePosition.x - panMouseX) / ruler.width * visible,
                    0f, Mathf.Max(0f, TimelineDuration - visible));
                input.Use(); Repaint(); return;
            }
            if (input.type == EventType.MouseUp && input.button == 2 && panningTimeline) { ReleasePointer(); input.Use(); }
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
                ReleasePointer();
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
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField("节点参数", EditorStyles.boldLabel);
                if (selectedNode != null)
                {
                    var selectionRect = GUILayoutUtility.GetRect(1f, 28f, GUILayout.ExpandWidth(true));
                    EditorGUI.DrawRect(selectionRect, new Color(.10f, .29f, .41f));
                    GUI.Label(selectionRect, SelectionSummary(), selectedClipLabel);
                }
                float previousLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 110f;
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
                        FocusSelection();
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
                EditorGUIUtility.labelWidth = previousLabelWidth;
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
