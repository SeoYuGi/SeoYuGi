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

        /// <summary>이 봇의 팀 — 핑 지휘 배달 필터용.</summary>
        public int Team { get; }
        public int UnitId => unitId;

        /// <summary>아군 인간의 핑 — 뇌에 전달 (▼ 집결 / ! 집중).</summary>
        public void CommandPing(SeoYuGi.Prediction.Cell cell, int type, float now) => brain.CommandPing(cell, type, now);

        /// <param name="orders">지휘관 모드에서 플레이어가 내린 상시 명령. null이면 완전 자율.</param>
        /// <param name="scaleDifficulty">난이도 적용 여부 — 적팀 봇만 true. 아군 봇은 항상 보통 (2026-09-06).</param>
        public AiSlotDriver(int unitId, UnitClass cls, int team, IIntentSink sink, Predictor predictor = null,
            CommandState orders = null, bool scaleDifficulty = true)
        {
            this.unitId = unitId;
            Team = team;
            this.sink = sink;
            brain = new AiBrain(unitId, AiConfig.ForClass((ClassId)(int)cls, scaleDifficulty), predictor, orders);
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
                    if (sink.Submit(BattleIntent.Skill(unitId, ToCoord(cmd.Target), cmd.SkillIndex)).accepted
                        && cmd.Predicted)
                        OnPredictedShot?.Invoke(unitId, cmd.Target);
                    break;
                case CommandType.Decoy:
                    sink.Submit(BattleIntent.Hack(unitId)); // 해킹 — 5초간 적 예측 교란
                    break;
            }
        }

        static Coord ToCoord(SeoYuGi.Prediction.Cell c) => new Coord(c.X, c.Y);
    }
}
