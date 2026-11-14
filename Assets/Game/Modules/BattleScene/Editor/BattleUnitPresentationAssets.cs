using System;
using System.IO;
using System.Linq;
using UnityEditor;
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
            profile.HealthDivider = ImportSprite("game_hud_bar_split.png");
            profile.ResourceFrame = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "Res/Textures/Hud/game_hud_bar_bg.png");
            profile.FocusFill = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "Res/Textures/Hud/game_hud_bar_cp.png");
            profile.FightingSpiritFill = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "Res/Textures/Hud/game_hud_bar_bp.png");
            if (!profile.ResourceFrame || !profile.FocusFill || !profile.FightingSpiritFill)
                throw new InvalidOperationException("Missing native Battle HUD personal resource sprites.");
            UpdateStatusAndShield(profile);
            profile.DeathParticles = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "Res/Effects/Death/Particles/death_pati_02.particle.json");
            profile.DeathTextures = Directory.GetFiles(Root + "Res/Effects/Death/Particles", "*.png").OrderBy(x => x, StringComparer.Ordinal)
                .Select(x => AssetDatabase.LoadAssetAtPath<Texture2D>(x.Replace('\\','/'))).ToArray();
            if (!profile.DeathParticles || profile.DeathTextures.Any(x => !x)) throw new InvalidOperationException("Run Tools/BattleScene/ImportDeathEffects.py first.");
            EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
            return profile;
        }
        private static void UpdateStatusAndShield(BattleUnitPresentationProfile profile)
        {
            const string hud = Root + "Res/Textures/Hud/";
            const string status = hud + "Statuses/";
            const string shield = Root + "Res/Effects/Shield/stse_protect";
            profile.Shield = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "Res/Textures/Hud/game_hud_bar_sh.png");
            profile.StatusManifest = AssetDatabase.LoadAssetAtPath<TextAsset>(status + "source.json");
            profile.StatusGlyphs = Directory.GetFiles(status, "stic_*.png").OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => AssetDatabase.LoadAssetAtPath<Sprite>(path.Replace('\\', '/'))).ToArray();
            profile.StatusDigits = Enumerable.Range(0, 10)
                .Select(digit => AssetDatabase.LoadAssetAtPath<Sprite>(status + "itxt_num" + digit + ".png")).ToArray();
            profile.LevelDigits = Enumerable.Range(0, 10)
                .Select(digit => AssetDatabase.LoadAssetAtPath<Sprite>(hud + "itxt_num" + digit + "_b.png")).ToArray();
            profile.ShieldAura = LoadAura(shield, "stse_protect", out profile.ShieldAuraEffect);
            profile.InvincibleAura = LoadAura(Root + "Res/Effects/Invincible/stse_invincible", "stse_invincible",
                out profile.InvincibleAuraEffect);
            if (!profile.Shield || !profile.StatusManifest || profile.StatusGlyphs.Any(sprite => !sprite) ||
                profile.StatusDigits.Any(sprite => !sprite) || profile.LevelDigits.Any(sprite => !sprite))
                throw new InvalidOperationException("Run ImportStatusHud.py before updating the unit profile.");
            profile.ValidateStatusArt();
        }
        private static BattleSkillPresentationAsset LoadAura(string path, string stem, out TextAsset effect)
        {
            effect = AssetDatabase.LoadAssetAtPath<TextAsset>(path + ".cfx.json");
            var asset = new BattleSkillPresentationAsset
            {
                source = "effect/" + stem + ".scsp",
                document = AssetDatabase.LoadAssetAtPath<TextAsset>(path + ".json"),
                atlas = AssetDatabase.LoadAssetAtPath<TextAsset>(path + ".atlas.txt"),
                pageNames = new[] { stem + ".sct" },
                pages = new[] { AssetDatabase.LoadAssetAtPath<Texture2D>(path + ".png") },
                premultipliedAlpha = true,
                scale = .01f
            };
            if (!effect || !asset.document || !asset.atlas || !asset.pages[0])
                throw new InvalidOperationException("Missing native status aura: " + path);
            return asset;
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
        [MenuItem("Tools/战斗场景/更新血条、状态与死亡资源引用")]
        public static void UpgradeScene() => BattleSceneBuilder.MigrateActorStatusTemplates();
    }
}
