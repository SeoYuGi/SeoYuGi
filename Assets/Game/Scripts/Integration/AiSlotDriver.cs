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
        readonly AiBrain brain;
        readonly MoveSystem move;
        readonly CombatSystem combat;

        public AiSlotDriver(int unitId, UnitClass cls, MoveSystem move, CombatSystem combat, Predictor predictor = null)
        {
            this.unitId = unitId;
            this.move = move;
            this.combat = combat;
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
                    combat.TryAttack(unitId, ToCoord(cmd.Target));
                    break;
                case CommandType.Heavy:
                    combat.TrySkill(unitId, ToCoord(cmd.Target));
                    break;
                case CommandType.Guard:
                    combat.TryGuard(unitId);
                    break;
                case CommandType.Decoy:
                    break; // 디코이 시스템 전 — 무시
            }
        }

        static Coord ToCoord(SeoYuGi.Prediction.Cell c) => new Coord(c.X, c.Y);
    }
}
