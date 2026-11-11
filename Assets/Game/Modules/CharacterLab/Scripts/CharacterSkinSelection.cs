using System;
using System.IO;
using AstralOath.Client.Skeletons;
namespace AstralOath.Client.CharacterLab
{
    public static class CharacterSkinSelection
    {
        public static int Apply(CharacterRecord character,bool portrait,ISkeletonPlayer player)
        {
            if(character==null)throw new ArgumentNullException(nameof(character));
            if(player==null)throw new ArgumentNullException(nameof(player));
            string field=portrait?nameof(character.portraitSkin):nameof(character.battleSkin);
            string skin=portrait?character.portraitSkin:character.battleSkin;
            if(string.IsNullOrWhiteSpace(skin))
                throw new InvalidDataException(character.id+" is missing explicit "+field+" configuration.");
            for(int i=0;i<player.Skins.Count;i++)
                if(string.Equals(player.Skins[i],skin,StringComparison.Ordinal))
                {player.SetSkin(skin);return i;}
            throw new InvalidDataException(character.id+" "+field+" '"+skin+"' is absent from this skeleton.");
        }
    }
}
