using System;
using AstralOath.Shared.Battle;
using UnityEngine;

namespace AstralOath.Client.Home
{
    public sealed class HomeHeroArt : ScriptableObject
    {
        public const string Address = "home/hero-art";

        [Serializable]
        public sealed class Hero
        {
            public string id, name, heroClass, elementId, zodiac;
            public BattleCatalogData.GrowthRow growth;
            public Sprite portrait, cardPortrait, illustration, element, cardElement, role, zodiacIcon;
            public TextAsset skeleton, atlas;
            public Texture2D page;
        }

        public Hero[] heroes = Array.Empty<Hero>();
        public Sprite star;
    }
}
