using System.Collections.Generic;
using SeoYuGi.Prediction;

namespace SeoYuGi.Integration
{
    /// <summary>
    /// 해킹 — 예측 카운터 장비 (기획서의 구 '디코이', 컨셉 변경 2026-09-05).
    /// 1기당 매치에 1회. 발동하면 5초간 적 예측 AI의 학습이 오염돼
    /// 우리 팀 행동을 기록하지 못하고 기존 예측의 확신도가 급락한다.
    /// 충전은 매치 단위 — Predictor처럼 라운드를 넘겨 살아남도록 매치당 1개 생성.
    /// </summary>
    public class HackSystem
    {
        public const float Duration = 5f;

        readonly Predictor predictor;
        readonly HashSet<int> used = new HashSet<int>();

        /// <summary>발동 성공 시 unitId 통지 — HUD·연출용.</summary>
        public event System.Action<int> OnHacked;

        public HackSystem(Predictor predictor)
        {
            this.predictor = predictor;
        }

        public bool Has(int unitId) => !used.Contains(unitId);

        public bool TryHack(int unitId, TeamId team)
        {
            if (!Has(unitId)) return false;
            used.Add(unitId);
            predictor.InjectDecoy(team, Duration);
            OnHacked?.Invoke(unitId);
            return true;
        }
    }
}
