using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Client.BattleScene.Editor
{
    public static class SummonBattlePreviewBuilder
    {
        public const string AssetPath =
            "Assets/Game/Modules/Summon/Res/Presentation/IseriaSkillLoop/BattleSkillPreviewDefinition.asset";

        [MenuItem("Tools/召唤/更新伊赛丽亚技能预览资源")]
        public static void Generate()
        {
            const string heroId = "c1024";
            const string graphId = "c_iseria_skill_03";
            const string slime = "Assets/Game/Modules/BattleScene/Res/Units/Slime/slime";
            var catalog = JObject.Parse(Load<TextAsset>("Assets/Game/Modules/CharacterLab/Configs/catalog.json").text);
            var character = catalog["characters"].Single(row => (string)row["id"] == heroId);
            var skill = character["skills"].Single(row => (string)row["id"] == graphId);
            var addresses = BattleSkillPresentationAssets.AddressMap();
            var definition = ScriptableObject.CreateInstance<BattleSkillPreviewDefinition>();
            try
            {
                definition.name = nameof(BattleSkillPreviewDefinition);
                definition.HeroId = heroId;
                definition.GraphId = graphId;
                definition.Graph = BattleSkillPresentationAssets.Load<TextAsset>((string)skill["graphAddress"], addresses);
                definition.TimelineOverrides = Load<TextAsset>(SkillTimelineOverrides.AssetPath);
                definition.Background = Load<GameObject>("Assets/Game/Modules/BattleScene/Res/Prefab/Forest2Stage.prefab");
                definition.Shadow = Load<Sprite>("Assets/Game/Modules/BattleScene/Res/Textures/Hud/UnitShadow.png");
                definition.CasterSkin = (string)character["battleSkin"];
                definition.CasterModel = new BattleSkillPresentationAsset();
                definition.TargetSkin = "ice";
                definition.TargetModel = new BattleSkillPresentationAsset
                {
                    source = "preview.slime",
                    document = Load<TextAsset>(slime + ".spine.json"),
                    atlas = Load<TextAsset>(slime + ".atlas.txt"),
                    pages = new[] { Load<Texture2D>(slime + ".png") },
                    pageNames = new[] { "slime.sct" },
                    scale = .01f,
                    premultipliedAlpha = true
                };
                using (var serialized = new SerializedObject(definition))
                {
                    BattleSkillPresentationAssets.SetSkeleton(serialized.FindProperty(nameof(definition.CasterModel)),
                        (string)character["battleSkeletonAddress"], addresses);
                    BattleSkillPresentationAssets.Collect(serialized.FindProperty(nameof(definition.SkillAssets)),
                        new[] { definition.Graph });
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                var existing = AssetDatabase.LoadAssetAtPath<BattleSkillPreviewDefinition>(AssetPath);
                if (existing)
                {
                    EditorUtility.CopySerialized(definition, existing);
                    EditorUtility.SetDirty(existing);
                }
                else
                    AssetDatabase.CreateAsset(definition, AssetPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"ASTRAL_SUMMON_BATTLE_PREVIEW_READY {heroId} {graphId} {definition.SkillAssets.Length} assets");
            }
            finally
            {
                if (!EditorUtility.IsPersistent(definition))
                    UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        private static T Load<T>(string path) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new FileNotFoundException("Missing summon preview asset.", path);
    }
}
