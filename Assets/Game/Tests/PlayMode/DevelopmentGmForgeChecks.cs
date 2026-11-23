#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using AstralOath.Client.Common;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralOath.Client.Tests
{
    internal static class DevelopmentGmForgeChecks
    {
        internal const string Rules = @"{
            ""tiers"":[{""itemLevel"":85,""minEnhance"":0,""maxEnhance"":15,""stats"":[
                {""kind"":""attack"",""mainBase"":80,""mainStep"":26,""step"":1},
                {""kind"":""healthPct"",""rollMin"":500,""rollMax"":700,""step"":100},
                {""kind"":""speed"",""rollMin"":2,""rollMax"":4,""step"":1},
                {""kind"":""health"",""rollMin"":75,""rollMax"":130,""step"":1},
                {""kind"":""critChanceBp"",""rollMin"":300,""rollMax"":500,""step"":100}]}],
            ""slots"":[{""slot"":""weapon"",""mainKinds"":[""attack""],
                ""subKinds"":[""attack"",""healthPct"",""speed"",""health"",""critChanceBp""]}],
            ""grades"":[{""grade"":""normal"",""initialSubstats"":0,""unlockEnhancements"":[3,6,9,12]},
                {""grade"":""rare"",""initialSubstats"":2,""unlockEnhancements"":[0,0,9,12]},
                {""grade"":""heroic"",""initialSubstats"":3,""unlockEnhancements"":[0,0,0,12]},
                {""grade"":""epic"",""initialSubstats"":4,""unlockEnhancements"":[0,0,0,0]}],
            ""sets"":[""speed"",""critical""]}";

        internal static IEnumerator Check(BindingMap panel, ConcurrentQueue<string> requests, GameObject canvas,
            Action releaseLostResponse)
        {
            void Click(string key) => panel.Require<GameButton>(key).onClick.Invoke();
            TMP_InputField Input(string key) => panel.Require<TMP_InputField>(key);
            var make = panel.Require<GameButton>("forgeMake");
            int before = requests.Count;
            Click("forge");
            Assert.That(panel.Require<GameObject>("forgePanel").activeInHierarchy, Is.True);
            Assert.That(panel.Require<GameButton>("forgeMain").GetComponentInChildren<GameText>().text, Does.Contain("80"));
            Assert.That(make.interactable, Is.True);
            Input("forgeValue0").text = "8";
            Assert.That(make.interactable, Is.False);
            Assert.That(panel.Require<GameText>("forgeBudget").text, Does.Contain("5%").And.Contain("7%"));
            Click("forgeMake"); // The submission path must also reject an invalid recipe.
            Assert.That(requests.Count, Is.EqualTo(before));
            Click("forgeGrade");
            Assert.That(panel.Require<CanvasGroup>("forgeRow0").interactable, Is.False);
            Input("forgeEnhance").text = "3";
            Assert.That(panel.Require<CanvasGroup>("forgeRow0").interactable, Is.True);
            Assert.That(panel.Require<CanvasGroup>("forgeRow1").interactable, Is.False);
            Assert.That(panel.Require<GameButton>("forgeMain").GetComponentInChildren<GameText>().text, Does.Contain("158"));
            Click("forgeGrade");
            Assert.That(panel.Require<CanvasGroup>("forgeRow1").interactable, Is.True);
            Assert.That(panel.Require<CanvasGroup>("forgeRow2").interactable, Is.False);
            Input("forgeEnhance").text = "15";
            Input("forgeRolls0").text = "1"; Input("forgeValue0").text = "5";
            Input("forgeRolls1").text = "2"; Input("forgeValue1").text = "4";
            Input("forgeRolls2").text = "2"; Input("forgeValue2").text = "150";
            Input("forgeRolls3").text = "2"; Input("forgeValue3").text = "6";
            Assert.That(make.interactable, Is.False, "Rare +15 has only one shared upgrade for its two late rows.");
            Click("forgeGrade");
            Input("forgeRolls3").text = "3"; Input("forgeValue3").text = "9";
            Assert.That(make.interactable, Is.False, "Heroic +15 row four can roll at most twice.");
            Click("forgeGrade");
            Input("forgeEnhance").text = "3";
            Assert.That(Input("forgeRolls0").text, Is.EqualTo("2"));
            Input("forgeRolls0").text = "1";
            Input("forgeValue0").text = "6";
            Assert.That(make.interactable, Is.False);
            Input("forgeRolls1").text = "2";
            Input("forgeValue1").text = "4";
            Input("forgeCount").text = "2";
            Assert.That(make.interactable, Is.True);
            yield return DevelopmentGmCapture.Frame(canvas, "forge");
            Click("forgeMake");
            yield return new WaitUntil(() => requests.Count > before);
            var amount = Input("forgeCount");
            amount.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
            yield return null;
            if (amount.isFocused) amount.ProcessEvent(Event.KeyboardEvent("8"));
            Assert.That(amount.isFocused, Is.False, "A pointer click must not start an edit on the submitted quantity.");
            Assert.That(amount.text, Is.EqualTo("2"));
            Assert.That(amount.IsInteractable(), Is.False, "The submitted quantity must stay fixed until the POST is resolved.");
            Assert.That(Input("forgeEnhance").IsInteractable(), Is.False);
            Assert.That(Input("forgeValue0").IsInteractable(), Is.False);
            Assert.That(panel.Require<GameButton>("forgeTier").IsInteractable(), Is.False);
            Assert.That(make.IsInteractable(), Is.False, "A reply is still outstanding.");
            Click("forgeMake");
            Assert.That(requests.Count, Is.EqualTo(before + 1));
            releaseLostResponse();
            yield return new WaitUntil(() => panel.Require<GameText>("status").text.Contains("尚未确认"));
            Assert.That(make.IsInteractable(), Is.True);
            Assert.That(amount.IsInteractable(), Is.False, "Only the original recipe can be retried.");
            var original = requests.Last();
            Click("forgeMake");
            Assert.That(make.IsInteractable(), Is.False);
            yield return new WaitUntil(() => requests.Count == before + 2 && panel.Require<GameText>("status").text.Contains("已生效"));
            Assert.That(requests.Last(), Is.EqualTo(original), "Retry must preserve the entire ordered recipe and request ID.");
            Assert.That(amount.IsInteractable(), Is.True);
            Assert.That(amount.text, Is.EqualTo("2"));
            var payload = JObject.Parse(requests.Last());
            Assert.That((string)payload["action"], Is.EqualTo("forgeEquipment"));
            Assert.That((string)payload["key"], Is.EqualTo("weapon"));
            Assert.That((int)payload["value"], Is.EqualTo(2));
            Assert.That((string)payload["requestId"], Is.Not.Empty);
            var recipe = payload["forge"];
            Assert.That((int)recipe["itemLevel"], Is.EqualTo(85));
            Assert.That((int)recipe["enhanceLevel"], Is.EqualTo(3));
            Assert.That((string)recipe["grade"], Is.EqualTo("epic"));
            Assert.That((string)recipe["mainKind"], Is.EqualTo("attack"));
            Assert.That((string)recipe["setId"], Is.EqualTo("speed"));
            var subs = (JArray)recipe["substats"];
            Assert.That(subs.Count, Is.EqualTo(4));
            Assert.That(subs.Select(s => (string)s["kind"]).Distinct().Count(), Is.EqualTo(4));
            Assert.That(subs.Sum(s => (int)s["rolls"]), Is.EqualTo(5));
            Assert.That((int)subs.Single(s => (string)s["kind"] == "healthPct")["value"], Is.EqualTo(600));
            Assert.That((int)subs.Single(s => (string)s["kind"] == "speed")["value"], Is.EqualTo(4));
            amount.text = "8";
            Assert.That(make.IsInteractable(), Is.True);
        }
    }
}
#endif
