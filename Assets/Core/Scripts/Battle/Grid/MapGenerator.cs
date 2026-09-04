using System;
using System.Collections.Generic;

namespace SeoYuGi.Battle
{
    /// <summary>
    /// 자동 맵 생성 (세부기획 B: 벽 타일 4~8개로 골목 구조).
    /// - 벽은 180° 점대칭 쌍으로 배치 → 양팀 공정.
    /// - 예약 칸(스폰 등)은 피하고, 모든 빈 칸의 연결성을 보장한다.
    /// - 같은 시드 → 같은 맵 (System.Random(seed)).
    /// </summary>
    public static class MapGenerator
    {
        /// <param name="wallTiles">목표 벽 수. 대칭 쌍 배치라 홀수면 1 내림.</param>
        public static List<Coord> GenerateWalls(GridConfig config, int wallTiles, int seed, IReadOnlyCollection<Coord> reserved)
        {
            var rng = new Random(seed);
            var walls = new HashSet<Coord>();
            var reservedSet = reserved != null ? new HashSet<Coord>(reserved) : new HashSet<Coord>();

            int target = wallTiles - (wallTiles % 2);
            int guard = 1000; // 배치 불가능한 예약/크기 조합에서도 종료 보장
            while (walls.Count < target && guard-- > 0)
            {
                var c = new Coord(rng.Next(config.width), rng.Next(config.height));
                var m = new Coord(config.width - 1 - c.x, config.height - 1 - c.y); // 180° 대칭
                if (c == m) continue; // 정중앙(홀수 크기)은 쌍이 안 됨
                if (walls.Contains(c) || walls.Contains(m)) continue;
                if (reservedSet.Contains(c) || reservedSet.Contains(m)) continue;

                walls.Add(c);
                walls.Add(m);
                if (!AllFreeCellsConnected(config, walls))
                {
                    walls.Remove(c); // 맵을 두 동강 내는 배치는 되돌림
                    walls.Remove(m);
                }
            }

            // HashSet 순회 순서에 의존하지 않도록 정렬 (결정론)
            var result = new List<Coord>(walls);
            result.Sort((a, b) => a.y != b.y ? a.y - b.y : a.x - b.x);
            return result;
        }

        /// <summary>벽을 제외한 모든 칸이 하나로 이어져 있는지 BFS로 확인.</summary>
        static bool AllFreeCellsConnected(GridConfig config, HashSet<Coord> walls)
        {
            int freeTotal = config.width * config.height - walls.Count;
            if (freeTotal <= 0) return false;

            Coord start = default;
            bool found = false;
            for (int y = 0; y < config.height && !found; y++)
            for (int x = 0; x < config.width && !found; x++)
            {
                var c = new Coord(x, y);
                if (!walls.Contains(c)) { start = c; found = true; }
            }

            var visited = new HashSet<Coord> { start };
            var queue = new Queue<Coord>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var dir in Coord.Directions4)
                {
                    var next = cur + dir;
                    if (next.x < 0 || next.x >= config.width || next.y < 0 || next.y >= config.height) continue;
                    if (walls.Contains(next) || visited.Contains(next)) continue;
                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }
            return visited.Count == freeTotal;
        }
    }
}
