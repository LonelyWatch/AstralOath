using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Client.BattleScene.Editor
{
    internal static class BattleDamageFeedbackAssets
    {
        private const string Folder = "Assets/Game/Modules/BattleScene/Res/Textures/Hud/Damage/";

        internal static IEnumerable<Sprite> Sprites()
        {
            for (int digit = 0; digit < 10; digit++)
                yield return SpriteAt("game_eff_dmg_p" + digit.ToString(CultureInfo.InvariantCulture));
            yield return SpriteAt("game_eff_dmg_hits");
            yield return SpriteAt("ef_blood03");
        }

        private static Sprite SpriteAt(string name)
        {
            string path = Folder + name + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single ||
                importer.npotScale != TextureImporterNPOTScale.None || importer.mipmapEnabled || !importer.alphaIsTransparency)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
