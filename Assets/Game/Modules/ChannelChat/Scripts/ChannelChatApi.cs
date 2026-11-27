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

namespace AstralOath.Client.ChannelChat
{
    public sealed class ChannelChatRequestException:IOException
    {
        public HttpStatusCode Status{get;} public string Code{get;}
        public ChannelChatRequestException(HttpStatusCode status,string code):base("Chat request rejected: "+(int)status){Status=status;Code=code;}
    }
    public sealed class ChannelChatApi:IChannelChatApi
    {
        private const int MaximumResponseCharacters=1024*1024;
        [Serializable,Preserve]private sealed class Error{public string code;}
        [Serializable,Preserve]private sealed class Invalidation{public string kind;}
        private readonly Uri endpoint;
        private readonly Func<CancellationToken,Task<string>> bearer;
        private readonly HttpClient http;
        public ChannelChatApi(Uri endpoint,Func<CancellationToken,Task<string>> bearer,HttpMessageHandler transport=null)
        {
            this.endpoint=endpoint??throw new ArgumentNullException(nameof(endpoint));
            this.bearer=bearer??throw new ArgumentNullException(nameof(bearer));
            if(!endpoint.IsAbsoluteUri||(endpoint.Scheme!="http"&&endpoint.Scheme!="https"))throw new ArgumentException("Invalid chat endpoint.",nameof(endpoint));
            http=transport==null?new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseProxy=!endpoint.IsLoopback}):new HttpClient(transport);
            http.Timeout=Timeout.InfiniteTimeSpan;
        }
        public Task<ChatPreferences> PreferencesAsync(CancellationToken token)=>Send<ChatPreferences>("preferences",null,token);
        public Task<ChatPreferences> SavePreferencesAsync(ChatPreferenceRequest request,CancellationToken token)=>Send<ChatPreferences>("preferences/actions",request??throw new ArgumentNullException(nameof(request)),token);
        public Task<ChatPage> ListAsync(int channel,string after,int waitMs,CancellationToken token)
        {
            ValidateChannel(channel);if(waitMs<0||waitMs>8000)throw new ArgumentOutOfRangeException(nameof(waitMs));
            return Send<ChatPage>("channels/"+channel+"/messages?after="+Uri.EscapeDataString(after??"0")+"&waitMs="+waitMs,null,token);
        }
        public Task<ChatMessage> SendAsync(int channel,ChatSendRequest request,CancellationToken token)
        {ValidateChannel(channel);return Send<ChatMessage>("channels/"+channel+"/messages",request??throw new ArgumentNullException(nameof(request)),token);}
        private async Task<HttpRequestMessage> Request(string path,object body,CancellationToken token)
        {
            string auth=await bearer(token);token.ThrowIfCancellationRequested();
            if(string.IsNullOrWhiteSpace(auth))throw new ChannelChatRequestException(HttpStatusCode.Unauthorized,"unauthorized");
            var request=new HttpRequestMessage(body==null?HttpMethod.Get:HttpMethod.Post,new Uri(endpoint,"/api/chat/"+path));
            try
            {
                request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",auth);
                if(body!=null)request.Content=new StringContent(JsonConvert.SerializeObject(body),Encoding.UTF8,"application/json");
                return request;
            }
            catch{request.Dispose();throw;}
        }
        private async Task<T> Send<T>(string path,object body,CancellationToken token)where T:class
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                using var request=await Request(path,body,deadline.Token);deadline.Token.ThrowIfCancellationRequested();
                if(body is ChatSendRequest send)send.dispatched=true;
                if(body is ChatPreferenceRequest preferences)preferences.dispatched=true;
                using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
                if(response.Content==null)
                {if(!response.IsSuccessStatusCode)throw new ChannelChatRequestException(response.StatusCode,"chat_request_failed");throw new IOException("Invalid chat response.");}
                using var stream=await response.Content.ReadAsStreamAsync();using var reader=new StreamReader(stream,Encoding.UTF8);
                using var close=deadline.Token.Register(()=>stream.Dispose());var content=new StringBuilder();var buffer=new char[4096];int count;
                try
                {
                    while((count=await reader.ReadAsync(buffer,0,buffer.Length))>0)
                    {deadline.Token.ThrowIfCancellationRequested();if(content.Length+count>MaximumResponseCharacters)throw new IOException("Chat response exceeds limit.");content.Append(buffer,0,count);}
                }
                catch(Exception e)when(!deadline.IsCancellationRequested&&(int)response.StatusCode>=400&&(int)response.StatusCode<500&&(e is IOException||e is SocketException||e is ObjectDisposedException))
                {throw new ChannelChatRequestException(response.StatusCode,"chat_request_failed");}
                deadline.Token.ThrowIfCancellationRequested();
                if(!response.IsSuccessStatusCode)
                {
                    string code="chat_request_failed";try{var value=JsonConvert.DeserializeObject<Error>(content.ToString())?.code;if(SafeCode(value))code=value;}catch(JsonException){}
                    throw new ChannelChatRequestException(response.StatusCode,code);
                }
                try{return JsonConvert.DeserializeObject<T>(content.ToString())??throw new IOException("Invalid chat response.");}
                catch(JsonException){throw new IOException("Invalid chat response.");}
            }
            catch(Exception e)when(deadline.IsCancellationRequested&&(e is IOException||e is HttpRequestException||e is SocketException||e is ObjectDisposedException||e is OperationCanceledException))
            {throw new OperationCanceledException(deadline.Token);}
            catch(HttpRequestException){throw new IOException("Chat connection unavailable.");}
            catch(SocketException){throw new IOException("Chat connection unavailable.");}
            catch(JsonException){throw new IOException("Invalid chat request.");}
        }
        public async Task WatchAsync(int channel,Action invalidated,CancellationToken token)
        {
            ValidateChannel(channel);if(invalidated==null)throw new ArgumentNullException(nameof(invalidated));
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                using var request=await Request("channels/"+channel+"/events",null,deadline.Token);
                using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
                if(!response.IsSuccessStatusCode)throw new ChannelChatRequestException(response.StatusCode,response.StatusCode==HttpStatusCode.Unauthorized?"unauthorized":"chat_events_failed");
                // The existing mother preserves this virtual formatter, but strips the
                // separate MediaType getter. Keep the same media-type check without a new AOT call.
                string contentType=response.Content.Headers.ContentType?.ToString();
                if(contentType==null||!string.Equals(contentType.Split(';')[0].Trim(),"application/x-ndjson",StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid chat stream.");
                using var stream=await response.Content.ReadAsStreamAsync();using var reader=new StreamReader(stream,Encoding.UTF8);
                using var close=deadline.Token.Register(()=>stream.Dispose());
                while(true)
                {
                    string line=await ReadLine(reader,deadline.Token);if(line==null)return;deadline.CancelAfter(TimeSpan.FromSeconds(20));
                    Invalidation item;try{item=JsonConvert.DeserializeObject<Invalidation>(line);}catch(JsonException){throw new IOException("Invalid chat event.");}
                    if(item?.kind=="sync"||item?.kind=="chat")invalidated();
                    else if(item?.kind!="heartbeat")throw new IOException("Invalid chat event.");
                }
            }
            catch(Exception e)when(deadline.IsCancellationRequested&&(e is IOException||e is HttpRequestException||e is SocketException||e is ObjectDisposedException||e is OperationCanceledException))
            {throw new OperationCanceledException(deadline.Token);}
            catch(HttpRequestException){throw new IOException("Chat stream unavailable.");}
            catch(SocketException){throw new IOException("Chat stream unavailable.");}
        }
        private static async Task<string> ReadLine(StreamReader reader,CancellationToken token)
        {
            var line=new StringBuilder();var buffer=new char[1];
            while(true){token.ThrowIfCancellationRequested();if(await reader.ReadAsync(buffer,0,1)==0)return line.Length==0?null:line.ToString();if(buffer[0]=='\n')return line.ToString().TrimEnd('\r');if(line.Length>=4096)throw new IOException("Chat event exceeds limit.");line.Append(buffer[0]);}
        }
        private static bool SafeCode(string value)
        {if(string.IsNullOrEmpty(value)||value.Length>64)return false;foreach(char c in value)if(c!='_'&&(c<'a'||c>'z')&&(c<'0'||c>'9'))return false;return true;}
        private static void ValidateChannel(int channel){if(channel<1||channel>9999)throw new ArgumentOutOfRangeException(nameof(channel));}
        public void Dispose()=>http.Dispose();
    }
}
