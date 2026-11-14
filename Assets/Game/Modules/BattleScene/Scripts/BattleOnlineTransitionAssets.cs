using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Scripting;

namespace AstralOath.Client.BattleScene
{
    // Kept in Resources because the gate closes before the battle scene is loaded.
    public sealed class BattleOnlineTransitionAssets : ScriptableObject
    {
        [SerializeField] internal BattleHudPreview.SpineEffect[] skeletons;
        [SerializeField] internal AudioClip doorClose, doorOpen;
        [SerializeField] internal TextAsset closeCfx, openCfx, ignitionCfx;
        [SerializeField] internal TextAsset[] particles;
        [SerializeField] internal Texture2D[] textures;
        [SerializeField] internal Sprite arenaBackdrop;
        [SerializeField] internal TextAsset zhHans;

        [Serializable, Preserve]
        internal sealed class Copy
        {
            public string arenaTitle, arenaMode, reconnecting, connectionEnded;
            public string yourTurn, opponentTurn, localPlayer, opponentPlayer;

            internal static Copy Load(TextAsset text)
            {
                var result = text ? JsonUtility.FromJson<Copy>(text.text) : null;
                if (result == null || string.IsNullOrWhiteSpace(result.arenaTitle) ||
                    string.IsNullOrWhiteSpace(result.reconnecting) || string.IsNullOrWhiteSpace(result.connectionEnded) ||
                    string.IsNullOrWhiteSpace(result.arenaMode) || string.IsNullOrWhiteSpace(result.yourTurn) ||
                    string.IsNullOrWhiteSpace(result.opponentTurn) || string.IsNullOrWhiteSpace(result.localPlayer) ||
                    string.IsNullOrWhiteSpace(result.opponentPlayer))
                    throw new InvalidOperationException("Online battle localization resources are incomplete.");
                return result;
            }
        }

        internal static BattleOnlineTransitionAssets Load()
        {
            var asset = Resources.Load<BattleOnlineTransitionAssets>("AstralOathOnlineTransition");
            if (!asset || asset.skeletons == null || asset.skeletons.Length == 0 ||
                asset.skeletons.Any(item => item == null || !item.skeleton || !item.atlas || !item.page) ||
                asset.skeletons.Select(item => item.page.name).Distinct(StringComparer.Ordinal).Count() != asset.skeletons.Length ||
                !asset.closeCfx || !asset.openCfx || !asset.ignitionCfx || !asset.doorClose || !asset.doorOpen ||
                !asset.arenaBackdrop || !asset.zhHans ||
                asset.particles == null || asset.particles.Any(item => !item) ||
                asset.textures == null || asset.textures.Any(item => !item))
                throw new InvalidOperationException("Online battle transition resources are incomplete.");
            return asset;
        }
    }
}
