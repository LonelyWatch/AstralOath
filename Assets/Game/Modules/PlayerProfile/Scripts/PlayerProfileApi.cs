using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine.Scripting;
namespace AstralOath.Client.PlayerProfile
{
    public sealed class PlayerProfileRequestException:IOException
    {
        public HttpStatusCode Status{get;}public string Code{get;}
        public PlayerProfileRequestException(HttpStatusCode status,string code):base("Profile request rejected: "+(int)status){Status=status;Code=code;}
    }
    public sealed class PlayerProfileApi:IPlayerProfileApi
    {
        [Serializable,Preserve]private sealed class Error{public string code;}
        [Serializable,Preserve]private sealed class Check{public string nickname;}
        private readonly Uri endpoint;private readonly Func<CancellationToken,Task<string>> bearer;private readonly HttpClient http;
        public PlayerProfileApi(Uri endpoint,Func<CancellationToken,Task<string>> bearer,HttpMessageHandler transport=null)
        {
            this.endpoint=endpoint??throw new ArgumentNullException(nameof(endpoint));this.bearer=bearer??throw new ArgumentNullException(nameof(bearer));
            http=transport==null?new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseProxy=!endpoint.IsLoopback}):new HttpClient(transport);http.Timeout=Timeout.InfiniteTimeSpan;
        }
        public Task<PlayerProfileData> MeAsync(CancellationToken token)=>Send<PlayerProfileData>("me",null,token);
        public Task<PlayerProfileData> PlayerAsync(string id,CancellationToken token)=>Send<PlayerProfileData>("players/"+Uri.EscapeDataString(id??""),null,token);
        public Task<NicknameCheck> CheckNicknameAsync(string nickname,CancellationToken token)=>Send<NicknameCheck>("nickname/check",new Check{nickname=nickname},token);
        public Task<PlayerProfileData> ActAsync(ProfileActionRequest request,CancellationToken token)=>Send<PlayerProfileData>("me/actions",request,token);
        private async Task<T> Send<T>(string path,object data,CancellationToken token)where T:class
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
            string auth=await bearer(deadline.Token);deadline.Token.ThrowIfCancellationRequested();
            if(string.IsNullOrWhiteSpace(auth))throw new PlayerProfileRequestException(HttpStatusCode.Unauthorized,"unauthorized");
            using var request=new HttpRequestMessage(data==null?HttpMethod.Get:HttpMethod.Post,new Uri(endpoint,"/api/profile/"+path));
            request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",auth);
            if(data!=null)request.Content=new StringContent(JsonConvert.SerializeObject(data),Encoding.UTF8,"application/json");
            deadline.Token.ThrowIfCancellationRequested();if(data is ProfileActionRequest action)action.dispatched=true;
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            using var stream=await response.Content.ReadAsStreamAsync();using var reader=new StreamReader(stream,Encoding.UTF8);
            var text=new StringBuilder();var buffer=new char[2048];
            using(deadline.Token.Register(()=>stream.Dispose()))
            {int n;while((n=await reader.ReadAsync(buffer,0,buffer.Length))>0){if(text.Length+n>131072)throw new IOException("Profile response exceeds limit.");text.Append(buffer,0,n);}}
            deadline.Token.ThrowIfCancellationRequested();
            if(!response.IsSuccessStatusCode)
            {
                string code="profile_request_failed";try{code=JsonConvert.DeserializeObject<Error>(text.ToString())?.code??code;}catch(JsonException){}
                throw new PlayerProfileRequestException(response.StatusCode,code);
            }
            try{return JsonConvert.DeserializeObject<T>(text.ToString())??throw new IOException("Invalid profile response.");}
            catch(JsonException){throw new IOException("Invalid profile response.");}
            }
            // Known HTTP rejection remains authoritative even if cancellation races its handling.
            catch(PlayerProfileRequestException){throw;}
            catch(Exception error)when(deadline.IsCancellationRequested&&(error is IOException||error is HttpRequestException||error is SocketException||error is ObjectDisposedException||error is OperationCanceledException))
            {throw new OperationCanceledException(deadline.Token);}
            catch(SocketException){throw new IOException("Profile request unavailable.");}
        }
        public void Dispose()=>http.Dispose();
    }
}
