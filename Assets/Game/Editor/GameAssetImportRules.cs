using System;
using UnityEditor;

namespace AstralOath.Editor
{
    public sealed class GameAssetImportRules : AssetPostprocessor
    {
        private bool InGame => assetPath.StartsWith("Assets/Game/", StringComparison.Ordinal);

        private void OnPreprocessTexture()
        {
            if (!InGame)
                return;
            var importer = (TextureImporter)assetImporter;
            if (importer.textureType == TextureImporterType.Sprite)
                importer.mipmapEnabled = false;

            // Automatic Android compression follows the ASTC/ETC2 player subtarget.
            var mobile = importer.GetPlatformTextureSettings("Android");
            if (!mobile.overridden)
            {
                mobile.overridden = true;
                mobile.maxTextureSize = 2048;
                mobile.format = TextureImporterFormat.Automatic;
                importer.SetPlatformTextureSettings(mobile);
            }
        }

        private void OnPreprocessAudio()
        {
            if (!InGame)
                return;
            var importer = (AudioImporter)assetImporter;
            var defaults = importer.defaultSampleSettings;
            defaults.compressionFormat = UnityEngine.AudioCompressionFormat.Vorbis;
            defaults.loadType = assetPath.Contains("/Voices/")
                ? UnityEngine.AudioClipLoadType.CompressedInMemory
                : UnityEngine.AudioClipLoadType.DecompressOnLoad;
            importer.defaultSampleSettings = defaults;
        }
    }
}