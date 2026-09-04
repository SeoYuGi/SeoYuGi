using System.Collections.Generic;
using NUnit.Framework;

namespace SeoYuGi.Battle.Tests
{
    /// <summary>고정 맵 5장 — 대칭·연결성·거점·스폰 검증.</summary>
    public class BattleMapsTests
    {
        static IEnumerable<int> AllMapIndices()
        {
            for (int i = 0; i < BattleMaps.Count; i++) yield return i;
        }

        [Test]
        public void HasFiveMaps()
        {
            Assert.AreEqual(5, BattleMaps.Count);
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
        public void ThreeZonePatches_3x3_LeftToRight_OnMiddleRow(int index)
        {
            var map = BattleMaps.Get(index);
            Assert.AreEqual(3, map.Zones.Count);
            var centers = new List<Coord>();
            foreach (var zone in map.Zones)
            {
                Assert.AreEqual(9, zone.Count, $"{map.Name}: 거점은 3×3 패치");
                centers.Add(CenterOf(zone));
            }
            Assert.Less(centers[0].x, centers[1].x);
            Assert.Less(centers[1].x, centers[2].x);
            foreach (var c in centers)
                Assert.AreEqual(map.Height / 2, c.y, $"{map.Name}: 거점 중심은 중앙 행");
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

            var reached = Pathfinding.FloodFill(grid, map.Spawns[1], int.MaxValue, null);
            int floorCells = map.Width * map.Height - map.Walls.Count;
            // FloodFill은 시작 칸 제외
            Assert.AreEqual(floorCells - 1, reached.Count,
                $"{map.Name}: 고립된 바닥 칸 존재 — 맵이 두 동강");
        }

        [TestCaseSource(nameof(AllMapIndices))]
        public void ZonePatches_HaveOpenEntrances(int index)
        {
            var map = BattleMaps.Get(index);
            var walls = new HashSet<Coord>(map.Walls);
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
                    if (inBounds && !walls.Contains(n)) open++;
                }
                Assert.GreaterOrEqual(open, 4, $"{map.Name}: 거점 {CenterOf(zone)} 입구 부족");
            }
        }

        [Test]
        public void Get_WrapsIndex()
        {
            Assert.AreEqual(BattleMaps.Get(0).Name, BattleMaps.Get(5).Name);
            Assert.AreEqual(BattleMaps.Get(4).Name, BattleMaps.Get(-1).Name);
        }
    }
}
