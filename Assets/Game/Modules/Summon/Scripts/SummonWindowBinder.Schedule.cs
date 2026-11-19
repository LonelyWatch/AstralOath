using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AstralOath.Client.Core;
using UnityEngine;

namespace AstralOath.Client.Summon
{
    public sealed partial class SummonWindowBinder
    {
        private readonly Dictionary<string, double> bannerDeadlines = new Dictionary<string, double>(StringComparer.Ordinal);
        private SummonState timedSnapshot;
        private double nextScheduleUpdate = double.PositiveInfinity;

        private void WatchBannerSchedule(BindingScope scope)
        {
            Canvas.preWillRenderCanvases += CheckSchedule;
            scope.Add(() => Canvas.preWillRenderCanvases -= CheckSchedule);
        }

        private void CheckSchedule()
        {
            if (Time.realtimeSinceStartupAsDouble >= nextScheduleUpdate && RefreshBannerSchedule())
                RenderMain();
        }

        private bool RefreshBannerSchedule()
        {
            var now = Time.realtimeSinceStartupAsDouble;
            if (!ReferenceEquals(timedSnapshot, state))
            {
                timedSnapshot = state;
                bannerDeadlines.Clear();
                if (state?.banners != null && ParseServerDate(state.serverTimeUtc, out var serverTime))
                    foreach (var item in state.banners)
                        if (ParseServerDate(item.endUtc, out var end))
                            bannerDeadlines[item.id] = now + (end - serverTime).TotalSeconds;
            }

            var changed = false;
            if (bannerDeadlines.Count != 0 && bannerDeadlines.Values.Min() <= now)
            {
                state.banners = state.banners.Where(item =>
                    !bannerDeadlines.TryGetValue(item.id, out var end) || end > now).ToArray();
                foreach (var expired in bannerDeadlines.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
                    bannerDeadlines.Remove(expired);
                if (Banner == null)
                    bannerId = state.banners.FirstOrDefault()?.id ?? Covenant;
                if (quote != null && state.banners.All(item => item.id != quote.bannerId))
                {
                    quote = null;
                    ReturnFromConfirmation();
                }
                if (lastReceipt != null && state.banners.All(item => item.id != lastReceipt.bannerId))
                    view.Button("resultAgain").interactable = false;
                changed = true;
            }

            nextScheduleUpdate = bannerDeadlines.Count == 0 ? double.PositiveInfinity : bannerDeadlines.Values.Min();
            bool timed = bannerDeadlines.TryGetValue(bannerId, out var deadline);
            view.Panel("eventTimerRow").SetActive(timed);
            if (timed)
            {
                var left = TimeSpan.FromSeconds(deadline - now);
                string caption = left.TotalDays >= 1 ? F("countdown.days", left.Days, left.Hours)
                    : left.TotalHours >= 1 ? F("countdown.hours", left.Hours, left.Minutes)
                    : left.Minutes > 0 ? F("countdown.minutes", left.Minutes) : L("countdown.last_minute");
                view.Text("eventCountdown").text = caption;
                // Update just after the displayed minute changes; expiry uses its exact deadline.
                nextScheduleUpdate = Math.Min(nextScheduleUpdate, now + left.TotalSeconds % 60d + .01d);
            }
            return changed;
        }

        private static bool ParseServerDate(string input, out DateTimeOffset result) =>
            DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }
}
