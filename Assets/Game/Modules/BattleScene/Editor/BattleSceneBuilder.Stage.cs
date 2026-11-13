using System;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Client.BattleScene.Editor
{
    public static partial class BattleSceneBuilder
    {
        // Authoring coordinates use the source HUD's 1280×720 design frame.
        private static readonly (string Position, float AllyX, float EnemyX, float Y, float MarkerY, float Scale)[] Slots =
        {
            ("01 Front", 453, 827, 227, 410, 0.60f),
            ("02 Lower", 341, 969, 167, 351, 0.56f),
            ("03 Upper", 292, 969, 297, 468, 0.55f),
            ("04 Rear", 166, 1110, 237, 399, 0.53f)
        };

        private static void CreateFormation()
        {
            var units = new GameObject("Units").transform;
            CreateTeam(Child("Allies", units), false);
            CreateTeam(Child("Enemies", units), true);
        }

        private static void CreateTeam(Transform team, bool enemy)
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                var layout = Slots[i];
                var slot = Child(layout.Position, team);
                slot.localPosition = World(enemy ? layout.EnemyX : layout.AllyX, layout.Y);
                PlaceSprite("Shadow", slot, Asset<Sprite>(HudTextures + "UnitShadow.png"), 640, 363, 85, 23, -50);
            }
        }

        // Keep depth ordering for actors within the world and below the HUD canvas.
        private static int ActorDrawOrder(float y) => Mathf.RoundToInt(Mathf.Clamp01(1 - y / 720f) * 80);

        private static Transform Child(string name, Transform parent)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static Vector3 World(float x, float y) => new Vector3((x - 640) * WorldScale, (y - 360) * WorldScale);

        private static GameObject CreateForest(string folder, string prefix)
        {
            var root = new GameObject(folder + "Stage");
            Sprite Layer(int id) => Asset<Sprite>(BackgroundTextures + folder + "/" + prefix + "_" + id + ".png");
            var sky = Layer(11);
            PlaceSprite("Sky", root.transform, sky, 640, 466, 1282, 512, -90);
            var shrubs = Layer(4);
            PlaceSprite("Distant Shrubs", root.transform, shrubs, 640, 248, 1282, 291, -85);
            var ground = Layer(0);
            // The atlas has shrubs above the playable grass. Frame the floor from
            // the bottom edge so the rear row also lands on continuous grass.
            const float groundHeight = 488f;
            const float groundBottom = -2f;
            float groundWidth = groundHeight * ground.rect.width / ground.rect.height;
            PlaceSprite("Ground", root.transform, ground, 640, groundBottom + groundHeight * 0.5f,
                groundWidth, groundHeight, -70);
            PlaceSprite("Canopy Left", root.transform, Layer(3), 89, 510, 751, 553, -64);
            PlaceSprite("Medium Tree Right", root.transform, Layer(2), 1192, 398, 385, 325, -65);
            PlaceSprite("Round Tree Right", root.transform, Layer(6), 991, 357, 141, 160, -66);
            return root;
        }

        private static SpriteRenderer PlaceSprite(string name, Transform parent, Sprite sprite, float x, float y,
            float width, float height, int order)
        {
            var item = new GameObject(name, typeof(SpriteRenderer));
            item.transform.SetParent(parent, false);
            item.transform.localPosition = World(x, y);
            var renderer = item.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            Vector3 size = sprite.bounds.size;
            item.transform.localScale = new Vector3(width * WorldScale / size.x, height * WorldScale / size.y, 1f);
            return renderer;
        }

        private const string BattleActorPrefab = Prefabs + "BattleActor.prefab";

        private static GameObject GetBattleActorPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BattleActorPrefab);
            if (prefab)
            {
                if (!prefab.GetComponent<BattlePreviewActor>())
                    throw new InvalidOperationException("The shared battle actor prefab requires a BattlePreviewActor.");
                return prefab;
            }

            var root = new GameObject("BattleActor", typeof(BattlePreviewActor));
            return SavePrefab(root, "BattleActor");
        }

        private static void ConfigureHeroActor(BattlePreviewActor actor, string id, SerializedProperty model)
        {
            var source = model.FindPropertyRelative("skeleton");
            var names = source.FindPropertyRelative("pageNames");
            var pages = source.FindPropertyRelative("pages");
            if (names.arraySize != 1 || pages.arraySize != 1)
                throw new InvalidOperationException("The authored battle actor requires one atlas page: " + id);

            var options = new SerializedObject(actor);
            options.FindProperty("heroId").stringValue = id;
            options.FindProperty("skeleton").objectReferenceValue = source.FindPropertyRelative("document").objectReferenceValue;
            options.FindProperty("atlas").objectReferenceValue = source.FindPropertyRelative("atlas").objectReferenceValue;
            options.FindProperty("page").objectReferenceValue = pages.GetArrayElementAtIndex(0).objectReferenceValue;
            options.FindProperty("pageName").stringValue = names.GetArrayElementAtIndex(0).stringValue;
            options.FindProperty("skin").stringValue = model.FindPropertyRelative("skin").stringValue;
            options.FindProperty("premultipliedAlpha").boolValue = source.FindPropertyRelative("premultipliedAlpha").boolValue;
            options.FindProperty("sourceScale").floatValue = source.FindPropertyRelative("scale").floatValue;
            options.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void PlaceHero(Transform slot, GameObject prefab, string id, string label,
            SerializedProperty model, bool enemy, int index)
        {
            var previous = slot.GetComponentInChildren<BattlePreviewActor>(true);
            var previousOptions = previous ? new SerializedObject(previous) : null;
            var layout = Slots[index];
            int order = previousOptions?.FindProperty("drawOrder").intValue ?? ActorDrawOrder(layout.Y);
            var actor = previous;
            if (!previous || PrefabUtility.GetCorrespondingObjectFromSource(previous.gameObject) != prefab)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, slot);
                actor = instance.GetComponent<BattlePreviewActor>();
                if (previous)
                {
                    instance.transform.localPosition = previous.transform.localPosition;
                    instance.transform.localRotation = previous.transform.localRotation;
                    instance.transform.SetSiblingIndex(previous.transform.GetSiblingIndex());
                }
                bool migratedSlime = previousOptions?.FindProperty("pageName").stringValue == "slime.sct";
                instance.transform.localScale = previous && !migratedSlime
                    ? previous.transform.localScale : Vector3.one * layout.Scale;
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
                if (previous)
                    UnityEngine.Object.DestroyImmediate(previous.gameObject);
            }

            actor.gameObject.name = $"{label} [{id}]";
            ConfigureHeroActor(actor, id, model);
            var options = new SerializedObject(actor);
            options.FindProperty("faceLeft").boolValue = enemy;
            options.FindProperty("drawOrder").intValue = order;
            options.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(actor.gameObject);
        }
    }
}
