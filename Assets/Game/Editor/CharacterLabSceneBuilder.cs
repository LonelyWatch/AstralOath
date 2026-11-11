using System;
using System.Collections.Generic;
using System.IO;
using AstralOath.Client.Bootstrap;
using AstralOath.Client.Common;
using AstralOath.Client.CharacterLab;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace AstralOath.Editor
{
    public static class CharacterLabSceneBuilder
    {
        public const string ScenePath="Assets/Game/Modules/CharacterLab/Scenes/CharacterLab.unity";
        public const string PrefabPath="Assets/Game/Modules/CharacterLab/Res/Prefab/CharacterLabWindow.prefab";
        public const string BlurShaderPath="Assets/Game/Modules/CharacterPresentation/Res/Materials/CharacterSpriteBlur.shader";
        public const string BlurMaterialPath="Assets/Game/Modules/CharacterPresentation/Res/Materials/CharacterSpriteBlur.mat";
        private static readonly Color PanelColor=new Color32(24,34,51,248);
        [MenuItem("Tools/角色实验室/生成场景")]
        public static void CreateScene()
        {
            if(!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            AssetDatabase.Refresh();
            EnsureBlurMaterial();
            var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/Modules/FoundationDemo/Res/Fonts/FoundationFont.asset");
            if(!font) throw new InvalidOperationException("Foundation font must be imported first.");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var cameraObject=new GameObject("Character Preview Camera",typeof(Camera),typeof(AudioListener));
            var camera=cameraObject.GetComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=5;camera.nearClipPlane=.1f;camera.farClipPlane=100;
            camera.transform.position=new Vector3(0,1,-10);camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color32(18,25,38,255);cameraObject.tag="MainCamera";
            // The preview camera renders to a texture at runtime. Keep a separate backbuffer
            // camera so URP always presents a screen target before the Overlay Canvas.
            var outputObject=new GameObject("CharacterLab Screen Output Camera",typeof(Camera));
            var output=outputObject.GetComponent<Camera>();
            output.targetTexture=null;output.cullingMask=0;output.depth=camera.depth+1;
            output.clearFlags=CameraClearFlags.SolidColor;output.backgroundColor=camera.backgroundColor;
            output.allowHDR=false;output.allowMSAA=false;output.useOcclusionCulling=false;
            var stage=new GameObject("Character Stage");
            var canvas=new GameObject("CharacterLab Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var safe=Rect("Safe Area",canvas.transform);Stretch(safe);safe.gameObject.AddComponent<SafeAreaLayout>();
            var startup=Label("Startup Status",safe,font,Vector2.zero,new Vector2(1200,160),30);
            var window=CreateWindow(safe,font);
            if(!PrefabUtility.SaveAsPrefabAsset(window,PrefabPath))throw new IOException("Could not save CharacterLab window.");
            UnityEngine.Object.DestroyImmediate(window);
            new GameObject("Event System",typeof(EventSystem),typeof(StandaloneInputModule));
            var bootstrap=new GameObject("CharacterLab Bootstrap",typeof(CharacterLabBootstrap)).GetComponent<CharacterLabBootstrap>();
            var serialized=new SerializedObject(bootstrap);
            serialized.FindProperty("viewRoot").objectReferenceValue=safe;
            serialized.FindProperty("worldRoot").objectReferenceValue=stage.transform;
            serialized.FindProperty("previewCamera").objectReferenceValue=camera;
            serialized.FindProperty("startupStatus").objectReferenceValue=startup;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if(!EditorSceneManager.SaveScene(scene,ScenePath))throw new IOException("Could not save CharacterLab scene.");
            AssetDatabase.SaveAssets();
            CharacterLabAssets.ConfigureCollectors();
            UIViewRegistry.ValidatePrefab<CharacterLabWindow>(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            Debug.Log("ASTRAL_CHARACTERLAB_SCENE_READY");
        }
        private static void EnsureBlurMaterial()
        {
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(BlurShaderPath);
            if(!shader)throw new FileNotFoundException("CharacterLab blur shader is missing.",BlurShaderPath);
            var material=AssetDatabase.LoadAssetAtPath<Material>(BlurMaterialPath);
            if(!material)
            {
                material=new Material(shader){name="CharacterSpriteBlur"};
                AssetDatabase.CreateAsset(material,BlurMaterialPath);
            }
            else if(material.shader!=shader)material.shader=shader;
            material.SetFloat("u_range",0);material.SetFloat("u_sample",3);material.SetFloat("u_ratio",0);
            EditorUtility.SetDirty(material);
        }
        private static GameObject CreateWindow(Transform parent,TMP_FontAsset font)
        {
            var root=Rect("CharacterLabWindow",parent);Stretch(root);
            var map=root.gameObject.AddComponent<BindingMap>();
            var items=new List<(string,Component)>();
            var preview=Rect("Camera Preview",root);Size(preview,Vector2.zero,new Vector2(1920,1080));
            var raw=preview.gameObject.AddComponent<RawImage>();raw.raycastTarget=false;
            items.Add(("preview",raw));preview.gameObject.SetActive(false);
            void Text(string key,Transform container,Vector2 pos,Vector2 size,float point=22)
                =>items.Add((key,Label(key,container,font,pos,size,point)));
            void Button(string key,Transform container,Vector2 pos,Vector2 size)
            {
                var button=ButtonControl(key,container,font,pos,size,out var label);
                items.Add((key,button));items.Add((key+"Label",label));
            }
            var header=Panel("Header",root,new Vector2(0,480),new Vector2(1920,120));
            Text("title",header,new Vector2(-670,10),new Vector2(480,58),38);
            Text("subtitle",header,new Vector2(-70,8),new Vector2(650,48),22);
            Text("status",header,new Vector2(545,5),new Vector2(420,75),20);
            Button("close",header,new Vector2(840,5),new Vector2(120,46));
            var left=Panel("Browser",root,new Vector2(-750,-15),new Vector2(380,850));
            var inputRect=Panel("Search",left,new Vector2(0,375),new Vector2(340,46));
            var input=inputRect.gameObject.AddComponent<TMP_InputField>();
            var viewport=Rect("Viewport",inputRect);Stretch(viewport);viewport.gameObject.AddComponent<RectMask2D>();
            var inputText=Label("Text",viewport,font,Vector2.zero,new Vector2(320,42),22).GetComponent<TextMeshProUGUI>();
            var placeholder=Label("Placeholder",viewport,font,Vector2.zero,new Vector2(320,42),22);
            input.textViewport=viewport;input.textComponent=inputText;input.placeholder=placeholder.GetComponent<TextMeshProUGUI>();
            input.targetGraphic=inputRect.GetComponent<Image>();items.Add(("search",input));items.Add(("searchPlaceholder",placeholder));
            items.Add(("characters",ScrollList("Characters",left,new Vector2(0,260),new Vector2(340,164))));
            Button("battle",left,new Vector2(-87,145),new Vector2(166,42));Button("portrait",left,new Vector2(87,145),new Vector2(166,42));
            Text("animationsHeading",left,new Vector2(0,96),new Vector2(340,42));
            items.Add(("animations",ScrollList("Animations",left,new Vector2(0,-122),new Vector2(340,380))));
            Button("skin",left,new Vector2(0,-370),new Vector2(340,46));
            var right=Panel("Details",root,new Vector2(710,-15),new Vector2(460,850));
            Text("voicesHeading",right,new Vector2(0,385),new Vector2(420,42),21);
            items.Add(("voices",ScrollList("Voices",right,new Vector2(0,230),new Vector2(420,260))));
            Text("skillsHeading",right,new Vector2(0,70),new Vector2(420,42));
            items.Add(("skills",ScrollList("Skills",right,new Vector2(0,-20),new Vector2(420,132))));
            Text("diagnosticsHeading",right,new Vector2(0,-118),new Vector2(420,42));
            var diagnosticContent=ScrollList("Diagnostic Scroll",right,new Vector2(0,-274),new Vector2(420,260));
            var diagnostic=Label("Diagnostics",diagnosticContent,font,Vector2.zero,new Vector2(410,250),17);
            var layout=diagnostic.gameObject.AddComponent<LayoutElement>();layout.minHeight=250;
            diagnostic.GetComponent<TextMeshProUGUI>().alignment=TextAlignmentOptions.TopLeft;
            items.Add(("diagnostics",diagnostic));
            var controls=Panel("Playback",root,new Vector2(-50,-390),new Vector2(1020,260));
            Text("transport",controls,new Vector2(0,102),new Vector2(980,38),23);
            string[] first={"play","pause","stop","step","loop"};
            for(int i=0;i<first.Length;i++)Button(first[i],controls,new Vector2(-390+i*195,53),new Vector2(185,42));
            string[] second={"slower","faster","seekBack","seekForward","reset"};
            for(int i=0;i<second.Length;i++)Button(second[i],controls,new Vector2(-390+i*195,2),new Vector2(185,42));
            string[] third={"zoomIn","zoomOut","flip","background","experimental"};
            for(int i=0;i<third.Length;i++)Button(third[i],controls,new Vector2(-390+i*195,-49),new Vector2(185,42));
            Text("targets",controls,new Vector2(0,-100),new Vector2(980,38),17);
            var template=ButtonControl("RowTemplate",root,font,Vector2.zero,new Vector2(330,42),out _);
            template.gameObject.AddComponent<LayoutElement>().preferredHeight=42;template.gameObject.SetActive(false);
            items.Add(("rowTemplate",template));
            var serialized=new SerializedObject(map);var entries=serialized.FindProperty("entries");entries.arraySize=items.Count;
            for(int i=0;i<items.Count;i++) {var row=entries.GetArrayElementAtIndex(i);row.FindPropertyRelative("key").stringValue=items[i].Item1;row.FindPropertyRelative("component").objectReferenceValue=items[i].Item2;}
            serialized.ApplyModifiedPropertiesWithoutUndo();
            UIViewRegistry.ValidatePrefab<CharacterLabWindow>(root.gameObject);
            root.gameObject.SetActive(false);return root.gameObject;
        }
        private static RectTransform ScrollList(string name,Transform parent,Vector2 position,Vector2 size)
        {
            var root=Panel(name,parent,position,size);
            var scroll=root.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=35;
            var viewport=Rect("Viewport",root);Stretch(viewport);viewport.gameObject.AddComponent<RectMask2D>();
            var content=Rect("Content",viewport);content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(.5f,1);content.sizeDelta=Vector2.zero;
            var layout=content.gameObject.AddComponent<VerticalLayoutGroup>();layout.spacing=6;layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;
            var fitter=content.gameObject.AddComponent<ContentSizeFitter>();fitter.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport=viewport;scroll.content=content;return content;
        }
        private static GameButton ButtonControl(string name,Transform parent,TMP_FontAsset font,Vector2 pos,Vector2 size,out GameText label)
        {
            var root=Panel(name,parent,pos,size);root.GetComponent<Image>().color=new Color32(43,64,87,255);
            var button=root.gameObject.AddComponent<Button>();button.targetGraphic=root.GetComponent<Image>();
            label=Label("Label",root,font,Vector2.zero,size-new Vector2(16,0),19);
            var rect=label.GetComponent<RectTransform>();Stretch(rect);rect.offsetMin=new Vector2(8,0);rect.offsetMax=new Vector2(-8,0);
            return root.gameObject.AddComponent<GameButton>();
        }
        private static GameText Label(string name,Transform parent,TMP_FontAsset font,Vector2 pos,Vector2 size,float point)
        {
            var rect=Rect(name,parent);Size(rect,pos,size);
            var label=rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.fontSize=point;label.color=new Color32(231,238,247,255);label.alignment=TextAlignmentOptions.MidlineLeft;
            label.raycastTarget=false;label.richText=false;
            var control=rect.gameObject.AddComponent<GameText>();
            // TMP initialization can overwrite its asset; persist the font/material pair last.
            var serialized=new SerializedObject(label);
            serialized.FindProperty("m_fontAsset").objectReferenceValue=font;
            serialized.FindProperty("m_sharedMaterial").objectReferenceValue=font.material;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return control;
        }
        private static RectTransform Panel(string name,Transform parent,Vector2 pos,Vector2 size)
        {
            var rect=Rect(name,parent);Size(rect,pos,size);rect.gameObject.AddComponent<Image>().color=PanelColor;return rect;
        }
        private static RectTransform Rect(string name,Transform parent)
        {var rect=(RectTransform)new GameObject(name,typeof(RectTransform)).transform;rect.SetParent(parent,false);return rect;}
        private static void Size(RectTransform rect,Vector2 pos,Vector2 size)
        {rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.anchoredPosition=pos;rect.sizeDelta=size;}
        private static void Stretch(RectTransform rect)
        {rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
    }
}
