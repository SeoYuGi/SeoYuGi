using System;
using System.Collections.Generic;

namespace SeoYuGi.Battle
{
    public enum MoveDenied
    {
        None,
        Dead,        // 유닛 없음/사망
        Locked,      // 노랑 이동 쿨타임 중
        Unreachable  // 범위 밖 / 경로 없음 / 목적지 점유
    }

    public struct MoveAttempt
    {
        public bool success;
        public bool isYellow;          // 게이지 초과 이동이었나
        public List<Coord> path;       // 출발 제외, 도착 포함
        public MoveDenied denied;
    }

    /// <summary>
    /// 실시간 이동 규칙 (탱고파이브식). 순수 C# — 외부에서 Tick(dt)를 호출한다.
    ///
    /// 유닛별 이동 게이지(0..freeRange, 초당 회복):
    ///  - 파랑 = 현재 게이지 이내 거리. 이동 시 게이지만 소모, 즉시 다음 행동 가능.
    ///  - 노랑 = 게이지 초과 ~ maxRange. 이동 시 게이지 0 + 쿨타임(이동 불가).
    ///  - 쿨타임이 끝나면 게이지가 가득 찬 상태로 복귀(파랑+노랑 전부 사용 가능).
    /// 이동은 논리상 즉시 적용(점유 이동). 연출은 View가 path를 재생한다.
    /// </summary>
    public class MoveSystem
    {
        public BattleState State { get; }
        public MoveConfig Config { get; }

        /// <summary>(unitId, path, isYellow) — 연출용.</summary>
        public event Action<int, IReadOnlyList<Coord>, bool> OnUnitMoved;

        public MoveSystem(BattleState state, MoveConfig config)
        {
            State = state;
            Config = config;
            foreach (var unit in state.Units)
                unit.moveGauge = config.freeRange;
        }

        public void Tick(float deltaTime)
        {
            foreach (var unit in State.Units)
            {
                if (!unit.alive) continue;
                if (unit.moveCooldown > 0f)
                {
                    unit.moveCooldown -= deltaTime;
                    if (unit.moveCooldown <= 0f)
                    {
                        unit.moveCooldown = 0f;
                        unit.moveGauge = Config.freeRange; // 쿨타임 종료 → 풀 게이지 복귀
                    }
                }
                else
                {
                    unit.moveGauge = Math.Min(Config.freeRange,
                        unit.moveGauge + Config.gaugeRegenPerSecond * deltaTime);
                }
            }
        }

        /// <summary>현재 게이지로 무료 이동 가능한 칸 수.</summary>
        public int BlueSteps(UnitState unit) => (int)unit.moveGauge;

        /// <summary>이동 범위 조회. 쿨타임 중이면 둘 다 빈 채로 반환.</summary>
        public void GetRanges(int unitId, List<Coord> blue, List<Coord> yellow)
        {
            blue.Clear();
            yellow.Clear();
            var unit = State.GetUnit(unitId);
            if (unit == null || !unit.alive || unit.moveCooldown > 0f) return;

            int blueSteps = BlueSteps(unit);
            var reach = Pathfinding.FloodFill(State.Grid, unit.pos, Config.maxRange, OtherUnitCells(unitId));
            foreach (var r in reach)
                (r.dist <= blueSteps ? blue : yellow).Add(r.coord);
        }

        public MoveAttempt TryMove(int unitId, Coord dest)
        {
            var unit = State.GetUnit(unitId);
            if (unit == null || !unit.alive)
                return new MoveAttempt { denied = MoveDenied.Dead };
            if (unit.moveCooldown > 0f)
                return new MoveAttempt { denied = MoveDenied.Locked };

            var path = Pathfinding.FindPath(State.Grid, unit.pos, dest, Config.maxRange, OtherUnitCells(unitId));
            if (path == null)
                return new MoveAttempt { denied = MoveDenied.Unreachable };

            bool yellow = path.Count > BlueSteps(unit);

            State.Grid.MoveOccupant(unit.pos, dest);
            unit.pos = dest;

            if (yellow)
            {
                unit.moveGauge = 0f;
                unit.moveCooldown = Config.yellowCooldownSeconds;
            }
            else
            {
                unit.moveGauge -= path.Count;
            }

            OnUnitMoved?.Invoke(unitId, path, yellow);
            return new MoveAttempt { success = true, isYellow = yellow, path = path };
        }

        HashSet<Coord> OtherUnitCells(int exceptUnitId)
        {
            var set = new HashSet<Coord>();
            foreach (var u in State.Units)
                if (u.id != exceptUnitId && u.alive)
                    set.Add(u.pos);
            return set;
        }
    }
}
