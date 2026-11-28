using System.IO;
using System.Linq;
using AstralOath.Client.Common;
using AstralOath.Client.Mail;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        public const string SingleItemRewardPath="Assets/Game/Common/Res/Prefab/SingleItemReward.prefab";
        private const string SingleRewardRes="Assets/Game/Common/Res/SingleItemReward/";
        public static void BuildSingleItemRewardAssets()
        {
            font=Load<TMP_FontAsset>(Common+"Fonts/EpicSeven.asset");
            var source=JObject.Parse(File.ReadAllText(SingleRewardRes+"source-particles.json"));
            var root=Root("SingleItemReward");root.anchorMin=root.anchorMax=new Vector2(.5f,1);root.pivot=Vector2.one*.5f;root.sizeDelta=new Vector2(1024,256);root.anchoredPosition=new Vector2(0,-164);
            root.gameObject.AddComponent<CanvasGroup>().blocksRaycasts=false;
            var effect=Picture(root,"Original64 frame blue base",Load<Sprite>(SingleRewardRes+"SingleRewardFrames.png"),0,0,1,1);effect.material=Load<Material>(Common+"Materials/UiTouchPulse.mat");effect.raycastTarget=false;
            var tile=Area(root,"Single item",.38f,.254f,.123f,.492f);Paint(tile,Load<Sprite>(SingleRewardRes+"ItemSlot.png"),Color.white);
            var icon=Picture(tile,"Reward icon",null,0,0,1,1,true);icon.enabled=false;
            var quantity=Text(tile,"Quantity",null,30,0,.75f,1,.30f,Color.white,TextAlignmentOptions.BottomRight,true);
            var kind=Text(root,"Kind",null,26,.53f,.30f,.43f,.20f,Mist,TextAlignmentOptions.MidlineLeft,true);
            var text=Text(root,"Text",null,31,.53f,.47f,.43f,.28f,Color.white,TextAlignmentOptions.MidlineLeft,true);
            quantity.richText=kind.richText=text.richText=false;
            var particles=Area(root,"Original burst particles",0,0,0,0);particles.anchorMin=particles.anchorMax=particles.pivot=Vector2.one*.5f;
            particles.anchoredPosition=new Vector2(0,(float)source["sourceY"]*(float)source["sourceToDesignScale"]);
            int capacity=((JArray)source["emitters"]).Sum(e=>(int)e["maxParticles"]);
            var star=Load<Sprite>(SingleRewardRes+"ZodiacStar.png");var material=Load<Material>(Common+"Materials/SourceAdditive.mat");
            for(int i=0;i<capacity;i++)
            {
                var rect=Area(particles,"Source star "+i,0,0,0,0);rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.one*.5f;
                var image=Paint(rect,star,Color.white);image.material=material;image.raycastTarget=false;image.enabled=false;
            }
            Bind(root,("effect",effect),("icon",icon),("quantity",quantity),("kind",kind),("text",text),("particles",particles));
            Directory.CreateDirectory(Path.GetDirectoryName(SingleItemRewardPath));Save(root.gameObject,SingleItemRewardPath);
            UIViewRegistry.ValidatePrefab<SingleItemReward>(Load<GameObject>(SingleItemRewardPath));
            AssetDatabase.SaveAssets();
        }
    }
}
