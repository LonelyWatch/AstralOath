using System;
using AstralOath.Shared.Battle;
using UnityEngine;

namespace AstralOath.Client.Home
{
    public sealed class HomeHeroArt : ScriptableObject
    {
        public const string Address = "home/hero-art";

        [Serializable]
        public sealed class Skill
        {
            public string name, description, soul, cooldown, burnTitle, burnCost, burnDescription, upgrades;
            public string effectName, effectDescription;
            public Sprite icon, effectIcon;
            public bool debuff;
        }

        [Serializable]
        public sealed class Hero
        {
            public string id, name, heroClass, elementId, zodiac;
            public BattleCatalogData.GrowthRow growth;
            public Skill[] skills;
            public Imprint imprint;
            public Sprite imprintFormation;
            public Sprite portrait, cardPortrait, illustration, element, cardElement, role, zodiacIcon;
            public TextAsset skeleton, atlas;
            public Texture2D page;
            public TextAsset modelSkeleton, modelAtlas;
            public Texture2D modelPage;
        }

        [Serializable]
        public sealed class Imprint
        {
            public string id, direction, stat, release, selfStat, focus;
        }

        public Hero[] heroes = Array.Empty<Hero>();
        public Sprite star;
    }
}
