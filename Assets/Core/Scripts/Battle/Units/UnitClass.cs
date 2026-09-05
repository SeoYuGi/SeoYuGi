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
        Smash,      // 던져버리기: 예고 → 피해2 + 5칸 던짐(벽꿍 +1). 열거형 이름은 구 '강타' 시절 유산
        Dash,       // 돌파: 직선·대각 즉발 대시, 경로 적 피해+밀침+0.8s 스턴, 유닛 통과(벽 불가)
        Scream,     // 비명 교란: 인접8 적 전원, 짧은 예고 후 스턴
        Blink,      // 그림자 도약: 5×5 내 점멸 + 일반공격 버프, 적 시야 밖이면 고스트 없음
        Claw,       // 발톱 쥐어짜기: 인접8 예고 → 피해3
        Burst,      // 파열탄: 5×5 내 지정 예고 → 십자 5칸 피해1 (자폭 없음)
        BombDeliver,// 폭탄 배달: 맨해튼4 내 지정, 1초 비행(무적) 후 착지 + 십자 5칸 피해2
        Snatch,     // 낚아채기: 날아가 적을 발에 걸고 돌아와 시전자 앞칸에 내려놓는다. 앞칸이 막혔으면 피해만
        KnockShot,  // 넉백샷: 인접8 즉발 피해1 + 본인 반대로 2칸 후퇴(벽 막힘·낙하 자기 부담)
        Snipe,       // 조준 사격: 직선 4방 최대 5칸 관통 예고 → 피해3, 벽 차단(고지 사수는 관통)
        BasicAttack  // 평타 — 스킬 슬롯엔 없다. 예고 아이콘 표시용 꼬리표 (뒤에 붙여 기존 값 불변)
    }

    /// <summary>기본공격 사거리 모양 (범위 다이어그램 원본).</summary>
    public enum AttackShape
    {
        Melee8,  // 5×5(반경2) — 너구리·고라니·검은냥. 2026-09-05 사거리 +1 (이름은 구 3×3 시절 유산)
        Circle2, // 반경 3 원형(모서리 제외) — 비둘기. 2026-09-05 사거리 +1
        Square2  // 7×7(반경3, 48칸) — 까치. 2026-09-05 사거리 +1
    }

    public class SkillDef
    {
        public SkillKind kind;
        public float telegraphSeconds; // 0 = 즉시 발동
        public int damage;
        public int range;
        public float cooldownSeconds;
        public float stunSeconds;      // Scream·Dash — 판정/충돌 시 스턴 부여
        public float slowSeconds;      // Burst — 판정 시 둔화(이동 범위·게이지 절반). 비둘기 서포터 컨셉 (2026-09-06)
    }

    /// <summary>클래스 정적 스탯. 스킬 실행은 CombatSystem.</summary>
    public class ClassDef
    {
        public UnitClass id;
        public int maxHp;
        public int sightRange;
        public AttackShape attackShape;

        /// <summary>클래스별 평타 피해. 0이면 CombatConfig.attackDamage(공용 기본값)를 쓴다.</summary>
        public int basicAttackDamage;

        public MoveProfile move;
        public SkillDef[] skills; // [0] = 스킬1(짧은 쿨), [1] = 스킬2(긴 쿨·고위력)
    }

    /// <summary>
    /// 전장 이름표·킬로그용 짧은 호칭. 동물팀(0)은 종(種), 기계팀(1)은 역할.
    /// 카드 UI의 긴 이름(ClassCard.Meta)과 별개 — 머리 위 이름표는 두세 글자여야 읽힌다.
    /// </summary>
    public static class ClassNames
    {
        static readonly string[] Animal = { "너구리", "고라니", "검은냥", "비둘기", "까치" };
        static readonly string[] Machine = { "방패", "돌격", "은신", "포격", "저격" };

        public static string For(int team, UnitClass cls) =>
            (team == 1 ? Machine : Animal)[(int)cls];

        // 봇 콜사인 (2026-09-05 유저 지정). 동물팀 = 별명, 기계팀 = 별명 + "봇" (라니봇/너굴봇).
        // 양 팀 같은 별명은 킬피드·결과창·무전에서 "라니: 라니 처치"가 됐고, 기계 이름(방패/돌격)은 직관적이지 않았다 (2026-09-06).
        // 같은 팀 안 중복은 DisambiguateCallsigns가 ①② 붙임.
        static readonly string[] Nicks = { "너굴", "라니", "깜냥", "둘기", "까돌" };

        public static string Nick(int team, UnitClass cls) => team == 1 ? Nicks[(int)cls] + "봇" : Nicks[(int)cls];
    }

    /// <summary>
    /// 클래스 데이터 테이블 (캐릭터 기획 v1.7: HP 15/10/8/8/5 — 원킬 콤보 방지 ×2.5 상향).
    /// 이동 프로필은 역할 해석: 탱커·스나이퍼 둔중, 어쌔신 기동, 나머지 표준.
    /// 2026-09-05: 이동 템포 상향 — 노랑 쿨 ~40% 감소, 게이지 회복 ~35% 증가 (전반적으로 답답하다는 피드백).
    /// </summary>
    public static class ClassCatalog
    {
        static readonly ClassDef[] defs =
        {
            new ClassDef
            {
                id = UnitClass.Tank, maxHp = 15, sightRange = 3, attackShape = AttackShape.Melee8,
                move = new MoveProfile { freeRange = 1, maxRange = 3, gaugeRegenPerSecond = 0.60f, regenDelaySeconds = 0f, yellowCooldownSeconds = 1.5f },
                skills = new[]
                {
                    new SkillDef { kind = SkillKind.ShieldPush, telegraphSeconds = 2f, damage = 1, range = 2, cooldownSeconds = 6f },
                    new SkillDef { kind = SkillKind.Smash, telegraphSeconds = 2f, damage = 2, range = 2, cooldownSeconds = 8f } // 던져버리기 — 밀침 5칸은 PushSpecOf
                }
            },
            new ClassDef
            {
                id = UnitClass.Balance, maxHp = 10, sightRange = 4, attackShape = AttackShape.Melee8,
                move = new MoveProfile { freeRange = 2, maxRange = 4, gaugeRegenPerSecond = 0.80f, regenDelaySeconds = 0f, yellowCooldownSeconds = 1.3f },
                skills = new[]
                {
                    new SkillDef { kind = SkillKind.Dash, telegraphSeconds = 0f, damage = 1, range = 3, cooldownSeconds = 6f, stunSeconds = 0.8f }, // 들이받힌 적 0.8초 스턴 (2026-09-05)
                    new SkillDef { kind = SkillKind.Scream, telegraphSeconds = 1.6f, damage = 0, range = 2, cooldownSeconds = 8f, stunSeconds = 1f }
                }
            },
            new ClassDef
            {
                id = UnitClass.Assassin, maxHp = 8, sightRange = 4, attackShape = AttackShape.Melee8,
                move = new MoveProfile { freeRange = 3, maxRange = 5, gaugeRegenPerSecond = 1.10f, regenDelaySeconds = 0f, yellowCooldownSeconds = 1.1f },
                skills = new[]
                {
                    new SkillDef { kind = SkillKind.Blink, telegraphSeconds = 0f, damage = 0, range = 3, cooldownSeconds = 6f },
                    new SkillDef { kind = SkillKind.Claw, telegraphSeconds = 2.4f, damage = 3, range = 2, cooldownSeconds = 10f }
                }
            },
            new ClassDef
            {
                id = UnitClass.Grenadier, maxHp = 8, sightRange = 4, attackShape = AttackShape.Circle2,
                move = new MoveProfile { freeRange = 2, maxRange = 4, gaugeRegenPerSecond = 0.80f, regenDelaySeconds = 0f, yellowCooldownSeconds = 1.3f },
                skills = new[]
                {
                    new SkillDef { kind = SkillKind.Burst, telegraphSeconds = 2.4f, damage = 1, range = 3, cooldownSeconds = 6f, slowSeconds = 3f }, // 맞은 적 3초 둔화 — 낚아채기와 묶어 "자리 통제" 서포터
                    new SkillDef { kind = SkillKind.Snatch, telegraphSeconds = 1.0f, damage = 1, range = 5, cooldownSeconds = 10f } // 위치 강제 이동이 강력해 피해는 낮게, 비행은 빠르게
                }
            },
            new ClassDef
            {
                id = UnitClass.Sniper, maxHp = 5, sightRange = 5, attackShape = AttackShape.Square2,
                basicAttackDamage = 2, // HP 5로 제일 무르고 접근당하면 죽는다 — 사거리로 버는 만큼 한 방이 무거워야 (2026-09-05)
                move = new MoveProfile { freeRange = 1, maxRange = 3, gaugeRegenPerSecond = 0.60f, regenDelaySeconds = 0f, yellowCooldownSeconds = 1.4f },
                skills = new[]
                {
                    new SkillDef { kind = SkillKind.KnockShot, telegraphSeconds = 0f, damage = 1, range = 2, cooldownSeconds = 7f },
                    new SkillDef { kind = SkillKind.Snipe, telegraphSeconds = 2.4f, damage = 4, range = 6, cooldownSeconds = 10f }
                }
            }
        };

        public static ClassDef Get(UnitClass c) => defs[(int)c];
    }
}
