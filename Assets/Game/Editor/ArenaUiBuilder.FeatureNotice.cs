using AstralOath.Client.Common;
using AstralOath.Client.FoundationDemo;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private const string FeatureNoticePath=Common+"Prefab/FeatureNotice.prefab";
        public static void GenerateFeatureNotice()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            font=Load<TMP_FontAsset>(Common+"Fonts/EpicSeven.asset");
            BuildFeatureNoticeAssets();
            AssetDatabase.SaveAssets();
        }

        private static void BuildFeatureNoticeAssets()
        {
            var root=Root("FeatureNotice");
            // Full-screen dismissal is intentional: the source says click to close,
            // unlike the two-second non-modal GameToast.
            var dismiss=Button(Area(root,"Dismiss",0,0,1,1),null,Black(.76f));
            var frame=Area(root,"Source banner",0,0,1,0);
            frame.anchorMin=new Vector2(0,.5f);frame.anchorMax=new Vector2(1,.5f);
            frame.sizeDelta=new Vector2(0,380*HomeDesignScale);frame.anchoredPosition=Vector2.zero;
            var surface=PaintSourceMessageBox(frame);surface.raycastTarget=false;
            var title=Text(frame,"Title",null,50*.63f*HomeDesignScale,.12f,.065f,.76f,.13f,
                Color.white,TextAlignmentOptions.Center,true);
            var message=Text(frame,"Message",null,24*.82f*HomeDesignScale,.12f,.38f,.76f,.24f,
                new Color32(180,180,180,255),TextAlignmentOptions.Center,true);
            var close=Text(frame,"Click to close",null,24*.84f*HomeDesignScale,.3f,.79f,.4f,.12f,
                new Color32(136,136,136,255),TextAlignmentOptions.Center);
            title.richText=message.richText=close.richText=false;
            var ornament=Load<Sprite>(Common+"Textures/FeatureNotice/_bar_23.png");
            void Rule(string name,float down)
            {
                var rect=Area(frame,name,.5f,down,0,0);
                rect.sizeDelta=new Vector2(640,12)*HomeDesignScale;
                rect.pivot=Vector2.one*.5f;
                Paint(rect,ornament,new Color32(255,255,255,75));
            }
            Rule("Source upper ornament",.24f);Rule("Source lower ornament",.73f);
            var arrowRect=Area(frame,"Close arrow",.5f,.91f,0,0);
            arrowRect.pivot=Vector2.one*.5f;arrowRect.sizeDelta=new Vector2(18,12)*1.25f*HomeDesignScale;
            var arrow=Paint(arrowRect,Load<Sprite>(Common+"Textures/FeatureNotice/cm_icon_etcarrow.png"),
                new Color32(136,136,136,255));
            Bind(root,("dismiss",dismiss),("surface",surface),("title",title),("message",message),("closeLabel",close),("arrow",arrow));
            Save(root.gameObject,FeatureNoticePath);
            UIViewRegistry.ValidatePrefab<FeatureNotice>(Load<GameObject>(FeatureNoticePath));
        }
    }
}
