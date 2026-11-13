using System.Collections.Generic;
using UnityEngine;
namespace AstralOath.Client.BattleScene
{
    // Presentation policy and native resources, separate from authoritative health values.
    [CreateAssetMenu(menuName = "AstralOath/Battle Unit Presentation")]
    public sealed class BattleUnitPresentationProfile : ScriptableObject
    {
        public string StatusAnchor = "top";
        public Vector2 StatusOffset;
        [Min(1)] public int HealthSections = 10;
        public Sprite DamageTrail, HealthDivider, Shield;
        public Sprite ResourceFrame, FocusFill, FightingSpiritFill;
        public TextAsset StatusManifest;
        public Sprite[] StatusGlyphs, StatusDigits, LevelDigits;
        [Min(1)] public int StatusIconsPerRow = 5;
        public BattleSkillPresentationAsset ShieldAura;
        public TextAsset ShieldAuraEffect;
        [Min(.001f)] public float DamageSeconds = .16f, HealSeconds = .18f, TrailSeconds = .28f;
        [Min(0)] public float TrailDelaySeconds = .12f;
        public TextAsset DeathParticles;
        public Texture2D[] DeathTextures;
        [Min(.001f)] public float DeathDarkenSeconds = .2f, DeathFadeSeconds = .4f;
        internal BattleStatusArt CreateStatusArt() => new BattleStatusArt(StatusManifest, StatusGlyphs, StatusDigits);

        public void ValidateStatusArt() => CreateStatusArt().Validate();

        internal IReadOnlyDictionary<string, Texture2D> CreateDeathTextures()
        {
            var result = new Dictionary<string, Texture2D>(System.StringComparer.Ordinal);
            if (DeathTextures != null) foreach (var texture in DeathTextures) if (texture) result.Add(texture.name, texture);
            return result;
        }
    }
}
