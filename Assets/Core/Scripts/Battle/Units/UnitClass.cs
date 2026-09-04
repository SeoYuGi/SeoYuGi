namespace SeoYuGi.Battle
{
    /// <summary>
    /// 클래스 5종 (캐릭터 기획 v1.7). SeoYuGi.Ai.ClassId와 같은 순서 — 캐스팅 호환 유지할 것.
    /// </summary>
    public enum UnitClass
    {
        Tank,      // 🦝 너구리 — 방패 밀어붙이기 / 강타
        Balance,   // 🦌 고라니 — 돌파 / 비명 교란
        Assassin,  // 🐈‍⬛ 검은 고양이 — 그림자 도약 / 발톱 쥐어짜기
        Grenadier, // 🐦 비둘기 — 파열탄 / 폭탄 배달
        Sniper     // 🐦‍⬛ 까치 — 넉백샷 / 조준 사격
    }

    public enum SkillKind
    {
        ShieldPush, // 방패 밀어붙이기: 인접8 예고 → 피해1 + 2칸 밀침(벽꿍 +1)
        Smash,      // 강타: 인접8 예고 → 피해2 + 1칸 밀침
        Dash,       // 돌파: 직선 2칸 즉발 대시, 경로 적 피해+밀침, 유닛 통과(벽 불가)
        Scream,     // 비명 교란: 인접8 적 전원, 짧은 예고 후 스턴
        Blink,      // 그림자 도약: 5×5 내 점멸 + 일반공격 버프, 적 시야 밖이면 고스트 없음
        Claw,       // 발톱 쥐어짜기: 인접8 예고 → 피해3
        Burst,      // 파열탄: 5×5 내 지정 예고 → 십자 5칸 피해1 (자폭 없음)
        BombDeliver,// 폭탄 배달: 맨해튼4 내 지정, 1초 비행(무적) 후 착지 + 십자 5칸 피해2
        KnockShot,  // 넉백샷: 인접8 즉발 피해1 + 본인 반대로 2칸 후퇴(벽 막힘·낙하 자기 부담)
        Snipe       // 조준 사격: 직선 4방 최대 5칸 관통 예고 → 피해3, 벽 차단(고지 사수는 관통)
    }

    /// <summary>기본공격 사거리 모양 (범위 다이어그램 원본).</summary>
    public enum AttackShape
    {
        Melee8,  // 3×3 인접 8방 — 너구리·고라니·검은냥
        Circle2, // 반경 2 원형(모서리 제외) — 비둘기
        Square2  // 5×5 링(24칸) — 까치
    }

    public class SkillDef
    {
        public SkillKind kind;
        public float telegraphSeconds; // 0 = 즉시 발동
        public int damage;
        public int range;
        public float apCost;
        public float cooldownSeconds;
        public float stunSeconds;      // Scream 전용
    }

    /// <summary>클래스 정적 스탯. 스킬 실행은 CombatSystem.</summary>
    public class ClassDef
    {
        public UnitClass id;
        public int maxHp;
        public int sightRange;
        public AttackShape attackShape;
        public MoveProfile move;
        public SkillDef[] skills; // [0] = 스킬1(AP2), [1] = 스킬2(AP3)
    }

    /// <summary>
    /// 클래스 데이터 테이블 (캐릭터 기획 v1.7: HP 15/10/8/8/5 — 원킬 콤보 방지 ×2.5 상향).
    /// 이동 프로필은 역할 해석: 탱커·스나이퍼 둔중, 어쌔신 기동, 나머지 표준.
    /// </summary>
    public static class ClassCatalog
    {
        static readonly ClassDef[] defs =
        {
            new ClassDef
            {
                id = UnitClass.Tank, maxHp = 15, sightRange = 3, attackShape = AttackShape.Melee8,
                move = new MoveProfile { freeRange = 1, maxRange = 3, gaugeRegenPerSecond = 0.4f, regenDelaySeconds = 1.2f, yellowCooldownSeconds = 3.5f },
                skills = new[]
                {
                    new SkillDef { kind = SkillKind.ShieldPush, telegraphSeconds = 0.6f, damage = 1, range = 1, apCost = 2f, cooldownSeconds = 2f },
                    new SkillDef { kind = SkillKind.Smash, telegraphSeconds = 0.6f, damage = 2, range = 1, apCost = 3f, cooldownSeconds = 3f }
                }
            },
            new ClassDef
            {
                id = UnitClass.Balance, maxHp = 10, sightRange = 4, attackShape = AttackShape.Melee8,
                move = new MoveProfile { freeRange = 2, maxRange = 4, gaugeRegenPerSecond = 0.5f, regenDelaySeconds = 1f, yellowCooldownSeconds = 3f },
                skills = new[]
                {
                    new SkillDef { kind = SkillKind.Dash, telegraphSeconds = 0f, damage = 1, range = 2, apCost = 2f, cooldownSeconds = 2f },
                    new SkillDef { kind = SkillKind.Scream, telegraphSeconds = 0.4f, damage = 0, range = 1, apCost = 3f, cooldownSeconds = 3f, stunSeconds = 1f }
                }
            },
            new ClassDef
            {
                id = UnitClass.Assassin, maxHp = 8, sightRange = 4, attackShape = AttackShape.Melee8,
                move = new MoveProfile { freeRange = 3, maxRange = 5, gaugeRegenPerSecond = 0.7f, regenDelaySeconds = 0.8f, yellowCooldownSeconds = 2.5f },
                skills = new[]
                {
                    new SkillDef { kind = SkillKind.Blink, telegraphSeconds = 0f, damage = 0, range = 2, apCost = 2f, cooldownSeconds = 2f },
                    new SkillDef { kind = SkillKind.Claw, telegraphSeconds = 0.8f, damage = 3, range = 1, apCost = 3f, cooldownSeconds = 4f }
                }
            },
            new ClassDef
            {
                id = UnitClass.Grenadier, maxHp = 8, sightRange = 4, attackShape = AttackShape.Circle2,
                move = new MoveProfile { freeRange = 2, maxRange = 4, gaugeRegenPerSecond = 0.5f, regenDelaySeconds = 1f, yellowCooldownSeconds = 3f },
                skills = new[]
                {
                    new SkillDef { kind = SkillKind.Burst, telegraphSeconds = 0.8f, damage = 1, range = 2, apCost = 2f, cooldownSeconds = 2f },
                    new SkillDef { kind = SkillKind.BombDeliver, telegraphSeconds = 1f, damage = 2, range = 4, apCost = 3f, cooldownSeconds = 4f }
                }
            },
            new ClassDef
            {
                id = UnitClass.Sniper, maxHp = 5, sightRange = 5, attackShape = AttackShape.Square2,
                move = new MoveProfile { freeRange = 1, maxRange = 3, gaugeRegenPerSecond = 0.4f, regenDelaySeconds = 1.2f, yellowCooldownSeconds = 4f },
                skills = new[]
                {
                    new SkillDef { kind = SkillKind.KnockShot, telegraphSeconds = 0f, damage = 1, range = 1, apCost = 2f, cooldownSeconds = 3f },
                    new SkillDef { kind = SkillKind.Snipe, telegraphSeconds = 0.8f, damage = 3, range = 5, apCost = 3f, cooldownSeconds = 4f }
                }
            }
        };

        public static ClassDef Get(UnitClass c) => defs[(int)c];
    }
}
