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

        private static bool PreserveDesktopUiColor(string path)
        {
            if (!path.EndsWith(".png", StringComparison.Ordinal)) return false;
            int slash = path.LastIndexOf('/');
            string directory = path.Substring(0, slash);
            string name = path.Substring(slash + 1);
            switch (directory)
            {
                case "Assets/Game/Modules/BattleScene/Res/Textures/Hud/Faces":
                    return name.StartsWith("c", StringComparison.Ordinal) && name.EndsWith("_s.png", StringComparison.Ordinal);
                case "Assets/Game/Modules/Home/Res/Textures":
                    return name == "lobby_bg3.png" || name == "c5009_s.png" ||
                        name.StartsWith("main_hud_btn_", StringComparison.Ordinal) ||
                        name.StartsWith("renew_cm_hud_", StringComparison.Ordinal) ||
                        name.StartsWith("renew_icon_quick_", StringComparison.Ordinal) ||
                        name.StartsWith("cm_hud_top_btn_", StringComparison.Ordinal) ||
                        name.StartsWith("icon_border_", StringComparison.Ordinal);
                case "Assets/Game/Modules/Home/Res/Textures/Inventory":
                    return name.StartsWith("base_", StringComparison.Ordinal) ||
                        name.StartsWith("icon_menu_", StringComparison.Ordinal) ||
                        name.StartsWith("renew_icon_quick_", StringComparison.Ordinal);
                case "Assets/Game/Modules/Arena/Res/Textures":
                    return name == "room_courtyard.png" || name == "arena_avatar_frame.png" || name == "c1001_avatar_room.png" ||
                        name.StartsWith("icon_menu_", StringComparison.Ordinal) ||
                        name.StartsWith("icon_pvp_sa_", StringComparison.Ordinal) ||
                        name.StartsWith("battle_pvp_icon_", StringComparison.Ordinal);
                default:
                    return false;
            }
        }

        private static void GetPlatformEncoding(string path, string platform, out TextureImporterFormat format,
            out TextureImporterCompression compression)
        {
            bool preserve = platform == "Standalone" && PreserveDesktopUiColor(path);
            format = preserve ? TextureImporterFormat.RGBA32 : TextureImporterFormat.Automatic;
            compression = preserve ? TextureImporterCompression.Uncompressed : TextureImporterCompression.Compressed;
        }

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
                GetPlatformEncoding(assetPath, platform, out var format, out var compression);
                settings.format = format;
                settings.textureCompression = compression;
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
                    GetPlatformEncoding(path, platform, out var format, out var compression);
                    if (!settings.overridden || settings.maxTextureSize != TextureMaxSize ||
                        settings.format != format || settings.textureCompression != compression)
                        throw new BuildFailedException($"Reimport {path} to apply Foundation {platform} texture defaults.");
                }
            }
        }
    }
}
