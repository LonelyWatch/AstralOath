using System;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;

namespace AstralOath.Client.FoundationDemo
{
    public sealed class FeatureNoticeBinder
    {
        private readonly FeatureNotice view;
        public FeatureNoticeBinder(FeatureNotice view)=>this.view=view??throw new ArgumentNullException(nameof(view));

        public void Open(BindingScope scope,string title,string message,string closeLabel,Action close)
        {
            if(scope==null)throw new ArgumentNullException(nameof(scope));
            if(close==null)throw new ArgumentNullException(nameof(close));
            scope.Token.ThrowIfCancellationRequested();
            view.Title.richText=view.Message.richText=view.CloseLabel.richText=false;
            view.Title.text=title??"";view.Message.text=message??"";view.CloseLabel.text=closeLabel??"";
            view.Dismiss.FeedbackSound=GameUiFeedback.Sound.Cancel;
            bool consumed=false;
            view.Dismiss.Bind(scope,ignored=>
            {
                if(!consumed){consumed=true;close();}
                return Task.CompletedTask;
            });
            scope.Add(()=>{if(view.Root)view.Hide();});
            view.Show();
            GameUiFeedback.Play(view.Dismiss,GameUiFeedback.Sound.Blocked);
        }
    }
}
