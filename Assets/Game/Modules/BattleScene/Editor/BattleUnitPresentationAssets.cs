using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace AstralOath.Client.BattleScene.Editor
{
    public static class BattleUnitPresentationAssets
    {
        const string Root = "Assets/Game/Modules/BattleScene/";
        internal static BattleUnitPresentationProfile Load()
        {
            string directory = Root + "Res/Configs";
            Directory.CreateDirectory(directory); AssetDatabase.Refresh();
            var profile = AssetDatabase.LoadAssetAtPath<BattleUnitPresentationProfile>(directory + "/UnitPresentation.asset");
            if (!profile) { profile = ScriptableObject.CreateInstance<BattleUnitPresentationProfile>(); AssetDatabase.CreateAsset(profile, directory + "/UnitPresentation.asset"); }
            profile.DamageTrail = ImportSprite("game_hud_bar_dhp.png");
            profile.HealthDivider = ImportSprite("game_hud_bar_split.png");
            profile.DeathParticles = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "Res/Effects/Death/Particles/death_pati_02.particle.json");
            profile.DeathTextures = Directory.GetFiles(Root + "Res/Effects/Death/Particles", "*.png").OrderBy(x => x, StringComparer.Ordinal)
                .Select(x => AssetDatabase.LoadAssetAtPath<Texture2D>(x.Replace('\\','/'))).ToArray();
            if (!profile.DeathParticles || profile.DeathTextures.Any(x => !x)) throw new InvalidOperationException("Run Tools/BattleScene/ImportDeathEffects.py first.");
            EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
            return profile;
        }
        static Sprite ImportSprite(string name)
        {
            string path = Root + "Res/Textures/Hud/" + name;
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false; importer.alphaIsTransparency = true; importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        [MenuItem("Tools/战斗场景/更新血条与死亡资源引用")]
        public static void UpgradeScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(BattleSceneBuilder.ScenePath);
            var host = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<BattleHudPreview>()).Single();
            var serialized = new SerializedObject(host);
            serialized.FindProperty("unitPresentation").objectReferenceValue = Load();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Debug.Log("ASTRAL_UNIT_PRESENTATION_UPGRADED");
        }
    }
}
