using System;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Net.Http;
namespace AstralOath.Client.PlayerProfile
{
    public sealed class PlayerProfileSession : IDisposable
    {
        private sealed class ActionIntent
        {
            internal ProfileActionRequest request;internal string player,realm;
            internal Task<PlayerProfileData> running;internal bool everDispatched;
        }
        private readonly IPlayerProfileApi api;
        private readonly CancellationTokenSource lifetime=new CancellationTokenSource();
        private readonly CancellationToken cancellation;
        private ActionIntent pending;
        private bool disposed;
        public PlayerProfileSession(IPlayerProfileApi api) { this.api=api??throw new ArgumentNullException(nameof(api));cancellation=lifetime.Token; }
        public PlayerProfileData Self { get; private set; }
        public bool HasPending => pending!=null;
        public event Action<PlayerProfileData> Changed;
        public async Task<PlayerProfileData> ReadAsync(string playerId,CancellationToken token)
        {
            using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,cancellation);
            var data=string.IsNullOrEmpty(playerId)?await api.MeAsync(linked.Token):await api.PlayerAsync(playerId,linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if(data==null||string.IsNullOrWhiteSpace(data.playerId)||string.IsNullOrWhiteSpace(data.realmId)||
                (string.IsNullOrEmpty(playerId)?!data.isSelf:playerId!=data.playerId))throw new IOException("Invalid profile response.");
            return data.isSelf?ApplySelf(data):data;
        }
        public async Task<NicknameCheck> CheckNicknameAsync(string nickname,CancellationToken token)
        {
            using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,cancellation);
            var result=await api.CheckNicknameAsync(PlayerProfileRules.NormalizeNickname(nickname),linked.Token);
            linked.Token.ThrowIfCancellationRequested();return result??throw new IOException("Invalid nickname response.");
        }
        private PlayerProfileData ApplySelf(PlayerProfileData data)
        {
            if(Self!=null&&(Self.playerId!=data.playerId||Self.realmId!=data.realmId))throw new IOException("Profile identity changed.");
            if(Self!=null&&data.version<=Self.version)return Self;
            Self=data;Changed?.Invoke(data);return data;
        }
        public Task<PlayerProfileData> SubmitAsync(PlayerProfileData subject,ProfileActionRequest values,CancellationToken page)
        {
            cancellation.ThrowIfCancellationRequested();page.ThrowIfCancellationRequested();
            if(subject==null||!subject.isSelf||!subject.canEdit)throw new InvalidOperationException("This profile is read only.");
            if(values==null)throw new ArgumentNullException(nameof(values));
            if(pending!=null)throw new InvalidOperationException("A profile action is awaiting a receipt.");
            var intent=new ActionIntent{player=subject.playerId,realm=subject.realmId,request=new ProfileActionRequest{
                requestId=Guid.NewGuid().ToString("N"),expectedVersion=subject.version,kind=values.kind,nickname=values.nickname,
                greeting=values.greeting,seasonVisible=values.seasonVisible,bestVisible=values.bestVisible}};
            pending=intent;return Start(intent,page);
        }
        public Task<PlayerProfileData> RetryPendingAsync(CancellationToken page)
        {cancellation.ThrowIfCancellationRequested();page.ThrowIfCancellationRequested();return pending==null?Task.FromResult(Self):Start(pending,page);}
        private Task<PlayerProfileData> Start(ActionIntent intent,CancellationToken page)
        {if(intent.running==null||intent.running.IsCompleted)intent.running=SendAsync(intent,page);return AwaitPage(intent.running,page);}
        private async Task<PlayerProfileData> SendAsync(ActionIntent intent,CancellationToken page)
        {
            var frozen=intent.request;
            var call=new ProfileActionRequest{requestId=frozen.requestId,expectedVersion=frozen.expectedVersion,kind=frozen.kind,
                nickname=frozen.nickname,greeting=frozen.greeting,seasonVisible=frozen.seasonVisible,bestVisible=frozen.bestVisible};
            using var attempt=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            using var cancelled=page.Register(()=>{if(!call.dispatched)attempt.Cancel();});
            try
            {
                var result=await api.ActAsync(call,attempt.Token);attempt.Token.ThrowIfCancellationRequested();cancellation.ThrowIfCancellationRequested();
                if(result==null||!result.isSelf||result.playerId!=intent.player||result.realmId!=intent.realm)throw new IOException("Invalid profile receipt.");
                // Replayed receipts can predate another device's change; complete the intent without rolling state back.
                if(ReferenceEquals(pending,intent))pending=null;return ApplySelf(result);
            }
            catch(PlayerProfileRequestException error) when((int)error.Status>=400&&(int)error.Status<500){if(ReferenceEquals(pending,intent))pending=null;throw;}
            catch(Exception error) when(error is IOException||error is HttpRequestException||error is OperationCanceledException)
            {
                intent.everDispatched|=call.dispatched;
                if((!intent.everDispatched||cancellation.IsCancellationRequested)&&ReferenceEquals(pending,intent))pending=null;
                throw;
            }
        }
        private static async Task<PlayerProfileData> AwaitPage(Task<PlayerProfileData> task,CancellationToken page)
        {
            if(!page.CanBeCanceled)return await task;
            var cancelled=new TaskCompletionSource<bool>();using var registration=page.Register(()=>cancelled.TrySetResult(true));
            if(await Task.WhenAny(task,cancelled.Task)!=task){_=Observe(task);throw new OperationCanceledException(page);}
            page.ThrowIfCancellationRequested();return await task;
        }
        private static async Task Observe(Task task){try{await task;}catch(Exception){}}
        public void Dispose()
        {if(disposed)return;disposed=true;lifetime.Cancel();pending=null;Self=null;Changed=null;api.Dispose();lifetime.Dispose();}
    }
}
