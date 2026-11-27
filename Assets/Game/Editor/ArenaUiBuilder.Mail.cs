using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using AstralOath.Client.Common;
using AstralOath.Client.Mail;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        public const string MailWindowPath = "Assets/Game/Modules/Mail/Res/Prefab/MailWindow.prefab";
        public const string MailEntryPath = "Assets/Game/Modules/Mail/Res/Prefab/MailEntry.prefab";
        private const string MailRes = "Assets/Game/Modules/Mail/Res/";
        private static Sprite MailArt(string name) => Load<Sprite>(MailRes + "Textures/" + name + ".png");
        [MenuItem("Tools/游戏主页/生成邮件")]
        public static void GenerateMail()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            font = Load<TMP_FontAsset>(Common + "Fonts/EpicSeven.asset");
            BuildSourceMessageBoxControls();
            Directory.CreateDirectory(Path.GetDirectoryName(MailWindowPath));
            var effectShader=Load<Shader>(MailRes+"Materials/SourceAdditive.shader");var effectMaterial=new Material(effectShader);if(File.Exists(MailRes+"Materials/SourceAdditive.mat")){EditorUtility.CopySerialized(effectMaterial,Load<Material>(MailRes+"Materials/SourceAdditive.mat"));Object.DestroyImmediate(effectMaterial);}else AssetDatabase.CreateAsset(effectMaterial,MailRes+"Materials/SourceAdditive.mat");
            Save(BuildMailEntry().gameObject, MailEntryPath);
            Save(BuildMailWindow().gameObject, MailWindowPath);
            UIViewRegistry.ValidatePrefab<MailWindow>(Load<GameObject>(MailWindowPath));
            UIViewRegistry.ValidatePrefab<MailEntry>(Load<GameObject>(MailEntryPath));
            AssetDatabase.SaveAssets();
        }
        private static RectTransform BuildMailEntry()
        {
            var root = Root("MailEntry"); root.sizeDelta = new Vector2(59,66)*1.5f;
            var button = Button(root, MailArt("cm_hud_top_btn_mail"), Color.white);
            PressSprites(button, MailArt("cm_hud_top_btn_mail"), MailArt("cm_hud_top_btn_mail_p"));
            var (counter,count)=BuildMailEntryBadge(root);
            button.FeedbackSound=GameUiFeedback.Sound.Silent;var audio=root.gameObject.AddComponent<AudioSource>();audio.playOnAwake=false;audio.clip=Load<AudioClip>(MailRes+"Sounds/MailOpen.wav");Bind(root,("button",button),("count",count),("counter",counter.gameObject),("audio",audio));return root;
        }
        [Serializable] private sealed class MailParticleFile { public MailParticleEmitter[] emitters; }
        [Serializable] private sealed class MailParticleEmitter { public float emissionRate,duration; public string textureFileName; }
        private static (int Count,string Texture)[] MailEffectParticleDataForEditor() => JsonUtility.FromJson<MailParticleFile>(File.ReadAllText(MailRes+"reward-particles.json")).emitters.Select(e=>((int)Math.Ceiling(e.emissionRate*e.duration),"fx_"+Path.GetFileNameWithoutExtension(e.textureFileName))).ToArray();
        private static RectTransform BuildMailWindow()
        {
            var root = Root("MailWindow"); var keys = new List<(string Key,Object Target)>();
            void Link(string k,Object o)=>keys.Add((k,o));
            GameText Label(string key,RectTransform parent,float x,float y,float w,float h,float size=28,TextAlignmentOptions align=TextAlignmentOptions.MidlineLeft)
            { var t=Text(parent,key,null,size,x,y,w,h,Color.white,align,true);t.richText=false; Link(key,t);return t; }
            GameButton Control(string key,RectTransform parent,float x,float y,float w,float h,string art="_btn_green_r",string caption=null)
            { var r=Area(parent,key,x,y,w,h);var b=Button(r,MailArt(art),Color.white);
              string press=art=="_btn_blue_r_3"?"_btn_blue_r_p_3":art=="_btn_gold_r_2"?"_btn_gold_r_p_2":art+"_p";
              if(File.Exists(MailRes+"Textures/"+press+".png"))PressSprites(b,MailArt(art),MailArt(press));
              if(art=="_btn_green_r"){var states=b.spriteState;states.disabledSprite=MailArt("_btn_white_r_p");b.spriteState=states;}((Image)b.targetGraphic).pixelsPerUnitMultiplier=1f/1.5f;Link(key,b);
              if(caption!=null)Label(key+"Label",r,0,0,1,1,29,TextAlignmentOptions.Center).text=MailLabels.Text(caption);
              return b; }
            void Image(string key,RectTransform p,string art,float x,float y,float w,float h,bool aspect=true)
            { Link(key,Picture(p,key,MailArt(art),x,y,w,h,aspect)); }
            GameObject Layer(string key)
            { var r=Area(root,key,0,0,1,1);Paint(r,null,new Color(0,0,0,.68f),true);Link(key,r.gameObject);r.gameObject.AddComponent<CanvasGroup>();return r.gameObject; }
            Paint(root,MailArt("base_menu"),Color.white,true);
            root.gameObject.AddComponent<CanvasGroup>();
            var header=Area(root,"Header",0,0,1,.092f);
            Link("back",SharedHeaderBack(header));Label("title",header,.067f,0,.24f,1,36).text=MailLabels.Text("title");
            DrawNativeTopBarIcons(root);
            var balances=Area(header,"Authoritative balances",0,0,0,0);balances.anchorMin=balances.anchorMax=new Vector2(1,.5f);balances.pivot=new Vector2(1,.5f);balances.anchoredPosition=new Vector2(-457.5f,0);balances.sizeDelta=new Vector2(802.5f,99);Link("balancesFrame",balances);
            Image("energyIcon",balances,"to_stamina",0,.14f,.055f,.72f);Label("energy",balances,.063f,0,.22f,1,27);
            Image("goldIcon",balances,"to_gold",.31f,.14f,.06f,.72f);Label("gold",balances,.375f,0,.31f,1,27);
            Image("crystalsIcon",balances,"to_crystal",.715f,.14f,.055f,.72f);Label("crystals",balances,.777f,0,.22f,1,27);
            Paint(Area(root,"Header rule",0,.092f,1,.002f),null,new Color(1,1,1,.17f));
            var tab=Area(root,"Tabs",0,.104f,.074f,.87f);
            Image("inboxSelected",tab,"_tap_select_blue",0,0,1,.11f,false);
            Image("historySelected",tab,"_tap_select_blue",0,.12f,1,.11f,false);
            var inbox=Control("inbox",tab,0,0,1,.11f,"_box_r_list");((Image)inbox.targetGraphic).color=Color.clear;
            var history=Control("history",tab,0,.12f,1,.11f,"_box_r_list");((Image)history.targetGraphic).color=Color.clear;
            Image("inboxIcon",tab,"cm_icon_mail",.33f,.02f,.36f,.07f);
            Image("historyIcon",tab,"cm_icon_mail_log",.33f,.14f,.36f,.07f);
            Paint(Area(root,"Tab rule",.074f,.094f,.0007f,.906f),null,new Color(1,1,1,.24f));
            var scroll=Scroll(root,"Letters",.09f,.128f,.895f,.731f);Link("scroll",scroll);
            scroll.content.GetComponent<VerticalLayoutGroup>().spacing=13;
            var row=Area(scroll.content,"Mail row",0,0,1,0);row.sizeDelta=new Vector2(0,168);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight=168;
            var fields=new List<(string Key,Object Target)>();
            void R(string k,Object o)=>fields.Add((k,o));
            var bg=Paint(row,MailArt("_box_r_list"),new Color32(70,84,103,255),true,true); bg.pixelsPerUnitMultiplier=1f/1.5f;
            var face=Area(row,"Face",.012f,.09f,.07f,.82f);
            R("avatar",Picture(face,"Avatar",MailArt("npc1010_s"),.033f,.033f,.934f,.934f,true));
            Picture(face,"Ring",CommonArt("_hero_s_frame_ally"),0,0,1,1,true);
            R("gm",Picture(face,"GM",MailArt("cm_icon_gm"),.56f,-.04f,.50f,.34f,true).gameObject);
            var sender=Text(row,"Sender",null,27,.087f,.13f,.49f,.29f,Mist);sender.richText=false;R("sender",sender);
            var title=Text(row,"Title",null,25,.087f,.41f,.49f,.43f,Color.white,TextAlignmentOptions.TopLeft,true);title.richText=false;R("title",title);
            Paint(Area(row,"Time divider",.62f,.12f,.001f,.72f),null,new Color(1,1,1,.15f));
            R("timeLabel",Text(row,"Time label",null,22,.631f,.20f,.125f,.24f,Mist));
            R("time",Text(row,"Time",null,25,.65f,.46f,.107f,.30f));
            Picture(row,"Timer",MailArt("cm_icon_etctime_b"),.631f,.46f,.017f,.29f,true);
            var item=Area(row,"Attachment",.758f,.105f,.068f,.79f);Paint(item,MailArt("_itembg_stuff_1"),Color.white);
            var itemButton=Button(Area(item,"Inspect",0,0,1,1),null,Color.clear);R("attachment",itemButton);
            R("icon",Picture(item,"Icon",null,0,0,1,1,true));R("quantity",Text(item,"Quantity",null,28,0,.73f,1,.30f,Color.white,TextAlignmentOptions.BottomRight));
            var actionRect=Area(row,"Action",.842f,.27f,.145f,.48f);var action=Button(actionRect,MailArt("_btn_green_r"),Color.white);PressSprites(action,MailArt("_btn_green_r"),MailArt("_btn_green_r_p"));((Image)action.targetGraphic).pixelsPerUnitMultiplier=1f/1.5f;
            R("action",action);R("actionLabel",Text(actionRect,"Label",null,30,0,0,1,1,Color.white,TextAlignmentOptions.Center));
            Bind(row,fields.ToArray());Link("row",row.gameObject);row.gameObject.SetActive(false);
            Label("empty",root,.10f,.53f,.87f,.07f,26,TextAlignmentOptions.Center).color=Mist; Image("emptyIcon",root,"cm_icon_etcinfor",.52f,.475f,.03f,.04f);
            Paint(Area(root,"Footer rule",.085f,.862f,.9f,.002f),null,new Color(1,1,1,.20f));
            var sort=Control("sort",root,.09f,.902f,.139f,.055f,"_box_r_list","sort");Image("sortIcon",(RectTransform)sort.transform,"_sorting",.05f,.25f,.15f,.5f);foreach(var pair in keys)if(pair.Key=="sortLabel")Place(((GameText)pair.Target).rectTransform,.22f,0,.75f,1);
            Label("capacity",root,.43f,.89f,.40f,.075f,26,TextAlignmentOptions.MidlineRight);
            Label("historyHint",root,.09f,.89f,.70f,.09f,22);
            var allControl=Control("claimAll",root,.835f,.89f,.145f,.075f,"_btn_green_r","claimAll");Image("allArrow",(RectTransform)allControl.transform,"cm_icon_etcdown",.73f,.24f,.18f,.52f);foreach(var pair in keys)if(pair.Key=="claimAllLabel")Place(((GameText)pair.Target).rectTransform,.02f,0,.71f,1);
            var notice=CreateDirectoryToast(root);Link("messageChrome",notice);
            Link("message",notice.GetComponent<BindingMap>().Require<GameText>("message"));
            var detail=(RectTransform)Layer("detail").transform;
            var close=Button(Area(detail,"Dismiss letter",0,0,1,1),null,Color.clear);Link("detailClose",close);
            var panel=Area(detail,"Letter",0,0,0,0);panel.anchorMin=panel.anchorMax=panel.pivot=Vector2.one*.5f;panel.sizeDelta=new Vector2(1440,765);panel.anchoredPosition=new Vector2(0,-15);Link("detailPanel",panel);
            var panelImg=Paint(panel,MailArt("_box_popup"),Color.white,true,true);panelImg.pixelsPerUnitMultiplier=1f/1.5f;
            Image("detailAvatar",panel,"npc1010_s",.036f,.076f,.088f,.164f);Picture(panel,"Ring",CommonArt("_hero_s_frame_ally"),.033f,.07f,.094f,.175f,true);
            Image("detailGm",panel,"cm_icon_gm",.086f,.067f,.045f,.055f);
            Label("detailSender",panel,.142f,.075f,.68f,.085f,34).color=Mist;
            Label("detailTime",panel,.142f,.18f,.65f,.065f,27);
            var detailItem=Control("detailAttachment",panel,.866f,.083f,.09f,.17f,"_itembg_stuff_1");Image("detailIcon",(RectTransform)detailItem.transform,"to_stamina",0,0,1,1);
            var leftBar=Picture(panel,"Ornament left",MailArt("_bar_24"),.018f,.287f,.482f,.04f);leftBar.type=UnityEngine.UI.Image.Type.Sliced;leftBar.pixelsPerUnitMultiplier=1f/1.5f;leftBar.color=new Color32(42,70,116,255);
            var mirror=Picture(panel,"Ornament right",MailArt("_bar_24"),.5f,.287f,.482f,.04f);mirror.type=UnityEngine.UI.Image.Type.Sliced;mirror.pixelsPerUnitMultiplier=1f/1.5f;mirror.color=new Color32(42,70,116,255);mirror.rectTransform.localScale=new Vector3(-1,1,1);
            var bodyScroll=Scroll(panel,"Letter body",.035f,.36f,.93f,.39f);var body=Label("detailBody",bodyScroll.content,0,0,1,1,27,TextAlignmentOptions.TopLeft);body.color=new Color32(163,163,163,255);body.overflowMode=TextOverflowModes.Overflow;
            Control("detailClaim",panel,.35f,.81f,.30f,.105f,"_btn_green_r","receive");
            var confirm=(RectTransform)Layer("confirmation").transform;
            var confirmation=SourceConfirmation(confirm,"Confirm",0,.285f,1,.43f);
            foreach(var key in new[]{"Title","Body"}){var text=confirmation.Require<GameText>(key=="Title"?"title":"body");text.text=MailLabels.Text("confirm"+key);Link("confirm"+key,text);}
            Link("confirmCancel",confirmation.Require<GameButton>("cancel"));Link("confirmOk",confirmation.Require<GameButton>("confirm"));
            var cancelLabel=confirmation.Require<GameText>("cancelLabel");cancelLabel.text=MailLabels.Text("cancel");Link("confirmCancelLabel",cancelLabel);
            var confirmLabel=confirmation.Require<GameText>("confirmLabel");confirmLabel.text=MailLabels.Text("confirm");Link("confirmOkLabel",confirmLabel);
            RecordInventoryButton(confirmation.gameObject);
            var reward=(RectTransform)Layer("reward").transform;
            var rewardButton=Button(Area(reward,"Dismiss rewards",0,0,1,1),null,Color.clear);Link("rewardClose",rewardButton);
            var rewardPanel=Area(reward,"Reward",0,.17f,1,.65f);var rewardSurface=Paint(rewardPanel,MailArt("renew_popup_bg_2_alpha"),Color.white,false,true);rewardSurface.pixelsPerUnitMultiplier=1f/1.5f;
            Label("rewardTitle",rewardPanel,.12f,.035f,.76f,.12f,43,TextAlignmentOptions.Center).text=MailLabels.Text("rewardTitle");
            var titleRule=Picture(rewardPanel,"Title rule",MailArt("_bar_23"),.285f,.165f,.43f,.015f);titleRule.type=UnityEngine.UI.Image.Type.Sliced;titleRule.pixelsPerUnitMultiplier=1f/1.5f;titleRule.color=new Color(1,1,1,.45f);
            Label("rewardBody",rewardPanel,.15f,.24f,.70f,.08f,28,TextAlignmentOptions.Center);
            var rewards=Area(rewardPanel,"Items",.22f,.43f,.56f,.25f);Link("rewardItems",rewards);
            for(int i=0;i<20;i++)
            {var tile=Area(rewards,"Reward "+i,0,0,.105f,1);Link("rewardTile"+i,tile.gameObject);Paint(tile,MailArt("_itembg_stuff_1"),Color.white);Link("rewardIcon"+i,Picture(tile,"Icon",null,0,0,1,1,true));var rewardCount=Label("rewardQuantity"+i,tile,0,.70f,1,.36f,29,TextAlignmentOptions.BottomRight);rewardCount.enableAutoSizing=true;rewardCount.fontSizeMin=16;rewardCount.fontSizeMax=29;rewardCount.enableWordWrapping=false;}
            var coverCard=Area(rewardPanel,"Illustration card",0,0,0,0);coverCard.anchorMin=coverCard.anchorMax=coverCard.pivot=new Vector2(.5f,.471f);coverCard.sizeDelta=new Vector2(337.5f,222);Link("rewardCoverCard",coverCard.gameObject);
            var coverMask=Area(coverCard,"Original illustration viewport",4f/225,12.5f/148,217f/225,123f/148);coverMask.gameObject.AddComponent<RectMask2D>();
            var coverImage=Picture(coverMask,"Cover",MailArt("cover_gm_gift"),0,0,1,1);Link("rewardCover",coverImage);var coverFit=coverImage.gameObject.AddComponent<AspectRatioFitter>();coverFit.aspectMode=AspectRatioFitter.AspectMode.EnvelopeParent;coverFit.aspectRatio=2400f/1080f;coverImage.rectTransform.localScale=Vector3.one*1.1f;
            Picture(coverCard,"Original card frame",MailArt("_frame_s"),0,0,1,1);
            var closeRule=Picture(rewardPanel,"Close rule",MailArt("_bar_2"),.28f,.80f,.44f,.012f);closeRule.type=UnityEngine.UI.Image.Type.Sliced;closeRule.pixelsPerUnitMultiplier=1f/1.5f;closeRule.color=new Color(1,1,1,.38f);
            Label("rewardCloseLabel",rewardPanel,.25f,.85f,.5f,.08f,28,TextAlignmentOptions.Center).text=MailLabels.Text("clickClose");
            var fxTitle=Area(reward,"Original title flash",0,.068f,1,.333f).gameObject.AddComponent<RawImage>();fxTitle.texture=Load<Texture2D>(MailRes+"Textures/RewardTitleFrames.png");fxTitle.material=Load<Material>(Common+"Materials/UiTouchPulse.mat");fxTitle.raycastTarget=false;fxTitle.enabled=false;Link("effectTitle",fxTitle);
            var fxParticles=Area(reward,"Original reward particles",0,0,1,1);Link("effectParticles",fxParticles);
            int particleIndex=0;
            foreach(var emitter in MailEffectParticleDataForEditor())
            {for(int i=0;i<emitter.Count;i++){var spark=Area(fxParticles,"Particle "+particleIndex++,0,0,0,0);spark.anchorMin=spark.anchorMax=spark.pivot=new Vector2(.5f,.767f);var image=Paint(spark,MailArt(emitter.Texture),Color.white);image.material=Load<Material>(MailRes+"Materials/SourceAdditive.mat");image.raycastTarget=false;image.enabled=false;}}
            var single=Area(root,"Single reward",0,0,0,0);single.anchorMin=single.anchorMax=new Vector2(.5f,1);single.pivot=Vector2.one*.5f;single.sizeDelta=new Vector2(1024,256);single.anchoredPosition=new Vector2(0,-164);Link("singleReward",single.gameObject);single.gameObject.AddComponent<CanvasGroup>().blocksRaycasts=false;
            var singleEffect=single.gameObject.AddComponent<RawImage>();singleEffect.texture=Load<Texture2D>(MailRes+"Textures/SingleRewardFrames.png");singleEffect.material=Load<Material>(Common+"Materials/UiTouchPulse.mat");singleEffect.raycastTarget=false;Link("singleEffect",singleEffect);
            var singleTile=Area(single,"Single item",.38f,.254f,.123f,.492f);Paint(singleTile,MailArt("_itembg_stuff_1"),Color.white);Image("singleIcon",singleTile,"to_stamina",0,0,1,1);Label("singleQuantity",singleTile,0,.75f,1,.30f,30,TextAlignmentOptions.BottomRight);
            Label("singleKind",single,.53f,.30f,.43f,.20f,26).color=Mist;Label("singleText",single,.53f,.47f,.43f,.28f,31);
            var tooltip=(RectTransform)Layer("tooltip").transform;
            foreach(var binding in BuildMailTooltip(tooltip))Link(binding.Key,binding.Target);
            var artRoot=Area(root,"Artwork",0,0,0,0);
            foreach(string file in Directory.GetFiles(MailRes+"Textures","*.png"))
            {string id=Path.GetFileNameWithoutExtension(file);if(id=="RewardTitleFrames"||id=="SingleRewardFrames")continue;Link("art_"+id,Picture(artRoot,id,Load<Sprite>(file),0,0,1,1));}
            artRoot.gameObject.SetActive(false);
            var audio=root.gameObject.AddComponent<AudioSource>();audio.playOnAwake=false;Link("audio",audio);
            foreach(string name in new[]{"MailOpen","SingleReward","BundleReward","RewardTitle"}) { var cue=Area(root,"Audio "+name,0,0,0,0).gameObject.AddComponent<AudioSource>();cue.playOnAwake=false;cue.clip=Load<AudioClip>(MailRes+"Sounds/"+name+".wav");Link("sound_"+name,cue); }
            foreach(string name in new[]{"UiConfirm","UiBlocked"}) { var cue=Area(root,"Audio "+name,0,0,0,0).gameObject.AddComponent<AudioSource>();cue.playOnAwake=false;cue.clip=Load<AudioClip>(Common+"Sounds/"+name+".wav");Link("sound_"+name,cue); }
            foreach(string key in new[]{"detail","confirmation","reward","tooltip","singleReward"})
            {foreach(var pair in keys)if(pair.Key==key)((GameObject)pair.Target).SetActive(false);}
            Bind(root,keys.ToArray());return root;
        }
    }
}
