using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Skeletons;
using UnityEngine;

namespace AstralOath.Client.Home
{
    // Owned by one login. A cached source contains parsed data, never a live camera or actor.
    public sealed class HeroPortraitSources
    {
        private readonly Dictionary<HomeHeroArt.Hero, SkeletonPlayerSource> sources = new Dictionary<HomeHeroArt.Hero, SkeletonPlayerSource>();

        internal SkeletonPlayerSource Get(HomeHeroArt.Hero hero)
        {
            if (hero == null || !hero.skeleton || !hero.atlas || !hero.page) return null;
            if (!sources.TryGetValue(hero, out var source))
            {
                source = Create(hero);
                if (source != null) sources.Add(hero, source);
            }
            return source;
        }

        internal static SkeletonPlayerSource Create(HomeHeroArt.Hero hero)
        {
            string atlasText = hero.atlas.text;
            using var reader = new StringReader(atlasText);
            string page;
            do { page = reader.ReadLine()?.Trim().TrimStart('\uFEFF'); } while (page != null && page.Length == 0);
            if (page == null) return null;
            return new SkeletonPlayerSource(hero.skeleton.text, atlasText,
                new Dictionary<string, Texture2D>(StringComparer.Ordinal) { [page] = hero.page }, premultipliedAlpha: true);
        }

        public Task PrepareAsync(HomeHeroArt.Hero hero, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var source = Get(hero);
            return source == null ? Task.CompletedTask : new SkeletonPlayerFactory().PrepareAsync(source, token);
        }

        public void Clear() => sources.Clear();
    }
}
