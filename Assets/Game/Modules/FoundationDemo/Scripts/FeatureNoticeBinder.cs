using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;

namespace AstralOath.Client.FoundationDemo
{
    public sealed class FeatureNoticeBinder
    {
        private readonly FeatureNotice view;
        private BindingScope opening;
        private int generation;
        public FeatureNoticeBinder(FeatureNotice view)=>this.view=view??throw new ArgumentNullException(nameof(view));

        public void Open(BindingScope scope,string title,string message,string closeLabel,Action close)
        {
            if(scope==null)throw new ArgumentNullException(nameof(scope));
            if(close==null)throw new ArgumentNullException(nameof(close));
            scope.Token.ThrowIfCancellationRequested();
            opening?.Dispose();
            var owner=new BindingScope(scope.Token);
            opening=owner;
            int mine=++generation;
            owner.Add(scope.Add(owner.Dispose));
            var group=view.Root.GetComponent<CanvasGroup>()??view.Root.AddComponent<CanvasGroup>();
            group.alpha=0;
            group.blocksRaycasts=true;
            group.interactable=true;
            view.Title.richText=view.Message.richText=view.CloseLabel.richText=false;
            view.Title.text=title??"";view.Message.text=message??"";view.CloseLabel.text=closeLabel??"";
            view.Dismiss.FeedbackSound=GameUiFeedback.Sound.Cancel;
            view.Dismiss.KeepBrightnessOnPress=true;
            bool consumed=false;
            view.Dismiss.Bind(owner,async ignored=>
            {
                if(consumed)return;
                consumed=true;
                await FadeAsync(0,.16f,owner.Token);
                if(mine!=generation)return;
                owner.Dispose();
                close();
            });
            owner.Add(()=>{if(mine==generation&&view.Root){group.alpha=0;view.Hide();}});
            view.Show();
            _=ShowAsync();
            GameUiFeedback.Play(view.Dismiss,GameUiFeedback.Sound.Blocked);

            async Task ShowAsync()
            {
                try{await FadeAsync(1,.18f,owner.Token,true);}
                catch(OperationCanceledException)when(owner.Token.IsCancellationRequested){}
            }
            async Task FadeAsync(float target,float seconds,CancellationToken token,bool entering=false)
            {
                float from=group.alpha;
                double start=Time.unscaledTimeAsDouble;
                while(true)
                {
                    token.ThrowIfCancellationRequested();
                    if(mine!=generation||(entering&&consumed)||!group)return;
                    float progress=Mathf.Clamp01((float)((Time.unscaledTimeAsDouble-start)/seconds));
                    group.alpha=Mathf.Lerp(from,target,progress*progress*(3-2*progress));
                    if(progress>=1)return;
                    await Task.Yield();
                }
            }
        }
    }
}
