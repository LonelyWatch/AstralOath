using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Skeletons;
using AstralOath.Shared.Battle;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene
{
    internal sealed class BattleResultBinder : IDisposable
    {
        private readonly BindingScope scope = new BindingScope(CancellationToken.None);
        private readonly ISkeletonPlayer emblem;
        private readonly string loop;

        internal BattleResultBinder(Transform parent, BattleOnlineTransitionAssets assets, BattleOnlineTransitionAssets.Copy copy,
            BattleSnapshot snapshot, IReadOnlyDictionary<string, Sprite> sprites, Func<string, string> label, Action confirm)
        {
            if (snapshot.Outcome == BattleOutcome.InProgress) throw new ArgumentException("A terminal battle snapshot is required.");
            var instance = UnityEngine.Object.Instantiate(assets.resultPrefab, parent, false);
            instance.name = assets.resultPrefab.name;
            scope.Add(() =>
            {
                if (!instance) return;
                instance.SetActive(false);
                instance.transform.SetParent(null, false);
                UnityEngine.Object.Destroy(instance);
            });
            try
            {
                var view = new BattleResult();
                view.Bind(instance);
                var map = view.Map;
                var owner = parent.GetComponentInParent<Canvas>();
                var canvas = instance.GetComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingLayerID = owner.sortingLayerID;
                canvas.sortingOrder = 31002;
                if (snapshot.Outcome != BattleOutcome.Draw)
                {
                    bool won = snapshot.Outcome == BattleOutcome.AllyVictory;
                    string prefix = won ? "victory" : "defeat";
                    loop = prefix + "_loop";
                    // The atlas export is double-size. Match result_base's top anchor and P2's
                    // settled VICTORY word (about 27% of the frame width at 08:47).
                    emblem = assets.resultEmblem.Create(map.Require<RectTransform>("emblem"), "Battle Result Emblem",
                        .75f * (won ? .85f : .9f), canvas.sortingOrder + 1);
                    scope.Add(emblem.Dispose);
                    emblem.Root.SetActive(true);
                    emblem.Play(prefix + "_instant", false);
                    emblem.Tick(0);
                    var audio = instance.GetComponent<AudioSource>();
                    audio.clip = won ? assets.resultVictory : assets.resultDefeat;
                    var resultSound = UiAudioPlayback.Play(audio, audio.clip);
                    if (resultSound) scope.Add(resultSound.FadeOut);
                }
                BindTeam(BattleSide.Ally, 0);
                BindTeam(BattleSide.Enemy, 4);
                map.Require<GameText>("turn").text = string.Format(copy.resultTurn, snapshot.TurnNumber);
                map.Require<GameText>("title").text = snapshot.Outcome == BattleOutcome.Draw ? copy.resultDraw : copy.resultDetails;
                map.Require<GameText>("allies").text = copy.localPlayer + " · " + copy.resultHealth;
                map.Require<GameText>("enemies").text = copy.opponentPlayer + " · " + copy.resultHealth;
                map.Require<GameText>("detailsLabel").text = copy.resultDetails;
                map.Require<GameText>("confirmLabel").text = copy.resultConfirm;
                map.Require<GameText>("closeLabel").text = copy.resultClose;
                var statistics = map.Require<GameObject>("statistics");
                Toggle(false);
                Bind(map.Require<GameButton>("details"), () => Toggle(true));
                Bind(map.Require<GameButton>("close"), () => Toggle(false));
                Bind(map.Require<GameButton>("confirm"), () => confirm());
                view.Show();

                void Toggle(bool open)
                {
                    statistics.SetActive(open);
                    if (emblem != null) emblem.Root.SetActive(!open);
                }
                void BindTeam(BattleSide side, int start)
                {
                    var units = snapshot.Units.Where(unit => unit.Slot.Side == side).OrderBy(unit => unit.Slot.Position).ToArray();
                    for (int i = 0; i < 4; i++)
                    {
                        var line = map.Require<GameText>("unit" + (start + i));
                        line.gameObject.SetActive(i < units.Length);
                        if (side == BattleSide.Ally) map.Require<GameObject>("hero" + i).SetActive(i < units.Length);
                        if (i >= units.Length) continue;
                        var unit = units[i];
                        line.text = label(unit.NameKey) + "     " + unit.CurrentHealth + "/" + unit.MaximumHealth;
                        if (side != BattleSide.Ally) continue;
                        var face = map.Require<Image>("face" + i);
                        face.sprite = sprites[unit.DefinitionId + "_s"];
                        map.Require<GameText>("friendship" + i).text = "1";
                    }
                }
            }
            catch { scope.Dispose(); throw; }
        }

        private void Bind(GameButton button, UnityAction action)
        {
            button.onClick.AddListener(action);
            scope.Add(() => button.onClick.RemoveListener(action));
        }

        internal void Tick(float delta)
        {
            if (emblem == null || !emblem.Root.activeSelf) return;
            float remaining = emblem.Duration - emblem.Time;
            if (emblem.CurrentAnimation != loop && delta >= remaining)
            {
                emblem.Play(loop, true);
                delta -= Mathf.Max(0, remaining);
            }
            emblem.Tick(delta);
        }

        public void Dispose() => scope.Dispose();
    }
}
