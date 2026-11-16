using System.Collections.Generic;
using AstralOath.Shared.Battle;

namespace AstralOath.Client.BattleScene
{
    internal enum BattleFeedbackCue { None, Counter, DualAttack, ExtraAttack, ExtraTurn }

    // Presentation deduplication only. Delayed extra-turn callbacks can arrive after a later action.
    internal sealed class BattleFeedbackCueTracker
    {
        private readonly HashSet<ulong> shown = new HashSet<ulong>();
        private ulong resetSequence;

        internal bool TryTake(ulong sequence, BattleFlowEventKind kind, BattleActionOrigin origin, bool soulBurn,
            out BattleFeedbackCue cue, out bool burn)
        {
            cue = BattleFeedbackCue.None;
            burn = false;
            if (sequence <= resetSequence || shown.Contains(sequence)) return false;
            if (kind == BattleFlowEventKind.ExtraTurnGranted) cue = BattleFeedbackCue.ExtraTurn;
            else if (kind == BattleFlowEventKind.Action)
            {
                cue = origin switch
                {
                    BattleActionOrigin.Counter => BattleFeedbackCue.Counter,
                    BattleActionOrigin.DualAttack => BattleFeedbackCue.DualAttack,
                    BattleActionOrigin.ExtraAttack => BattleFeedbackCue.ExtraAttack,
                    _ => BattleFeedbackCue.None
                };
                burn = origin == BattleActionOrigin.Primary && soulBurn;
            }
            if (cue == BattleFeedbackCue.None && !burn) return false;
            shown.Add(sequence);
            return true;
        }

        internal static string SourceFor(BattleFeedbackCue cue) => cue switch
        {
            BattleFeedbackCue.Counter => "stse_chain",
            BattleFeedbackCue.DualAttack => "stse_chain",
            BattleFeedbackCue.ExtraAttack => "stse_chain",
            BattleFeedbackCue.ExtraTurn => "stse_addturn",
            _ => null
        };

        internal void Clear(ulong lastEventSequence)
        {
            shown.Clear();
            resetSequence = lastEventSequence;
        }
    }
}
