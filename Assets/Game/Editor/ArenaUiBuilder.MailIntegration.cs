using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        // Rebuild only pages whose shared header now nests the MailEntry prefab.
        public static void GenerateMailIntegration()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            font = Load<TMP_FontAsset>(Common + "Fonts/EpicSeven.asset");
            labels = JsonUtility.FromJson<LabelFile>(File.ReadAllText(Assets[4].Path)).rows
                .ToDictionary(row => row.key, row => row.text);
            GenerateMail();
            Save(BuildHome(), Assets[0].Path);
            Save(BuildDevelopmentGmWindow(), GmPath);
            BuildSummonAssets();
            BuildInventoryAssets();
            ConfigureCollectors();
            Validate();
        }
    }
}
