using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine.Scripting;
namespace AstralOath.Client.CharacterLab
{
    public static class CharacterLabMessages
    {
        public const string Address="characterlab/localization";
        [Serializable,Preserve] private sealed class Rows { [Preserve] public Entry[] rows; }
        [Serializable,Preserve] private sealed class Entry { [Preserve] public string key; [Preserve] public string zhCn; }
        public static async Task<ILocaleService> LoadAsync(IConfigService config,BindingScope scope)
        {
            var source=await config.ReadAsync<Rows>(Address,scope);scope.Token.ThrowIfCancellationRequested();
            if(source?.rows==null||source.rows.Length==0)throw new InvalidDataException("CharacterLab localization is empty.");
            var messages=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var row in source.rows)
                if(row==null||string.IsNullOrWhiteSpace(row.key)||string.IsNullOrWhiteSpace(row.zhCn)||!messages.TryAdd(row.key,row.zhCn))
                    throw new InvalidDataException("Invalid or duplicate CharacterLab localization key: "+row?.key);
            return new LocaleService(new Dictionary<string,IReadOnlyDictionary<string,string>>{["zh-Hans"]=messages},"zh-Hans","ja","ja");
        }
    }
}
