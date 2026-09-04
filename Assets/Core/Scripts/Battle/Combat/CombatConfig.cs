using System;

namespace SeoYuGi.Battle
{
    /// <summary>AP 경제 + 설치형 공격 파라미터 (기획서 §03). 이동은 AP 무관 — 게이지 시스템.</summary>
    [Serializable]
    public class CombatConfig
    {
        public float apMax = 5f;
        public float apRegenPerSecond = 1f;

        public float costAttack = 2f; // 스킬 AP 비용은 SkillDef.apCost (스킬1=2, 스킬2=3). 방어는 기획 삭제.

        public int attackDamage = 1;
        public float attackTelegraphSeconds = 0.5f; // 모든 공격은 예고 후 판정 — 즉발 없음
        public float hitRefund = 1f;                // 적중 = 예측 성공 → AP 환급

        public float blinkBuffSeconds = 3f;         // 그림자 도약: 일반공격 +1 지속
        public int blinkBuffBonus = 1;

        public int fallDamage = 1;                  // 고지대에서 밀려 떨어질 때 (벽꿍과 같은 수치 감각)
    }
}
