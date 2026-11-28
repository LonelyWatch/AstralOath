using System;
using System.Text.RegularExpressions;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    public sealed class ArtifactInformationBinder
    {
        public ArtifactInformationBinder(ArtifactInformation view, ArtifactArtCatalog.Entry entry, ILocaleService locale, BindingScope scope, Action close)
        {
            var map=view.Map;
            var template=entry.template;
            var dismiss=map.Require<GameButton>("close");
            dismiss.KeepBrightnessOnPress=true;
            Hook(dismiss,()=>close());
            Art("card",entry.card);
            Art("icon",entry.combatIcon);
            Art("paintingArt",entry.card);
            var painting=map.Require<GameObject>("painting");
            var page=map.Require<RectTransform>("paintingPage");
            var image=map.Require<Image>("paintingArt");
            var gestures=new ArtifactPaintingGestures(image.rectTransform);
            bool rotated=false;
            Vector2 lastSize=Vector2.zero;
            map.Require<GameText>("paintingTitle").text=template.Name;
            map.Require<GameText>("rotateLabel").text=locale.Resolve("home.artifacts.rotate");
            map.Require<GameText>("gestureHelp").text=locale.Resolve("home.artifacts.painting_help");
            Hook(map.Require<GameButton>("zoom"),()=>
            {
                rotated=false; lastSize=Vector2.zero;
                painting.SetActive(true); Orient(); gestures.Reset();
            });
            Hook(map.Require<GameButton>("paintBack"),()=>painting.SetActive(false));
            Hook(map.Require<GameButton>("rotate"),()=>{rotated=!rotated;lastSize=Vector2.zero;Orient();gestures.Reset();});
            Canvas.willRenderCanvases+=RenderPainting;
            scope.Add(()=>Canvas.willRenderCanvases-=RenderPainting);
            map.Require<GameText>("name").text=template.Name;
            for (int i = 0; i < 5; i++)
                map.Require<Image>("star" + i).enabled = i < template.Stars;
            map.Require<GameText>("grade").text=locale.Resolve("home.artifacts.rarity"+template.Stars);
            foreach (var role in new[]{"warrior","knight","ranger","mage","thief","soul_weaver"})
                map.Require<Image>("class."+role).enabled=role==template.HeroClass;
            map.Require<GameText>("attackLabel").text=locale.Resolve("home.hero_inventory.stat.attack");
            map.Require<GameText>("healthLabel").text=locale.Resolve("home.catalog.health");
            map.Require<GameText>("attack").SetText("{0}",template.AttackAt(0));
            map.Require<GameText>("health").SetText("{0}",template.HealthAt(0));
            var skill=map.Require<GameButton>("skill");
            var story=map.Require<GameButton>("story");
            map.Require<GameText>("skillLabel").text=locale.Resolve("home.artifacts.catalogSkill");
            map.Require<GameText>("storyLabel").text=locale.Resolve("home.artifacts.catalogStory");
            string skillFormat = Regex.Replace(template.Description, @"\{[01]\}%?", "<color=#FFAA27>$0</color>");
            string skillDescription = template.FormatDescription(0, skillFormat);
            Hook(skill,()=>Select(false));
            Hook(story,()=>Select(true));
            Select(false);
            view.Show();

            void Select(bool background)
            {
                var description = map.Require<GameText>("description");
                description.richText = !background;
                description.text = background ? locale.Resolve("home.artifacts.story."+template.Id) : skillDescription;
                skill.targetGraphic.color=background?Color.clear:Color.white;
                story.targetGraphic.color=background?Color.white:Color.clear;
                map.Require<GameText>("skillLabel").color=background?Color.gray:Color.black;
                map.Require<Image>("skillGlyph").color=background?Color.gray:Color.black;
                map.Require<GameText>("storyLabel").color=background?Color.black:Color.gray;
                var scroll=map.Require<ScrollRect>("descriptionScroll");
                scroll.StopMovement();scroll.content.anchoredPosition=Vector2.zero;
            }
            void RenderPainting()
            {
                if (!painting || !painting.activeInHierarchy) return;
                Orient();
                gestures.Tick();
            }
            void Orient()
            {
                var size=((RectTransform)painting.transform).rect.size;
                if(size==lastSize) return;
                lastSize=size;
                page.anchorMin=page.anchorMax=page.pivot=Vector2.one*.5f;
                page.sizeDelta=rotated?new Vector2(size.y,size.x):size;
                page.anchoredPosition=Vector2.zero;
                page.localEulerAngles=new Vector3(0,0,rotated?90:0);
                float width = rotated ? .82f : .96f;
                image.rectTransform.anchorMin=new Vector2((1-width)*.5f,.022f);
                image.rectTransform.anchorMax=new Vector2((1+width)*.5f,.978f);
                image.rectTransform.offsetMin=image.rectTransform.offsetMax=Vector2.zero;
                // Use screen height for source pixels. Only the outside margin changes on wider screens.
                float span = size.y / Mathf.Max(1f,size.x) / 1200f;
                Place((RectTransform)map.Require<GameButton>("rotate").transform, rotated ?
                    new Rect(.702f,1-371*span,.203f,87*span) : new Rect(.5f+236*span,.705f,244*span,.067f));
                Place(map.Require<RectTransform>("gesturePanel"), rotated ?
                    new Rect(.09f,1-273*span,.8175f,221*span) : new Rect(.5f-456*span,.79f,938*span,.181f));
                Place(map.Require<RectTransform>("galleryShadow"), rotated ?
                    new Rect(-.16f,.75f,1.32f,.38f) : new Rect(.24f,.53f,.52f,.59f));
            }
            void Place(RectTransform target,Rect area)
            {
                target.anchorMin=new Vector2(area.xMin,1-area.yMax);
                target.anchorMax=new Vector2(area.xMax,1-area.yMin);
                target.offsetMin=target.offsetMax=Vector2.zero;
            }
            void Art(string key,Sprite sprite)
            {
                var image=map.Require<Image>(key);
                image.sprite=sprite;
                image.enabled=sprite;
            }
            void Hook(GameButton button,UnityAction action)
            {
                button.onClick.AddListener(action);
                scope.Add(()=>{if(button)button.onClick.RemoveListener(action);});
            }
        }
    }
}
