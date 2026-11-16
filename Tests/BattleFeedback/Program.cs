using AstralOath.Client.BattleScene;
using AstralOath.Shared.Battle;

int passed = 0, failed = 0;
Check("only accepted primary soul burn requests an avatar effect", () => {
    var state = new BattleFeedbackCueTracker();
    Equal(false, state.TryTake(1, BattleFlowEventKind.SoulChanged, BattleActionOrigin.Primary, true, out _, out _));
    Equal(false, state.TryTake(2, BattleFlowEventKind.Action, BattleActionOrigin.Primary, false, out _, out _));
    Equal(true, state.TryTake(3, BattleFlowEventKind.Action, BattleActionOrigin.Primary, true, out var cue, out var burn));
    Equal(BattleFeedbackCue.None, cue); Equal(true, burn);
});
foreach (var pair in new[] { (BattleActionOrigin.Counter, BattleFeedbackCue.Counter),
    (BattleActionOrigin.DualAttack, BattleFeedbackCue.DualAttack), (BattleActionOrigin.ExtraAttack, BattleFeedbackCue.ExtraAttack) })
    Check("separate action " + pair.Item1, () => {
        var state = new BattleFeedbackCueTracker();
        Equal(true, state.TryTake(1, BattleFlowEventKind.Action, pair.Item1, true, out var cue, out var burn));
        Equal(pair.Item2, cue); Equal(false, burn);
    });
Check("extra turn is a separate event and passive is not an extra attack", () => {
    var state = new BattleFeedbackCueTracker();
    Equal(false, state.TryTake(1, BattleFlowEventKind.Action, BattleActionOrigin.Passive, false, out _, out _));
    Equal(true, state.TryTake(2, BattleFlowEventKind.ExtraTurnGranted, BattleActionOrigin.Primary, false, out var cue, out var burn));
    Equal(BattleFeedbackCue.ExtraTurn, cue); Equal(false, burn);
});
Check("repeated response cannot replay either effect", () => {
    var state = new BattleFeedbackCueTracker();
    Equal(true, state.TryTake(9, BattleFlowEventKind.Action, BattleActionOrigin.Primary, true, out _, out _));
    Equal(false, state.TryTake(9, BattleFlowEventKind.Action, BattleActionOrigin.Primary, true, out _, out var burn));
    Equal(false, burn);
});
Check("late scheduled extra-turn does not get lost behind newer action", () => {
    var state = new BattleFeedbackCueTracker();
    Equal(true, state.TryTake(10, BattleFlowEventKind.Action, BattleActionOrigin.Counter, false, out _, out _));
    Equal(true, state.TryTake(7, BattleFlowEventKind.ExtraTurnGranted, BattleActionOrigin.Primary, false, out var cue, out _));
    Equal(BattleFeedbackCue.ExtraTurn, cue);
});
Check("snapshot reset excludes historical events and new battle clears floor", () => {
    var state = new BattleFeedbackCueTracker(); state.Clear(20);
    Equal(false, state.TryTake(19, BattleFlowEventKind.Action, BattleActionOrigin.Counter, false, out _, out _));
    Equal(true, state.TryTake(21, BattleFlowEventKind.Action, BattleActionOrigin.Counter, false, out _, out _));
    state.Clear(0);
    Equal(true, state.TryTake(1, BattleFlowEventKind.Action, BattleActionOrigin.Counter, false, out _, out _));
});
Check("counter onset uses observed red lightning, never the counter status icon", () => {
    Equal("stse_chain", BattleFeedbackCueTracker.SourceFor(BattleFeedbackCue.Counter));
});
Check("extra turn retains its own arrow/text source after counter correction", () => {
    Equal("stse_addturn", BattleFeedbackCueTracker.SourceFor(BattleFeedbackCue.ExtraTurn));
    Equal<string>(null, BattleFeedbackCueTracker.SourceFor(BattleFeedbackCue.None));
});
Check("unverified dual and extra action mappings retain their existing brief source", () => {
    Equal("stse_chain", BattleFeedbackCueTracker.SourceFor(BattleFeedbackCue.DualAttack));
    Equal("stse_chain", BattleFeedbackCueTracker.SourceFor(BattleFeedbackCue.ExtraAttack));
});
Console.WriteLine($"BattleFeedback: {passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

void Check(string name, Action test) { try { test(); passed++; Console.WriteLine("PASS " + name); } catch(Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
