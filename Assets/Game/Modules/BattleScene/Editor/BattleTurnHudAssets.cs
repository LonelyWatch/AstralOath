using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Client.BattleScene.Editor
{
    internal static class BattleTurnHudAssets
    {
        private const string Folder = "Assets/Game/Modules/BattleScene/Res/Textures/Hud/Readiness";

        internal static void Import()
        {
            AssetDatabase.Refresh();
            foreach (string file in Directory.GetFiles(Folder, "*.png"))
            {
                string path = file.Replace('\\', '/');
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                if (importer.textureType == TextureImporterType.Sprite && importer.spriteImportMode == SpriteImportMode.Single &&
                    importer.npotScale == TextureImporterNPOTScale.None && !importer.mipmapEnabled)
                    continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
        }

        internal static IEnumerable<Sprite> Sprites()
        {
            foreach (string file in Directory.GetFiles(Folder, "*.png"))
                yield return AssetDatabase.LoadAssetAtPath<Sprite>(file.Replace('\\', '/'));
        }
    }
}
