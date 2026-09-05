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
            {
                if (unit.profile == null) unit.profile = config.defaultProfile;
                unit.moveGauge = unit.profile.freeRange * MoveScale;
            }
        }

        public void Tick(float deltaTime)
        {
            foreach (var unit in State.Units)
            {
                if (!unit.alive) continue;
                var p = unit.profile;

                if (unit.moveCooldown > 0f)
                {
                    unit.moveCooldown -= deltaTime;
                    if (unit.moveCooldown <= 0f)
                    {
                        unit.moveCooldown = 0f;
                        unit.moveGauge = p.freeRange * MoveScale; // 쿨타임 종료 → 풀 게이지 복귀
                    }
                }
                else if (unit.regenDelay > 0f)
                {
                    unit.regenDelay -= deltaTime; // 이동 직후: 회복 정지 (홉 스팸 방지)
                }
                else
                {
                    unit.moveGauge = Math.Min(p.freeRange * MoveScale,
                        unit.moveGauge + p.gaugeRegenPerSecond * deltaTime);
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
            var reach = Pathfinding.FloodFill(State.Grid, unit.pos, MaxRange(unit), OtherUnitCells(unitId));
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
            if (unit.stunnedUntil > State.time || unit.flyingUntil > State.time)
                return new MoveAttempt { denied = MoveDenied.Locked }; // 스턴·비행 중 = 제자리 고정

            var p = unit.profile;
            var path = Pathfinding.FindPath(State.Grid, unit.pos, dest, MaxRange(unit), OtherUnitCells(unitId));
            if (path == null)
                return new MoveAttempt { denied = MoveDenied.Unreachable };

            // 게이지는 칸 수가 아니라 진입 비용 합(고지대 2)으로 소모
            int cost = 0;
            foreach (var c in path) cost += State.Grid.EnterCost(c);
            bool yellow = cost > BlueSteps(unit);

            State.Grid.MoveOccupant(unit.pos, dest);
            unit.pos = dest;
            unit.regenDelay = p.regenDelaySeconds; // 모든 이동 직후 회복 정지

            if (yellow)
            {
                unit.moveGauge = 0f;
                unit.moveCooldown = p.yellowCooldownSeconds;
            }
            else
            {
                unit.moveGauge -= cost;
            }

            OnUnitMoved?.Invoke(unitId, path, yellow);
            return new MoveAttempt { success = true, isYellow = yellow, path = path };
        }

        /// <summary>이번 라운드 규칙. null이면 평범한 라운드. 러너가 조립 때 꽂는다.</summary>
        public RoundRule Rule { get; set; }

        float MoveScale => Rule != null ? Rule.MoveScale : 1f;

        /// <summary>고지대 위 유닛은 이동 범위 +1 — 시야·사거리 보너스와 한 세트 (2026-09-05).</summary>
        int MaxRange(UnitState unit) =>
            (int)Math.Round((unit.profile.maxRange + (State.Grid.IsHighland(unit.pos) ? 1 : 0)) * MoveScale);

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
