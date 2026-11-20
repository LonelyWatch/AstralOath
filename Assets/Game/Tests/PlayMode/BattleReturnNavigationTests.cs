using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AstralOath.Client.Tests
{
    public class BattleReturnNavigationTests
    {
        private sealed class SceneHandle : ILoadedScene
        {
            public Scene Scene { get; }
            internal SceneHandle(Scene scene) => Scene = scene;
            public async Task UnloadAsync()
            {
                var unloading = SceneManager.UnloadSceneAsync(Scene);
                while (!unloading.isDone) await Task.Yield();
            }
        }

        [UnityTest] public IEnumerator ReturningHostNeverExposesDraftWhileLeavingTheRoom() => Return(false);
        [UnityTest] public IEnumerator ReturningGuestCanRestoreAnExistingRoomWithoutDraftFlash() => Return(true);

        private static IEnumerator Return(bool recoverRoom)
        {
            var type = typeof(BattleOnlineLaunch);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            var previous = SceneManager.GetActiveScene();
            var home = SceneManager.CreateScene("Return home");
            var battle = SceneManager.CreateScene("Return battle");
            var roomUi = new GameObject("Home canvas", typeof(Canvas)).GetComponent<Canvas>();
            SceneManager.MoveGameObjectToScene(roomUi.gameObject, home);
            var draft = new GameObject("Previously displayed draft");
            draft.transform.SetParent(roomUi.transform);
            var destination = new GameObject(recoverRoom ? "Arena room" : "Arena directory");
            destination.transform.SetParent(roomUi.transform);
            destination.SetActive(false);
            SceneManager.SetActiveScene(battle);
            type.GetField("home", flags).SetValue(null, home);
            type.GetField("onlineScene", flags).SetValue(null, battle);
            type.GetField("sceneHandle", flags).SetValue(null, new SceneHandle(battle));
            ((List<Behaviour>)type.GetField("SuspendedHome", flags).GetValue(null)).Add(roomUi);
            roomUi.enabled = false;
            var leaveStarted = new TaskCompletionSource<bool>();
            var serviceResponse = new TaskCompletionSource<bool>();
            async Task PrepareDestination()
            {
                leaveStarted.SetResult(true);
                await serviceResponse.Task;
                draft.SetActive(false);
                destination.SetActive(true);
            }
            Func<Task> prepare = PrepareDestination;
            BattleOnlineLaunch.Returning += prepare;
            Task exit = null;
            try
            {
                exit = BattleOnlineLaunch.ExitAsync();
                yield return new WaitUntil(() => leaveStarted.Task.IsCompleted);
                yield return null;
                Assert.That(roomUi.enabled && draft.activeInHierarchy, Is.False, "The suspended canvas presented the previous draft during the pending room response.");
                Assert.That(exit.IsCompleted, Is.False, "Exit must include selection of the destination page.");
                serviceResponse.SetResult(true);
                yield return new WaitUntil(() => exit.IsCompleted);
                exit.GetAwaiter().GetResult();
                Assert.That(roomUi.enabled && destination.activeInHierarchy, Is.True);
                Assert.That(draft.activeInHierarchy, Is.False);
                Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(home));
            }
            finally
            {
                serviceResponse.TrySetResult(true);
                BattleOnlineLaunch.Returning -= prepare;
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (battle.IsValid() && battle.isLoaded) SceneManager.UnloadSceneAsync(battle);
                if (home.IsValid() && home.isLoaded) SceneManager.UnloadSceneAsync(home);
            }
        }
    }
}
