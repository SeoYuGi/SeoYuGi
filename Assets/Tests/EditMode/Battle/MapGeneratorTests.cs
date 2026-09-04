using System.Collections.Generic;
using NUnit.Framework;

namespace SeoYuGi.Battle.Tests
{
    /// <summary>자동 맵 생성: 대칭·연결성·예약 칸 회피·결정론.</summary>
    public class MapGeneratorTests
    {
        readonly GridConfig config = new GridConfig { width = 9, height = 9 };

        static readonly Coord[] Spawns =
        {
            new Coord(2, 1), new Coord(4, 1), new Coord(6, 1),
            new Coord(2, 7), new Coord(4, 7), new Coord(6, 7)
        };

        [Test]
        public void SameSeed_SameMap()
        {
            var a = MapGenerator.GenerateWalls(config, 8, seed: 42, Spawns);
            var b = MapGenerator.GenerateWalls(config, 8, seed: 42, Spawns);
            CollectionAssert.AreEqual(a, b);
        }

        [Test]
        public void DifferentSeed_DifferentMap()
        {
            var a = MapGenerator.GenerateWalls(config, 8, seed: 1, Spawns);
            var b = MapGenerator.GenerateWalls(config, 8, seed: 2, Spawns);
            CollectionAssert.AreNotEqual(a, b);
        }

        [Test]
        public void WallCount_EvenAndInRange()
        {
            var walls = MapGenerator.GenerateWalls(config, 8, seed: 7, Spawns);
            Assert.AreEqual(8, walls.Count);

            var odd = MapGenerator.GenerateWalls(config, 7, seed: 7, Spawns);
            Assert.AreEqual(6, odd.Count); // 홀수 → 내림
        }

        [Test]
        public void Walls_PointSymmetric()
        {
            var walls = MapGenerator.GenerateWalls(config, 8, seed: 99, Spawns);
            var set = new HashSet<Coord>(walls);
            foreach (var w in walls)
                Assert.IsTrue(set.Contains(new Coord(config.width - 1 - w.x, config.height - 1 - w.y)),
                    $"wall {w} has no mirror");
        }

        [Test]
        public void ReservedCells_NeverWalls()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var walls = MapGenerator.GenerateWalls(config, 8, seed, Spawns);
                foreach (var s in Spawns)
                    CollectionAssert.DoesNotContain(walls, s);
            }
        }

        [Test]
        public void AllFreeCells_StayConnected()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var walls = MapGenerator.GenerateWalls(config, 8, seed, Spawns);
                var grid = new GridModel(config);
                foreach (var w in walls) grid.SetObstacle(w);

                // 첫 번째 빈 칸에서 flood fill → 모든 빈 칸 도달
                var wallSet = new HashSet<Coord>(walls);
                var start = new Coord(0, 0);
                for (int y = 0; y < config.height; y++)
                for (int x = 0; x < config.width; x++)
                    if (!wallSet.Contains(new Coord(x, y))) { start = new Coord(x, y); y = config.height; break; }
                var reach = Pathfinding.FloodFill(grid, start, config.width * config.height, null);
                int freeCells = config.width * config.height - walls.Count;
                Assert.AreEqual(freeCells - 1, reach.Count, $"seed {seed}: map is split");
            }
        }
    }
}
