using System;
using System.Collections.Generic;

namespace SeoYuGi.Battle
{
    /// <summary>
    /// 팀 공유 시야 (세부기획 B): 팀원 시야의 합집합, 벽이 시선 차단, 고스트 마커.
    /// 시야 반경 = 클래스별 sightRange (체비쇼프 거리).
    /// 매 프레임 Tick()으로 갱신 — 81셀 × 6유닛이라 전량 재계산해도 싸다.
    /// </summary>
    public class VisionSystem
    {
        public BattleState State { get; }

        readonly HashSet<Coord>[] visible = { new HashSet<Coord>(), new HashSet<Coord>() };
        // 팀별: 적 유닛 id → 마지막 목격 위치 (고스트 마커)
        readonly Dictionary<int, Coord>[] lastSeen = { new Dictionary<int, Coord>(), new Dictionary<int, Coord>() };

        public VisionSystem(BattleState state)
        {
            State = state;
            Tick();
        }

        public void Tick()
        {
            for (int team = 0; team < 2; team++)
            {
                var set = visible[team];
                set.Clear();
                foreach (var unit in State.Units)
                {
                    if (!unit.alive || unit.team != team) continue;
                    AddUnitVision(unit, set);
                }
            }

            // 고스트: 적이 보이는 동안 마지막 목격 위치 갱신
            foreach (var unit in State.Units)
            {
                if (!unit.alive) continue;
                int enemyTeam = 1 - unit.team;
                if (visible[enemyTeam].Contains(unit.pos))
                    lastSeen[enemyTeam][unit.id] = unit.pos;
            }
        }

        void AddUnitVision(UnitState unit, HashSet<Coord> set)
        {
            int r = unit.sightRange;
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                var c = new Coord(unit.pos.x + dx, unit.pos.y + dy);
                if (!State.Grid.InBounds(c)) continue;
                if (set.Contains(c)) continue;
                if (HasLineOfSight(State.Grid, unit.pos, c))
                    set.Add(c);
            }
        }

        public bool IsVisibleTo(int team, Coord cell) => visible[team].Contains(cell);

        /// <summary>적 유닛의 마지막 목격 위치 (고스트 마커). 한 번도 못 봤으면 false.</summary>
        public bool TryGetLastSeen(int team, int enemyUnitId, out Coord pos) =>
            lastSeen[team].TryGetValue(enemyUnitId, out pos);

        /// <summary>격자 브레젠험 — 중간 칸에 벽이 있으면 차단. 끝점의 벽은 보인다(벽 자체는 목격 가능).</summary>
        public static bool HasLineOfSight(GridModel grid, Coord from, Coord to)
        {
            int x = from.x, y = from.y;
            int dx = Math.Abs(to.x - from.x), dy = Math.Abs(to.y - from.y);
            int sx = Math.Sign(to.x - from.x), sy = Math.Sign(to.y - from.y);
            int err = dx - dy;

            while (x != to.x || y != to.y)
            {
                int e2 = err * 2;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 < dx) { err += dx; y += sy; }

                if (x == to.x && y == to.y) break; // 끝점은 검사 안 함
                if (grid.GetCell(new Coord(x, y)).type == CellType.Obstacle)
                    return false;
            }
            return true;
        }
    }
}
