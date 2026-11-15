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
            public int Level, Stars, BaseStars, Attack, Defense, Health, Speed;
            public string[] FilterEffects = Array.Empty<string>();
            public Sprite Face, Card, Illustration, ElementIcon, ClassIcon;
            public TextAsset PortraitSkeleton, PortraitAtlas, ModelSkeleton, ModelAtlas;
            public Texture2D PortraitPage, ModelPage;
            private SkeletonPlayerSource portraitSource, modelSource;
            internal SkeletonPlayerSource MakePortrait() =>
                portraitSource ??= ArenaDraftPortrait.CreateSource(PortraitSkeleton, PortraitAtlas, PortraitPage);
            internal SkeletonPlayerSource MakeModel() =>
                modelSource ??= ArenaDraftPortrait.CreateSource(ModelSkeleton, ModelAtlas, ModelPage);
        }

        [Serializable]
        public sealed class NativeEffect
        {
            public TextAsset Skeleton, Atlas, Particles;
            public Texture2D Page;
            private SkeletonPlayerSource source;
            internal SkeletonPlayerSource MakeSource() =>
                source ??= ArenaDraftPortrait.CreateSource(Skeleton, Atlas, Page);
        }

        public NativeEffect[] BluePick = Array.Empty<NativeEffect>(), RedPick = Array.Empty<NativeEffect>();
        public NativeEffect FinalBan;
        public Texture2D[] ParticleTextures = Array.Empty<Texture2D>();
        public Hero[] Heroes = Array.Empty<Hero>();
        public Sprite[] ScoreDigits = Array.Empty<Sprite>();
        public Sprite[] LevelDigits = Array.Empty<Sprite>();
        public Sprite ActionGreen, ActionGreenPressed, ActionRed, ActionRedPressed;
    }
}
