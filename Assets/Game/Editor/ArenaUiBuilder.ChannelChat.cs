using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralOath.Client.ChannelChat;
using AstralOath.Client.Common;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;
namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        public const string ChannelChatPath="Assets/Game/Modules/ChannelChat/Res/Prefab/ChannelChat.prefab";
        public const string ChatEntryPath="Assets/Game/Modules/ChannelChat/Res/Prefab/ChatEntry.prefab";
        private const string ChatRes="Assets/Game/Modules/ChannelChat/Res/";
        private static Sprite ChatArt(string name)=>Load<Sprite>(ChatRes+"Textures/"+name+".png");
        [MenuItem("Tools/游戏主页/生成公共频道")]
        public static void GenerateChannelChat()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);font=Load<TMP_FontAsset>(Common+"Fonts/EpicSeven.asset");
            Directory.CreateDirectory(Path.GetDirectoryName(ChannelChatPath));Save(BuildChannelChat().gameObject,ChannelChatPath);Save(BuildChatEntry().gameObject,ChatEntryPath);
            UIViewRegistry.ValidatePrefab<ChannelChat>(Load<GameObject>(ChannelChatPath));UIViewRegistry.ValidatePrefab<ChatEntry>(Load<GameObject>(ChatEntryPath));AssetDatabase.SaveAssets();
        }
        private static RectTransform BuildChatEntry()
        {
            var root=Root("ChatEntry");root.sizeDelta=new Vector2(59,66)*1.5f;var button=Button(root,ChatArt("HeaderChat"),Color.white);PressSprites(button,ChatArt("HeaderChat"),ChatArt("HeaderChatPressed"));
            button.FeedbackSound=GameUiFeedback.Sound.Normal;var source=root.gameObject.AddComponent<AudioSource>();source.playOnAwake=false;source.clip=Load<AudioClip>(Common+"Sounds/UiConfirm.wav");
            var s=new SerializedObject(button);s.FindProperty("clickAudio").objectReferenceValue=source;s.ApplyModifiedPropertiesWithoutUndo();Bind(root,("button",button),("source",source));return root;
        }
        private static RectTransform BuildChannelChat()
        {
            var root=Root("ChannelChat");var keys=new List<(string Key,Object Target)>();void Link(string key,Object value)=>keys.Add((key,value));string L(string key)=>ChannelChatLabels.Text(key);
            var close=Button(Area(root,"Outside close",0,0,1,1),ChatArt("ChatDim"),Color.white);Link("close",close);
            var drawer=Area(root,"Original chat drawer",0,0,0,0);drawer.anchorMin=drawer.anchorMax=drawer.pivot=new Vector2(1,0);drawer.sizeDelta=new Vector2(924,1080);drawer.anchoredPosition=Vector2.zero;Link("drawer",drawer);
            ChatSurface(ChatBox(drawer,"Original header shadow",119.64f,-152.61f,652.72f,277.22f),"ChatHeaderShadow");
            var surface=ChatSurface(ChatBox(drawer,"Original backdrop",0,-85.348f,900,875),"DrawerFrame",true);Link("drawerSurface",surface);
            var transparent=ChatSurface(ChatBox(drawer,"Original translucent backdrop",0,-85.348f,900,875),"DrawerFrameTransparent");transparent.enabled=false;Link("transparentBorder",transparent);
            Caption("channelName",drawer,63,8,225,39,"",39,Color.white);
            IconButton("favorite","Favorite",354,8,62,49,40);IconButton("channelSwitch","Switch",416,8,62,49,45);IconButton("opacity","Opacity",478,8,62,49,52);IconButton("settings","Settings",540,8,62,49,50);
            var opacityButton=(GameButton)keys.Find(k=>k.Key=="opacity").Target;var opacitySelected=ChatSurface(ChatBox((RectTransform)opacityButton.transform,"Opacity selected",0,0,62,49),"SmallWhite");opacitySelected.enabled=false;Link("opacitySelected",opacitySelected);opacitySelected.transform.SetAsFirstSibling();
            var selected=ChatSurface(ChatBox(drawer,"Public selected",61,59,156,58),"PublicTab");Link("publicSelected",selected);
            Tab("publicTab",61,"Talk",L("public"));Tab("guildTab",218,"Guild",L("guild"));
            Rule(drawer,54,102,552,"Divider");Rule(drawer,54,631,552,"Divider");
            var scroll=ChatScroll(ChatBox(drawer,"Messages",54,108,546,523));Link("messages",scroll);var layout=scroll.content.GetComponent<VerticalLayoutGroup>();layout.spacing=0;layout.childForceExpandWidth=true;
            var row=ChatBox(scroll.content,"Message source row",0,0,546,72);row.gameObject.AddComponent<LayoutElement>().preferredHeight=108;
            var rowKeys=new List<(string Key,Object Target)>();
            var avatar=Button(ChatBox(row,"Avatar",4.5f,2.5f,63,63),null,Color.clear);rowKeys.Add(("avatar",avatar));
            ChatSurface(ChatBox((RectTransform)avatar.transform,"Portrait background",3.5f,1.5f,56,56),"PortraitBackground");
            var face=Picture((RectTransform)avatar.transform,"Portrait",null,.055f,.025f,.89f,.89f,true);rowKeys.Add(("face",face));
            ChatSurface(ChatBox((RectTransform)avatar.transform,"Portrait source ring",0,0,63,63),"PortraitFrame");
            var sender=ChatCaption(row,"Sender",72,5,340,28,"",29.25f,Color.white);rowKeys.Add(("sender",sender));
            var time=ChatCaption(row,"Time",160,5,160,28,"",29.25f,new Color32(118,94,64,255));rowKeys.Add(("time",time));
            var body=ChatCaption(row,"Plain message",74,32,462,40,"",28,new Color32(180,180,180,255));body.enableWordWrapping=true;body.overflowMode=TextOverflowModes.Overflow;body.alignment=TextAlignmentOptions.TopLeft;rowKeys.Add(("body",body));
            var sticker=Picture(row,"Sticker",null,0,0,0,0,true);ChatPlace(sticker.rectTransform,71.5f,32,137,110);rowKeys.Add(("sticker",sticker));Bind(row,rowKeys.ToArray());Link("row",row.gameObject);row.gameObject.SetActive(false);
            var entered=ChatBox(drawer,"Entered channel strip",54,598,549,35);var enterBg=ChatSurface(entered,"SmallWhite");enterBg.color=new Color32(68,53,34,255);Link("enteredStrip",entered.gameObject);Caption("entered",entered,4,0,541,35,"",27.3f,new Color32(183,153,107,255),TextAlignmentOptions.Center);
            var noticeButton=Control("notice",drawer,546,110,53,44,"SmallWhite");((Image)noticeButton.targetGraphic).color=new Color32(116,85,45,255);
            var arrow=ChatSurface(ChatBox(drawer,"Notice collapse arrow",0,0,17,26),"NoticeArrow");arrow.rectTransform.pivot=Vector2.one*.5f;arrow.rectTransform.anchoredPosition=new Vector2(572.5f*1.5f,-130.5f*1.5f);arrow.rectTransform.localEulerAngles=new Vector3(0,0,-90);
            var emoji=Control("emoji",drawer,52,647,54,61,"SmallBlack");var emojiIcon=ChatSurface(ChatBox((RectTransform)emoji.transform,"Emoji",5,8,44,44),"Emoji");emojiIcon.color=new Color(1,1,1,.5f);
            var input=ChatInput(ChatBox(drawer,"Input",106,647.6f,339.9f,59.4f),"InputWhite",L("input"),400,32);Link("input",input);
            var send=Control("send",drawer,446,647,158,61,"GreenSend");ChatCaption((RectTransform)send.transform,"Send label",0,0,158,61,L("send"),31.5f,Color.white,TextAlignmentOptions.Center);
            // Both actions reuse the actually observed favorite editor. The unseen channel-layer layout is not invented.
            InputOverlay("favorite",L("favoriteTitle"),L("favoriteDescription"));InputOverlay("channel",L("channelTitle"),L("channelDescription"));
            var settingsLayer=Area(root,"Settings overlay",0,0,1,1);Link("settingsLayer",settingsLayer.gameObject);Link("settingsClose",Button(Area(settingsLayer,"Close outside settings",0,0,1,1),null,Color.clear));
            var settings=ChatBox(settingsLayer,"Original notification frame",0,0,422,637);settings.anchorMin=settings.anchorMax=settings.pivot=new Vector2(1,1);settings.anchoredPosition=new Vector2(30,-49.5f);ChatSurface(settings,"BlackGoldFrame",true);
            ChatCaption(settings,"Notification title",47,45,284,30,L("notificationTitle"),35.1f,Color.white);
            foreach(var tab in new[]{("notificationTab","Talk",60f),("guildNotificationTab","Guild",133f),("keywordTab","Keyword",206f)})
            {var b=Control(tab.Item1,settings,tab.Item3,94,72,58,null);var s=ChatSurface(ChatBox((RectTransform)b.transform,"Selected",0,0,72,58),"SettingsTab");Link(tab.Item1+"Selected",s);s.enabled=tab.Item1=="notificationTab";var icon=ChatSurface(ChatBox((RectTransform)b.transform,"Icon",16,9,40,40),tab.Item2);icon.color=new Color32(180,180,180,255);}
            Rule(settings,50,133.5f,318,"SettingsDivider");
            var notificationContent=ChatBox(settings,"Notification toggles",0,0,422,637);Link("notificationContent",notificationContent.gameObject);
            Check("allNotifications",notificationContent,148,L("all"),true);for(int i=0;i<4;i++)Check("notification"+i,notificationContent,215+i*53,L("notification"+i),false);Rule(notificationContent,49,202,320,"Divider");
            var keywordContent=ChatBox(settings,"Keyword rows",0,0,422,637);Link("keywordContent",keywordContent.gameObject);
            for(int i=0;i<5;i++)
            {var k=ChatInput(ChatBox(keywordContent,"Keyword "+i,49,152+i*53,320,51),"SmallBlack",L("keywordPlaceholder"),40,30);k.textComponent.color=Color.white;((GameText)k.placeholder).color=new Color32(136,136,136,255);k.textViewport.offsetMin=new Vector2(71,6);Link("keyword"+i,k);var icon=ChatSurface(ChatBox((RectTransform)k.transform,"Keyword icon",5,6,39,39),"Keyword");icon.color=new Color(1,1,1,.5f);}
            var info=ChatSurface(ChatBox(keywordContent,"Information",196,431,23,23),"Information");info.color=new Color(1,1,1,.3f);ChatCaption(keywordContent,"Keyword instructions",60,477,300,94,L("keywordHelp"),27.3f,new Color32(125,125,125,255),TextAlignmentOptions.TopLeft,true);
            keywordContent.gameObject.SetActive(false);settingsLayer.gameObject.SetActive(false);
            var stickerLayer=Area(root,"Sticker overlay",0,0,1,1);Link("stickerLayer",stickerLayer.gameObject);Link("stickerClose",Button(Area(stickerLayer,"Close sticker picker",0,0,1,1),null,Color.clear));
            var picker=ChatBox(stickerLayer,"Original sticker picker",0,0,449,437);picker.anchorMin=picker.anchorMax=picker.pivot=new Vector2(1,0);picker.anchoredPosition=new Vector2(-844.5f, -36);ChatSurface(picker,"BlackGoldFrame",true);Link("stickerPanel",picker);
            var stickerScroll=ChatScroll(ChatBox(picker,"Stickers",47,35,351,362));Object.DestroyImmediate(stickerScroll.content.GetComponent<VerticalLayoutGroup>());
            var grid=stickerScroll.content.gameObject.AddComponent<GridLayoutGroup>();grid.cellSize=new Vector2(175.5f,151.5f);grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=3;Link("stickerScroll",stickerScroll);
            var cell=ChatBox(stickerScroll.content,"Sticker cell",0,0,117,101);var cb=Button(cell,null,Color.clear);var ci=Picture(cell,"Original sticker",null,0,0,0,0,true);ChatPlace(ci.rectTransform,7.125f,18.75f,102.75f,82.5f);Bind(cell,("button",cb),("icon",ci));Link("stickerCell",cell.gameObject);cell.gameObject.SetActive(false);
            var art=ChatBox(root,"Sticker art catalogue",0,0,1,1);var entries=new List<(string Key,Object Target)>();
            foreach(string path in System.IO.Directory.GetFiles(ChatRes+"Stickers","*.png").OrderBy(p=>p,StringComparer.Ordinal))
            {var s=Load<Sprite>(path.Replace('\\','/'));var image=Picture(art,s.name,s,0,0,1,1,true);entries.Add((s.name,image));}
            Bind(art,entries.ToArray());Link("stickerArt",art.GetComponent<BindingMap>());art.gameObject.SetActive(false);stickerLayer.gameObject.SetActive(false);
            var toast=CreateDirectoryToast(root);Link("messageChrome",toast);toast.transform.SetAsLastSibling();Bind(root,keys.ToArray());return root;

            GameText Caption(string key,RectTransform parent,float x,float y,float w,float h,string value,float size,Color color,TextAlignmentOptions align=TextAlignmentOptions.MidlineLeft)
            {var t=ChatCaption(parent,key,x,y,w,h,value,size,color,align);Link(key,t);return t;}
            GameButton Control(string key,RectTransform host,float x,float y,float w,float h,string skin)
            {var b=Button(ChatBox(host,key,x,y,w,h),skin==null?null:ChatArt(skin),skin==null?Color.clear:Color.white);if(skin!=null){((Image)b.targetGraphic).pixelsPerUnitMultiplier=1/1.5f;if(File.Exists(ChatRes+"Textures/"+skin+"Pressed.png"))PressSprites(b,ChatArt(skin),ChatArt(skin+"Pressed"));}b.FeedbackSound=GameUiFeedback.Sound.Normal;Link(key,b);return b;}
            void IconButton(string key,string icon,float x,float y,float w,float h,float iconSize)
            {var b=Control(key,drawer,x,y,w,h,"SmallBlack");var i=ChatSurface(ChatBox((RectTransform)b.transform,"Icon",(w-iconSize)/2,(h-iconSize)/2,iconSize,iconSize),icon);i.color=new Color(1,1,1,.5f);Link(key+"Icon",i);}
            void Tab(string key,float x,string icon,string label)
            {var b=Control(key,drawer,x,59,156,58,null);ChatSurface(ChatBox((RectTransform)b.transform,"Icon",35,10,35,35),icon);ChatCaption((RectTransform)b.transform,"Label",67,10,79,35,label,32.37f,Color.white);}
            void Rule(RectTransform host,float x,float y,float width,string artName)
            {var r=ChatSurface(ChatBox(host,"Original gold rule",x,y,width,10),artName);r.color=new Color32(146,109,62,255);}
            void InputOverlay(string kind,string title,string description)
            {
                var layer=Area(root,kind+" overlay",0,0,1,1);Link(kind+"Layer",layer.gameObject);Link(kind+"Close",Button(Area(layer,"Close outside "+kind,0,0,1,1),ChatArt("FavoriteDim"),Color.white));
                var shadow=ChatBox(layer,kind+" original shadow",0,0,878.712f,705.742f);shadow.anchorMin=shadow.anchorMax=shadow.pivot=Vector2.one*.5f;shadow.anchoredPosition=new Vector2(-.015f,-2.8095f);ChatSurface(shadow,"FavoriteShadow");
                var box=ChatBox(layer,kind+" original frame",0,0,558,386);box.anchorMin=box.anchorMax=box.pivot=Vector2.one*.5f;box.anchoredPosition=new Vector2(3,24);ChatSurface(box,"BlackGoldFrame",true);
                ChatCaption(box,"Title",168.857f,53.74f,218.28f,26.52f,title,38.25f,Color.white,TextAlignmentOptions.Center);
                var left=ChatSurface(ChatBox(box,"Title rule left",54.998f,93,222,14),"TitleDivider");left.color=new Color32(146,109,62,178);
                var right=ChatSurface(ChatBox(box,"Title rule right",275.998f,92.002f,222,14),"TitleDivider");right.rectTransform.pivot=Vector2.one*.5f;right.rectTransform.anchoredPosition=new Vector2((275.998f+111)*1.5f,-(92.002f+7)*1.5f);right.rectTransform.localRotation=Quaternion.Euler(0,0,180);right.color=left.color;
                ChatCaption(box,"Description",63.5f,104.15f,427,105.7f,description,28,new Color32(136,136,136,255),TextAlignmentOptions.MidlineLeft,true);
                Caption(kind+"Value",box,105.2f,215.12f,349.6f,19.76f,"",29.64f,new Color32(171,135,89,255),TextAlignmentOptions.Center);
                var field=ChatInput(ChatBox(box,"Channel input",57,246,281,55),"SmallWhite",L("input"),4,31.5f);field.contentType=TMP_InputField.ContentType.IntegerNumber;Link(kind+"Input",field);
                var save=Control(kind+"Save",box,338,246,165,57,"GreenJoin");ChatCaption((RectTransform)save.transform,"Input label",0,0,165,57,L("send"),31.5f,Color.white,TextAlignmentOptions.Center);
                ChatCaption(box,"Allowed channel range",137.69f,307.38f,268.62f,19.24f,L("range"),28.86f,new Color32(125,125,125,255),TextAlignmentOptions.Center);
                var c=Button(ChatBox(layer,"Close caption",0,0,200,38),null,Color.clear);c.transform.SetAsLastSibling();var cr=(RectTransform)c.transform;cr.anchorMin=cr.anchorMax=cr.pivot=new Vector2(.5f,0);cr.anchoredPosition=new Vector2(0,17);ChatCaption(cr,"Label",0,0,200,38,L("close"),28,new Color32(180,180,180,255),TextAlignmentOptions.Center);Link(kind+"CloseCaption",c);layer.gameObject.SetActive(false);
            }
            void Check(string key,RectTransform host,float y,string label,bool all)
            {
                var b=Control(key,host,50,y,318,53,null);
                if(all){var bg=ChatSurface(ChatBox((RectTransform)b.transform,"Selected background",0,0,318,53),"SmallWhite");bg.color=new Color32(29,66,25,255);Link("allBackground",bg);}
                ChatSurface(ChatBox((RectTransform)b.transform,"Check background",10,9,34,35),"CheckBackground");var check=ChatSurface(ChatBox((RectTransform)b.transform,"Check mark",10,9,34,35),"CheckMark");Link(key+"Check",check);
                var labelText=ChatCaption((RectTransform)b.transform,"Label",48,1,268,50,label,all?32.37f:29.64f,all?new Color32(125,204,18,255):new Color32(171,135,89,255));Link(key+"Label",labelText);
            }
        }
        private static RectTransform ChatBox(RectTransform parent,string name,float x,float y,float w,float h)
        {var rect=Area(parent,name,0,0,0,0);ChatPlace(rect,x,y,w,h);return rect;}
        private static void ChatPlace(RectTransform rect,float x,float y,float w,float h)
        {rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y)*1.5f;rect.sizeDelta=new Vector2(w,h)*1.5f;}
        private static Image ChatSurface(RectTransform rect,string art,bool raycast=false)
        {var image=Paint(rect,ChatArt(art),Color.white,raycast,true);image.pixelsPerUnitMultiplier=1/1.5f;return image;}
        private static GameText ChatCaption(RectTransform host,string name,float x,float y,float w,float h,string value,float size,Color color,TextAlignmentOptions align=TextAlignmentOptions.MidlineLeft,bool wrap=false)
        {var t=Text(ChatBox(host,name,x,y,w,h),"Text",null,size,0,0,1,1,color,align,wrap);t.fontStyle=FontStyles.Normal;t.richText=false;t.text=value;return t;}
        private static TMP_InputField ChatInput(RectTransform rect,string skin,string hint,int limit,float size)
        {
            var image=ChatSurface(rect,skin,true);var input=rect.gameObject.AddComponent<TMP_InputField>();var viewport=Area(rect,"Text viewport",.04f,.04f,.92f,.92f);viewport.gameObject.AddComponent<RectMask2D>();
            var text=Text(viewport,"Plain value",null,size,0,0,1,1,new Color32(96,61,42,255));text.richText=false;text.overflowMode=TextOverflowModes.Overflow;
            var placeholder=Text(viewport,"Hint",null,size,0,0,1,1,new Color32(96,61,42,255));placeholder.text=hint;placeholder.richText=false;
            input.targetGraphic=image;input.textViewport=viewport;input.textComponent=text;input.placeholder=placeholder;input.richText=false;input.characterLimit=limit;return input;
        }
        private static ScrollRect ChatScroll(RectTransform rect)
        {
            var scroll=Scroll(rect,"Scroll",0,0,1,1);var layout=scroll.content.GetComponent<VerticalLayoutGroup>();layout.childForceExpandHeight=false;layout.childForceExpandWidth=true;layout.spacing=0;scroll.scrollSensitivity=50;return scroll;
        }
    }
}
