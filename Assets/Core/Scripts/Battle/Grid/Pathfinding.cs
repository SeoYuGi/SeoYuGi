using System.Collections.Generic;

namespace SeoYuGi.Battle
{
    /// <summary>
    /// BFS 이동 범위·경로(기획서 §2.3). 계획 페이즈 입력/표시용 — 해석(Resolve)에는 쓰지 않는다.
    /// 방향 순회는 Coord.Directions4 고정 순서 → 같은 입력이면 같은 경로.
    /// </summary>
    public static class Pathfinding
    {
        public readonly struct Reach
        {
            public readonly Coord coord;
            public readonly int dist;

            public Reach(Coord coord, int dist)
            {
                this.coord = coord;
                this.dist = dist;
            }
        }

        /// <summary>start에서 maxDist칸 이내 도달 가능한 셀 목록(start 제외). BFS 방문 순서.</summary>
        public static List<Reach> FloodFill(GridModel grid, Coord start, int maxDist, HashSet<Coord> blocked)
        {
            var result = new List<Reach>();
            var dist = new Dictionary<Coord, int> { [start] = 0 };
            var queue = new Queue<Coord>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                int d = dist[cur];
                if (d >= maxDist) continue;

                foreach (var dir in Coord.Directions4)
                {
                    var next = cur + dir;
                    if (dist.ContainsKey(next)) continue;
                    if (!grid.IsWalkableTerrain(next)) continue;
                    if (blocked != null && blocked.Contains(next)) continue;

                    dist[next] = d + 1;
                    result.Add(new Reach(next, d + 1));
                    queue.Enqueue(next);
                }
            }
            return result;
        }

        /// <summary>최단 경로의 셀 목록(start 제외, goal 포함). 도달 불가·거리 초과면 null.</summary>
        public static List<Coord> FindPath(GridModel grid, Coord start, Coord goal, int maxDist, HashSet<Coord> blocked)
        {
            if (start == goal) return null;

            var parent = new Dictionary<Coord, Coord>();
            var dist = new Dictionary<Coord, int> { [start] = 0 };
            var queue = new Queue<Coord>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                int d = dist[cur];
                if (d >= maxDist) continue;

                foreach (var dir in Coord.Directions4)
                {
                    var next = cur + dir;
                    if (dist.ContainsKey(next)) continue;
                    if (!grid.IsWalkableTerrain(next)) continue;
                    if (blocked != null && blocked.Contains(next)) continue;

                    dist[next] = d + 1;
                    parent[next] = cur;

                    if (next == goal)
                    {
                        var path = new List<Coord>();
                        for (var c = goal; c != start; c = parent[c])
                            path.Add(c);
                        path.Reverse();
                        return path;
                    }
                    queue.Enqueue(next);
                }
            }
            return null;
        }
    }
}
