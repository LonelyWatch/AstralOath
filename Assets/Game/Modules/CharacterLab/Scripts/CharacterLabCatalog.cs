using System;
using UnityEngine.Scripting;
namespace AstralOath.Client.CharacterLab
{
    [Serializable, Preserve] public sealed class CharacterLabCatalog { public int schemaVersion = 1; public CharacterRecord[] characters; public string[] sharedBanks; }
    [Serializable, Preserve] public sealed class CharacterRecord
    {
        public string id, name, displayNameKey, battleSkeletonAddress, portraitSkeletonAddress, voiceBankAddress, timelineAddress;
        public string battleSkin, portraitSkin;
        public CharacterVoiceRecord[] voices;
        public CharacterSkillRecord[] skills;
    }
    [Serializable, Preserve] public sealed class CharacterVoiceRecord { public string id, eventPath, audioTableKey, displayNameKey; }
    [Serializable, Preserve] public sealed class CharacterSkillRecord { public string id, displayNameKey, graphAddress, eventPath, voiceEventPath; }
    [Serializable, Preserve] public sealed class SkeletonDescriptor
    {
        public string version, jsonAddress, atlasAddress;
        public SkeletonTexturePage[] pages;
        public bool premultipliedAlpha;
        public float scale = .01f;
    }
    [Serializable, Preserve] public sealed class SkeletonTexturePage { public string name, address; }
}
