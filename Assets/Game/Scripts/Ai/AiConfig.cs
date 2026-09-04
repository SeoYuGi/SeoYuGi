namespace SeoYuGi.Ai
{
    /// AiBrain 튜닝 수치. 코어에서 ScriptableObject로 감싸 노출 권장.
    public class AiConfig
    {
        // AP 비용 — 코어 규칙과 반드시 일치시킬 것 (기획서 §03)
        public float CostMove = 1f;
        public float CostAttack = 2f;
        public float CostHeavy = 3f;
        public float CostGuard = 1f;
        public float CostDecoy = 3f;

        public float MinDecisionInterval = 0.25f; // 초당 최대 4회 판단 — 인간다운 템포
        public float DodgeWindow = 0.6f;          // 예고 판정까지 이 시간 안이면 회피 시도
        public float ReserveAp = 0f;              // 이만큼은 항상 남겨둠 (역할별 프리셋)
        public float HumanTargetBonus = 3f;       // 타겟 선정 시 인간 슬롯 가중(거리 환산)
        public int SnipeRange = 6;
        public float DecoyCooldown = 12f;
        public float AggressionDelay = 0f;        // 판단 후 실행 지연 — 난이도 낮출 때 증가

        public static AiConfig ForClass(ClassId cls)
        {
            switch (cls)
            {
                case ClassId.Runner:
                    return new AiConfig { ReserveAp = 0f };
                case ClassId.Sniper:
                    return new AiConfig { ReserveAp = 3f, MinDecisionInterval = 0.35f };
                case ClassId.Jammer:
                    return new AiConfig { ReserveAp = 1f };
                default:
                    return new AiConfig();
            }
        }
    }
}
