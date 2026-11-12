using UnityEditor;
using UnityEngine;

namespace AstralOath.Client.BattleScene.Editor
{
    public static partial class BattleSceneBuilder
    {
        // Authoring coordinates use the source HUD's 1280×720 design frame.
        // These are editable preview positions, including the height of the unit's HUD marker.
        private static readonly (string Position, string Name, string Id, string Model, string Element,
            float X, float Y, float MarkerY, float Scale)[] Allies =
        {
            ("01 Front", "Ras", "c1001", "ras", "fire", 453, 227, 410, 0.60f),
            ("02 Lower", "Angelica", "c1062", "angelica", "ice", 341, 167, 351, 0.56f),
            ("03 Upper", "Mercedes", "c1005", "mercedes", "fire", 292, 297, 468, 0.55f),
            ("04 Rear", "Ras Reserve", "c1001", "ras", "fire", 166, 237, 399, 0.53f)
        };

        private static readonly (string Position, string Name, string Skin, float X, float Y, float MarkerY)[] Enemies =
        {
            ("01 Front", "Ice Slime", "ice", 827, 227, 337),
            ("02 Lower", "Fire Slime", "fire", 969, 167, 276),
            ("03 Upper", "Wind Slime", "wind", 969, 297, 407),
            ("04 Rear", "Dark Slime", "dark", 1110, 237, 348)
        };

        private static void CreateFormation()
        {
            var units = new GameObject("Units").transform;
            var allies = Child("Allies", units);
            var enemies = Child("Enemies", units);
            foreach (var unit in Allies)
            {
                var slot = Child(unit.Position, allies);
                slot.localPosition = World(unit.X, unit.Y);
                Hero(slot, unit.Name, unit.Id, unit.Model, ActorDrawOrder(unit.Y), unit.Scale);
            }
            foreach (var unit in Enemies)
            {
                var slot = Child(unit.Position, enemies);
                slot.localPosition = World(unit.X, unit.Y);
                Slime(slot, unit.Name, unit.Skin, ActorDrawOrder(unit.Y));
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

        private static void Hero(Transform parent, string label, string id, string model, int order, float scale)
        {
            string root = "Assets/Game/Modules/CharacterPresentation/Res/Characters/" + id;
            Actor(parent, label + " [" + id + "]", order, false, "default", scale, model,
                root + "/Animations/model/" + model + ".spine.json", root + "/Atlas/model/" + model + ".atlas.txt",
                root + "/Textures/model/" + model + ".png");
        }

        private static void Slime(Transform parent, string label, string skin, int order)
        {
            const string root = Module + "/Res/Units/Slime/slime";
            Actor(parent, label, order, true, skin, 0.66f, "slime", root + ".spine.json",
                root + ".atlas.txt", root + ".png");
        }

        private static void Actor(Transform parent, string label, int order, bool faceLeft,
            string skin, float scale, string model, string skeleton, string atlas, string texture)
        {
            PlaceSprite(label + " Shadow", parent, Asset<Sprite>(HudTextures + "UnitShadow.png"),
                640, 363, faceLeft ? 94 : 85, 23, -50);
            var actor = new GameObject(label, typeof(BattlePreviewActor));
            actor.transform.SetParent(parent, false);
            actor.transform.localScale = Vector3.one * scale;
            var options = new SerializedObject(actor.GetComponent<BattlePreviewActor>());
            options.FindProperty("skeleton").objectReferenceValue = Asset<TextAsset>(skeleton);
            options.FindProperty("atlas").objectReferenceValue = Asset<TextAsset>(atlas);
            options.FindProperty("page").objectReferenceValue = Asset<Texture2D>(texture);
            options.FindProperty("pageName").stringValue = model + ".sct";
            options.FindProperty("skin").stringValue = skin;
            options.FindProperty("faceLeft").boolValue = faceLeft;
            options.FindProperty("drawOrder").intValue = order;
            options.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
