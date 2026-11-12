using UnityEditor;
using UnityEngine;

namespace AstralOath.Client.BattleScene.Editor
{
    public static partial class BattleSceneBuilder
    {
        private static Vector3 World(float x, float y) => new Vector3((x - 640) * WorldScale, (y - 360) * WorldScale);

        private static GameObject CreateForest(string folder, string prefix)
        {
            var root = new GameObject(folder + "Stage");
            Sprite Layer(int id) => Asset<Sprite>(BackgroundTextures + folder + "/" + prefix + "_" + id + ".png");
            var sky = Layer(11);
            PlaceSprite("Sky", root.transform, sky, 640, 466, 1282, 512, -90);
            var shrubs = Layer(4);
            PlaceSprite("Distant Shrubs", root.transform, shrubs, 640, 248, 1282, 291, -85);
            PlaceSprite("Ground", root.transform, Layer(0), 640, 168, 1282, 340, -70);
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

        private static void Hero(Transform parent, string label, string id, string model, float x, float y, int order, float scale)
        {
            string root = "Assets/Game/Modules/CharacterPresentation/Res/Characters/" + id;
            Actor(parent, label + " [" + id + "]", x, y, order, false, "default", scale, model,
                root + "/Animations/model/" + model + ".spine.json", root + "/Atlas/model/" + model + ".atlas.txt",
                root + "/Textures/model/" + model + ".png");
        }

        private static void Slime(Transform parent, string label, string skin, float x, float y, int order)
        {
            const string root = Module + "/Res/Units/Slime/slime";
            Actor(parent, label, x, y, order, true, skin, 0.72f, "slime", root + ".spine.json",
                root + ".atlas.txt", root + ".png");
        }

        private static void Actor(Transform parent, string label, float x, float y, int order, bool faceLeft,
            string skin, float scale, string model, string skeleton, string atlas, string texture)
        {
            PlaceSprite(label + " Shadow", parent, Asset<Sprite>(HudTextures + "UnitShadow.png"),
                x, y + 3, faceLeft ? 94 : 85, 23, -50);
            var actor = new GameObject(label, typeof(BattlePreviewActor));
            actor.transform.SetParent(parent, false);
            actor.transform.localPosition = World(x, y);
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
