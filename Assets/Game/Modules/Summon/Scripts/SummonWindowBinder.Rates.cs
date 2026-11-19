using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Summon
{
    public sealed partial class SummonWindowBinder
    {
        private Task ShowInfo()
        {
            var banner = Banner;
            if (banner == null) return Task.CompletedTask;
            ShowPanel("infoPanel");
            bool wishes = banner.usesWishlist;
            view.Text("infoNoteTitle").text = L(wishes ? "info.covenant_notes" : "info.pickup_notes");
            string[] notices = wishes ? new[] { L("info.wish1"), L("info.wish2"), L("info.wish3"), L("info.wish4") }
                : new[] { F("info.pickup1", Name("hero", banner.featuredHero)), F("info.pickup2", Name("artifact", banner.featuredArtifact)),
                    banner.guaranteeAfter > 0 ? F("info.pity", banner.guaranteeAfter) : "", L("info.pickup4") };
            for (int index = 0; index < notices.Length; ++index)
            {
                var label = view.Text("infoNote" + index);
                label.text = notices[index];
                label.transform.parent.gameObject.SetActive(notices[index].Length > 0);
            }
            return ShowRateTab(false);
        }

        private Task ShowRateTab(bool rates)
        {
            view.Panel("infoRatesTabSelected").SetActive(rates);
            view.Panel("infoNotesTabSelected").SetActive(!rates);
            view.Panel("infoNotes").SetActive(!rates);
            view.Panel("infoRates").SetActive(rates);
            if (!rates) return Task.CompletedTask;
            var entries = Banner.entries;
            var totalWeight = entries.Sum(entry => (long)entry.weight);
            var heroGroups = entries.Where(entry => entry.kind == "hero").GroupBy(entry =>
                (Moonlight: IsMoonlight(entry), Stars: entry.stars)).ToArray();
            var artifactGroups = entries.Where(entry => entry.kind == "artifact").GroupBy(entry =>
                (Moonlight: false, Stars: entry.stars)).ToArray();
            // Keep the two item breakdowns on a common baseline, including when their grade counts differ.
            int gradeRows = Math.Max(heroGroups.Length, artifactGroups.Length);
            float heroHeight = DrawProbabilityColumn("Hero", "hero", heroGroups, gradeRows, totalWeight);
            float artifactHeight = DrawProbabilityColumn("Artifact", "artifact", artifactGroups, gradeRows, totalWeight);
            view.Items("infoItems").SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(heroHeight, artifactHeight));
            var scroll = view.Panel("infoRates").GetComponent<ScrollRect>();
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 1;
            return Task.CompletedTask;
        }

        private bool IsMoonlight(SummonEntry entry) => heroes.TryGetValue(entry.templateId, out var art)
            && art.elementId is "light" or "dark";

        private float DrawProbabilityColumn(string key, string kind,
            IGrouping<(bool Moonlight, int Stars), SummonEntry>[] groups, int gradeRows, long totalWeight)
        {
            string columnKey = "info" + key + "Column";
            Clear(columnKey);
            var column = view.Items(columnKey);
            var template = view.Panel("infoRowTemplate");
            string displayKind = L("entry." + kind);
            groups = groups.OrderBy(group => group.Key.Moonlight).ThenByDescending(group => group.Key.Stars).ToArray();
            string Percent(long weight) => (totalWeight > 0 ? 100m * weight / totalWeight : 0m)
                .ToString("0.#####", CultureInfo.InvariantCulture) + "%";
            string Grade((bool Moonlight, int Stars) group) => F("info.stars", group.Stars, group.Moonlight ? L("info.moonlight") : displayKind);
            view.Text("info" + key + "Total").text = F("info.total", displayKind,
                Percent(groups.SelectMany(group => group).Sum(entry => (long)entry.weight)));
            const float scale = 1.5f;
            float y = 0;
            void Row(string name, string value, float height, bool item = false, bool divider = false)
            {
                var instance = UnityEngine.Object.Instantiate(template, column, false);
                var rect = (RectTransform)instance.transform;
                rect.sizeDelta = new Vector2(0, height * scale);
                rect.anchoredPosition = new Vector2(0, -y * scale);
                var map = instance.GetComponent<BindingMap>();
                var label = map.Require<GameText>("name");
                var rate = map.Require<GameText>("detail");
                label.text = name;
                rate.text = value;
                label.color = rate.color = item ? new Color32(166, 204, 210, 148) : Color.white;
                map.Require<Image>("divider").enabled = divider;
                instance.SetActive(true);
                y += height;
            }
            foreach (var group in groups)
                Row(Grade(group.Key), Percent(group.Sum(entry => (long)entry.weight)), 30);
            y = gradeRows * 30;
            Row("", "", 8, divider: true);
            foreach (var group in groups)
            {
                Row(Grade(group.Key), "", 23);
                foreach (var entry in group.OrderByDescending(entry => entry.featured).ThenBy(entry => entry.templateId, StringComparer.Ordinal))
                    Row(Name(kind, entry.templateId), Percent(entry.weight), 23, item: true);
            }
            return (98f + y) * scale;
        }
    }
}
