using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.FoundationDemo;
using AstralOath.Client.Mail;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class MailEntryBinderTests
    {
        [UnityTest]
        public IEnumerator HidingAndReopeningTheSameHeaderKeepsItsLoginPageBinding()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/Mail/Res/Prefab/MailEntry.prefab");
            Assert.That(prefab, Is.Not.Null);
            var root = Object.Instantiate(prefab);
            using var scope = new BindingScope(CancellationToken.None);
            using var session = new MailSession(new EmptyApi());
            try
            {
                var map = root.GetComponent<BindingMap>();
                int opened = 0;
                MailEntryBinder.Bind(map, scope, session, _ => { opened++; return Task.CompletedTask; });
                map.Require<GameButton>("button").onClick.Invoke();
                yield return null;
                root.SetActive(false);
                yield return null;
                root.SetActive(true);
                map.Require<GameButton>("button").onClick.Invoke();
                yield return null;
                Assert.That(opened, Is.EqualTo(2), "更多菜单隐藏后再次打开，邮件入口仍应能触发。");
                session.ApplySummary(new MailSummary { version = 1, effectiveCount = 3 });
                Assert.That(map.Require<GameText>("count").text, Is.EqualTo("3"));
                scope.Dispose();
                map.Require<GameButton>("button").onClick.Invoke();
                yield return null;
                Assert.That(opened, Is.EqualTo(2), "页面作用域结束必须解绑。");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private sealed class EmptyApi : IMailApi
        {
            public void Dispose() { }
            public Task<MailSummary> SummaryAsync(CancellationToken token) => Task.FromResult(new MailSummary());
            public Task<MailPage> ListAsync(bool history, string cursor, CancellationToken token) => throw new NotSupportedException();
            public Task<MailLetter> ReadAsync(string id, CancellationToken token) => throw new NotSupportedException();
            public Task<MailClaimReceipt> ClaimAsync(MailClaimRequest request, CancellationToken token) => throw new NotSupportedException();
            public Task<MailClaimReceipt> ClaimAllAsync(MailClaimAllRequest request, CancellationToken token) => throw new NotSupportedException();
            public Task WatchAsync(Action<long> version, CancellationToken token) => Task.CompletedTask;
        }
    }
}
