using System;
using System.Collections.Generic;

namespace SeoYuGi.Battle
{
    [Serializable]
    public class PickupConfig
    {
        public int healAmount = 2;        // HP 2~6 스케일 — 2면 오버워치 대형팩 체감
        public float respawnSeconds = 10f;
    }

    public class HealPack
    {
        public Coord pos;
        public bool active = true;
        public float respawnAt;
    }

    /// <summary>
    /// 힐팩 픽업 (오버워치식): 칸을 밟으면 즉시 회복 → 팩 소멸 → 일정 시간 후 같은 자리 리스폰.
    /// 풀피 유닛은 밟아도 소모되지 않는다(팩 아끼기). AP·이동 게이지와 무관 — 위치 자체가 자원.
    /// 순수 C# — 외부에서 Tick()을 호출한다. 시간은 State.time(CombatSystem이 전진) 기준.
    /// </summary>
    public class PickupSystem
    {
        public BattleState State { get; }
        public PickupConfig Config { get; }
        public List<HealPack> Packs { get; } = new List<HealPack>();

        /// <summary>(unitId, pos, healedAmount) — 연출용.</summary>
        public event Action<int, Coord, int> OnPickup;

        public PickupSystem(BattleState state, PickupConfig config, IEnumerable<Coord> spawnCells)
        {
            State = state;
            Config = config;
            foreach (var c in spawnCells)
                Packs.Add(new HealPack { pos = c });
        }

        public void Tick()
        {
            foreach (var pack in Packs)
            {
                if (!pack.active)
                {
                    if (State.time >= pack.respawnAt) pack.active = true;
                    continue;
                }

                int unitId = State.Grid.GetUnitAt(pack.pos);
                if (unitId == Cell.NoUnit) continue;

                var unit = State.GetUnit(unitId);
                if (unit == null || !unit.alive || unit.hp >= unit.maxHp) continue;

                Consume(pack, unit);
            }
        }

        /// <summary>이동 경로가 팩 칸을 지나면 픽업 — 멈추지 않아도 먹는다 (MoveSystem.OnUnitMoved에 배선).</summary>
        public void OnUnitPath(int unitId, IReadOnlyList<Coord> path)
        {
            var unit = State.GetUnit(unitId);
            if (unit == null || !unit.alive) return;
            foreach (var pack in Packs)
            {
                if (!pack.active) continue;
                if (unit.hp >= unit.maxHp) return; // 풀피 통과 = 팩 아낌 (밟기와 같은 규칙)
                foreach (var c in path)
                {
                    if (c.x != pack.pos.x || c.y != pack.pos.y) continue;
                    Consume(pack, unit);
                    break;
                }
            }
        }

        void Consume(HealPack pack, UnitState unit)
        {
            int healed = Math.Min(Config.healAmount, unit.maxHp - unit.hp);
            unit.hp += healed;
            pack.active = false;
            pack.respawnAt = State.time + Config.respawnSeconds;
            OnPickup?.Invoke(unit.id, pack.pos, healed);
        }
    }
}
