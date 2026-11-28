using System;
using AstralOath.Client.Common;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.UI;

namespace AstralOath.Client.Arena
{
    [Serializable, Preserve] public sealed class TowerPageDto
    {
        public int highestCleared, currentStage;
        public string rankId;
        public TowerStageDto[] stages;
    }
    [Serializable, Preserve] public sealed class TowerStageDto
    {
        public int stage, level, reward;
        public string[] heroIds;
        public bool cleared, unlocked;
    }
    [Serializable, Preserve] public sealed class TowerTeamDto
    {
        public int stage;
        public TowerHeroDto[] heroes;
        public string[] defenderHeroIds;
    }
    [Serializable, Preserve] public sealed class TowerHeroDto
    {
        public string instanceId, heroId, displayName;
        public int level, stars, slot;
    }

    public sealed class ArenaTower : UIBaseView
    {
        public const string Address = "arena/tower";
        public override string PrefabPath => Address;
        internal GameButton Button(string key) => Bindings.Require<GameButton>(key);
        internal GameText Text(string key) => Bindings.Require<GameText>(key);
        internal Image RankIcon => Bindings.Require<Image>("rankIcon");
        internal Sprite RankArt(string rank) => Bindings.Require<Image>("rank." + rank).sprite;
        internal Sprite Digit(int digit) => Bindings.Require<Image>("digit." + digit).sprite;
        internal Sprite Element(string id) => Bindings.Require<Image>("element." + id).sprite;
        internal ScrollRect List => Bindings.Require<ScrollRect>("list");
        internal GameObject Row => Bindings.Require<GameObject>("row");
        internal BindingMap Overlay => Bindings.Require<GameObject>("overlay").GetComponent<BindingMap>();
        public override void Validate()
        {
            foreach (string key in new[] { "back", "shop", "simulation", "record", "challenge" }) _ = Button(key);
            _ = Text("rank"); _ = Text("progress"); _ = Text("status"); _ = RankIcon; _ = List; _ = Row;
            if (!Overlay) throw new InvalidOperationException("Tower formation overlay requires a BindingMap.");
        }
    }
}
