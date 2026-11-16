#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class EntryFlowTests
    {
        private const string ScenePath = "Assets/Game/Modules/Home/Scenes/GameHome.unity";
        private Scene scene;
        private string oldEndpoint, oldSkip, oldRequireAccount;
        private EntryServer server;

        [UnityTest, Timeout(90000)]
        public IEnumerator SupportedClientShowsConfiguredThemeAndWaitsForTapBeforeHome()
        {
            Boot("");
            yield return SignIn();
            yield return Wait(() => Find<Button>("Select realm"), "Realm picker did not show.");
            Find<Button>("Select realm").onClick.Invoke();
            yield return Wait(() => Find<Button>("Tap to start"), "Ready screen did not show.");
            Assert.That(server.Selections, Is.EqualTo(1));
            Assert.That(server.AuthorizedRealmSelection, Is.True);
            Assert.That(Find<Image>("Title background").sprite.name, Is.EqualTo("26_halloween_bg"));
            Assert.That(Find<BindingMap>("HomeWindow"), Is.Null);
            Assert.That(Find<GameText>("Client version").text, Does.Contain("App:" + Application.version));
            Find<Button>("Tap to start").onClick.Invoke();
            yield return Wait(() => Find<BindingMap>("HomeWindow"), "Home did not load after tapping.");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator ForcedUpdateBlocksRealmAdmissionAndHome()
        {
            Boot("999.0.0");
            yield return SignIn();
            yield return Wait(() => Find<Button>("Select realm"), "Realm picker did not show.");
            Find<Button>("Select realm").onClick.Invoke();
            yield return Wait(() => Find<GameText>("QQ update instruction"), "Mandatory update panel did not appear.");
            Assert.That(Find<GameText>("QQ update instruction").text, Does.Contain("QQ群"));
            Assert.That(server.Selections, Is.Zero);
            Assert.That(Find<BindingMap>("HomeWindow"), Is.Null);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator TestLoginOpensHomeWhenAccountRoutesAreUnavailable()
        {
            Boot("", requireAccount: false, hasVersionRoute: false);
            yield return ClickTestLoginAndWaitFor(() => Find<BindingMap>("HomeWindow"));

            Assert.That(Find<BindingMap>("HomeWindow"), Is.Not.Null);
            AssertNoAccountOrRealmFlow();
            Assert.That(Find<Button>("Tap to start"), Is.Null);
            Assert.That(server.VersionChecks, Is.EqualTo(1));
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator TestLoginRespectsMandatoryUpdateFromAvailableVersionRoute()
        {
            Boot("999.0.0", requireAccount: false);
            yield return ClickTestLoginAndWaitFor(() => Find<GameText>("QQ update instruction"));

            var instruction = Find<GameText>("QQ update instruction");
            Assert.That(instruction, Is.Not.Null);
            Assert.That(instruction.text, Does.Contain("QQ群"));
            AssertNoAccountOrRealmFlow();
            Assert.That(server.VersionChecks, Is.EqualTo(1));
            Assert.That(Find<BindingMap>("HomeWindow"), Is.Null);
        }

        private IEnumerator ClickTestLoginAndWaitFor(Func<bool> completed)
        {
            yield return Wait(() => Find<Button>("Login"), "Login entry did not show.");
            Find<Button>("Login").onClick.Invoke();
            yield return Wait(() => completed() || Find<Button>("Submit account form"),
                "Test login reached neither the expected destination nor the account form.");
            Assert.That(Find<Button>("Submit account form"), Is.Null, "Test login opened an account form.");
        }

        private void AssertNoAccountOrRealmFlow()
        {
            Assert.That(Find<TMP_InputField>("entry.account"), Is.Null);
            Assert.That(Find<Button>("Select realm"), Is.Null);
            Assert.That(server.AccountLogins, Is.Zero);
            Assert.That(server.Selections, Is.Zero);
        }

        private IEnumerator SignIn()
        {
            yield return Wait(() => Find<Button>("Login"), "Login entry did not show.");
            Find<Button>("Login").onClick.Invoke();
            yield return Wait(() => Find<Button>("Submit account form"), "Login form did not show.");
            var inputs = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<TMP_InputField>())
                .ToArray();
            Assert.That(inputs.Length, Is.EqualTo(2));
            inputs.Single(x => x.contentType == TMP_InputField.ContentType.Password).text = "local-test-only-password";
            inputs.Single(x => x.contentType != TMP_InputField.ContentType.Password).text = "local_test";
            Find<Button>("Submit account form").onClick.Invoke();
        }

        private void Boot(string minimum, bool requireAccount = true, bool hasVersionRoute = true)
        {
            server = new EntryServer(minimum, hasVersionRoute);
            oldEndpoint = Environment.GetEnvironmentVariable("ASTRAL_FOUNDATION_URL");
            oldSkip = Environment.GetEnvironmentVariable("ASTRAL_TEST_SKIP_ENTRY");
            oldRequireAccount = Environment.GetEnvironmentVariable("ASTRAL_TEST_REQUIRE_ACCOUNT_ENTRY");
            Environment.SetEnvironmentVariable("ASTRAL_FOUNDATION_URL", server.Address);
            Environment.SetEnvironmentVariable("ASTRAL_TEST_SKIP_ENTRY", null);
            Environment.SetEnvironmentVariable("ASTRAL_TEST_REQUIRE_ACCOUNT_ENTRY", requireAccount ? "1" : null);
            scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
        }

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
            server?.Dispose();
            Environment.SetEnvironmentVariable("ASTRAL_FOUNDATION_URL", oldEndpoint);
            Environment.SetEnvironmentVariable("ASTRAL_TEST_SKIP_ENTRY", oldSkip);
            Environment.SetEnvironmentVariable("ASTRAL_TEST_REQUIRE_ACCOUNT_ENTRY", oldRequireAccount);
            yield return null;
        }

        private T Find<T>(string name) where T : Component =>
            scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<T>())
                .FirstOrDefault(item => item.gameObject.name == name || item.gameObject.name == name + "(Clone)");

        private static IEnumerator Wait(Func<bool> condition, string message)
        {
            float until = Time.realtimeSinceStartup + 35;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(condition(), Is.True, message);
        }

        private sealed class EntryServer : IDisposable
        {
            private readonly HttpListener listener = new HttpListener();
            private readonly Task serving;
            private int selections, authorized, accountLogins, versionChecks;
            public string Address { get; }
            public int Selections => Volatile.Read(ref selections);
            public int AccountLogins => Volatile.Read(ref accountLogins);
            public int VersionChecks => Volatile.Read(ref versionChecks);
            public bool AuthorizedRealmSelection => Volatile.Read(ref authorized) == 1;

            internal EntryServer(string minimum, bool hasVersionRoute)
            {
                var ephemeral = new TcpListener(IPAddress.Loopback, 0);
                ephemeral.Start();
                int port;
                try { port = ((IPEndPoint)ephemeral.LocalEndpoint).Port; }
                finally { ephemeral.Stop(); }
                Address = "http://127.0.0.1:" + port;
                listener.Prefixes.Add(Address + "/");
                listener.Start();
                serving = Serve(minimum, hasVersionRoute);
            }

            private async Task Serve(string minimum, bool hasVersionRoute)
            {
                while (listener.IsListening)
                {
                    HttpListenerContext request;
                    try { request = await listener.GetContextAsync().ConfigureAwait(false); }
                    catch (HttpListenerException) when (!listener.IsListening) { return; }
                    catch (ObjectDisposedException) { return; }
                    try
                    {
                        string reply;
                        switch (request.Request.Url.AbsolutePath)
                        {
                            case "/api/entry/login":
                                Interlocked.Increment(ref accountLogins);
                                reply = "{\"token\":\"fixture-admission\"}";
                                break;
                            case "/api/entry/version":
                                Interlocked.Increment(ref versionChecks);
                                if (!hasVersionRoute)
                                {
                                    request.Response.StatusCode = 404;
                                    reply = "{}";
                                    break;
                                }
                                reply = "{\"minimumAppVersion\":\"" + minimum + "\",\"realmId\":\"undying-memory\"}";
                                break;
                            case "/api/entry/select-realm":
                                Interlocked.Increment(ref selections);
                                if (request.Request.Headers["Authorization"] == "Bearer fixture-admission")
                                    Interlocked.Exchange(ref authorized, 1);
                                reply = "{\"token\":\"fixture-arena\",\"player\":{\"id\":\"fixture-player\",\"displayName\":\"Fixture\",\"avatarId\":\"c1001\"}}";
                                break;
                            default:
                                request.Response.StatusCode = 404;
                                reply = "{}";
                                break;
                        }
                        byte[] bytes = Encoding.UTF8.GetBytes(reply);
                        request.Response.ContentType = "application/json";
                        request.Response.ContentLength64 = bytes.Length;
                        await request.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                    }
                    finally { request.Response.Close(); }
                }
            }

            public void Dispose()
            {
                listener.Close();
                if (serving.IsFaulted) throw serving.Exception;
            }
        }
    }
}
#endif
