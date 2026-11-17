using System;
using UnityEngine;

namespace AstralOath.Client.Home
{
    public sealed class HomeHeroArt : ScriptableObject
    {
        public const string Address = "home/hero-art";

        [Serializable]
        public sealed class Hero
        {
            public string id, name, heroClass;
            public Sprite portrait, illustration, element, role;
            public TextAsset skeleton, atlas;
            public Texture2D page;
        }

        public Hero[] heroes = Array.Empty<Hero>();
        public Sprite star;
    }
}
