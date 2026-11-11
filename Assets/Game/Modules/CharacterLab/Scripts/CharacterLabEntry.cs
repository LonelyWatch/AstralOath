using System;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine.Scripting;
namespace AstralOath.Client.CharacterLab
{
    [Preserve]
    public sealed class CharacterLabEntry : ICharacterLabEntry
    {
        private BindingScope scope;
        private CharacterLabWindowBinder binder;
        public async Task StartAsync(CharacterLabLaunchContext context)
        {
            if (scope != null) throw new InvalidOperationException("CharacterLab already started.");
            scope = new BindingScope(context.Lifetime.Token);
            scope.Add(context.Lifetime.Add(Dispose));
            try {
                var locale=await CharacterLabMessages.LoadAsync(context.Services.Get<IConfigService>(),scope);
                var registry = new UIViewRegistry();
                registry.Register<CharacterLabWindow>();
                var view = await registry.CreateAsync<CharacterLabWindow>(context.Services.Get<IAssetService>(), context.ViewRoot, scope);
                scope.Token.ThrowIfCancellationRequested();
                binder = new CharacterLabWindowBinder(view, context, scope, locale, Dispose);
                await binder.OpenAsync();
            } catch(Exception startupError) { try{Dispose();}catch(Exception cleanupError){throw new AggregateException(startupError,cleanupError);}throw; }
        }
        public void Tick(float deltaSeconds) => binder?.Tick(deltaSeconds);
        public void Dispose()
        {
            var oldBinder=binder;binder=null;
            var oldScope=scope;scope=null;
            Exception binderError=null;
            try{oldBinder?.Dispose();}catch(Exception error){binderError=error;}
            try{oldScope?.Dispose();}
            catch(Exception scopeError){if(binderError!=null)throw new AggregateException(binderError,scopeError);throw;}
            if(binderError!=null)throw binderError;
        }
    }
}
