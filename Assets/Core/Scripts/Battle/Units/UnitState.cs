namespace SeoYuGi.Battle
{
    /// <summary>유닛 런타임 상태. 스킬/스탯은 본편 범위 — 여기선 위치·생존·이동 게이지만.</summary>
    public class UnitState
    {
        public readonly int id;
        public readonly int team;
        public Coord pos;
        public bool alive = true;

        // 캐릭터별 이동 특성. null이면 MoveSystem이 MoveConfig.defaultProfile을 꽂는다.
        public MoveProfile profile;
        // 이동 게이지 (0..profile.freeRange). 파랑 이동으로 소모, 초당 회복.
        public float moveGauge;
        // 노랑 이동 후 남은 쿨타임(초). > 0이면 이동 불가.
        public float moveCooldown;
        // 이동 직후 게이지 회복 정지 타이머(초).
        public float regenDelay;

        public UnitState(int id, int team, Coord pos)
        {
            this.id = id;
            this.team = team;
            this.pos = pos;
        }
    }
}
