using System;
using System.Collections.Generic;
using SeoYuGi.Prediction;

namespace SeoYuGi.Integration
{
    /// <summary>
    /// 해킹 — 예측 카운터 필살기 (기획서의 구 '디코이', 컨셉 변경 2026-09-05).
    /// 궁게이지제(2026-09-05, 구 '매치 1회' 대체): 기본 초당 1% + 적중 데미지 1당 10% 충전.
    /// 만충 시 발동 가능 — 발동하면 Duration초간 적 예측 AI 학습 오염 + 적 전원 위치 표시(시야 강탈).
    /// 게이지는 매치 단위로 유지(라운드 넘김) — 발동 시 0으로 소모. 시야 강탈 시각은 라운드 시계(BattleState.time).
    /// </summary>
    public class HackSystem
    {
        public const float Duration = 5f;
        public const float StunSeconds = 0.6f;             // 발동 순간 적 전원 정지 — 0.5는 예고 0.7초 세계에서 체감이 약해 0.6
        public const float PassiveChargePerSecond = 0.007f; // 기본 충전 — 초당 0.7% (스턴 추가로 코스트 상향, 구 1%)
        public const float ChargePerDamage = 0.07f;         // 적중 = 예측 성공 → 데미지 1당 7% (구 10%)

        readonly Predictor predictor;
        readonly List<int> unitIds = new List<int>();               // Tick 순회용 — gauge 키 스냅샷
        readonly Dictionary<int, float> gauge = new Dictionary<int, float>();       // 0..1
        readonly Dictionary<int, float> revealUntil = new Dictionary<int, float>(); // team → 시야 강탈 종료 시각

        /// <summary>발동 성공 시 unitId 통지 — HUD·연출용.</summary>
        public event System.Action<int> OnHacked;

        public HackSystem(Predictor predictor)
        {
            this.predictor = predictor;
        }

        /// <summary>라운드 조립 시 호출 — 게이지 슬롯 확보(기존 충전 유지) + 라운드 시계 리셋.</summary>
        public void BeginRound(IEnumerable<int> ids)
        {
            foreach (var id in ids)
                if (!gauge.ContainsKey(id))
                {
                    gauge[id] = 0f;
                    unitIds.Add(id);
                }
            revealUntil.Clear(); // BattleState.time이 0부터 다시 — 이전 라운드 잔여 시야 제거
        }

        /// <summary>매 프레임(호스트/싱글) — 전 유닛 기본 충전.</summary>
        public void Tick(float deltaTime)
        {
            for (int i = 0; i < unitIds.Count; i++)
            {
                int id = unitIds[i];
                gauge[id] = Math.Min(1f, gauge[id] + PassiveChargePerSecond * deltaTime);
            }
        }

        /// <summary>CombatSystem.OnDamageDealt 구독용 — 적중 데미지 비례 가속 충전.</summary>
        public void NotifyDamage(int attackerId, int damage)
        {
            if (gauge.TryGetValue(attackerId, out var g))
                gauge[attackerId] = Math.Min(1f, g + damage * ChargePerDamage);
        }

        public float Charge(int unitId) => gauge.TryGetValue(unitId, out var g) ? g : 0f;

        public bool IsReady(int unitId) => Charge(unitId) >= 1f;

        /// <summary>클라 미러 전용 — 호스트 스냅샷의 게이지 값을 덮어쓴다.</summary>
        public void SetCharge(int unitId, float value)
        {
            if (!gauge.ContainsKey(unitId)) unitIds.Add(unitId);
            gauge[unitId] = value;
        }

        /// <summary>클라 미러 전용 — 호스트 해킹 릴레이 수신 시 시야 강탈 창만 복제.</summary>
        public void MarkReveal(int team, float now) => revealUntil[team] = now + Duration;

        /// <summary>now = BattleState.time. 해당 팀이 해킹 시야 강탈 중인가.</summary>
        public bool RevealActive(int team, float now) =>
            revealUntil.TryGetValue(team, out var until) && now < until;

        public bool TryHack(int unitId, TeamId team, float now)
        {
            if (!IsReady(unitId)) return false;
            gauge[unitId] = 0f;
            revealUntil[(int)team] = now + Duration;
            predictor.InjectDecoy(team, Duration);
            OnHacked?.Invoke(unitId);
            return true;
        }
    }
}
