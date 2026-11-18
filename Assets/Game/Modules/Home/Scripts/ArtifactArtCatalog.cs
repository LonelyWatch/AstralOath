using System;
using System.Collections.Generic;
using AstralOath.Shared.Battle;
using UnityEngine;

namespace AstralOath.Client.Home
{
    public sealed class ArtifactArtCatalog : ScriptableObject
    {
        public const string Address = "home/artifact-art";

        [SerializeField] private TextAsset source;
        [SerializeField] private Sprite[] portraits = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] cards = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] combatIcons = Array.Empty<Sprite>();

        [NonSerialized] private ArtifactCatalog catalog;
        [NonSerialized] private Dictionary<string, Entry> entries;

        public sealed class Entry
        {
            public readonly ArtifactTemplate template;
            public readonly Sprite portrait;
            public readonly Sprite card;
            public readonly Sprite combatIcon;
            internal Entry(ArtifactTemplate template, Sprite portrait, Sprite card, Sprite combatIcon)
            {
                this.template = template;
                this.portrait = portrait;
                this.card = card;
                this.combatIcon = combatIcon;
            }
        }

        public ArtifactCatalog Catalog
        {
            get { EnsureLoaded(); return catalog; }
        }

        public Entry Find(string id)
        {
            EnsureLoaded();
            return !string.IsNullOrEmpty(id) && entries.TryGetValue(id, out var entry) ? entry : null;
        }

        public void Configure(TextAsset data, Sprite[] art, Sprite[] fullCards, Sprite[] icons)
        {
            source = data;
            portraits = art;
            cards = fullCards;
            combatIcons = icons;
            entries = null;
            catalog = null;
            EnsureLoaded();
        }

        private void EnsureLoaded()
        {
            if (entries != null) return;
            if (!source) throw new InvalidOperationException("Artifact catalog source is missing.");
            var data = JsonUtility.FromJson<ArtifactCatalogData>(source.text);
            if (data?.templates == null || portraits.Length != data.templates.Length ||
                cards.Length != data.templates.Length || combatIcons.Length != data.templates.Length)
                throw new InvalidOperationException("Artifact catalog and art roster disagree.");
            var parsed = new ArtifactCatalog(data);
            var indexed = new Dictionary<string, Entry>(StringComparer.Ordinal);
            for (int index = 0; index < data.templates.Length; ++index)
            {
                var row = data.templates[index];
                if (!portraits[index] || portraits[index].name != row.id || !cards[index] || cards[index].name != row.id ||
                    !combatIcons[index] || combatIcons[index].name != row.id)
                    throw new InvalidOperationException("Missing original artwork for artifact " + row.id + ".");
                indexed.Add(row.id, new Entry(parsed.Get(row.id), portraits[index], cards[index], combatIcons[index]));
            }
            catalog = parsed;
            entries = indexed;
        }
    }
}
