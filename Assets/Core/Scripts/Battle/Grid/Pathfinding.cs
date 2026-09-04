using System;
using System.Collections.Generic;

namespace SeoYuGi.Battle
{
    /// <summary>
    /// 이동 범위·경로(기획서 §2.3). 계획 페이즈 입력/표시용 — 해석(Resolve)에는 쓰지 않는다.
    /// 칸 진입 비용이 균일하지 않아(고지대 2) 버킷 다익스트라 — dist는 칸 수가 아니라 비용.
    /// 방향 순회는 Coord.Directions4 고정 순서 → 같은 입력이면 같은 경로.
    /// </summary>
    public static class Pathfinding
    {
        public readonly struct Reach
        {
            public readonly Coord coord;
            public readonly int dist; // 누적 진입 비용

            public Reach(Coord coord, int dist)
            {
                this.coord = coord;
                this.dist = dist;
            }
        }

        /// <summary>start에서 비용 maxDist 이내 도달 가능한 셀 목록(start 제외). 비용 오름차순.</summary>
        public static List<Reach> FloodFill(GridModel grid, Coord start, int maxDist, HashSet<Coord> blocked)
        {
            var best = Dijkstra(grid, start, maxDist, blocked, null, out var buckets);
            var result = new List<Reach>();
            var seen = new HashSet<Coord> { start };
            for (int d = 1; d < buckets.Count; d++)
                foreach (var c in buckets[d])
                    if (best[c] == d && seen.Add(c))
                        result.Add(new Reach(c, d));
            return result;
        }

        /// <summary>최소 비용 경로의 셀 목록(start 제외, goal 포함). 도달 불가·비용 초과면 null.</summary>
        public static List<Coord> FindPath(GridModel grid, Coord start, Coord goal, int maxDist, HashSet<Coord> blocked)
        {
            if (start == goal) return null;

            var parent = new Dictionary<Coord, Coord>();
            var best = Dijkstra(grid, start, maxDist, blocked, parent, out _);
            if (!best.ContainsKey(goal)) return null;

            var path = new List<Coord>();
            for (var c = goal; c != start; c = parent[c])
                path.Add(c);
            path.Reverse();
            return path;
        }

        /// <summary>비용 {1,2} 버킷 다익스트라 — 우선순위 큐 없이 결정론 보장.</summary>
        static Dictionary<Coord, int> Dijkstra(GridModel grid, Coord start, int maxDist,
            HashSet<Coord> blocked, Dictionary<Coord, Coord> parent, out List<List<Coord>> buckets)
        {
            // int.MaxValue 호출 대비 — 도달 가능한 최대 비용은 전 칸 × 2를 넘지 못한다
            int cap = Math.Min(maxDist, grid.Width * grid.Height * 2);

            var best = new Dictionary<Coord, int> { [start] = 0 };
            buckets = new List<List<Coord>> { new List<Coord> { start } };

            for (int d = 0; d < buckets.Count && d <= cap; d++)
            {
                var layer = buckets[d];
                for (int i = 0; i < layer.Count; i++)
                {
                    var cur = layer[i];
                    if (best[cur] != d) continue; // 더 싼 경로로 이미 갱신됨

                    foreach (var dir in Coord.Directions4)
                    {
                        var next = cur + dir;
                        if (!grid.IsWalkableTerrain(next)) continue;
                        if (blocked != null && blocked.Contains(next)) continue;

                        int nd = d + grid.EnterCost(next);
                        if (nd > cap) continue;
                        if (best.TryGetValue(next, out int old) && old <= nd) continue;

                        best[next] = nd;
                        if (parent != null) parent[next] = cur;
                        while (buckets.Count <= nd) buckets.Add(new List<Coord>());
                        buckets[nd].Add(next);
                    }
                }
            }
            return best;
        }
    }
}
