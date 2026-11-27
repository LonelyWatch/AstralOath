using System;
using System.Collections;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.PlayerProfile;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
namespace AstralOath.Client.Tests
{
    public sealed class PlayerProfileApiTests
    {
        private sealed class Transport:HttpMessageHandler
        {
            public string path,auth,body;public HttpStatusCode status=HttpStatusCode.OK;public string response="{}";
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            {path=request.RequestUri.AbsolutePath;auth=request.Headers.Authorization?.ToString();body=request.Content==null?null:await request.Content.ReadAsStringAsync();return new HttpResponseMessage(status){Content=new StringContent(response)};}
        }
        private static IEnumerator Done(Task task){while(!task.IsCompleted)yield return null;if(task.IsFaulted)throw task.Exception.InnerException;Assert.That(task.IsCanceled,Is.False);}
        [UnityTest]public IEnumerator WritesUseExactSocialContractAndNeverSerializeDispatchFlag()=>Done(Write());
        private async Task Write()
        {
            var transport=new Transport{response="{\"playerId\":\"p\",\"isSelf\":true,\"canEdit\":true,\"version\":2,\"rank\":{\"available\":false,\"value\":null}}"};
            using var api=new PlayerProfileApi(new Uri("http://localhost:7728"),_=>Task.FromResult("test-token"),transport);
            var request=new ProfileActionRequest{requestId="id",expectedVersion=1,kind="greeting",greeting="<b>原样</b>"};
            var result=await api.ActAsync(request,default);var json=JObject.Parse(transport.body);
            Assert.That(transport.path,Is.EqualTo("/api/profile/me/actions"));Assert.That(transport.auth,Is.EqualTo("Bearer test-token"));
            Assert.That((string)json["greeting"],Is.EqualTo("<b>原样</b>"));Assert.That(json["dispatched"],Is.Null);Assert.That(request.dispatched,Is.True);Assert.That(result.rank.value,Is.Null);
        }
        [UnityTest]public IEnumerator ErrorCodeComesFromSocialCodeField()=>Done(Error());
        private async Task Error()
        {using var api=new PlayerProfileApi(new Uri("http://localhost:7728"),_=>Task.FromResult("test-token"),new Transport{status=HttpStatusCode.Conflict,response="{\"code\":\"profile_version_conflict\"}"});try{await api.MeAsync(default);Assert.Fail();}catch(PlayerProfileRequestException e){Assert.That(e.Code,Is.EqualTo("profile_version_conflict"));}}
    }
}
