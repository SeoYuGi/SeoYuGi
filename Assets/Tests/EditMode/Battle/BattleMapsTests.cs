using System.Collections.Generic;
using NUnit.Framework;

namespace SeoYuGi.Battle.Tests
{
    /// <summary>고정 맵 6장 — 대칭·연결성·거점·스폰 검증.</summary>
    public class BattleMapsTests
    {
        static IEnumerable<int> AllMapIndices()
        {
            for (int i = 0; i < BattleMaps.Count; i++) yield return i;
        }

        [Test]
        public void HasSixMaps()
        {
            Assert.AreEqual(6, BattleMaps.Count);
        }

        [TestCaseSource(nameof(AllMapIndices))]
        public void Walls_ArePointSymmetric(int index)
        {
            var map = BattleMaps.Get(index);
            var walls = new HashSet<Coord>(map.Walls);
            foreach (var w in walls)
            {
                var mirror = new Coord(map.Width - 1 - w.x, map.Height - 1 - w.y);
                Assert.IsTrue(walls.Contains(mirror),
                    $"{map.Name}: 벽 {w}의 180° 대칭 {mirror}이 없음 — 양팀 불공정");
            }
        }

        static Coord CenterOf(List<Coord> cells)
        {
            int sx = 0, sy = 0;
            foreach (var c in cells) { sx += c.x; sy += c.y; }
            return new Coord(sx / cells.Count, sy / cells.Count);
        }

        [TestCaseSource(nameof(AllMapIndices))]
        public void ZonePatches_Odd_3x3_LeftToRight_PointSymmetric(int index)
        {
            var map = BattleMaps.Get(index);
            // 리마스터: 맵마다 거점 수 가변(1 또는 3) — 단 홀수(동점 없음)
            Assert.GreaterOrEqual(map.Zones.Count, 1);
            Assert.AreEqual(1, map.Zones.Count % 2, $"{map.Name}: 거점 수는 홀수여야 함");
            var centers = new List<Coord>();
            foreach (var zone in map.Zones)
            {
                Assert.AreEqual(9, zone.Count, $"{map.Name}: 거점은 3×3 패치");
                centers.Add(CenterOf(zone));
            }
            for (int i = 0; i < centers.Count - 1; i++)
                Assert.Less(centers[i].x, centers[i + 1].x, $"{map.Name}: 거점 좌→우 순서 위반");

            // 공정성: 180° 점대칭 — i번째 거점 ↔ 반대쪽 거점 미러, 가운데 거점은 자기 대칭
            Coord Mirror(Coord c) => new Coord(map.Width - 1 - c.x, map.Height - 1 - c.y);
            for (int i = 0; i < map.Zones.Count; i++)
            {
                var opposite = new HashSet<Coord>(map.Zones[map.Zones.Count - 1 - i]);
                foreach (var c in map.Zones[i])
                    Assert.IsTrue(opposite.Contains(Mirror(c)),
                        $"{map.Name}: 거점 {i} {c}의 대칭이 반대쪽 거점에 없음");
            }
        }

        [TestCaseSource(nameof(AllMapIndices))]
        public void HighlandsAndVoids_ArePointSymmetric(int index)
        {
            var map = BattleMaps.Get(index);
            var highs = new HashSet<Coord>(map.Highlands);
            foreach (var h in highs)
            {
                var mirror = new Coord(map.Width - 1 - h.x, map.Height - 1 - h.y);
                Assert.IsTrue(highs.Contains(mirror),
                    $"{map.Name}: 고지대 {h}의 180° 대칭 {mirror}이 없음 — 양팀 불공정");
            }
            var voids = new HashSet<Coord>(map.Voids);
            foreach (var v in voids)
            {
                var mirror = new Coord(map.Width - 1 - v.x, map.Height - 1 - v.y);
                Assert.IsTrue(voids.Contains(mirror),
                    $"{map.Name}: 구덩이 {v}의 180° 대칭 {mirror}이 없음 — 양팀 불공정");
            }
            var packs = new HashSet<Coord>(map.HealPacks);
            foreach (var p in packs)
            {
                var mirror = new Coord(map.Width - 1 - p.x, map.Height - 1 - p.y);
                Assert.IsTrue(packs.Contains(mirror),
                    $"{map.Name}: 힐팩 {p}의 180° 대칭 {mirror}이 없음 — 양팀 불공정");
            }
        }

        [TestCaseSource(nameof(AllMapIndices))]
        public void SixSpawns_TeamsOnOppositeEdges(int index)
        {
            var map = BattleMaps.Get(index);
            Assert.AreEqual(6, map.Spawns.Count);
            for (int id = 1; id <= 3; id++)
                Assert.AreEqual(0, map.Spawns[id].y, $"{map.Name}: 팀0 스폰은 아래");
            for (int id = 4; id <= 6; id++)
                Assert.AreEqual(map.Height - 1, map.Spawns[id].y, $"{map.Name}: 팀1 스폰은 위");
        }

        [TestCaseSource(nameof(AllMapIndices))]
        public void AllFloorCells_Connected(int index)
        {
            var map = BattleMaps.Get(index);
            var grid = new GridModel(new GridConfig { width = map.Width, height = map.Height });
            foreach (var w in map.Walls)
                grid.SetObstacle(w);
            foreach (var v in map.Voids)
                grid.SetVoid(v);

            var reached = Pathfinding.FloodFill(grid, map.Spawns[1], int.MaxValue, null);
            int floorCells = map.Width * map.Height - map.Walls.Count - map.Voids.Count;
            // FloodFill은 시작 칸 제외
            Assert.AreEqual(floorCells - 1, reached.Count,
                $"{map.Name}: 고립된 바닥 칸 존재 — 맵이 두 동강");
        }

        [TestCaseSource(nameof(AllMapIndices))]
        public void ZonePatches_HaveOpenEntrances(int index)
        {
            var map = BattleMaps.Get(index);
            var blocked = new HashSet<Coord>(map.Walls);
            blocked.UnionWith(map.Voids); // 구덩이도 진입 불가
            foreach (var zone in map.Zones)
            {
                var patch = new HashSet<Coord>(zone);
                int open = 0;
                foreach (var cell in zone)
                foreach (var dir in Coord.Directions4)
                {
                    var n = cell + dir;
                    if (patch.Contains(n)) continue;
                    bool inBounds = n.x >= 0 && n.x < map.Width && n.y >= 0 && n.y < map.Height;
                    if (inBounds && !blocked.Contains(n)) open++;
                }
                Assert.GreaterOrEqual(open, 4, $"{map.Name}: 거점 {CenterOf(zone)} 입구 부족");
            }
        }

        [Test]
        public void Get_WrapsIndex()
        {
            Assert.AreEqual(BattleMaps.Get(0).Name, BattleMaps.Get(6).Name);
            Assert.AreEqual(BattleMaps.Get(5).Name, BattleMaps.Get(-1).Name);
        }
    }
}
