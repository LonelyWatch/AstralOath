using System;
using System.Linq;
using System.Threading.Tasks;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.Scripting;

namespace AstralOath.Client.BattleScene
{
    // Loaded from the active Foundation bundle after its hot-update scripts have been loaded.
    public sealed class BattleOnlineTransitionAssets : ScriptableObject
    {
        public const string Address = "battle/online-transition";
        [SerializeField] internal BattleHudPreview.SpineEffect[] skeletons;
        [SerializeField] internal AudioClip doorClose, doorOpen, soulBurnRelease;
        [SerializeField] internal TextAsset closeCfx, openCfx, ignitionCfx;
        [SerializeField] internal TextAsset[] particles;
        [SerializeField] internal Texture2D[] textures;
        [SerializeField] internal Sprite arenaBackdrop;
        [SerializeField] internal TextAsset zhHans;
        [SerializeField] internal Sprite[] hudSprites;
        [SerializeField] internal TextAsset hudScoreLayout;
        [SerializeField] internal BattleHudPreview.SpineEffect arrivalWarning, arrivalDust;
        [SerializeField] internal GameObject resultPrefab;
        [SerializeField] internal BattleHudPreview.SpineEffect resultEmblem;

        [Serializable, Preserve]
        internal sealed class Copy
        {
            public string arenaTitle, arenaMode, reconnecting, connectionEnded;
            public string yourTurn, opponentTurn, localPlayer, opponentPlayer;
            public string clockUnavailable;
            public string invalidCommand, skillOnCooldown, presentationPending;
            public string resultDetails, resultConfirm, resultClose, resultDraw, resultTurn, resultHealth;

            internal static Copy Load(TextAsset text)
            {
                var result = text ? JsonUtility.FromJson<Copy>(text.text) : null;
                if (result == null || string.IsNullOrWhiteSpace(result.arenaTitle) ||
                    string.IsNullOrWhiteSpace(result.reconnecting) || string.IsNullOrWhiteSpace(result.connectionEnded) ||
                    string.IsNullOrWhiteSpace(result.arenaMode) || string.IsNullOrWhiteSpace(result.yourTurn) ||
                    string.IsNullOrWhiteSpace(result.opponentTurn) || string.IsNullOrWhiteSpace(result.localPlayer) ||
                    string.IsNullOrWhiteSpace(result.opponentPlayer) || string.IsNullOrWhiteSpace(result.clockUnavailable))
                    throw new InvalidOperationException("Online battle localization resources are incomplete.");
                return result;
            }
        }

        internal static async Task<BattleOnlineTransitionAssets> LoadAsync(IAssetService assets, BindingScope scope)
        {
            var asset = await assets.LoadAsync<BattleOnlineTransitionAssets>(Address, scope);
            if (!asset || asset.skeletons == null || asset.skeletons.Length == 0 ||
                asset.skeletons.Any(IsIncomplete) ||
                asset.skeletons.Select(item => item.page.name).Distinct(StringComparer.Ordinal).Count() != asset.skeletons.Length ||
                !asset.closeCfx || !asset.openCfx || !asset.ignitionCfx || !asset.doorClose || !asset.doorOpen || !asset.soulBurnRelease ||
                !asset.arenaBackdrop || !asset.zhHans ||
                !asset.hudScoreLayout || asset.hudSprites == null || asset.hudSprites.Length == 0 || asset.hudSprites.Any(item => !item) ||
                IsIncomplete(asset.arrivalWarning) || IsIncomplete(asset.arrivalDust) ||
                !asset.resultPrefab || IsIncomplete(asset.resultEmblem) ||
                asset.particles == null || asset.particles.Any(item => !item) ||
                asset.textures == null || asset.textures.Any(item => !item))
                throw new InvalidOperationException("Online battle transition resources are incomplete.");
            return asset;
        }

        private static bool IsIncomplete(BattleHudPreview.SpineEffect effect)
            => effect == null || !effect.skeleton || !effect.atlas || !effect.page;
    }
}
