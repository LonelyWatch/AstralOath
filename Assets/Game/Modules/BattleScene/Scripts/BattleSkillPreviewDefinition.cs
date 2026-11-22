using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene
{
    // Only the resources used by this single-actor, single-target presentation are bundled.
    public sealed class BattleSkillPreviewDefinition : ScriptableObject
    {
        public string HeroId, GraphId, CasterSkin = "default", TargetSkin = "ice";
        public BattleSkillPresentationAsset CasterModel, TargetModel;
        public TextAsset Graph, TimelineOverrides;
        public BattleSkillPresentationAsset[] SkillAssets;
        public GameObject Background;
        public Sprite Shadow;

        public BattleSkillPreviewSession Create(RawImage output) => new(this, output);
    }
}
