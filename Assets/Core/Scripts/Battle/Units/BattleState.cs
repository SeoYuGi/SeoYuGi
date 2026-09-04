using System;
using System.Collections.Generic;

namespace SeoYuGi.Battle
{
    /// <summary>전투 전체 상태: 그리드 + 유닛 목록.</summary>
    public class BattleState
    {
        public GridModel Grid { get; }

        // unitId 오름차순 유지 — 순회 순서 고정.
        readonly List<UnitState> units = new List<UnitState>();

        public IReadOnlyList<UnitState> Units => units;

        public BattleState(GridModel grid)
        {
            Grid = grid;
        }

        public void AddUnit(UnitState unit)
        {
            if (GetUnit(unit.id) != null)
                throw new ArgumentException($"duplicate unit id {unit.id}");
            Grid.PlaceUnit(unit.id, unit.pos);

            int i = units.FindIndex(u => u.id > unit.id);
            if (i < 0) units.Add(unit);
            else units.Insert(i, unit);
        }

        public UnitState GetUnit(int id)
        {
            for (int i = 0; i < units.Count; i++)
                if (units[i].id == id) return units[i];
            return null;
        }
    }
}
