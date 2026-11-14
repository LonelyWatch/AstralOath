using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene.Editor
{
    public static partial class BattleSceneBuilder
    {
        // This remains authoring data on BattleActor. Runtime projects a clone through the shared HUD Canvas.
        public static void ConfigureActorStatusPrefab(GameObject root)
        {
            if (!root || !root.GetComponent<BattlePreviewActor>())
                throw new ArgumentException("Expected the common BattleActor prefab root.", nameof(root));
            var owner = root.GetComponent<BattleActorStatusTemplate>();
            if (!owner) owner = root.AddComponent<BattleActorStatusTemplate>();
            var marker = root.transform.Find("StatusTemplate") as RectTransform;
            if (!marker)
            {
                marker = (RectTransform)new GameObject("StatusTemplate", typeof(RectTransform)).transform;
                marker.SetParent(root.transform, false);
                marker.anchorMin = marker.anchorMax = marker.pivot = Vector2.one * .5f;
                marker.sizeDelta = Vector2.zero;
            }
            marker.gameObject.SetActive(false);
            var frame = EnsureStatusPicture(marker, "Frame", "game_hud_bar_bg_n", 0, 0, 86, 26);
            EnsureStatusPicture(marker, "Element", "game_hud_bar_fire", -29, .5f, 24, 21);
            EnsureStatusPicture(marker, "Level", "itxt_num5_b", -29, 0, 26 * .53f, 34 * .53f);
            var health = marker.Find("HP")?.GetComponent<Image>();
            if (!health)
            {
                health = EnsureStatusPicture(marker, "HP", "game_hud_bar_hp", 11, 7, 56, 6);
                SetFill(health, 1f);
            }
            if (!health.transform.Find("Shield Fill"))
            {
                var shield = new GameObject("Shield Fill", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                shield.transform.SetParent(health.transform, false);
                shield.sprite = Asset<Sprite>(HudTextures + "game_hud_bar_sh.png");
                shield.raycastTarget = false;
                shield.rectTransform.anchorMin = shield.rectTransform.anchorMax = Vector2.zero;
                shield.rectTransform.sizeDelta = Vector2.zero;
                shield.gameObject.SetActive(false);
            }
            if (!frame.transform.Find("Status Icons"))
            {
                var icons = (RectTransform)new GameObject("Status Icons", typeof(RectTransform)).transform;
                icons.SetParent(frame.transform, false);
                icons.anchorMin = icons.anchorMax = Vector2.up * .5f;
                icons.pivot = Vector2.zero;
                icons.sizeDelta = Vector2.zero;
                icons.anchoredPosition = new Vector2(1f, 15f) * PixelScale;
            }
            var serialized = new SerializedObject(owner);
            serialized.FindProperty("markerTemplate").objectReferenceValue = marker;
            serialized.FindProperty("profile").objectReferenceValue = BattleUnitPresentationAssets.Load();
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Image EnsureStatusPicture(RectTransform parent, string name, string sprite,
            float x, float y, float width, float height)
        {
            var existing = parent.Find(name);
            if (existing)
            {
                if (!existing.TryGetComponent<Image>(out var image))
                    throw new InvalidOperationException("Actor status template child requires Image: " + name);
                return image;
            }
            return Picture(parent, name, sprite, x, y, width, height);
        }

        [MenuItem("Tools/战斗场景/迁移角色状态模板")]
        public static void MigrateActorStatusTemplates()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before migrating actor status templates.");
            var actor = PrefabUtility.LoadPrefabContents(Prefabs + "BattleActor.prefab");
            try
            {
                ConfigureActorStatusPrefab(actor);
                PrefabUtility.SaveAsPrefabAsset(actor, Prefabs + "BattleActor.prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(actor); }

            var hud = PrefabUtility.LoadPrefabContents(Prefabs + "BattleHud.prefab");
            try
            {
                var markers = hud.transform.Find("Unit Status") as RectTransform;
                if (!markers) markers = Group("Unit Status", (RectTransform)hud.transform);
                foreach (string side in new[] { "Allies", "Enemies" })
                {
                    var team = markers.Find(side) as RectTransform;
                    if (!team) team = Group(side, markers);
                    // Only known legacy status-marker roots are replaced; preserve unrelated authored children.
                    for (int index = team.childCount - 1; index >= 0; index--)
                    {
                        var item = team.GetChild(index);
                        if (item.Find("Frame") && item.Find("HP") && item.Find("Level") && item.Find("Element"))
                            UnityEngine.Object.DestroyImmediate(item.gameObject);
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(hud, Prefabs + "BattleHud.prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(hud); }
            AssetDatabase.SaveAssets();
            Debug.Log("ASTRAL_ACTOR_STATUS_TEMPLATES_MIGRATED");
        }
    }
}
