using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Core;
using AstralOath.Client.Common;
using UnityEngine;
using UnityEngine.Rendering;

namespace AstralOath.Client.Home
{
    public sealed class HomeWindowBinder
    {
        private readonly HomeWindow view;
        private readonly ILocaleService locale;
        private readonly Func<CancellationToken, Task> openArena;
        private readonly Func<CancellationToken, Task> openMore;
        private readonly Func<CancellationToken, Task> openBag;
        private readonly DesktopDisplayMode display;
        private readonly Sprite defaultFrame;
        private Func<CancellationToken,Task> openProfile,openSummon;
        private Func<string,CancellationToken,Task> unavailable;

        public HomeWindowBinder(HomeWindow view, ILocaleService locale, Func<CancellationToken, Task> openArena,
            Func<CancellationToken, Task> openMore, Func<CancellationToken, Task> openBag, DesktopDisplayMode display)
        {
            this.view = view;
            this.locale = locale;
            this.openArena = openArena;
            this.openMore = openMore;
            this.openBag = openBag;
            this.display = display;
            defaultFrame=view.ProfileFrame.sprite;
        }

        public void ConfigureNavigation(Func<CancellationToken,Task> profile,Func<CancellationToken,Task> summon,Func<string,CancellationToken,Task> unavailable)
        {openProfile=profile;openSummon=summon;this.unavailable=unavailable;}

        public void ApplyPlayerHeader(string displayName,Sprite avatar,Sprite frame=null,int? rank=null,float? experience=null)
        {
            view.Profile.text=displayName??"";view.Profile.richText=false;view.ProfileAvatar.sprite=avatar;view.ProfileAvatar.enabled=avatar;
            view.ProfileFrame.sprite=frame?frame:defaultFrame;
            view.ProfileRank.text=rank.HasValue?rank.Value.ToString(System.Globalization.CultureInfo.InvariantCulture):"—";
            view.ProfileExperienceFill.enabled=experience.HasValue;
            view.ProfileExperienceFill.fillAmount=experience.HasValue?Mathf.Clamp01(experience.Value):0;
            view.ProfileExperiencePercent.text=experience.HasValue?(Mathf.Clamp01(experience.Value)*100).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"%":"";
        }

        public Task OpenAsync(BindingScope screen)
        {
            view.Show();
            screen.Add(() => { if (view.Root) view.Hide(); });
            var actor = view.LobbyPortrait;
            void FitPortrait(ScriptableRenderContext _, Camera camera)
            {
                var target = camera.targetTexture;
                if (!target || target != actor.texture) return;
                var displaySize = UiRenderSurface.ScreenPixels(actor);
                int size = Mathf.Clamp(Mathf.Max(displaySize.x, displaySize.y), 1, UiRenderSurface.MaxDimension);
                if (target.width == size && target.height == size) return;
                // The prefab's existing player releases this target when hidden. Keep its identity and framing.
                camera.targetTexture = null;
                target.Release();
                target.width = target.height = size;
                target.Create();
                camera.targetTexture = target;
            }
            RenderPipelineManager.beginCameraRendering += FitPortrait;
            screen.Add(() => RenderPipelineManager.beginCameraRendering -= FitPortrait);
            view.Arena.Bind(screen, openArena);
            view.Summon.Bind(screen,openSummon??(token=>Unavailable("summon",token)));
            view.ProfileOpen.Bind(screen,openProfile??(token=>Unavailable("profile",token)));
            foreach(var id in HomeFeatureLabels.Main)
            {if(HomeFeatureLabels.MainAvailable(id))continue;string feature=id;view.Feature(feature).Bind(screen,token=>Unavailable(feature,token));}
            foreach(var id in HomeFeatureLabels.Rail)
            {string feature=id;view.Feature(feature).Bind(screen,token=>Unavailable(feature,token));}
            foreach(var id in new[]{"notifications","boost"})
            {string feature=id;view.Feature(feature).Bind(screen,token=>Unavailable(feature,token));}
            view.More.FeedbackSound = GameUiFeedback.Sound.Silent;
            view.More.Bind(screen, openMore);
            view.Bag.FeedbackSound = GameUiFeedback.Sound.InventoryOpen;
            view.Bag.Bind(screen, openBag);
            var button = view.DisplayMode;
            // DesktopDisplayMode still owns F11; the original lobby has no visible debug plate.
            button.gameObject.SetActive(false);
            if (button.gameObject.activeSelf)
            {
                UpdateDisplayLabel();
                display.Changed += UpdateDisplayLabel;
                screen.Add(() => { if (display) display.Changed -= UpdateDisplayLabel; });
                button.Bind(screen, ignored => { display.Toggle(); return Task.CompletedTask; });
            }
            return Task.CompletedTask;
        }

        private Task Unavailable(string id,CancellationToken token)=>unavailable?.Invoke(HomeFeatureLabels.Key(id),token)??Task.CompletedTask;

        public void HideArenaWhile(BindingScope overlay)
        {
            var navigation = view.ArenaNavigation.gameObject;
            bool wasActive = navigation.activeSelf;
            navigation.SetActive(false);
            overlay.Add(() => { if (navigation) navigation.SetActive(wasActive); });
        }

        private void UpdateDisplayLabel() => view.DisplayModeLabel.text = locale.Resolve(display.FullScreen
            ? "home.window.windowed" : "home.window.fullscreen");
    }
}
