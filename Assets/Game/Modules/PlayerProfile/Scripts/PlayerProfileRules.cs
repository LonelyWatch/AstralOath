using System;
using System.Text;
namespace AstralOath.Client.PlayerProfile
{
    public static class PlayerProfileRules
    {
        public static string NormalizeNickname(string value)
        { try { return (value??"").Normalize(NormalizationForm.FormC).Trim(); } catch(ArgumentException) { return value??""; } }
        public static bool ValidNickname(string value)
        {
            value=NormalizeNickname(value);int count=0;bool chinese=false,englishOnly=true;
            for(int i=0;i<value.Length;i++)
            {
                int code=value[i];
                if(char.IsHighSurrogate(value[i])) { if(i+1>=value.Length||!char.IsLowSurrogate(value[i+1]))return false;code=char.ConvertToUtf32(value[i],value[++i]); }
                else if(char.IsLowSurrogate(value[i]))return false;
                count++;bool letter=(code>='A'&&code<='Z')||(code>='a'&&code<='z');
                bool digit=code>='0'&&code<='9';bool han=(code>=0x3400&&code<=0x4dbf)||(code>=0x4e00&&code<=0x9fff)||(code>=0xf900&&code<=0xfaff)||(code>=0x20000&&code<=0x2fa1f)||(code>=0x30000&&code<=0x323af);
                if(!letter&&!digit&&!han)return false;chinese|=han;englishOnly&=letter;
            }
            return englishOnly?count>=4&&count<=12:chinese&&count>=2&&count<=8;
        }
        public static string NormalizeGreeting(string value)=>(value??"").Replace("\r\n","\n").Trim();
        public static bool ValidGreeting(string value)
        {
            value=NormalizeGreeting(value);int count=0;
            for(int i=0;i<value.Length;i++)
            {
                if(char.IsHighSurrogate(value[i])) { if(i+1>=value.Length||!char.IsLowSurrogate(value[i+1]))return false;i++; }
                else if(char.IsLowSurrogate(value[i])||(char.IsControl(value[i])&&value[i]!='\n'))return false;
                if(++count>50)return false;
            }
            return true;
        }
    }
}
