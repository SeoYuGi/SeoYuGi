using SeoYuGi.Ai;
using SeoYuGi.Battle;
using SeoYuGi.Prediction;

namespace SeoYuGi.Integration
{
    /// <summary>
    /// AI 슬롯 1개 = AiBrain 1개 + 명령 실행. 적팀 3기와 아군 백필이 전부 이걸 쓴다.
    /// AiCommand → Core 시스템 호출 매핑. 실패(게이지 잠금 등)는 무시 — 뇌가 다음 틱에 재판단.
    /// </summary>
    public class AiSlotDriver
    {
        readonly int unitId;
        readonly int team;
        readonly AiBrain brain;
        readonly MoveSystem move;
        readonly CombatSystem combat;
        readonly HackSystem hack;

        /// <summary>예측 사격 성공 제출 (unitId, 목표 칸) — 적중/실패 연출 배선용.</summary>
        public event System.Action<int, SeoYuGi.Prediction.Cell> OnPredictedShot;

        public AiSlotDriver(int unitId, int team, UnitClass cls, MoveSystem move, CombatSystem combat,
            Predictor predictor = null, HackSystem hack = null)
        {
            this.unitId = unitId;
            this.team = team;
            this.move = move;
            this.combat = combat;
            this.hack = hack;
            brain = new AiBrain(unitId, AiConfig.ForClass((ClassId)(int)cls), predictor);
        }

        public void Tick(IWorldView world)
        {
            var cmd = brain.Tick(world);
            switch (cmd.Type)
            {
                case CommandType.Move:
                    move.TryMove(unitId, ToCoord(cmd.Target));
                    break;
                case CommandType.Attack:
                    if (combat.TryAttack(unitId, ToCoord(cmd.Target)) == ActDenied.None && cmd.Predicted)
                        OnPredictedShot?.Invoke(unitId, cmd.Target);
                    break;
                case CommandType.Heavy:
                    if (combat.TrySkill(unitId, cmd.SkillIndex, ToCoord(cmd.Target)) == ActDenied.None && cmd.Predicted)
                        OnPredictedShot?.Invoke(unitId, cmd.Target);
                    break;
                case CommandType.Decoy:
                    hack?.TryHack(unitId, (TeamId)team); // 해킹 — 5초간 적 예측 교란
                    break;
            }
        }

        static Coord ToCoord(SeoYuGi.Prediction.Cell c) => new Coord(c.X, c.Y);
    }
}
