using SeoYuGi.Battle;
using SeoYuGi.Prediction;

namespace SeoYuGi.Integration
{
    /// <summary>
    /// 인텐트 → 코어 시스템 즉시 실행 (싱글 + 멀티 호스트 공용).
    /// 검증은 전부 기존 TryX가 담당 — AP·쿨타임·사거리·지형 검사가 이미 코어에 있다.
    /// </summary>
    public class LocalIntentSink : IIntentSink
    {
        readonly BattleState state;
        readonly MoveSystem move;
        readonly CombatSystem combat;
        readonly HackSystem hack;

        public LocalIntentSink(BattleState state, MoveSystem move, CombatSystem combat, HackSystem hack)
        {
            this.state = state;
            this.move = move;
            this.combat = combat;
            this.hack = hack;
        }

        public IntentResult Submit(in BattleIntent intent)
        {
            switch (intent.kind)
            {
                case IntentKind.Move:
                    var attempt = move.TryMove(intent.unitId, intent.target);
                    return new IntentResult { accepted = attempt.success, moveDenied = attempt.denied };

                case IntentKind.Attack:
                    return FromAct(combat.TryAttack(intent.unitId, intent.target));

                case IntentKind.Skill:
                    return FromAct(combat.TrySkill(intent.unitId, intent.target));

                case IntentKind.Guard:
                    return FromAct(combat.TryGuard(intent.unitId));

                case IntentKind.Hack:
                    var u = state.GetUnit(intent.unitId);
                    bool ok = u != null && hack != null && hack.TryHack(intent.unitId, (TeamId)u.team);
                    return ok ? IntentResult.Accepted : default;

                default:
                    return default;
            }
        }

        static IntentResult FromAct(ActDenied denied) =>
            new IntentResult { accepted = denied == ActDenied.None, actDenied = denied };
    }
}
