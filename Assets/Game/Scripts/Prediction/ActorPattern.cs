using System;
using System.Collections.Generic;

namespace SeoYuGi.Prediction
{
    /// 액터 1명의 이동 습관 통계. 문맥(open/nearZone) 2벌의 방향 마르코프 + 칸 방문 히트맵.
    internal class ActorPattern
    {
        // 방향 인덱스: 0=Stay 1=Up 2=Down 3=Left 4=Right
        internal const int DirCount = 5;
        internal const int CtxOpen = 0;
        internal const int CtxNearZone = 1;

        internal readonly float[,,] Trans = new float[2, DirCount, DirCount]; // [ctx, prevDir, nextDir]
        internal readonly float[,] Visits;
        internal int LastDir = 0;
        internal Cell CurCell;
        internal bool HasPosition;
        internal float FirstZoneEntryTime = -1f;
        internal int ObservedMoves;
        internal int ZoneEntries;      // 거점 칸 진입 횟수 — 러시 성향
        internal int HighlandEntries;  // 고지대 칸 진입 횟수 — 고지 성향
        internal readonly Dictionary<Cell, int> OpeningCells = new Dictionary<Cell, int>(); // 라운드별 첫 3수

        private readonly PredictionConfig _cfg;

        internal ActorPattern(PredictionConfig cfg)
        {
            _cfg = cfg;
            Visits = new float[cfg.MapWidth, cfg.MapHeight];
        }

        internal static int DirOf(Cell from, Cell to)
        {
            int dx = Math.Sign(to.X - from.X);
            int dy = Math.Sign(to.Y - from.Y);
            if (dx == 0 && dy == 0) return 0;
            if (dy > 0) return 1;
            if (dy < 0) return 2;
            return dx < 0 ? 3 : 4;
        }

        internal static Cell Step(Cell from, int dir)
        {
            switch (dir)
            {
                case 1: return new Cell(from.X, from.Y + 1);
                case 2: return new Cell(from.X, from.Y - 1);
                case 3: return new Cell(from.X - 1, from.Y);
                case 4: return new Cell(from.X + 1, from.Y);
                default: return from;
            }
        }

        internal int ContextOf(Cell cell)
        {
            foreach (var z in _cfg.ZoneCells)
            {
                int d = Math.Max(Math.Abs(z.X - cell.X), Math.Abs(z.Y - cell.Y));
                if (d <= _cfg.NearZoneRange) return CtxNearZone;
            }
            return CtxOpen;
        }

        internal void ObserveMove(Cell from, Cell to, float time, int roundMoveIndex)
        {
            Decay();

            int ctx = ContextOf(from);
            int dir = DirOf(from, to);
            Trans[ctx, LastDir, dir] += 1f;
            LastDir = dir;

            if (InMap(to)) Visits[to.X, to.Y] += 1f;
            CurCell = to;
            HasPosition = true;
            ObservedMoves++;

            if (roundMoveIndex < 3)
            {
                OpeningCells.TryGetValue(to, out int n);
                OpeningCells[to] = n + 1;
            }

            if (_cfg.ZoneCells.Contains(to)) ZoneEntries++;
            if (_cfg.HighlandCells.Contains(to)) HighlandEntries++;

            if (FirstZoneEntryTime < 0f && _cfg.ZoneCells.Contains(to))
                FirstZoneEntryTime = time;
        }

        internal void ResetRoundState()
        {
            // 학습은 유지하고 라운드 종속 상태만 초기화
            LastDir = 0;
            HasPosition = false;
            FirstZoneEntryTime = -1f;
        }

        internal bool InMap(Cell c) =>
            c.X >= 0 && c.X < _cfg.MapWidth && c.Y >= 0 && c.Y < _cfg.MapHeight;

        private void Decay()
        {
            float d = _cfg.DecayPerObservation;
            for (int c = 0; c < 2; c++)
                for (int i = 0; i < DirCount; i++)
                    for (int j = 0; j < DirCount; j++)
                        Trans[c, i, j] *= d;
            for (int x = 0; x < _cfg.MapWidth; x++)
                for (int y = 0; y < _cfg.MapHeight; y++)
                    Visits[x, y] *= d;
        }
    }
}
