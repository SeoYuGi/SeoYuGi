using System;

namespace SeoYuGi.Battle
{
    /// <summary>설치형 공격 파라미터 (기획서 §03). AP는 삭제됨(2026-09-05) — 평타는 쿨다운, 적중 보상은 해킹 게이지 충전.</summary>
    [Serializable]
    public class CombatConfig
    {
        public int attackDamage = 1;
        public float attackTelegraphSeconds = 0.5f; // 모든 공격은 예고 후 판정 — 즉발 없음
        public float attackCooldownSeconds = 2f;    // 일반공격 쿨다운 — 구 AP 경제의 스로틀 대체

        public float blinkBuffSeconds = 3f;         // 그림자 도약: 일반공격 +1 지속
        public int blinkBuffBonus = 1;

        public int fallDamage = 1;                  // 고지대에서 밀려 떨어질 때 (벽꿍과 같은 수치 감각)
    }
}
