namespace SeoYuGi.Battle
{
    /// <summary>유닛 런타임 상태. 클래스 스탯은 생성 시 ClassCatalog에서 초기화.</summary>
    public class UnitState
    {
        public readonly int id;
        public readonly int team;
        public readonly UnitClass unitClass;
        public readonly int maxHp;
        public readonly int sightRange;

        public Coord pos;
        public int hp;
        public bool alive = true;

        // 이동 특성. 기본은 클래스 프로필 — 테스트/특수 유닛만 교체.
        public MoveProfile profile;
        // 이동 게이지 (0..profile.freeRange). 파랑 이동으로 소모, 초당 회복.
        public float moveGauge;
        // 노랑 이동 후 남은 쿨타임(초). > 0이면 이동 불가.
        public float moveCooldown;
        // 이동 직후 게이지 회복 정지 타이머(초).
        public float regenDelay;

        // AP (0..CombatConfig.apMax). CombatSystem이 초기화·회복.
        public float ap;
        // 그림자 도약 버프 종료 시각. 지속 중 일반공격 +1.
        public float attackBuffUntil = -1f;
        // 스턴 종료 시각 (비명 교란). >= 현재 시각이면 이동·행동 불가.
        public float stunnedUntil = -1f;
        // 비행 종료 시각 (폭탄 배달). >= 현재 시각이면 무적 + 이동·행동 불가.
        public float flyingUntil = -1f;
        // 스킬별 재사용 가능 시각 [스킬1, 스킬2].
        public readonly float[] skillReadyAt = { -1f, -1f };

        public UnitState(int id, int team, Coord pos, UnitClass unitClass = UnitClass.Balance)
        {
            this.id = id;
            this.team = team;
            this.pos = pos;
            this.unitClass = unitClass;

            var def = ClassCatalog.Get(unitClass);
            maxHp = def.maxHp;
            hp = def.maxHp;
            sightRange = def.sightRange;
            profile = def.move;
        }
    }
}
