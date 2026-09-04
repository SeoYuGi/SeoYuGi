namespace SeoYuGi.Ai
{
    /// AiBrain 튜닝 수치. 코어에서 ScriptableObject로 감싸 노출 권장.
    public class AiConfig
    {
        // AP 비용 — 코어 규칙과 반드시 일치시킬 것 (기획서 §03)
        public float CostMove = 1f;
        public float CostAttack = 2f;
        public float CostHeavy = 3f;
        public float CostGuard = 1f; // 디코이는 장비(AP 무관)라 비용 없음

        public float MinDecisionInterval = 0.25f; // 초당 최대 4회 판단 — 인간다운 템포
        public float DodgeWindow = 0.6f;          // 예고 판정까지 이 시간 안이면 회피 시도
        public float MinDodgeLead = 0.15f;        // 판정까지 이보다 짧게 남으면 반응 불가 (인간적 한계)
        public float DodgeChance = 0.6f;          // 회피 성공률 — 스트라이크별 결정적 주사위
        public float IdleWanderAfter = 1.2f;      // 이 시간 이상 무행동이면 배회 — 프리징 방지
        public float AttackInterval = 1.0f;       // 공격·스킬 간 최소 간격 — AP 연타 방지 (인간적 템포)
        public float ReserveAp = 0f;              // 이만큼은 항상 남겨둠 (역할별 프리셋)
        public float HumanTargetBonus = 3f;       // 타겟 선정 시 인간 슬롯 가중(거리 환산)
        public int SnipeRange = 6;
        public float AggressionDelay = 0f;        // 판단 후 실행 지연 — 난이도 낮출 때 증가

        public int GrenadeRange = 3;

        public static AiConfig ForClass(ClassId cls)
        {
            switch (cls)
            {
                case ClassId.Tank:      // 둔중 — 잘 못 피하는 대신 몸으로 받는다
                    return new AiConfig { ReserveAp = 0f, MinDecisionInterval = 0.3f, DodgeChance = 0.35f, AttackInterval = 1.1f };
                case ClassId.Balance:
                    return new AiConfig { ReserveAp = 0f, DodgeChance = 0.6f, AttackInterval = 1.0f };
                case ClassId.Assassin:  // 기민 — 회피 특기, 공격도 빠른 편
                    return new AiConfig { ReserveAp = 3f, DodgeChance = 0.85f, AttackInterval = 0.8f };
                case ClassId.Grenadier:
                    return new AiConfig { ReserveAp = 1f, DodgeChance = 0.55f, AttackInterval = 1.3f };
                case ClassId.Sniper:    // 조준하는 무게 — 제일 느긋한 방아쇠
                    return new AiConfig { ReserveAp = 3f, MinDecisionInterval = 0.35f, DodgeChance = 0.65f, AttackInterval = 1.5f };
                default:
                    return new AiConfig();
            }
        }
    }
}
