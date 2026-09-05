namespace SeoYuGi.Ai
{
    /// AI 난이도 — 매치 시작 시 러너가 설정. ForClass가 이 값으로 수치를 스케일.
    public enum AiDifficulty { Easy, Normal, Hard }

    /// AiBrain 튜닝 수치. 코어에서 ScriptableObject로 감싸 노출 권장.
    public class AiConfig
    {
        public float MinDecisionInterval = 0.2f;  // 초당 최대 5회 판단 — 인간은 연타하는데 AI가 느릿했다 (2026-09-05 하향)
        public float DodgeWindow = 0.6f;          // 예고 판정까지 이 시간 안이면 회피 시도
        public float MinDodgeLead = 0.15f;        // 판정까지 이보다 짧게 남으면 반응 불가 (인간적 한계)
        public float DodgeChance = 0.6f;          // 회피 성공률 — 스트라이크별 결정적 주사위
        public float DodgeCooldown = 3f;          // 회피 성공 후 이 시간 동안은 못 피한다 — 사람도 옆걸음을 무한히 못 한다 (근접 허공질 방지)
        public float IdleWanderAfter = 1.2f;      // 이 시간 이상 무행동이면 배회 — 프리징 방지
        public float MoveInterval = 0.3f;         // 이동 한 걸음 뒤 최소 대기 (회피 제외) — 제자리 왔다갔다 방지 (0.45는 굼떴다)
        public float AttackInterval = 0.6f;       // 공격·스킬 간 최소 간격 — 코어 쿨(2s)이 실제 스로틀, 이건 "시도" 간격
        public float HumanTargetBonus = 3f;       // 타겟 선정 시 인간 슬롯 가중(거리 환산)
        public int SnipeRange = 7;   // 코어 조준사격 range 6 + 십자 끝 여유
        public float AggressionDelay = 0f;        // 판단 후 실행 지연 — 난이도 낮출 때 증가

        public int GrenadeRange = 4; // 코어 파열탄·폭탄 배달 +1에 맞춤

        // 힐팩 추구 성향 (클래스별 차등) — "전술적으로 안 먹기"를 두 문턱으로 표현
        public int HealSeekMissingHp = 2;         // 잃은 HP가 이 이상일 때만 힐팩을 노린다 (0=비활성)
        public int HealSeekRadius = 6;            // 이 칸 이내의 힐팩만 — 너무 멀면 거점 플레이 우선

        /// 매치 난이도 — 러너가 매치 시작 시 설정. ForClass가 이 값으로 수치를 스케일.
        public static AiDifficulty Difficulty = AiDifficulty.Normal;

        public static AiConfig ForClass(ClassId cls)
        {
            var cfg = ForClassBase(cls);
            ApplyDifficulty(cfg);
            return cfg;
        }

        /// 난이도 적용 — 하: 굼뜨고 잘 못 피함 / 상: 빠르고 회피·예측 정확.
        static void ApplyDifficulty(AiConfig c)
        {
            switch (Difficulty)
            {
                case AiDifficulty.Easy:
                    c.DodgeChance *= 0.45f;
                    c.AttackInterval *= 1.7f;      // 방아쇠 굼뜸
                    c.MinDecisionInterval *= 1.5f; // 판단 느림
                    c.AggressionDelay += 0.4f;     // 반응 지연
                    c.HealSeekRadius = System.Math.Max(2, c.HealSeekRadius - 2);
                    break;
                case AiDifficulty.Hard:
                    c.DodgeChance = System.Math.Min(0.95f, c.DodgeChance * 1.4f);
                    c.DodgeCooldown *= 0.6f;       // 자주 피함
                    c.AttackInterval *= 0.65f;     // 빠른 연사
                    c.MinDecisionInterval *= 0.7f; // 기민한 판단
                    c.HumanTargetBonus += 1.5f;    // 인간 집중 저격
                    break;
                    // Normal = 기본값 유지
            }
        }

        static AiConfig ForClassBase(ClassId cls)
        {
            switch (cls)
            {
                // 회피 재조정 (2026-09-05): 확률 하향 + 쿨타임 — 근접 1:1에서 영원히 허공 치는 문제.
                // 첫 공격은 피할 수 있어도 연속 공격은 맞는다. R1은 여기에 ×0.6 더 (EffectiveDodgeChance).
                case ClassId.Tank:      // 둔중 — 잘 못 피하는 대신 몸으로 받는다. HP 6, 힐팩 잘 안 챙김
                    return new AiConfig { MinDecisionInterval = 0.25f, DodgeChance = 0.25f, DodgeCooldown = 4f, AttackInterval = 0.7f,
                        HealSeekMissingHp = 3, HealSeekRadius = 4 };
                case ClassId.Balance:   // 표준
                    return new AiConfig { DodgeChance = 0.45f, DodgeCooldown = 3f, AttackInterval = 0.6f,
                        HealSeekMissingHp = 2, HealSeekRadius = 6 };
                case ClassId.Assassin:  // 기민 — 회피 특기지만 무한은 아님. HP 3 유리몸, 힐팩 적극
                    return new AiConfig { DodgeChance = 0.6f, DodgeCooldown = 2f, AttackInterval = 0.45f,
                        HealSeekMissingHp = 1, HealSeekRadius = 8 };
                case ClassId.Grenadier: // 후방 표준
                    return new AiConfig { DodgeChance = 0.45f, DodgeCooldown = 3.5f, AttackInterval = 0.8f,
                        HealSeekMissingHp = 2, HealSeekRadius = 6 };
                case ClassId.Sniper:    // 조준하는 무게 — 제일 느긋한 방아쇠. HP 2 최유리몸, 회복에 민감
                    return new AiConfig { MinDecisionInterval = 0.3f, DodgeChance = 0.5f, DodgeCooldown = 3f, AttackInterval = 1.0f,
                        HealSeekMissingHp = 1, HealSeekRadius = 8 };
                default:
                    return new AiConfig();
            }
        }
    }
}
