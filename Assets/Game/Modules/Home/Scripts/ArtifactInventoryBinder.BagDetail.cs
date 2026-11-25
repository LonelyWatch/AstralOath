using System;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    internal sealed partial class ArtifactInventoryBinder
    {
        private readonly Func<string, HeroInventoryItem> findWearer;
        private NotificationBadgeBinder detailBadge;
        private Sprite unlockedPlate;
        private string detailInstance;
        private bool bagStory;

        private void BindBagDetail(BindingScope scope)
        {
            detailBadge = new NotificationBadgeBinder(ui.Get<BindingMap>("DetailBadge"));
            unlockedPlate = (ui.Action("lock").targetGraphic as Image).sprite;
            void Hook(string key, Action action) => BindAction(scope, ui.Get<GameButton>(key), _ =>
            {
                if (!busy && pending == null && selected != null) action();
                return Task.CompletedTask;
            });
            Hook("DetailZoom", OpenArtifactPainting);
            Hook("Change", () => OpenReplacementFromBag(findWearer?.Invoke(selected.heroInstanceId), selected));
            Hook("DetailSkillTab", () => SelectBagDetailTab(false));
            Hook("DetailStoryTab", () => SelectBagDetailTab(true));
        }

        private void SelectBagDetailTab(bool showStory)
        {
            if (bagStory == showStory) return;
            bagStory = showStory;
            DrawBagDescription(art.Find(selected.templateId));
            ResetBagDescriptionScroll();
        }

        private void ResetBagDescriptionScroll()
        {
            var scroll = ui.Get<ScrollRect>("DescriptionScroll");
            scroll.StopMovement();
            scroll.content.anchoredPosition = Vector2.zero;
        }

        private void PaintBagDetail(ArtifactArtCatalog.Entry entry)
        {
            if (detailInstance != selected.instanceId)
            {
                detailInstance = selected.instanceId;
                bagStory = false;
                ResetBagDescriptionScroll();
            }
            var grade = ui.Get<GameText>("DetailGrade");
            grade.text = locale.Resolve(Prefix + "rarity" + entry.template.Stars);
            grade.color = entry.template.Stars == 5 ? new Color32(250,121,70,255) : entry.template.Stars == 4
                ? new Color32(225,124,233,255) : new Color32(109,175,239,255);
            SetImage(ui.Get<Image>("DetailEmblem"), entry.combatIcon);
            detailBadge.Set(false, selected.level, 15 + 3 * selected.limitBreaks);
            var portrait = HasWearer(selected) ? heroPortrait?.Invoke(selected.heroInstanceId) : null;
            SetImage(ui.Get<Image>("DetailWearer"), portrait);
            ui.Get<GameObject>("DetailWearerFrame").SetActive(portrait);
            int role = Array.IndexOf(FilterClasses, entry.template.HeroClass);
            SetImage(ui.Get<Image>("DetailClass"), role > 0 ? classIcons[role] : null);
            var icon = ui.Get<Image>(selected.locked ? "LockOn" : "LockOff");
            icon.color = selected.locked ? Color.black : Color.white;
            ui.Get<Image>("LockOn").enabled = selected.locked;
            ui.Get<Image>("LockOff").enabled = !selected.locked;
            ((Image)ui.Action("lock").targetGraphic).sprite = selected.locked ? ui.Get<Image>("DetailSkillActive").sprite : unlockedPlate;
            ui.Get<Image>("SellGlyph").color = selected.locked || HasWearer(selected)
                ? new Color32(116,128,141,130) : Color.white;
            ui.Get<GameButton>("Change").gameObject.SetActive(HasWearer(selected) && pickerHero == null);
            DrawBagDescription(entry);
        }

        private void DrawBagDescription(ArtifactArtCatalog.Entry entry)
        {
            ui.Get<GameText>("Skill").text = bagStory ? ArtifactStory(selected) : entry.template.DescriptionAt(selected.level);
            ui.Get<Image>("DetailSkillActive").enabled = !bagStory;
            ui.Get<Image>("DetailStoryActive").enabled = bagStory;
            ui.Get<GameText>("DetailSkillLabel").color = bagStory ? Color.gray : Color.black;
            ui.Get<Image>("DetailSkillGlyph").color = bagStory ? Color.gray : Color.black;
            ui.Get<GameText>("DetailStoryLabel").color = bagStory ? Color.black : Color.gray;
        }
    }
}
