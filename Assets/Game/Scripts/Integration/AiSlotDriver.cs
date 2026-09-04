using SeoYuGi.Ai;
using SeoYuGi.Battle;
using SeoYuGi.Prediction;

namespace SeoYuGi.Integration
{
    /// <summary>
    /// AI 슬롯 1개 = AiBrain 1개 + 인텐트 제출. 적팀 3기와 아군 백필이 전부 이걸 쓴다.
    /// AiCommand → BattleIntent 변환만 하고 실행은 싱크가 담당.
    /// 실패(게이지 잠금 등)는 무시 — 뇌가 다음 틱에 재판단.
    /// </summary>
    public class AiSlotDriver
    {
        readonly int unitId;
        readonly AiBrain brain;
        readonly IIntentSink sink;

        /// <summary>예측 사격 성공 제출 (unitId, 목표 칸) — 적중/실패 연출 배선용.</summary>
        public event System.Action<int, SeoYuGi.Prediction.Cell> OnPredictedShot;

        public AiSlotDriver(int unitId, UnitClass cls, IIntentSink sink, Predictor predictor = null)
        {
            this.unitId = unitId;
            this.sink = sink;
            brain = new AiBrain(unitId, AiConfig.ForClass((ClassId)(int)cls), predictor);
        }

        public void Tick(IWorldView world)
        {
            var cmd = brain.Tick(world);
            switch (cmd.Type)
            {
                case CommandType.Move:
                    sink.Submit(BattleIntent.Move(unitId, ToCoord(cmd.Target)));
                    break;
                case CommandType.Attack:
                    if (sink.Submit(BattleIntent.Attack(unitId, ToCoord(cmd.Target))).accepted && cmd.Predicted)
                        OnPredictedShot?.Invoke(unitId, cmd.Target);
                    break;
                case CommandType.Heavy:
                    if (sink.Submit(BattleIntent.Skill(unitId, ToCoord(cmd.Target))).accepted && cmd.Predicted)
                        OnPredictedShot?.Invoke(unitId, cmd.Target);
                    break;
                case CommandType.Guard:
                    sink.Submit(BattleIntent.Guard(unitId));
                    break;
                case CommandType.Decoy:
                    sink.Submit(BattleIntent.Hack(unitId)); // 해킹 — 5초간 적 예측 교란
                    break;
            }
        }

        static Coord ToCoord(SeoYuGi.Prediction.Cell c) => new Coord(c.X, c.Y);
    }
}
