using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    internal sealed partial class ArtifactInventoryBinder
    {
        private BindingMap heroPage;
        private ScrollRect heroList;
        private readonly List<ArtifactInventoryCardBinder> heroRows = new List<ArtifactInventoryCardBinder>();
        private ArtifactInventoryItem[] heroChoices = Array.Empty<ArtifactInventoryItem>();
        private ArtifactInventoryItem bagReturnItem;
        private bool inspectingWorn, paintingOpen, storyTab;
        private ScrollRect descriptionScroll;
        private float lastHeroListWidth;
        private int lastGestureFrame;
        private float previousPinch;
        private float paintingZoom = 1;
        private bool rotated;
        private Vector2 dragOrigin;
        private float lastTap;

        private T HeroControl<T>(string key) where T : UnityEngine.Object => heroPage.Require<T>(key);
        private string Local(string key) => locale.Resolve(Prefix + key);
        private ArtifactInventoryItem WornArtifact(HeroInventoryItem owner) =>
            owner == null ? null : snapshot?.artifacts?.FirstOrDefault(item => item.heroInstanceId == owner.instanceId);

        internal bool OpenWornFromBag(HeroInventoryItem owner)
        {
            var worn = WornArtifact(owner);
            if (worn == null || busy) return false;
            bagReturnItem = null;
            pickerHero = owner;
            active = inspectingWorn = true;
            storyTab = false;
            selected = worn;
            Refresh(true);
            return true;
        }

        internal bool OpenReplacementFromBag(HeroInventoryItem owner, ArtifactInventoryItem item)
        {
            if (owner == null || item == null || busy) return false;
            bagReturnItem = item;
            pickerHero = owner;
            active = true;
            inspectingWorn = false;
            selected = item;
            Refresh(true);
            return true;
        }

        private void BindHeroEquipment(BindingScope scope)
        {
            heroPage = ui.Get<BindingMap>("HeroEquipment");
            heroList = HeroControl<ScrollRect>("list");
            descriptionScroll = HeroControl<GameText>("popupBody").GetComponentInParent<ScrollRect>(true);
            void Click(string key, Action action) => BindAction(scope, HeroControl<GameButton>(key), _ =>
            {
                if (!busy) action();
                return Task.CompletedTask;
            });
            Click("outside", () => ClosePicker());
            HeroControl<GameButton>("back").FeedbackSound = GameUiFeedback.Sound.PageBack;
            HeroControl<GameButton>("paintBack").FeedbackSound = GameUiFeedback.Sound.PageBack;
            Click("back", () => ClosePicker());
            Click("change", () => { inspectingWorn = false; Refresh(true); });
            Click("skillTab", () => { storyTab = false; DrawHeroItem(); ResetWornDescription(); });
            Click("storyTab", () => { storyTab = true; DrawHeroItem(); ResetWornDescription(); });
            Click("cardZoom", OpenArtifactPainting);
            Click("popupZoom", OpenArtifactPainting);
            Click("paintBack", () => { paintingOpen = false; Refresh(false); });
            Click("rotate", () => { rotated = !rotated; ResetPainting(); });
            BindAction(scope, HeroControl<GameButton>("training"), OpenEnhancementAsync);
            BindAction(scope, HeroControl<GameButton>("enhance"), OpenEnhancementAsync);
            BindAction(scope, HeroControl<GameButton>("retry"), LoadAsync);
            HeroControl<GameButton>("lock").FeedbackSound = GameUiFeedback.Sound.Silent;
            BindAction(scope, HeroControl<GameButton>("lock"), token => SubmitHeroAction("lock",token));
            BindAction(scope, HeroControl<GameButton>("equip"), token => SubmitHeroAction("equip",token));
            BindAction(scope, HeroControl<GameButton>("remove"), token => SubmitHeroAction("unequip",token));
            BindAction(scope, HeroControl<GameButton>("unequip"), token => SubmitHeroAction("unequip",token));
            heroList.onValueChanged.AddListener(HeroScroll);
            Canvas.willRenderCanvases += UpdateHeroPresentation;
            scope.Add(() =>
            {
                Canvas.willRenderCanvases -= UpdateHeroPresentation;
                if (heroList) heroList.onValueChanged.RemoveListener(HeroScroll);
                foreach (var row in heroRows) if (row.Root) UnityEngine.Object.Destroy(row.Root.gameObject);
                heroRows.Clear();
            });
        }

        private async Task SubmitHeroAction(string action, CancellationToken token)
        {
            if (busy || pending != null || pickerHero == null || selected == null) return;
            if (action == "equip" && HasWearer(selected))
            {
                await PreviewAction(action);
                return;
            }
            pending = new Pending(action,selected,pickerHero.instanceId);
            await ConfirmAsync(token);
        }

        private void OpenArtifactPainting()
        {
            if (selected == null) return;
            rotated = false;
            paintingOpen = true;
            lastTap = -1;
            ResetPainting();
            Refresh(false);
        }

        private bool RefreshHeroEquipment(bool reset)
        {
            if (!heroPage) return false;
            bool showing = active && !EnhancementOpen && (pickerHero != null || paintingOpen) && !pickingMaterial;
            heroPage.gameObject.SetActive(showing);
            if (!showing && !EnhancementOpen) return false;
            browser.gameObject.SetActive(false);
            ui.Get<GameButton>("PickerShade").gameObject.SetActive(false);
            ui.Get<GameObject>("Confirmation").SetActive(showing && pending != null);
            if (EnhancementOpen) return true;
            HeroControl<GameObject>("popup").SetActive(!paintingOpen && inspectingWorn);
            HeroControl<GameObject>("picker").SetActive(!paintingOpen && !inspectingWorn);
            HeroControl<GameObject>("painting").SetActive(paintingOpen);
            if (paintingOpen && selected != null)
            {
                var entry = art.Find(selected.templateId);
                if (entry != null)
                {
                    SetImage(HeroControl<Image>("paintingImage"),entry.card);
                    HeroControl<GameText>("paintingTitle").text = entry.template.Name;
                }
                return true;
            }
            var candidates = snapshot?.artifacts?.Where(CanDisplay) ?? Enumerable.Empty<ArtifactInventoryItem>();
            heroChoices = OrderedArtifacts(candidates, pickerOptions);
            selected = inspectingWorn ? WornArtifact(pickerHero) :
                heroChoices.FirstOrDefault(item=>item.instanceId == selected?.instanceId) ?? heroChoices.FirstOrDefault();
            HeroControl<GameText>("capacity").text = snapshot == null ? "" : Format("capacity",snapshot.used,snapshot.capacity);
            HeroControl<GameText>("sortLabel").text = locale.Resolve(sortCaptionKeys[pickerOptions.order]);
            var status = HeroControl<GameText>("status");
            status.gameObject.SetActive(failed || loading || selected == null || failure != null);
            status.text = Local(failure ?? (loading ? "loading" : failed ? "load_failed" : "no_matches"));
            HeroControl<GameButton>("retry").gameObject.SetActive(failed);
            HeroControl<GameObject>("content").SetActive(selected != null);
            if (reset) { heroList.StopMovement(); heroList.content.anchoredPosition=Vector2.zero; }
            DrawHeroItem();
            if (reset && inspectingWorn) ResetWornDescription();
            DrawHeroRows();
            if (pending != null) DrawConfirmation();
            return true;
        }

        private void ResetWornDescription()
        {
            if (!descriptionScroll) return;
            descriptionScroll.StopMovement();
            descriptionScroll.content.anchoredPosition = Vector2.zero;
        }

        private void DrawHeroItem()
        {
            if (selected == null) return;
            var entry = art.Find(selected.templateId);
            var item = selected;
            var template = entry.template;
            string prefix = inspectingWorn ? "popup" : "card";
            SetImage(HeroControl<Image>(prefix+"Portrait"),entry.card);
            SetImage(HeroControl<Image>(prefix+"Emblem"),entry.combatIcon);
            SetImage(HeroControl<Image>(prefix+"Wearer"),HasWearer(item) ? heroPortrait?.Invoke(item.heroInstanceId) : null);
            new NotificationBadgeBinder(HeroControl<BindingMap>(prefix+"Badge")).Set(false,item.level,15 + 3 * item.limitBreaks);
            for (int i = 0; i < 5; ++i)
            {
                HeroControl<Image>(prefix+"Star"+i).enabled = i < template.Stars;
                HeroControl<Image>(prefix+"Break"+i).enabled = i < item.limitBreaks;
            }
            HeroControl<GameText>(prefix+"Attack").text = template.AttackAt(item.level).ToString(CultureInfo.InvariantCulture);
            HeroControl<GameText>(prefix+"Health").text = template.HealthAt(item.level).ToString(CultureInfo.InvariantCulture);
            var grade = HeroControl<GameText>(inspectingWorn ? "popupGrade" : "grade");
            grade.text = Local("rarity"+template.Stars);
            grade.color = template.Stars == 5 ? new Color32(250,121,70,255) : template.Stars == 4 ?
                new Color32(225,124,233,255) : new Color32(109,175,239,255);
            HeroControl<GameText>(inspectingWorn ? "popupName" : "name").text = template.Name;
            HeroControl<GameText>(inspectingWorn ? "popupLevel" : "level").text = ArtifactSkillLevel(item);
            var training = HeroControl<GameButton>(inspectingWorn ? "enhance" : "training");
            training.interactable = item.level < 30;
            HeroControl<GameText>(inspectingWorn ? "enhanceLabel" : "trainingLabel").text =
                Local(item.level >= 15+item.limitBreaks*3 && item.limitBreaks < 5 ? "limit_break" : "enhance");
            if (inspectingWorn)
            {
                HeroControl<Image>("lockGlyph").color = item.locked ? Color.white : new Color32(151,165,182,255);
                HeroControl<GameText>("popupBody").text = storyTab ? ArtifactStory(item) : template.DescriptionAt(item.level);
                foreach (var tab in new[]{"skillTab","storyTab"})
                {
                    bool activeTab = (tab == "storyTab") == storyTab;
                    HeroControl<GameButton>(tab).targetGraphic.color = activeTab ? Color.white : new Color32(0,0,0,76);
                    HeroControl<GameText>(tab+"Label").color = activeTab ? Color.black : new Color32(163,179,191,255);
                }
                HeroControl<Image>("skillTabGlyph").color = storyTab ? new Color32(163,179,191,255) : Color.black;
                return;
            }
            bool worn = item.heroInstanceId == pickerHero?.instanceId;
            var state = HeroControl<GameText>("state");
            state.text = locale.Resolve("home.gear."+(worn ? "singleWorn" : "singleChoosing"));
            HeroControl<Image>("stateRule").color = state.color = worn ? new Color32(229,170,67,255) : new Color32(139,216,51,255);
            HeroControl<GameText>("story").text = ArtifactStory(item);
            HeroControl<GameText>("cardName").text = template.Name;
            HeroControl<GameText>("class").text = string.IsNullOrEmpty(template.HeroClass) ? "" :
                Format("class_only",locale.Resolve("home.hero_inventory.role."+template.HeroClass));
            HeroControl<GameText>("body").text = template.DescriptionAt(item.level);
            HeroControl<GameButton>("equip").gameObject.SetActive(!worn);
            HeroControl<GameButton>("unequip").gameObject.SetActive(worn);
        }

        private void HeroScroll(Vector2 _) => DrawHeroRows();

        private void DrawHeroRows()
        {
            if (!heroList || !heroList.gameObject.activeInHierarchy) return;
            float width = heroList.viewport.rect.width;
            if (width < 1) return;
            lastHeroListWidth = width;
            float inset = width * .028f;
            float rowWidth = width - 2f * inset;
            float rowHeight = rowWidth * 135f / 358f;
            heroList.content.sizeDelta = new Vector2(0,heroChoices.Length*rowHeight+inset);
            int first = Mathf.Max(0,Mathf.FloorToInt(heroList.content.anchoredPosition.y/rowHeight)-1);
            int count = Mathf.Clamp(heroChoices.Length-first,0,Mathf.CeilToInt(heroList.viewport.rect.height/rowHeight)+3);
            while (heroRows.Count < count)
            {
                var row = new ArtifactInventoryCardBinder(UnityEngine.Object.Instantiate(HeroControl<GameObject>("row"),heroList.content,false));
                row.Button.onClick.AddListener(() =>
                {
                    if (busy || pending != null) return;
                    selected = row.Item;
                    DrawHeroItem();
                    DrawHeroRows();
                });
                heroRows.Add(row);
            }
            for(int i=0;i<heroRows.Count;++i)
            {
                var row=heroRows[i];
                if(i>=count) { row.Root.gameObject.SetActive(false); continue; }
                row.Root.anchorMin = row.Root.anchorMax = row.Root.pivot = new Vector2(0,1);
                row.Root.sizeDelta=new Vector2(rowWidth,rowHeight);
                row.Root.anchoredPosition=new Vector2(inset,-.35f * inset-(first+i)*rowHeight);
                var item=heroChoices[first+i];
                var entry=art.Find(item.templateId);
                int role=Array.IndexOf(FilterClasses,entry.template.HeroClass);
                row.Display(item,entry,role>0 ? classIcons[role] : null,ArtifactSkillLevel(item),selected==item,
                    HasWearer(item) ? heroPortrait?.Invoke(item.heroInstanceId) : null);
            }
        }

        private void ResetPainting()
        {
            var image=HeroControl<Image>("paintingImage").rectTransform;
            paintingZoom=1;
            previousPinch=0;
            image.anchoredPosition=Vector2.zero;
            image.localScale=Vector3.one;
            image.localEulerAngles=new Vector3(0,0,rotated ? 90 : 0);
        }

        private void UpdateHeroPresentation()
        {
            if (!heroPage || !heroPage.gameObject.activeInHierarchy) return;
            if (!paintingOpen)
            {
                if (heroList.gameObject.activeInHierarchy && !Mathf.Approximately(lastHeroListWidth,heroList.viewport.rect.width)) DrawHeroRows();
                return;
            }
            if (lastGestureFrame==Time.frameCount) return;
            lastGestureFrame=Time.frameCount;
            var img=HeroControl<Image>("paintingImage").rectTransform;
            if(Input.touchCount>1)
            {
                float distance=Vector2.Distance(Input.GetTouch(0).position,Input.GetTouch(1).position);
                if(previousPinch>0) paintingZoom=Mathf.Clamp(paintingZoom*distance/previousPinch,1,4);
                previousPinch=distance;
            }
            else
            {
                previousPinch=0;
                var mouse=(Vector2)Input.mousePosition;
                if(Input.GetMouseButtonDown(0))
                {
                    dragOrigin=mouse;
                    if(Time.unscaledTime-lastTap<.30f) ResetPainting();
                    lastTap=Time.unscaledTime;
                }
                if(Input.GetMouseButton(0))
                {
                    float scale=img.GetComponentInParent<Canvas>().rootCanvas.scaleFactor;
                    img.anchoredPosition+=(mouse-dragOrigin)/Mathf.Max(.01f,scale);
                    dragOrigin=mouse;
                }
                paintingZoom=Mathf.Clamp(paintingZoom+Input.mouseScrollDelta.y*.12f,1,4);
            }
            img.localScale=Vector3.one*paintingZoom;
        }
    }
}
