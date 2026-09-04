namespace SeoYuGi.Battle
{
    /// <summary>
    /// 클래스 5종 (세부기획 C). SeoYuGi.Ai.ClassId와 같은 순서 — 캐스팅 호환 유지할 것.
    /// </summary>
    public enum UnitClass
    {
        Tank,      // 🦝 너구리 — 강타
        Balance,   // 🐈 치즈태비 — 돌파
        Assassin,  // 🐈‍⬛ 검은 고양이 — 그림자 도약
        Grenadier, // 🐦 비둘기 — 파열탄
        Sniper     // 🐦‍⬛ 까치 — 조준 사격
    }

    public enum SkillKind
    {
        Smash, // 강타: 인접 1칸 예고 → 피해 + 2칸 밀침(벽 충돌 +1)
        Dash,  // 돌파: 직선 2칸 즉시 대시, 경로 피해 + 1칸 밀침
        Blink, // 그림자 도약: 2칸 내 점멸 + 일반공격 버프
        Burst, // 파열탄: 3칸 내 지정 예고 → 십자 5칸
        Snipe  // 조준 사격: 직선 1열 예고 → 관통
    }

    public class SkillDef
    {
        public SkillKind kind;
        public float telegraphSeconds; // 0 = 즉시 발동
        public int damage;
        public int range;
    }

    /// <summary>클래스 정적 스탯. 스킬 실행은 CombatSystem.</summary>
    public class ClassDef
    {
        public UnitClass id;
        public int maxHp;
        public int sightRange;
        public MoveProfile move;
        public SkillDef skill;
    }

    /// <summary>
    /// 클래스 데이터 테이블 (세부기획 C 수치: HP 6/4/3/3/2, 시야 3/4/4/4/5).
    /// 이동 프로필은 역할 해석: 탱커·스나이퍼 둔중, 어쌔신 기동, 나머지 표준.
    /// </summary>
    public static class ClassCatalog
    {
        static readonly ClassDef[] defs =
        {
            new ClassDef
            {
                id = UnitClass.Tank, maxHp = 6, sightRange = 3,
                move = new MoveProfile { freeRange = 1, maxRange = 3, gaugeRegenPerSecond = 0.4f, regenDelaySeconds = 1.2f, yellowCooldownSeconds = 3.5f },
                skill = new SkillDef { kind = SkillKind.Smash, telegraphSeconds = 0.6f, damage = 1, range = 1 }
            },
            new ClassDef
            {
                id = UnitClass.Balance, maxHp = 4, sightRange = 4,
                move = new MoveProfile { freeRange = 2, maxRange = 4, gaugeRegenPerSecond = 0.5f, regenDelaySeconds = 1f, yellowCooldownSeconds = 3f },
                skill = new SkillDef { kind = SkillKind.Dash, telegraphSeconds = 0f, damage = 1, range = 2 }
            },
            new ClassDef
            {
                id = UnitClass.Assassin, maxHp = 3, sightRange = 4,
                move = new MoveProfile { freeRange = 3, maxRange = 5, gaugeRegenPerSecond = 0.7f, regenDelaySeconds = 0.8f, yellowCooldownSeconds = 2.5f },
                skill = new SkillDef { kind = SkillKind.Blink, telegraphSeconds = 0f, damage = 0, range = 2 }
            },
            new ClassDef
            {
                id = UnitClass.Grenadier, maxHp = 3, sightRange = 4,
                move = new MoveProfile { freeRange = 2, maxRange = 4, gaugeRegenPerSecond = 0.5f, regenDelaySeconds = 1f, yellowCooldownSeconds = 3f },
                skill = new SkillDef { kind = SkillKind.Burst, telegraphSeconds = 0.8f, damage = 1, range = 3 }
            },
            new ClassDef
            {
                id = UnitClass.Sniper, maxHp = 2, sightRange = 5,
                move = new MoveProfile { freeRange = 1, maxRange = 3, gaugeRegenPerSecond = 0.4f, regenDelaySeconds = 1.2f, yellowCooldownSeconds = 4f },
                skill = new SkillDef { kind = SkillKind.Snipe, telegraphSeconds = 0.8f, damage = 2, range = 0 } // range 0 = 열 전체
            }
        };

        public static ClassDef Get(UnitClass c) => defs[(int)c];
    }
}
