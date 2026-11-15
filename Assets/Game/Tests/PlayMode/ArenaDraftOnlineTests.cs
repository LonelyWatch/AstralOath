#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AstralOath.Client.Arena;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class ArenaDraftOnlineTests
    {
        private const string HomePath = "Assets/Game/Modules/Home/Scenes/GameHome.unity";
        private Scene home;
        private Scene previous;
        private string endpoint, priorEndpoint, peerToken, roomPath;

        [UnityTest, Timeout(140000)]
        public IEnumerator HomeAndAuthenticatedOpponentDraftAndLoadBattle()
        {
            endpoint = Environment.GetEnvironmentVariable("ASTRAL_ARENA_TEST_URL");
            if (string.IsNullOrWhiteSpace(endpoint)) Assert.Ignore("Set ASTRAL_ARENA_TEST_URL to the local arena server.");
            priorEndpoint = Environment.GetEnvironmentVariable("ASTRAL_FOUNDATION_URL");
            Environment.SetEnvironmentVariable("ASTRAL_FOUNDATION_URL", endpoint);
            previous = SceneManager.GetActiveScene();
            home = EditorSceneManager.LoadSceneInPlayMode(HomePath, new LoadSceneParameters(LoadSceneMode.Additive));
            yield return Wait(() => home.IsValid() && home.isLoaded, "Home scene did not finish loading.");
            Assert.That(SceneManager.SetActiveScene(home), Is.True);
            yield return Wait(() => Screen("HomeWindow"), "Home did not load.");
            Click(Screen("HomeWindow"), "arena");
            yield return Wait(() => Screen("ArenaDirectory") && Screen("ArenaDirectory").Require<GameButton>("create").interactable,
                "Arena directory did not open.");
            Click(Screen("ArenaDirectory"), "create");
            yield return Wait(() => Screen("ArenaCreateRoom"), "Create dialog did not open.");
            var create = Screen("ArenaCreateRoom");
            string name = "在线选禁" + RequestId()[..8];
            create.Require<TMP_InputField>("name").text = name;
            Click(create, "rule_basic");
            Click(create, "confirm");
            yield return Wait(() => Screen("ArenaRoom"), "Arena room did not open.");

            yield return Peer("sessions", new { displayName = "选禁对手" }, state => peerToken = (string)state["token"]);
            yield return Peer("rooms?query=" + Uri.EscapeDataString(name), null, response =>
            {
                var room = response["rooms"].Single();
                Assert.That((string)room["name"], Is.EqualTo(name));
                Assert.That((string)room["rule"], Is.EqualTo("basic"));
                Assert.That((int)room["prebanCount"], Is.EqualTo(2));
                Assert.That((bool)room["banProtection"], Is.True);
                roomPath = "rooms/" + (string)room["id"];
            });
            yield return Peer(roomPath + "/join", new { });
            yield return Wait(() => Screen("ArenaRoom").Require<GameText>("count").text == "2/50", "Second player did not appear.");
            yield return Peer(roomPath + "/draft/ready", new { ready = true });
            yield return Wait(() => Screen("ArenaRoom").Require<GameText>("guestReady").gameObject.activeInHierarchy,
                "The host did not see the guest ready.");
            yield return Capture("arena-draft-room-ready");
            Click(Screen("ArenaRoom"), "start");
            yield return Wait(() => Screen("ArenaDraft") && HeroCount(Screen("ArenaDraft"), "grid") >= 14, "Preban grid did not initialize.");
            var draft = Screen("ArenaDraft");
            JObject state = null;
            string route = roomPath + "/draft";
            yield return Peer(route, null, next => state = next);
            Assert.That((string)state["phase"], Is.EqualTo("preban"));
            var pool = state["eligibleHeroIds"].Values<string>().ToArray();
            ClickHero(draft, "grid", pool[0]);
            ClickHero(draft, "grid", pool[1]);
            var selectedMark = Hero(draft, "grid", pool[1]).GetComponent<BindingMap>().Require<Image>("mark");
            Assert.That(selectedMark.sprite.name, Is.EqualTo("preban_banned_icon"));
            var footer = draft.Require<GameObject>("preSelected").transform;
            var footerCards = footer.Cast<Transform>().Where(card => card.gameObject.activeSelf).ToArray();
            Assert.That(footerCards.Length, Is.EqualTo(2));
            var footerStar = footerCards[1].GetComponent<BindingMap>().Require<Image>("star0");
            var gridStar = Hero(draft, "grid", pool[1]).GetComponent<BindingMap>().Require<Image>("star0");
            Assert.That(footerStar.sprite, Is.SameAs(gridStar.sprite));
            Assert.That(footer.GetComponent<GridLayoutGroup>().cellSize.x, Is.EqualTo(138f).Within(.1f),
                "The selected portrait must use the native footer size.");
            Assert.That(selectedMark.rectTransform.anchorMax.x - selectedMark.rectTransform.anchorMin.x,
                Is.GreaterThan(1f), "The slash must cover the gold portrait frame.");
            yield return new WaitForSecondsRealtime(.35f);
            yield return Capture("arena-draft-preban");
            Click(draft, "preAction");
            yield return ObserveHostSubmission(route, "preban", next => state = next);
            Assert.That(state["host"]["prebans"].Count(), Is.EqualTo(0));
            yield return Wait(() => !draft.Require<GameButton>("preAction").interactable, "Preban stayed clickable.");
            Assert.That(draft.Require<GameText>("preActionLabel").text, Is.EqualTo("选择"));
            Assert.That(draft.Require<GameButton>("preAction").targetGraphic.color.r, Is.LessThan(.5f));
            yield return Capture("bp-preban-waiting");
            yield return PeerAction(route, state, "preban", pool.Skip(2).Take(2).ToArray(), next => state = next);
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASTRAL_ARENA_CAPTURE_DIR")))
            {
                yield return Wait(() => draft.Require<GameObject>("reveal").activeInHierarchy, "Preban reveal did not show.");
                yield return Capture("arena-draft-preban-reveal");
            }

            yield return Wait(() => draft.Require<GameText>("title").text == "你的回合", "The draft did not enter the host turn.");
            Assert.That(draft.transform.Find("Native reference layout/Draft stage/Exit draft"), Is.Null);
            Assert.That(draft.Require<GameObject>("revealedOwn").transform.childCount, Is.EqualTo(2));
            Assert.That(draft.Require<GameObject>("revealedEnemy").transform.childCount, Is.EqualTo(2));
            yield return Wait(() => draft.Require<GameButton>("openRosterFilters").interactable,
                "The roster did not become interactive after the reveal.");
            var scroll = draft.Require<ScrollRect>("roster");
            var rollFirst = (RectTransform)Hero(draft, "roster", pool[0]).transform;
            var rollThird = (RectTransform)Hero(draft, "roster", pool[2]).transform;
            float rowStep = (rollFirst.anchoredPosition.y - rollThird.anchoredPosition.y) / 2f;
            Assert.That(-rollFirst.anchoredPosition.y, Is.GreaterThan(2f * rowStep), "First row must begin on the third slot.");
            Assert.That(rollFirst.localScale.x, Is.GreaterThan(rollThird.localScale.x));
            var rowMap = rollFirst.GetComponent<BindingMap>();
            var star = rowMap.Require<Image>("star0");
            var nextStar = rowMap.Require<Image>("star1");
            Assert.That(star.sprite.name, Is.EqualTo("cm_icon_star_j"));
            Assert.That(nextStar.rectTransform.anchorMin.x - star.rectTransform.anchorMin.x,
                Is.LessThan(star.rectTransform.anchorMax.x - star.rectTransform.anchorMin.x));
            // Compare the live prefab on both sides with the authored slot_line attachment bounds.
            var plateWidths = new[] { 291f, 226f, 196f, 196f, 226f };
            var glowHeights = new[] { 131f, 132f, 131f, 131f, 130f };
            for (int slot = 0; slot < plateWidths.Length; slot++)
            {
                foreach (string side in new[] { "blue", "red" })
                {
                    RectTransform outline = draft.Require<BindingMap>(side + slot).Require<Image>("pending").rectTransform;
                    Vector2 extent = outline.anchorMax - outline.anchorMin;
                    Assert.That(extent.x * plateWidths[slot], Is.EqualTo(plateWidths[slot] + 44f).Within(.01f), side + slot);
                    Assert.That(extent.y * 85f, Is.EqualTo(glowHeights[slot]).Within(.01f), side + slot);
                    Assert.That(Vector2.Distance((outline.anchorMin + outline.anchorMax) * .5f, Vector2.one * .5f),
                        Is.LessThan(.001f), side + slot);
                }
            }
            var emptyOutline = draft.Require<BindingMap>("blue0").Require<Image>("pending");
            Assert.That(emptyOutline.enabled, Is.True);
            Assert.That(emptyOutline.sprite.name, Does.EndWith("_glow_blue"));
            Assert.That(emptyOutline.color.b, Is.GreaterThan(emptyOutline.color.r));
            float previousAlpha = emptyOutline.color.a;
            yield return Wait(() => Mathf.Abs(emptyOutline.color.a - previousAlpha) > .2f,
                "The pending slot outline did not breathe.", 2f);
            yield return Wait(() => emptyOutline.color.a > .88f, "The pending slot outline did not brighten.", 2f);
            yield return Capture("arena-draft-roster-initial");
            scroll.content.anchoredPosition = new Vector2(0, rowStep * 2f);
            yield return Wait(() => rollThird.localScale.x > rollFirst.localScale.x, "Roster focus did not follow scroll.");
            Assert.That(rollThird.GetSiblingIndex(), Is.EqualTo(scroll.content.childCount - 1));
            var sidebar = (RectTransform)scroll.transform.parent;
            var focusBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(sidebar, rollThird);
            var viewportBounds = new Bounds(sidebar.InverseTransformPoint(scroll.viewport.TransformPoint(scroll.viewport.rect.center)), scroll.viewport.rect.size);
            Assert.That(focusBounds.min.x, Is.LessThan(sidebar.rect.xMin - sidebar.rect.width * .06f),
                "The focused card must extend visibly past the sidebar divider.");
            Assert.That(focusBounds.min.x, Is.GreaterThanOrEqualTo(viewportBounds.min.x),
                "The scroll viewport must not clip the focused card horizontally.");
            float plateRight = focusBounds.max.x - 8f * rollThird.localScale.x;
            Assert.That(sidebar.rect.xMax - plateRight, Is.InRange(-1f, 4f),
                "The focused card must align with the sidebar right edge.");

            var visibleRows = scroll.content.Cast<RectTransform>()
                .Where(row => row.gameObject.activeInHierarchy)
                .OrderByDescending(row => row.anchoredPosition.y).ToArray();
            for (int i = 1; i < visibleRows.Length; i++)
            {
                var upperRow = visibleRows[i - 1];
                var lowerRow = visibleRows[i];
                float overlap = (upperRow.rect.height * upperRow.localScale.y +
                    lowerRow.rect.height * lowerRow.localScale.y) * .5f -
                    (upperRow.anchoredPosition.y - lowerRow.anchoredPosition.y);
                Assert.That(overlap, Is.GreaterThan(10f), "Even the smallest neighboring cards should overlap.");
            }
            Assert.That(scroll.content.anchoredPosition.y + rollFirst.anchoredPosition.y,
                Is.GreaterThan(scroll.content.anchoredPosition.y + rollThird.anchoredPosition.y));
            yield return Capture("arena-draft-roster-scrolled");
            scroll.content.anchoredPosition = Vector2.zero;
            yield return Wait(() => draft.Require<GameButton>("openRosterFilters").interactable,
                "Roster filter was not enabled after the reveal.");
            Click(draft, "openRosterFilters");
            var filter = draft.Require<GameObject>("rosterFilters").GetComponent<BindingMap>();
            yield return Wait(() => filter.gameObject.activeInHierarchy, "Roster filter did not open.");
            yield return Capture("arena-draft-roster-filter");
            var options = filter.Require<BindingMap>("element").Require<RectTransform>("options");
            Click(options.Find("fire").GetComponent<BindingMap>(), "button");
            Assert.That(Hero(draft, "roster", "c1001").gameObject.activeSelf, Is.True);
            yield return Wait(() => Hero(draft, "roster", "c1003") == null, "Fire filter did not hide the ice hero c1003.");
            Click(options.Find("all").GetComponent<BindingMap>(), "button");
            Assert.That(Hero(draft, "roster", "c1003").gameObject.activeSelf, Is.True);
            Click(filter, "close");

            int beforePreview = (int)state["version"];
            string scrolledCandidate = pool[5];
            var focusedRow = (RectTransform)Hero(draft, "roster", scrolledCandidate).transform;
            float focusOffset = rollFirst.anchoredPosition.y - focusedRow.anchoredPosition.y;
            scroll.content.anchoredPosition = new Vector2(0, focusOffset + .27f * rowStep);
            yield return ReadUntil(route, next => (string)next["previewHeroId"] == scrolledCandidate, next => state = next);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(focusOffset).Within(1f));
            Assert.That((int)state["version"], Is.EqualTo(beforePreview));
            Assert.That(state["host"]["picks"].Count(), Is.Zero);
            Assert.That(draft.Require<GameButton>("action").interactable, Is.True);
            yield return Capture("arena-draft-scroll-preview");

            bool[] hostPicks = { true, false, false, true, true, false, false, true, true, false };
            for (int i = 0; i < hostPicks.Length; i++)
            {
                string candidate = pool[4 + i];
                if (!hostPicks[i])
                {
                    if (i == 1)
                    {
                        var opponentCard = draft.Require<BindingMap>("red0");
                        var portrait = opponentCard.Require<Image>("portrait");
                        string opponentPreview = pool[7];
                        yield return Peer(route + "/preview", new
                        {
                            draftId = (string)state["id"], version = (int)state["version"],
                            heroId = opponentPreview, sequence = 1
                        }, next =>
                        {
                            Assert.That((string)next["previewHeroId"], Is.EqualTo(opponentPreview));
                            Assert.That((int)next["version"], Is.EqualTo((int)state["version"]));
                            Assert.That(next["guest"]["picks"].Count(), Is.Zero);
                        });
                        yield return Wait(() => portrait.enabled && portrait.sprite, "The remote candidate did not show in the empty team slot.");
                        var draftArt = AssetDatabase.LoadAssetAtPath<ArenaDraftArt>("Assets/Game/Modules/Arena/Configs/ArenaDraftArt.asset");
                        var previewArt = draftArt.Heroes.Single(hero => hero.Id == opponentPreview);
                        var central = draft.Require<Image>("featuredFallback");
                        var previewSprite = previewArt.Illustration ? previewArt.Illustration : previewArt.Card;
                        yield return Wait(() => central.sprite == previewSprite,
                            "The opponent candidate must also replace the central hero portrait.", 3f);
                        Assert.That(opponentCard.Require<GameButton>("button").interactable, Is.False);
                        var opponentOutline = opponentCard.Require<Image>("pending");
                        Assert.That(opponentOutline.enabled, Is.True);
                        Assert.That(opponentOutline.sprite.name, Does.EndWith("_glow_red"));
                        Assert.That(opponentOutline.color.r, Is.GreaterThan(.9f));
                        Assert.That(opponentOutline.color.g, Is.LessThan(.5f));
                        float portraitAlpha = portrait.color.a;
                        yield return Wait(() => Mathf.Abs(portrait.color.a - portraitAlpha) > .14f,
                            "The opponent candidate did not flash.", 2f);
                        yield return Wait(() => opponentOutline.color.a > .88f, "The red pending outline did not brighten.", 2f);
                        yield return Capture("arena-draft-opponent-preview");
                    }

                    yield return PeerAction(route, state, "pick", new[] { candidate }, next => state = next);
                    if (i == 1)
                    {
                        var redEffect = draft.Require<BindingMap>("red0").Require<RawImage>("confirm");
                        yield return Wait(() => redEffect.enabled && redEffect.texture, "The opponent's confirmed pick did not play red feedback.");
                        yield return CapturePickSequence(redEffect, "bp-pick-confirm-red");
                    }
                    continue;
                }
                yield return Wait(() => Hero(draft, "roster", candidate)?.interactable == true, "Host hero did not become selectable.");
                ClickHero(draft, "roster", candidate);
                if (i == 3)
                {
                    var shield = draft.Require<BindingMap>("blue2");
                    Assert.That(shield.Require<GameText>("guardCaption").enabled, Is.True);
                    Assert.That(shield.Require<Image>("pending").enabled, Is.True);
                    yield return Capture("arena-draft-pick");
                }
                int expected = state["host"]["picks"].Count() + 1;
                Click(draft, "action");
                yield return ReadUntil(route, next => next["host"]["picks"].Count() == expected, next => state = next);
                if (i == 0)
                {
                    var effect = draft.Require<BindingMap>("blue0").Require<RawImage>("confirm");
                    var pickPortrait = draft.Require<RawImage>("featured");
                    yield return Wait(() => effect.enabled && effect.texture, "Our confirmed pick did not play blue feedback.");
                    Assert.That(pickPortrait.rectTransform.anchoredPosition.y > 0f ||
                        pickPortrait.material.GetFloat("_WhiteFlash") > 0f, Is.True, "The featured hero did not hop or flash.");
                    yield return CapturePickSequence(effect, "bp-pick-confirm-blue");
                    yield return Wait(() => !effect.enabled, "The confirmed pick effect did not release.");
                    Assert.That(pickPortrait.material.GetFloat("_WhiteFlash"), Is.Zero);
                    Assert.That(draft.Require<GameText>("actionLabel").text, Does.StartWith("等待中"));
                    Assert.That(draft.Require<GameButton>("action").interactable, Is.False);
                    Assert.That(draft.Require<Image>("actionArrow").enabled, Is.True);
                    Assert.That(draft.Require<GameButton>("action").targetGraphic.color.a, Is.LessThan(.4f));
                    yield return Capture("bp-pick-opponent-waiting");
                }
            }
            Assert.That((string)state["phase"], Is.EqualTo("ban"));
            var target = draft.Require<BindingMap>("red0").Require<GameButton>("button");
            var protectedSlot = draft.Require<BindingMap>("red2");
            yield return Wait(() => target.interactable && !protectedSlot.Require<GameButton>("button").interactable,
                "Final ban did not enable ordinary targets and protect the third pick.");
            Assert.That(protectedSlot.Require<Image>("filledGuard").enabled, Is.True);
            var featured = draft.Require<RawImage>("featured");
            yield return Wait(() => !featured.enabled, "Final ban must start with an empty center.");
            yield return Capture("bp-final-ban-empty");
            target.onClick.Invoke();
            Assert.That(featured.enabled, Is.True);
            Assert.That(featured.color.r, Is.EqualTo(featured.color.g));
            Assert.That(featured.color.r, Is.LessThan(.6f));
            Assert.That(draft.Require<BindingMap>("red0").Require<Image>("dim").gameObject.activeSelf, Is.True);
            Assert.That(featured.material.GetFloat("_Desaturation"), Is.GreaterThan(.5f));
            yield return Capture("arena-draft-final-ban");
            Click(draft, "action");
            yield return ObserveHostSubmission(route, "ban", next => state = next);
            Assert.That(state["host"]["ban"].Type, Is.EqualTo(JTokenType.Null));
            yield return Wait(() => draft.Require<GameText>("actionLabel").text.StartsWith("等待中", StringComparison.Ordinal),
                "Final ban submit did not show waiting.");
            Assert.That(draft.Require<GameButton>("action").targetGraphic.color.a, Is.LessThan(.65f));
            yield return Capture("bp-final-ban-waiting");
            yield return Wait(() => draft.Require<RawImage>("banFeedback").enabled, "The native BANNED effect did not play.");
            yield return new WaitForSecondsRealtime(.46f);
            yield return Capture("bp-final-ban-effect");
            yield return new WaitForSecondsRealtime(.36f);
            yield return Capture("bp-final-ban-stamp");
            string hostBanned = (string)state["host"]["picks"][0];
            yield return PeerAction(route, state, "ban", new[] { hostBanned }, next => state = next);

            yield return Wait(() => draft.Require<GameObject>("formation").activeInHierarchy, "Formation did not open.");
            foreach (string slot in new[] { "blue0", "red0" })
            {
                var banned = draft.Require<BindingMap>(slot);
                Assert.That(banned.Require<Image>("dim").gameObject.activeSelf, Is.True);
                Assert.That(banned.Require<Image>("mark").enabled, Is.True);
                Assert.That(banned.Require<Image>("portrait").color.r, Is.LessThan(.5f));
            }
            Assert.That(draft.Require<RawImage>("banFeedback").enabled, Is.False);
            var hostPicksSurviving = state["host"]["picks"].Values<string>().Where(id => id != hostBanned).ToArray();
            var guestPicksSurviving = state["guest"]["picks"].Values<string>().Where(id => id != (string)state["host"]["ban"]).ToArray();
            var first = draft.Require<BindingMap>("formation0").Require<GameButton>("button");
            var second = draft.Require<BindingMap>("formation1").Require<GameButton>("button");
            Assert.That(first.interactable && second.interactable, Is.True);
            first.onClick.Invoke();
            second.onClick.Invoke();
            (hostPicksSurviving[0], hostPicksSurviving[1]) = (hostPicksSurviving[1], hostPicksSurviving[0]);
            yield return Capture("arena-draft-formation");
            Click(draft, "action");
            yield return ObserveHostSubmission(route, "formation", next => state = next);
            Assert.That(state["host"]["formation"].Count(), Is.EqualTo(0));
            yield return Wait(() => !draft.Require<GameButton>("action").interactable, "Formation stayed clickable.");
            Assert.That(draft.Require<GameText>("actionLabel").text, Does.StartWith("等待中"));
            Assert.That(draft.Require<GameButton>("action").targetGraphic.color.a, Is.LessThan(.65f));
            yield return Capture("bp-formation-waiting");
            yield return PeerAction(route, state, "formation", guestPicksSurviving, next => state = next);

            Assert.That((string)state["phase"], Is.EqualTo("battle"));
            Assert.That(state["host"]["formation"].Values<string>(), Is.EqualTo(hostPicksSurviving));
            JObject battle = null;
            yield return Peer(roomPath + "/battle", null, next => battle = next);
            Assert.That((string)battle["snapshot"]["battleId"], Is.EqualTo((string)state["battleId"]));
            Assert.That(battle["snapshot"]["units"].Count(), Is.EqualTo(8));

            yield return Wait(() => SceneManager.GetSceneByPath(BattleOnlineLaunch.ScenePath).isLoaded,
                "Draft did not transition to the online battle scene.", 30);
            var battleScene = SceneManager.GetSceneByPath(BattleOnlineLaunch.ScenePath);
            var hostHud = battleScene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BattleHudPreview>(true)).Single();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var arrival = typeof(BattleHudPreview).GetField("arrival", flags);
            var online = typeof(BattleHudPreview).GetField("online", flags);
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASTRAL_ARENA_CAPTURE_DIR")))
            {
                var actorsReady = typeof(BattleHudPreview).GetProperty("OnlineArrivalActorsReady", flags);
                var cameraBlending = typeof(BattleHudPreview).GetProperty("OnlineArrivalCameraBlending", flags);
                var arrivalTime = arrival.FieldType.GetField("time", flags);
                var blendTime = arrival.FieldType.GetField("cameraTime", flags);
                bool InArrival() => arrival.GetValue(hostHud) != null;
                yield return Wait(() => InArrival() && (float)arrivalTime.GetValue(arrival.GetValue(hostHud)) >= .47f &&
                    battleScene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Transform>())
                    .Any(node => node.name.StartsWith("PvP Arrival Warning ", StringComparison.Ordinal) &&
                        node.GetComponentsInChildren<MeshRenderer>().Any(renderer => renderer.enabled)),
                    "The original warning Spine effect did not appear.", 35);
                yield return CaptureBattle(battleScene, hostHud, "arena-draft-entry-warning", 1920, 900, gateOverlay: true);
                yield return Wait(() => InArrival() && (bool)actorsReady.GetValue(hostHud),
                    "All actors did not arrive before the camera transition.", 10);
                yield return CaptureBattle(battleScene, hostHud, "arena-draft-entry-grounded", 1920, 1080, gateOverlay: true);
                yield return Wait(() => InArrival() && (bool)cameraBlending.GetValue(hostHud) &&
                    (float)blendTime.GetValue(arrival.GetValue(hostHud)) >= .25f,
                    "The camera did not move closer at the end of entry.", 10);
                yield return CaptureBattle(battleScene, hostHud, "arena-draft-entry-camera", 1920, 900, gateOverlay: true);
            }
            yield return Wait(() => online.GetValue(hostHud) != null && arrival.GetValue(hostHud) == null,
                "The online battle did not complete stage entry.", 40);
            // The second seat in this fixture is an HTTP peer, so explicitly acknowledge
            // its presentation window after the local scene has completed real entry.
            JObject timedBattle = null;
            yield return Peer(roomPath + "/battle", null, value => timedBattle = value);
            var timedSnapshot = timedBattle["snapshot"];
            yield return Peer(roomPath + "/battle/ready", new
            {
                battleId = (string)timedSnapshot["battleId"], commandSequence = (ulong)timedSnapshot["lastCommandSequence"],
                eventSequence = (ulong)timedSnapshot["lastEventSequence"], actorId = (string)timedSnapshot["currentActorId"]
            }, null);
            var clockNode = hostHud.transform.Find("Online PvP HUD/Authoritative Turn Clock");
            yield return Wait(() => clockNode && clockNode.gameObject.activeInHierarchy,
                "Both ready seats did not produce an authoritative visible countdown.");
            var heroId = typeof(BattlePreviewActor).GetProperty("HeroId", flags);
            var units = battleScene.GetRootGameObjects().Single(root => root.name == "Units").transform;
            string[] HeroesAtPositions(string team) => units.Find(team).Cast<Transform>()
                .Select(slot => slot.GetComponentInChildren<BattlePreviewActor>(true))
                .Select(actor => (string)heroId.GetValue(actor)).ToArray();
            Assert.That(HeroesAtPositions("Allies"), Is.EqualTo(hostPicksSurviving));
            Assert.That(HeroesAtPositions("Enemies"), Is.EqualTo(guestPicksSurviving));
            AssertOnlineFraming(battleScene, hostHud.GetComponent<Canvas>().worldCamera, units);
            yield return CaptureBattle(battleScene, hostHud, "arena-draft-battle", 1920, 900);
            yield return CaptureBattle(battleScene, hostHud, "arena-draft-battle-16x9", 1920, 1080);
            yield return CaptureBattle(battleScene, hostHud, "arena-draft-battle-21x9", 2240, 960);
            var liveSession = online.GetValue(hostHud);
            BattleSnapshot LiveSnapshot() => (BattleSnapshot)liveSession.GetType().GetProperty("Snapshot", flags).GetValue(liveSession);
            var simulation = typeof(BattleHudPreview).GetField("simulation", flags).GetValue(hostHud);
            var busy = simulation.GetType().GetProperty("Busy", flags);
            var battleCatalog = JsonConvert.DeserializeObject<BattleCatalogData>(AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/Game/Modules/BattleScene/Configs/battle-catalog.json").text);
            var policy = new BattleAiPolicy(battleCatalog);
            for (int step = 0; step < 8 && LiveSnapshot().GetUnit(LiveSnapshot().CurrentActorId).Slot.Side == BattleSide.Enemy; step++)
            {
                var current = LiveSnapshot();
                var active = current.GetUnit(current.CurrentActorId);
                var choice = policy.ChooseTarget(current, active.Form.SkillIds[0]) ?? policy.ChooseAction(current);
                Assert.That(choice, Is.Not.Null);
                JObject commandReply = null;
                yield return Peer(roomPath + "/battle/commands", new
                {
                    battleId = current.BattleId, rulesVersion = current.RulesVersion,
                    sequence = current.LastCommandSequence + 1, commandId = Guid.NewGuid().ToString("N"),
                    actorId = current.CurrentActorId, skillId = choice.SkillId, targetId = choice.TargetId,
                    useSoulBurn = choice.UseSoulBurn, contentHash = (string)timedBattle["contentHash"]
                }, value => commandReply = value);
                Assert.That((int)commandReply["status"], Is.EqualTo((int)BattleCommandStatus.Accepted));
                ulong acceptedSequence = (ulong)commandReply["snapshot"]["lastCommandSequence"];
                yield return Wait(() => LiveSnapshot().LastCommandSequence >= acceptedSequence && !(bool)busy.GetValue(simulation),
                    "The accepted opponent action did not finish its real presentation.", 30);
                current = LiveSnapshot();
                yield return Peer(roomPath + "/battle/ready", new
                {
                    battleId = current.BattleId, commandSequence = current.LastCommandSequence,
                    eventSequence = current.LastEventSequence, actorId = current.CurrentActorId
                }, null);
                yield return Wait(() => clockNode.gameObject.activeInHierarchy, "The next real turn did not open its clock.");
            }
            Assert.That(LiveSnapshot().GetUnit(LiveSnapshot().CurrentActorId).Slot.Side, Is.EqualTo(BattleSide.Ally));
            Assert.That((bool)simulation.GetType().GetProperty("IsTurnEntryPlaying", flags).GetValue(simulation), Is.False,
                "The authoritative clock must wait for the visible turn-entry animation.");
            yield return CaptureBattle(battleScene, hostHud, "arena-draft-my-turn-16x9", 1920, 1080);
            yield return CaptureBattle(battleScene, hostHud, "arena-draft-my-turn-21x9", 2240, 960);
            var unit = LiveSnapshot().Units.First(entry => entry.Slot.Side == BattleSide.Enemy && entry.IsAlive);
            var views = (IDictionary)simulation.GetType().GetField("views", flags).GetValue(simulation);
            var deathView = views[unit.InstanceId];
            var particles = (TextAsset)deathView.GetType().GetField("deathParticles", flags).GetValue(deathView);
            Assert.That(particles.name, Is.EqualTo("death_pati_01.particle"), "Arena must use the red native death variant.");
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASTRAL_ARENA_CAPTURE_DIR")))
            {
                // Sample the same presentation in the live arena after normal authoritative turns are verified.
                simulation.GetType().GetProperty("IsPaused", flags).SetValue(simulation, true);
                var actor = (BattlePreviewActor)deathView.GetType().GetProperty("Actor", flags).GetValue(deathView);
                actor.PlayBattleAnimation("attacked");
                void Present(string name, params object[] args) => deathView.GetType().GetMethod(name, flags).Invoke(deathView, args);
                Present("SetHealth", 0L, unit.MaximumHealth);
                Present("BeginDeath");
                Present("Show", false);
                Present("TickVisual", .1f);
                yield return CaptureBattle(battleScene, hostHud, "arena-death-final-hit", 1920, 1080);
                Present("TickVisual", .17f);
                yield return CaptureBattle(battleScene, hostHud, "arena-death-silhouette", 1920, 1080);
                Present("TickVisual", .26f);
                yield return CaptureBattle(battleScene, hostHud, "arena-death-red-burst", 1920, 1080);
            }
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            if (home.IsValid() && !home.isLoaded)
            {
                float deadline = Time.realtimeSinceStartup + 8;
                while (home.IsValid() && !home.isLoaded && Time.realtimeSinceStartup < deadline) yield return null;
            }
            if (BattleOnlineLaunch.IsActive && SceneManager.GetSceneByPath(BattleOnlineLaunch.ScenePath).isLoaded)
            {
                var exit = BattleOnlineLaunch.ExitAsync();
                yield return Wait(() => exit.IsCompleted, "Online battle did not unload.");
                exit.GetAwaiter().GetResult();
            }
            var room = Screen("ArenaRoom");
            if (room)
            {
                Click(room, "leave");
                yield return Wait(() => !Screen("ArenaRoom"), "Arena room did not close.");
            }
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (home.IsValid() && home.isLoaded) yield return SceneManager.UnloadSceneAsync(home);
            if (roomPath != null && peerToken != null) yield return Peer(roomPath + "/leave", new { });
            if (endpoint != null) Environment.SetEnvironmentVariable("ASTRAL_FOUNDATION_URL", priorEndpoint);
        }

        private BindingMap Screen(string name) => !home.IsValid() || !home.isLoaded ? null : home.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<BindingMap>()).FirstOrDefault(map =>
                map.gameObject.name == name || map.gameObject.name == name + "(Clone)");

        private static string RequestId() => Guid.NewGuid().ToString("N");
        private static void Click(BindingMap map, string name)
        {
            var button = map.Require<GameButton>(name);
            Assert.That(button.interactable, Is.True, name);
            button.onClick.Invoke();
        }

        private static Transform Heroes(BindingMap map, string list) => map.Require<ScrollRect>(list).content;
        private static int HeroCount(BindingMap map, string list) => Heroes(map, list).childCount;
        private static GameButton Hero(BindingMap map, string list, string heroId)
        {
            var row = Heroes(map, list).Cast<Transform>().FirstOrDefault(item => item && item.gameObject.activeInHierarchy &&
                string.Equals(item.gameObject.name, heroId, StringComparison.Ordinal));
            return row ? row.GetComponent<BindingMap>().Require<GameButton>("button") : null;
        }
        private static void ClickHero(BindingMap map, string list, string heroId)
        {
            var button = Hero(map, list, heroId);
            Assert.That(button, Is.Not.Null, heroId + " in " + list + ": " +
                string.Join(", ", Heroes(map, list).Cast<Transform>().Select(child => child.gameObject.name)));
            Assert.That(button.interactable, Is.True, heroId);
            button.onClick.Invoke();
        }

        private IEnumerator ObserveHostSubmission(string route, string phase, Action<JObject> observe) =>
            ReadUntil(route, next => (string)next["phase"] == phase && (bool)next["host"]["submitted"], observe);

        private IEnumerator ReadUntil(string route, Func<JObject, bool> condition, Action<JObject> observe)
        {
            float deadline = Time.realtimeSinceStartup + 8;
            while (Time.realtimeSinceStartup < deadline)
            {
                JObject state = null;
                yield return Peer(route, null, next => state = next);
                if (condition(state))
                {
                    observe(state);
                    yield break;
                }
                yield return new WaitForSecondsRealtime(.14f);
            }
            Assert.Fail("The server did not observe the pending host selection.");
        }

        private IEnumerator PeerAction(string route, JObject draft, string kind, string[] ids, Action<JObject> observe) =>
            Peer(route + "/actions", new
            {
                requestId = RequestId(), draftId = (string)draft["id"], version = (int)draft["version"], kind, heroIds = ids
            }, observe);

        private IEnumerator Peer(string route, object payload, Action<JObject> receive = null)
        {
            using var request = new UnityWebRequest(new Uri(new Uri(endpoint), "/api/arena/" + route),
                payload == null ? UnityWebRequest.kHttpVerbGET : UnityWebRequest.kHttpVerbPOST)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 8
            };
            if (payload != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload)));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            if (peerToken != null) request.SetRequestHeader("Authorization", "Bearer " + peerToken);
            yield return request.SendWebRequest();
            Assert.That(request.result, Is.EqualTo(UnityWebRequest.Result.Success), $"{route}: HTTP {request.responseCode}");
            if (receive != null) receive(JObject.Parse(request.downloadHandler.text));
        }

        private static IEnumerator Wait(Func<bool> condition, string message, float seconds = 15)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, message);
        }

        private static void AssertOnlineFraming(Scene scene, Camera camera, Transform units)
        {
            var viewport = camera.GetComponent<BattlePreviewViewport>();
            var apply = typeof(BattlePreviewViewport).GetMethod("ApplyScreen", BindingFlags.Instance | BindingFlags.NonPublic);
            var backdrop = scene.GetRootGameObjects().Single(root => root.name == "RTA 021 Stage").GetComponent<SpriteRenderer>();
            void Apply(Vector2Int size, Rect safe) => apply.Invoke(viewport, new object[] { size, safe });
            try
            {
                foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(2240, 960) })
                {
                    Apply(size, new Rect(0, 0, size.x, size.y));
                    var lower = camera.WorldToViewportPoint(units.Find("Allies").GetChild(1).position);
                    var upper = camera.WorldToViewportPoint(units.Find("Allies").GetChild(2).position);
                    Assert.That(lower.y, Is.InRange(.12f, .18f), "The lower line must clear the bottom HUD.");
                    Assert.That(upper.y - lower.y, Is.GreaterThan(.2f), "The arena must preserve front-to-back depth.");
                    Assert.That(camera.WorldToViewportPoint(backdrop.bounds.center).y, Is.InRange(.66f, .70f));
                    var bottom = camera.WorldToViewportPoint(backdrop.bounds.min);
                    var top = camera.WorldToViewportPoint(backdrop.bounds.max);
                    Assert.That(bottom.x <= 0f && bottom.y <= 0f && top.x >= 1f && top.y >= 1f, Is.True,
                        "The stage must cover the final camera on both aspect ratios.");
                }
            }
            finally
            {
                Apply(new Vector2Int(UnityEngine.Screen.width, UnityEngine.Screen.height), UnityEngine.Screen.safeArea);
            }
        }

        private static IEnumerator CaptureBattle(Scene scene, BattleHudPreview host, string label,
            int width, int height, bool gateOverlay = false)
        {
            string folder = Environment.GetEnvironmentVariable("ASTRAL_ARENA_CAPTURE_DIR");
            if (string.IsNullOrEmpty(folder)) yield break;
            Directory.CreateDirectory(folder);
            var canvas = host.GetComponent<Canvas>();
            var scaler = host.GetComponent<CanvasScaler>();
            var worldCamera = canvas.worldCamera;
            float originalScale = canvas.scaleFactor;
            bool originalScalerEnabled = scaler.enabled;
            Assert.That(worldCamera, Is.Not.Null, "Use the camera the battle HUD actually renders through.");
            var cameras = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>())
                .Where(camera => camera.enabled && !camera.targetTexture).OrderBy(camera => camera.depth).ToArray();
            Assert.That(cameras, Does.Contain(worldCamera));
            var original = cameras.Select(camera => (camera.targetTexture, camera.rect, camera.aspect)).ToArray();
            var viewport = worldCamera.GetComponent<BattlePreviewViewport>();
            var apply = typeof(BattlePreviewViewport).GetMethod("ApplyScreen", BindingFlags.Instance | BindingFlags.NonPublic);
            var target = RenderTexture.GetTemporary(width, height, 24);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            var active = RenderTexture.active;
            var transitionCanvas = gateOverlay ? GameObject.Find("Online Battle Gate")?.GetComponent<Canvas>() : null;
            var transitionMode = transitionCanvas ? transitionCanvas.renderMode : default;
            var transitionCamera = transitionCanvas ? transitionCanvas.worldCamera : null;
            float transitionDistance = transitionCanvas ? transitionCanvas.planeDistance : 0f;
            try
            {
                foreach (var camera in cameras)
                {
                    camera.targetTexture = target;
                    camera.rect = new Rect(0, 0, 1, 1);
                    camera.aspect = (float)width / height;
                }
                // CanvasScaler uses the Game view's physical size, which differs from this render target.
                // One canvas unit per capture pixel lets the real viewport apply its design scale once.
                scaler.enabled = false;
                canvas.scaleFactor = 1f;
                apply.Invoke(viewport, new object[] { new Vector2Int(width, height), new Rect(0, 0, width, height) });
                if (transitionCanvas)
                {
                    transitionCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                    transitionCanvas.worldCamera = worldCamera;
                    transitionCanvas.planeDistance = 1f;
                }
                Canvas.ForceUpdateCanvases();
                yield return null;
                Canvas.ForceUpdateCanvases();
                foreach (var camera in cameras) camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply(false, false);

                var samples = image.GetPixels32();
                int illuminatedBackdropSamples = 0;
                for (int y = height / 5; y < height * 4 / 5; y += 40)
                for (int x = width / 5; x < width * 4 / 5; x += 40)
                {
                    var sample = samples[y * width + x];
                    if (sample.r + sample.g + sample.b > 96) illuminatedBackdropSamples++;
                }
                Assert.That(illuminatedBackdropSamples, Is.GreaterThan(80),
                    "The battle capture must contain the rendered arena across the frame.");
                File.WriteAllBytes(Path.Combine(folder, label + ".png"), image.EncodeToPNG());
            }
            finally
            {
                for (int i = 0; i < cameras.Length; i++)
                {
                    cameras[i].targetTexture = original[i].targetTexture;
                    cameras[i].rect = original[i].rect;
                    cameras[i].aspect = original[i].aspect;
                }
                if (transitionCanvas)
                {
                    transitionCanvas.renderMode = transitionMode;
                    transitionCanvas.worldCamera = transitionCamera;
                    transitionCanvas.planeDistance = transitionDistance;
                }
                canvas.scaleFactor = originalScale;
                scaler.enabled = originalScalerEnabled;
                apply.Invoke(viewport, new object[] { new Vector2Int(UnityEngine.Screen.width, UnityEngine.Screen.height), UnityEngine.Screen.safeArea });
                Canvas.ForceUpdateCanvases();
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(image);
            }
        }

        private static IEnumerator CapturePickSequence(RawImage surface, string label)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASTRAL_ARENA_CAPTURE_DIR"))) yield break;
            float firstFrame = Time.unscaledTime;
            yield return Capture(label);
            yield return new WaitUntil(() => Time.unscaledTime >= firstFrame + .20f);
            if (surface.enabled) yield return Capture(label + "-outline");
        }

        private static IEnumerator Capture(string label)
        {
            string path = Environment.GetEnvironmentVariable("ASTRAL_ARENA_CAPTURE_DIR");
            if (string.IsNullOrEmpty(path)) yield break;
            Directory.CreateDirectory(path);
            yield return null;
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            var camera = roots.SelectMany(root => root.GetComponentsInChildren<Camera>()).First(item => item && item.enabled);
            var canvas = roots.SelectMany(root => root.GetComponentsInChildren<Canvas>()).First(item => item && item.isRootCanvas);
            foreach (var portrait in UnityEngine.Object.FindObjectsOfType<Camera>().Where(item => item.targetTexture && item != camera))
                portrait.Render();
            const int width = 1920, height = 900;
            var target = RenderTexture.GetTemporary(width, height, 24);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            var active = RenderTexture.active;
            var originalTarget = camera.targetTexture;
            var originalMask = camera.cullingMask;
            var originalMode = canvas.renderMode;
            var originalCamera = canvas.worldCamera;
            var originalDistance = canvas.planeDistance;
            try
            {
                camera.targetTexture = target;
                camera.cullingMask = ~0;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases();
                yield return null;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(path, label + ".png"), image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = originalTarget;
                camera.cullingMask = originalMask;
                canvas.renderMode = originalMode;
                canvas.worldCamera = originalCamera;
                canvas.planeDistance = originalDistance;
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(image);
            }
        }
    }
}
#endif
