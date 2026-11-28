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
            root.gameObject.AddComponent<CanvasGroup>();
            // Full-screen dismissal is intentional: the source says click to close,
            // unlike the two-second non-modal GameToast.
            // The old .76 Linear alpha transmitted .525 of a white header pixel.
            // The actual reference transmits .208 with the authored dim stacked on
            // the sRGB black backdrop. Convert the intended transmission once.
            var dismiss=Button(Area(root,"Dismiss",0,0,1,1),null,
                Black(1-Mathf.GammaToLinearSpace(1-.76f)));
            var dim=Paint(Area(root,"Source dim",0,0,1,1),
                Load<Sprite>(Common+"Textures/FeatureNotice/_dim.png"),Color.white,sliced:true);
            dim.pixelsPerUnitMultiplier=1f/HomeDesignScale;
            var frame=Area(root,"Source banner",0,0,1,0);
            frame.anchorMin=new Vector2(0,.5f);frame.anchorMax=new Vector2(1,.5f);
            // Runtime steady frame is390 native units high, after the CSB's380
            // initial size. Positions below are measured against61.5s at equal height.
            frame.sizeDelta=new Vector2(0,390*HomeDesignScale);frame.anchoredPosition=Vector2.zero;
            var surface=Paint(frame,Load<Sprite>(Common+"Textures/FeatureNotice/renew_popup_bg_2.png"),
                Color.white,sliced:true);surface.pixelsPerUnitMultiplier=1f/HomeDesignScale;
            GameText NoticeText(string name,float size,float height,float centerY,Color ink)
            {
                var text=Text(frame,name,null,size,.12f,0,.76f,0,ink,TextAlignmentOptions.Center,true);
                var rect=text.rectTransform;rect.anchorMin=new Vector2(.12f,.5f);rect.anchorMax=new Vector2(.88f,.5f);
                rect.pivot=Vector2.one*.5f;rect.sizeDelta=new Vector2(0,height);rect.anchoredPosition=new Vector2(0,centerY);
                return text;
            }
            var title=NoticeText("Title",50*.63f*HomeDesignScale,76,214.2f,Color.white);
            var message=NoticeText("Message",24*.82f*HomeDesignScale,136,0,new Color32(180,180,180,255));
            // The source closes over the blue gradient. Preserve its measured blue-grey
            // ink instead of the old opaque neutral136 grey (reference core150/162/180).
            var close=NoticeText("Click to close",24*.84f*HomeDesignScale,54,-178.5f,new Color32(150,162,180,255));
            title.richText=message.richText=close.richText=false;
            UnityEngine.UI.Image Rule(string name,string sprite,float width,float height,float centerY)
            {
                var rect=Area(frame,name,.5f,.5f,0,0);
                rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.one*.5f;
                rect.sizeDelta=new Vector2(width,height)*HomeDesignScale;rect.anchoredPosition=new Vector2(0,centerY);
                var rule=Paint(rect,Load<Sprite>(Common+"Textures/FeatureNotice/"+sprite+".png"),Color.white,sliced:true);
                rule.pixelsPerUnitMultiplier=1f/HomeDesignScale;return rule;
            }
            var upper=Rule("Source upper ornament","_bar_23",640,12,154.8f);
            var lower=Rule("Source lower ornament","_bar_2",637,10,-129.6f);
            var arrowRect=Area(frame,"Close arrow",.5f,.5f,0,0);
            arrowRect.anchorMin=arrowRect.anchorMax=arrowRect.pivot=Vector2.one*.5f;
            arrowRect.anchoredPosition=new Vector2(0,-225);
            arrowRect.sizeDelta=new Vector2(18,12)*1.25f*HomeDesignScale;
            var arrow=Paint(arrowRect,Load<Sprite>(Common+"Textures/FeatureNotice/cm_icon_etcarrow.png"),Color.white);
            Bind(root,("dismiss",dismiss),("surface",surface),("dim",dim),("upperRule",upper),("lowerRule",lower),
                ("title",title),("message",message),("closeLabel",close),("arrow",arrow));
            Save(root.gameObject,FeatureNoticePath);
            UIViewRegistry.ValidatePrefab<FeatureNotice>(Load<GameObject>(FeatureNoticePath));
        }
    }
}
