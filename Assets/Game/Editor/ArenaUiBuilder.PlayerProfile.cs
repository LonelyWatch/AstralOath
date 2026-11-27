using System.Collections.Generic;
using System.IO;
using AstralOath.Client.Common;
using AstralOath.Client.PlayerProfile;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;
namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        public const string PlayerProfilePath="Assets/Game/Modules/PlayerProfile/Res/Prefab/PlayerProfile.prefab";
        private const string ProfileRes="Assets/Game/Modules/PlayerProfile/Res/";
        private static Sprite ProfileArt(string name)=>Load<Sprite>(ProfileRes+"Textures/"+name+".png");
        [MenuItem("Tools/游戏主页/生成玩家资料")]
        public static void BuildPlayerProfileAssets()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);font=Load<TMP_FontAsset>(Common+"Fonts/EpicSeven.asset");
            Directory.CreateDirectory(Path.GetDirectoryName(PlayerProfilePath));
            Save(BuildPlayerProfile().gameObject,PlayerProfilePath);
            UIViewRegistry.ValidatePrefab<PlayerProfile>(Load<GameObject>(PlayerProfilePath));AssetDatabase.SaveAssets();
        }
        private static RectTransform BuildPlayerProfile()
        {
            var root=Root("PlayerProfile");var keys=new List<(string Key,Object Target)>();void Link(string key,Object value)=>keys.Add((key,value));
            var close=Button(Area(root,"Outside close",0,0,1,1),null,new Color(0,0,0,.60f));Link("close",close);
            var design=ProfileDesign(root,"Profile source design",new Vector2(1470,1080));
            // user_profile.csb: win_bg980x631; 720 source design ->1080 Canvas design.
            var panel=ProfileFixed(design,"Original profile frame",0,54,1470,946.5f);
            var surface=Paint(panel,ProfileArt("ProfileFrame"),Color.white,true,true);surface.pixelsPerUnitMultiplier=1/1.5f;
            var content=Area(panel,"Profile content",0,0,1,1);Link("profileContent",content.gameObject);
            // Original user_profile.csb siblings1/2/3/12: source shadow, one shared
            // record backing, lower rule and clockwise90 z_tile_bar. No replacement fill.
            var shade=ProfileFixed(content,"Original right-side source shade",652.5f,936,926.7f,801.735f);
            shade.localRotation=Quaternion.Euler(0,0,90);Paint(shade,ProfileArt("RecordShade"),new Color32(8,14,23,76));
            Paint(ProfileFixed(content,"Original record backing",653.8785f,289.5f,805.5f,307.5f),ProfileArt("RecordWhite"),new Color32(0,0,0,63));
            Paint(ProfileFixed(content,"Original record lower rule",653.25f,596.25f,805.5f,1.5f),ProfileArt("RecordWhite"),new Color(1,1,1,Mathf.GammaToLinearSpace(25f/255)));
            var column=ProfileFixed(content,"Original record column rule",1053,303,277.5f,6);column.pivot=new Vector2(0,.5f);column.localRotation=Quaternion.Euler(0,0,-90);
            Paint(column,ProfileArt("RecordColumnRule"),Color.white,false,true).pixelsPerUnitMultiplier=1/1.5f;
            var card=ProfileFixed(content,"Forest personal card",60,79.35f,538.65f,795.15f);
            Paint(card,ProfileArt("Forest"),Color.white);card.gameObject.AddComponent<RectMask2D>();
            // 00:13's forest card crops the feet. Its dynamic actor is not stored in user_profile.csb;
            // 1.3 and top31 are the measured composition relative to the already verified forest viewport.
            // Retain a square RT and enlarge/reposition the square itself, never stretch the actor.
            const float portraitSquare=795.15f*1.3f;
            var portrait=ProfileFixed(card,"Representative hero",(538.65f-portraitSquare)*.5f,31,portraitSquare,portraitSquare).gameObject.AddComponent<RawImage>();portrait.raycastTarget=false;Link("portrait",portrait);
            var avatar=ProfileAvatar(content,"Account avatar",684,67.5f,180);Link("avatar",avatar);
            var clan=Picture(content,"Unassigned guild",ProfileArt("Clan"),0,0,0,0,true);ProfilePlace(clan.rectTransform,919.62f,58.62f,95.76f,95.76f);clan.color=new Color32(136,136,136,255);
            Caption("clan",L("clan"),896,182,150,90,26,new Color32(180,180,180,255),TextAlignmentOptions.Center);
            var server=Caption("serverId","",1065,36,365,36,24,new Color32(180,180,180,255));
            var copy=Button(ProfileFixed(content,"Copy player identifier",1060,26,380,52),null,Color.clear);Link("copyId",copy);
            Caption("displayName","",1062,78,370,48,35,Color.white);
            Caption("rank","Rank. —",1063,125,365,45,35,new Color32(255,137,0,255));
            Caption("friends",L("friends"),1062,171,365,36,28,new Color32(166,204,210,255));
            Small("nickname",content,1063.5f,198,378,79.5f,L("nickname"),28);
            Section("season",676.5f,753,939,966);
            Section("best",1065,1143,1329,1360.5f);
            // The caption and record fields retain their source columns. Missing authoritative records have no invented badge.
            Small("support",content,670.5f,606,171,94.5f,L("support"),27);
            var supportAvatar=ProfileAvatar(content,"Support avatar",855.75f,611.25f,79.5f);supportAvatar.enabled=false;Link("supportAvatar",supportAvatar);
            foreach(var item in new[]{("warrior",979.5f),("knight",1062f),("assassin",1143f),("ranger",1225.5f),("mage",1311f),("manauser",1389f)})
            {var icon=Picture(content,"Unconfigured support "+item.Item1,ProfileArt("Role_"+item.Item1),0,0,0,0,true);ProfilePlace(icon.rectTransform,item.Item2-22.425f,628.575f,44.85f,44.85f);icon.color=new Color(1,1,1,.25f);}
            var greeting=Small("greeting",content,667.5f,706.5f,765,100.5f,"",27);
            var letter=(Image)greeting.targetGraphic;letter.sprite=ProfileArt("Letter");greeting.transition=Selectable.Transition.ColorTint;
            Caption("greetingText",L("greetingDefault"),690,721,685,69,28,Color.black);
            var pencil=Picture(content,"Edit greeting",ProfileArt("Pencil"),0,0,0,0,true);ProfilePlace(pencil.rectTransform,1381,761,42,42);pencil.color=new Color(0,0,0,.35f);Link("greetingPencil",pencil);
            var actions=ProfileFixed(content,"Self actions",667.5f,816,765,88.5f);Link("selfActions",actions.gameObject);
            // 13s runtime presents all three actions on the lower row, unlike the inactive CSB n_btn default y72.
            Small("representative",actions,0,0,249,88.5f,L("representative"),29);
            Small("frame",actions,258,0,249,88.5f,L("frame"),29);
            Small("customCard",actions,516,0,249,88.5f,L("customCard"),29);
            foreach(var feature in new[]{"support","representative","frame","customCard"})
            {var button=(GameButton)keys.Find(item=>item.Key==feature).Target;button.gameObject.AddComponent<CanvasGroup>().alpha=.4f;}
            var closeLabel=Button(ProfileFixed(design,"Close caption button",600,1018,270,55),null,Color.clear);Link("closeLabelButton",closeLabel);
            var xicon=Picture((RectTransform)closeLabel.transform,"Close cross",ProfileArt("Close"),0,.21f,.10f,.52f,true);xicon.color=new Color32(180,180,180,255);
            var closeText=Text((RectTransform)closeLabel.transform,"Close text",null,26,.13f,0,.80f,1,new Color32(180,180,180,255));closeText.text=L("close");closeText.richText=false;
            Editor("nickname",new Vector2(859.5f,600),"NicknameFrame");Editor("greeting",new Vector2(834,609),"ProfileFrame");
            var toast=CreateDirectoryToast(root);Link("messageChrome",toast);toast.transform.SetAsLastSibling();
            Bind(root,keys.ToArray());return root;

            string L(string key)=>PlayerProfileLabels.Text(key);
            GameText Caption(string key,string value,float x,float y,float w,float h,float size,Color color,TextAlignmentOptions align=TextAlignmentOptions.MidlineLeft,RectTransform host=null)
            {var r=ProfileFixed(host?host:content,key,x,y,w,h);var t=Text(r,"Text",null,size,0,0,1,1,color,align,true);t.fontStyle=FontStyles.Normal;t.richText=false;t.text=value;Link(key,t);return t;}
            GameButton Small(string key,RectTransform host,float x,float y,float w,float h,string caption,float size)
            {
                var r=ProfileFixed(host,key,x,y,w,h);var button=Button(r,ProfileArt("SmallBlack"),Color.white);((Image)button.targetGraphic).pixelsPerUnitMultiplier=1/1.5f;
                PressSprites(button,ProfileArt("SmallBlack"),ProfileArt("SmallBlackPressed"));Link(key,button);
                var t=Text(r,"Label",null,size,.03f,0,.94f,1,new Color32(180,180,180,255),TextAlignmentOptions.Center,true);t.text=caption;t.richText=false;t.fontStyle=FontStyles.Normal;return button;
            }
            void Section(string prefix,float titleX,float normalX,float worldX,float eyeX)
            {
                Caption(prefix+"Title",L(prefix),titleX,305.925f,300,66.15f,28,new Color32(180,180,180,255));
                foreach(var row in new[]{("Arena",normalX,"arena"),("World",worldX,"worldArena")})
                {Caption(prefix+row.Item1+"Caption",L(row.Item3),row.Item2-93,493,186,36,26,new Color32(180,180,180,255),TextAlignmentOptions.Center);Caption(prefix+row.Item1,L("notParticipated"),row.Item2-93,529,186,55,29,Color.white,TextAlignmentOptions.Top);}
                var button=Small(prefix+"Privacy",content,eyeX,303,76.5f,70.5f,"",24);
                var selected=Picture((RectTransform)button.transform,"Private selected",ProfileArt("SmallWhite"),0,0,1,1);selected.type=Image.Type.Sliced;selected.pixelsPerUnitMultiplier=1/1.5f;Link(prefix+"Private",selected);
                var icon=Picture((RectTransform)button.transform,"Visibility icon",ProfileArt("Eye"),.02f,.02f,.96f,.96f,true);Link(prefix+"Eye",icon);
            }
            void Editor(string kind,Vector2 size,string skin)
            {
                var layer=Area(root,kind+" editor",0,0,1,1);Link(kind+"Layer",layer.gameObject);
                Paint(layer,null,new Color(0,0,0,.55f),true);
                var host=ProfileDesign(layer,kind+" design",new Vector2(1470,1080));
                bool nickname=kind=="nickname";
                var box=ProfileFixed(host,kind+" source frame",(1470-size.x)*.5f+(nickname?15.75f:3),(1080-size.y)*.5f+(nickname?2.427f:30),size.x,size.y);
                var bg=Paint(box,ProfileArt(skin),Color.white,true,true);bg.pixelsPerUnitMultiplier=1/1.5f;
                if(nickname)
                {
                    // Cocos frame573x400, content in source coordinates; its PNG's white line is
                    // ~32px inside the transparent 116px image. The cap is correct and stays intact.
                    var heading=Text(box,"Title",null,41.34f,0,0,1,1,Color.white,TextAlignmentOptions.Center);heading.text=L("nickname");heading.richText=false;
                    ProfilePlace(heading.rectTransform,260.4225f,82.83f,325.155f,41.34f);
                    var line=Picture(box,"Original title rule",ProfileArt("Divider"),0,0,1,1);ProfilePlace(line.rectTransform,91.5f,136.5f,669,18);line.type=Image.Type.Sliced;line.pixelsPerUnitMultiplier=1/1.5f;
                    var field=ProfileInput(box,"Nickname",24,0,0,1,1);ProfilePlace((RectTransform)field.transform,99,172.4985f,421.5f,82.5f);Link("nicknameInput",field);
                    SharedFixed("nicknameCheck",box,520.5045f,168,232.5f,91.5f,"Green",L("check"),30);
                    var instructions=Text(box,"Nickname rules",null,27,0,0,1,1,new Color32(180,180,180,255),TextAlignmentOptions.TopLeft,true);ProfilePlace(instructions.rectTransform,130.0275f,273,588.945f,107.835f);instructions.text=L("nicknameRules");instructions.richText=false;
                    SharedFixed("nicknameCancel",box,127.5f,417,229.5f,97.5f,"Gold",L("cancel"),32);
                    SharedFixed("nicknameSave",box,357,417,372,97.5f,"Green",L("change"),32);
                    layer.gameObject.SetActive(false);return;
                }
                // Greeting uses wnd/_z_input.csb, not the separate nickname confirmation CSB.
                // n_top adds26 to all content Y; frame556x406 is at base642/137.
                var title=Text(box,"Title",null,41.34f,0,0,1,1,Color.white,TextAlignmentOptions.Center);title.text=L("greetingTitle");title.richText=false;
                ProfilePlace(title.rectTransform,296.955f,43.83f,240.09f,41.34f);
                var rule=Picture(box,"Original title rule",ProfileArt("GreetingDivider"),0,0,1,1);ProfilePlace(rule.rectTransform,52.5f,97.5f,729,18);rule.type=Image.Type.Sliced;rule.pixelsPerUnitMultiplier=1/1.5f;
                var input=ProfileInput(box,"Greeting",100,0,0,1,1);ProfilePlace((RectTransform)input.transform,94.5f,135,646.5f,226.5f);Link("greetingInput",input);
                input.lineType=TMP_InputField.LineType.MultiLineNewline;input.textComponent.alignment=TextAlignmentOptions.TopLeft;input.textComponent.enableWordWrapping=true;((GameText)input.placeholder).alignment=TextAlignmentOptions.TopLeft;
                var limit=Text(box,"Greeting length",null,27,0,0,1,1,new Color32(180,180,180,255),TextAlignmentOptions.Center);ProfilePlace(limit.rectTransform,301.125f,390,231.75f,27);limit.text=L("greetingLimit");limit.richText=false;
                SharedFixed("greetingCancel",box,133.5f,472.5f,288,91.5f,"Gold",L("cancel"),32);
                SharedFixed("greetingSave",box,417,472.5f,288,91.5f,"Green",L("change"),32);
                layer.gameObject.SetActive(false);
            }
            void SharedFixed(string key,RectTransform host,float x,float y,float w,float h,string style,string caption,float size)
            {var button=InventoryAction(host,key,null,size,0,0,1,1,out var label,style);ProfilePlace((RectTransform)button.transform,x,y,w,h);label.text=caption;label.richText=false;Link(key,button);RecordInventoryButton(button.gameObject);}
        }
        private static RectTransform ProfileFixed(RectTransform parent,string name,float x,float y,float w,float h)
        {var rect=Area(parent,name,0,0,0,0);ProfilePlace(rect,x,y,w,h);return rect;}
        private static void ProfilePlace(RectTransform rect,float x,float y,float w,float h)
        {rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(w,h);}
        private static RectTransform ProfileDesign(RectTransform root,string name,Vector2 size)
        {
            var viewport=Area(root,name+" viewport",0,0,1,1);var fit=viewport.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.FitInParent;fit.aspectRatio=size.x/size.y;
            var design=Area(viewport,name,0,0,0,0);design.anchorMin=design.anchorMax=design.pivot=Vector2.one*.5f;design.anchoredPosition=Vector2.zero;design.sizeDelta=size;
            var layout=viewport.gameObject.AddComponent<AuthoredHeightLayout>();var serialized=new SerializedObject(layout);serialized.FindProperty("content").objectReferenceValue=design;serialized.FindProperty("authoredHeight").floatValue=size.y;serialized.ApplyModifiedPropertiesWithoutUndo();return design;
        }
        private static Image ProfileAvatar(RectTransform host,string name,float x,float y,float size)
        {
            var rect=ProfileFixed(host,name,x,y,size,size);Paint(rect,ProfileArt("PortraitBackground"),Color.white);rect.gameObject.AddComponent<Mask>().showMaskGraphic=true;
            var face=Picture(rect,"Face",null,.035f,.035f,.93f,.93f,true);
            Picture(rect,name+" source ring",ProfileArt("PortraitFrame"),0,0,1,1,true);return face;
        }
        private static TMP_InputField ProfileInput(RectTransform host,string name,int limit,float x,float y,float w,float h)
        {
            var rect=Area(host,name,x,y,w,h);var image=Paint(rect,ProfileArt("NameInput"),Color.white,true,true);image.pixelsPerUnitMultiplier=1/1.5f;
            var input=rect.gameObject.AddComponent<TMP_InputField>();var viewport=Area(rect,"Viewport",.04f,.05f,.92f,.90f);viewport.gameObject.AddComponent<RectMask2D>();
            var value=Text(viewport,"Value",null,32,0,0,1,1,Color.black);value.richText=false;value.overflowMode=TextOverflowModes.Overflow;
            var hint=Text(viewport,"Hint",null,31,0,0,1,1,new Color32(70,70,70,255));hint.text=PlayerProfileLabels.Text("input");hint.richText=false;
            input.targetGraphic=image;input.textViewport=viewport;input.textComponent=value;input.placeholder=hint;input.characterLimit=limit;input.richText=false;return input;
        }
    }
}
