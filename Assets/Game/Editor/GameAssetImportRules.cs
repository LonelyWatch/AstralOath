using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace AstralOath.Editor
{
    public sealed class GameAssetImportRules : AssetPostprocessor
    {
        private const string GameRoot = "Assets/Game";
        private const string OverrideMarker = "AstralOath.ImportOverride";
        private const int TextureMaxSize = 2048;
        private static readonly string[] Platforms = { "Standalone", "Android" };

        private bool ApplyDefaults => assetPath.StartsWith(GameRoot + "/", StringComparison.Ordinal) &&
            !HasOverride(assetImporter);

        private static bool HasOverride(AssetImporter importer) => importer.userData == OverrideMarker;

        private void OnPreprocessTexture()
        {
            if (!ApplyDefaults)
                return;
            var importer = (TextureImporter)assetImporter;
            importer.isReadable = false;
            if (importer.textureType == TextureImporterType.Sprite)
                importer.mipmapEnabled = false;

            foreach (string platform in Platforms)
            {
                var settings = importer.GetPlatformTextureSettings(platform);
                settings.overridden = true;
                settings.maxTextureSize = TextureMaxSize;
                // Unity chooses the Windows BC format or Android build subtarget format.
                settings.format = TextureImporterFormat.Automatic;
                settings.textureCompression = TextureImporterCompression.Compressed;
                importer.SetPlatformTextureSettings(settings);
            }
        }

        private void OnPreprocessAudio()
        {
            if (!ApplyDefaults)
                return;
            var importer = (AudioImporter)assetImporter;
            var defaults = importer.defaultSampleSettings;
            defaults.compressionFormat = AudioCompressionFormat.Vorbis;
            defaults.loadType = assetPath.Contains("/Music/") || assetPath.Contains("/Long/")
                ? AudioClipLoadType.Streaming
                : assetPath.Contains("/Voices/")
                    ? AudioClipLoadType.CompressedInMemory
                    : AudioClipLoadType.DecompressOnLoad;
            importer.defaultSampleSettings = defaults;
            foreach (string platform in Platforms)
                if (importer.ContainsSampleSettingsOverride(platform))
                    importer.ClearSampleSettingOverride(platform);
        }

        internal static void ValidateTextureDefaults()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { GameRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer || HasOverride(importer))
                    continue;
                if (importer.isReadable || (importer.textureType == TextureImporterType.Sprite && importer.mipmapEnabled))
                    throw new BuildFailedException($"Reimport {path} to apply Foundation texture readability and mipmap defaults.");
                foreach (string platform in Platforms)
                {
                    var settings = importer.GetPlatformTextureSettings(platform);
                    if (!settings.overridden || settings.maxTextureSize != TextureMaxSize ||
                        settings.format != TextureImporterFormat.Automatic ||
                        settings.textureCompression != TextureImporterCompression.Compressed)
                        throw new BuildFailedException($"Reimport {path} to apply Foundation {platform} texture defaults.");
                }
            }
        }
    }
}
