using System;
using System.Collections.Generic;

namespace SeoYuGi.Battle
{
    /// <summary>
    /// 그리드 데이터 + 점유 관리(기획서 §2.3).
    /// 순수 C#. 월드좌표 변환은 Presentation 쪽 책임.
    /// </summary>
    public class GridModel
    {
        public int Width { get; }
        public int Height { get; }

        readonly Cell[] cells; // index = y * Width + x

        public GridModel(GridConfig config)
        {
            if (config.width <= 0 || config.height <= 0)
                throw new ArgumentException("grid size must be positive");

            Width = config.width;
            Height = config.height;
            cells = new Cell[Width * Height];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x;
                cells[i].coord = new Coord(x, y);
                cells[i].type = CellType.Empty;
                cells[i].occupantUnitId = Cell.NoUnit;
            }
        }

        int Index(Coord c) => c.y * Width + c.x;

        public bool InBounds(Coord c) => c.x >= 0 && c.x < Width && c.y >= 0 && c.y < Height;

        public Cell GetCell(Coord c) => cells[Index(c)];

        static bool WalkableType(CellType t) => t == CellType.Empty || t == CellType.Highland;

        /// <summary>맵 안 + 걸을 수 있는 지형(바닥·고지대) + 유닛 없음.</summary>
        public bool IsWalkable(Coord c)
        {
            if (!InBounds(c)) return false;
            ref var cell = ref cells[Index(c)];
            return WalkableType(cell.type) && cell.occupantUnitId == Cell.NoUnit;
        }

        /// <summary>지형만 판정(점유 무시). 해석기에서 사용.</summary>
        public bool IsWalkableTerrain(Coord c)
        {
            return InBounds(c) && WalkableType(cells[Index(c)].type);
        }

        public bool IsHighland(Coord c)
        {
            return InBounds(c) && cells[Index(c)].type == CellType.Highland;
        }

        /// <summary>이동 진입 비용: 고지대 2, 평지 1 — 올라가는 게 결단이 되게.</summary>
        public int EnterCost(Coord c) => IsHighland(c) ? 2 : 1;

        /// <summary>해당 칸의 유닛 id. 없으면 -1.</summary>
        public int GetUnitAt(Coord c) => InBounds(c) ? cells[Index(c)].occupantUnitId : Cell.NoUnit;

        public void SetObstacle(Coord c)
        {
            if (!InBounds(c)) throw new ArgumentOutOfRangeException(nameof(c));
            ref var cell = ref cells[Index(c)];
            if (cell.occupantUnitId != Cell.NoUnit)
                throw new InvalidOperationException($"cell {c} is occupied by unit {cell.occupantUnitId}");
            cell.type = CellType.Obstacle;
        }

        public void SetHighland(Coord c)
        {
            if (!InBounds(c)) throw new ArgumentOutOfRangeException(nameof(c));
            cells[Index(c)].type = CellType.Highland;
        }

        public void SetVoid(Coord c)
        {
            if (!InBounds(c)) throw new ArgumentOutOfRangeException(nameof(c));
            cells[Index(c)].type = CellType.Void;
        }

        public void PlaceUnit(int unitId, Coord c)
        {
            if (!IsWalkable(c))
                throw new InvalidOperationException($"cell {c} is not walkable");
            cells[Index(c)].occupantUnitId = unitId;
        }

        public void RemoveUnit(Coord c)
        {
            if (!InBounds(c)) throw new ArgumentOutOfRangeException(nameof(c));
            cells[Index(c)].occupantUnitId = Cell.NoUnit;
        }

        public void MoveOccupant(Coord from, Coord to)
        {
            int unitId = GetUnitAt(from);
            if (unitId == Cell.NoUnit)
                throw new InvalidOperationException($"no unit at {from}");
            RemoveUnit(from);
            PlaceUnit(unitId, to);
        }

        /// <summary>맨해튼 거리 range 이내의 맵 안 셀 목록. 순회 순서 고정(결정론).</summary>
        public List<Coord> GetCellsInRange(Coord center, int range)
        {
            var result = new List<Coord>();
            for (int dy = -range; dy <= range; dy++)
            for (int dx = -range; dx <= range; dx++)
            {
                if (Math.Abs(dx) + Math.Abs(dy) > range) continue;
                var c = new Coord(center.x + dx, center.y + dy);
                if (InBounds(c)) result.Add(c);
            }
            return result;
        }
    }
}
