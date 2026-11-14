using System;
using AstralOath.Client.Skeletons;
using UnityEngine;

namespace AstralOath.Client.Arena
{
    public sealed class ArenaDraftArt : ScriptableObject
    {
        public const string Address = "arena/draft-art";

        [Serializable]
        public sealed class Hero
        {
            public string Id, Name, Element, Class;
            public int Level, Stars;
            public Sprite Face, Card, Illustration, ElementIcon, ClassIcon;
            public TextAsset PortraitSkeleton, PortraitAtlas, ModelSkeleton, ModelAtlas;
            public Texture2D PortraitPage, ModelPage;
            private SkeletonPlayerSource portraitSource, modelSource;
            internal SkeletonPlayerSource MakePortrait() =>
                portraitSource ??= ArenaDraftPortrait.CreateSource(PortraitSkeleton, PortraitAtlas, PortraitPage);
            internal SkeletonPlayerSource MakeModel() =>
                modelSource ??= ArenaDraftPortrait.CreateSource(ModelSkeleton, ModelAtlas, ModelPage);
        }

        public Hero[] Heroes = Array.Empty<Hero>();
        public Sprite[] ScoreDigits = Array.Empty<Sprite>();
    }
}
