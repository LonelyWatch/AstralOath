using System;
using System.Collections.Generic;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    public sealed class HeroInformationBinder
    {
        public HeroInformationBinder(HeroInformation view, HomeHeroArt.Hero hero, ILocaleService locale, BindingScope scope, Action close)
        {
            var map=view.Map;
            var dismiss=map.Require<GameButton>("close");
            dismiss.KeepBrightnessOnPress=true;
            Hook(dismiss,()=>close());
            var portrait= new HeroRosterPortrait(map.Require<RawImage>("animated"),1024,true,15000,1.19f,.18f);
            var animated=portrait.Show(hero);
            portrait.SetVisible(animated);
            var fallback=map.Require<Image>("illustration");
            fallback.sprite=hero.illustration;
            fallback.enabled=!animated&&fallback.sprite;
            Canvas.willRenderCanvases+=portrait.Tick;
            scope.Add(()=>{ Canvas.willRenderCanvases-=portrait.Tick;portrait.Dispose(); });
            var inspect=new InfoPanelBinder(map.Require<GameObject>("skillTemplate"),(RectTransform)view.Root.transform);
            scope.Add(inspect.Dispose);
            var name=map.Require<GameText>("name");
            name.text=hero.name;
            var stars=map.Require<RectTransform>("stars");
            stars.anchorMin=new Vector2(.036f,stars.anchorMin.y);
            stars.anchorMax=new Vector2(.036f,stars.anchorMax.y);
            stars.offsetMin=new Vector2(Mathf.Min(520,name.preferredWidth)+10,0);
            stars.offsetMax=new Vector2(stars.offsetMin.x+151,0);
            int grade = hero.growth?.nativeStars ?? 5;
            for(int i=0;i<stars.childCount;i++) stars.GetChild(i).gameObject.SetActive(i<grade);
            for(int i=1;i<=6;i++) map.Require<Image>("limit"+i).enabled = i==grade;
            string introKey="summon.reveal."+hero.id+".intro";
            var intro=map.Require<GameText>("intro");
            try { intro.text=locale.Resolve(introKey); }
            catch (KeyNotFoundException) { intro.gameObject.SetActive(false); }
            float tagX = 36;
            Tag("element",hero.element,hero.elementId);
            Tag("role",hero.role,hero.heroClass);
            Tag("zodiac",hero.zodiacIcon,hero.zodiac);
            map.Require<Image>("imprintFormation").sprite = hero.imprintFormation;
            map.Require<Image>("imprintFormation").enabled = hero.imprintFormation;
            map.Require<Image>("imprintFormation").color = new Color32(94,94,94,255);
            var imprint = hero.imprint;
            map.Require<GameText>("imprintLock").text = locale.Resolve("home.catalog.imprint.locked");
            map.Require<GameText>("imprintLabel").text = imprint.stat + "\n" + locale.Resolve("home.catalog.imprint.bonus");
            var inspectImprint = map.Require<GameButton>("imprintButton");
            Hook(inspectImprint,()=>inspect.Show((RectTransform)inspectImprint.transform,
                new InfoPanelBinder.Card(hero.imprintFormation, locale.Resolve("home.catalog.imprint.title"),
                    imprint.stat + "\n" + imprint.release + "\n\n" + imprint.selfStat + "\n" + imprint.focus), preferRight:true));
            for(int i=0;i<3;i++)
            {
                var button=map.Require<GameButton>("skill"+i);
                var skill=hero.skills!=null && i<hero.skills.Length?hero.skills[i]:null;
                button.gameObject.SetActive(skill!=null);
                if(skill==null) continue;
                var icon=map.Require<Image>("skillIcon"+i);
                icon.sprite=skill.icon;
                icon.enabled=icon.sprite;
                var anchor = map.Require<RectTransform>("skillAnchor"+i);
                Hook(button,()=>
                {
                    InfoPanelBinder.Card? effect = null;
                    if (!string.IsNullOrEmpty(skill.effectName))
                        effect = new InfoPanelBinder.Card(skill.effectIcon,skill.effectName,skill.effectDescription,
                            skill.debuff ? new Color32(92,41,39,242) : new Color32(27,67,104,255),
                            skill.debuff ? new Color32(255,153,146,255) : new Color32(101,190,255,255));
                    inspect.Show(anchor,new InfoPanelBinder.Card(skill.icon,skill.name,skill.description,
                        soul:skill.soul,cooldown:skill.cooldown,burnTitle:skill.burnTitle,burnCost:skill.burnCost,
                        burnDescription:skill.burnDescription,upgrades:skill.upgrades),effect,preferRight:true);
                });
            }
            view.Show();

            void Tag(string type,Sprite sprite,string id)
            {
                var image=map.Require<Image>(type);
                image.sprite=sprite;
                image.enabled=sprite;
                var label = map.Require<GameText>(type+"Name");
                label.text=locale.Resolve("summon.reveal."+type+"."+id);
                Position(image.rectTransform, tagX, 44);
                tagX += 44;
                float width = label.preferredWidth + 4;
                Position(label.rectTransform, tagX, width);
                tagX += width + 7;
            }
            void Position(RectTransform rect,float left,float width)
            {
                rect.anchorMin = new Vector2(0,rect.anchorMin.y);
                rect.anchorMax = new Vector2(0,rect.anchorMax.y);
                rect.offsetMin = new Vector2(left,0);
                rect.offsetMax = new Vector2(left+width,0);
            }
            void Hook(GameButton button,UnityAction action)
            {
                button.onClick.AddListener(action);
                scope.Add(()=>{if(button)button.onClick.RemoveListener(action);});
            }
        }
    }
}
