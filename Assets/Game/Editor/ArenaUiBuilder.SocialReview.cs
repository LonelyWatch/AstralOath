using System.IO;
using System.Linq;
using AstralOath.Client.Common;
using AstralOath.Client.FoundationDemo;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        // Isolated iteration: Home/More can be edited in parallel without regenerating them.
        public static void GenerateSocialPanelsForReview()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            font=Load<TMP_FontAsset>(Common+"Fonts/EpicSeven.asset");
            labels=JsonUtility.FromJson<LabelFile>(File.ReadAllText(Assets[4].Path)).rows
                .ToDictionary(row=>row.key,row=>row.text);
            BuildSourceMessageBoxControls();
            BuildFeatureNoticeAssets();
            BuildPlayerProfileAssets();
            Save(BuildSummonWindow(),SummonPrefabPath);
            UIViewRegistry.ValidatePrefab<FeatureNotice>(Load<GameObject>(FeatureNoticePath));
            AssetDatabase.SaveAssets();
        }

        // Integrated rebuild, after all new source panels and header entries exist.
        public static void GenerateSocialIntegration()
        {
            GenerateChannelChat();
            GenerateSocialPanelsForReview();
            GenerateMailIntegration();
            BuildHomeNavigationAssets();
            ConfigureCollectors();
            Validate();
            AssetDatabase.SaveAssets();
        }
    }
}
