using System.Collections.Generic;
using NUnit.Framework;

namespace SeoYuGi.Battle.Tests
{
    public class PathfindingTests
    {
        GridModel grid;

        [SetUp]
        public void SetUp()
        {
            grid = new GridModel(new GridConfig()); // 12×12
        }

        [Test]
        public void FloodFill_RespectsMaxDist()
        {
            var reach = Pathfinding.FloodFill(grid, new Coord(5, 5), 2, null);

            Assert.IsTrue(reach.Count > 0);
            foreach (var r in reach)
            {
                Assert.LessOrEqual(r.dist, 2);
                Assert.LessOrEqual(Coord.Manhattan(new Coord(5, 5), r.coord), 2);
            }
            // 맨해튼 2 이내 셀 수(자기 제외): 4 + 8 = 12
            Assert.AreEqual(12, reach.Count);
        }

        [Test]
        public void FloodFill_BlockedCellsExcluded()
        {
            grid.SetObstacle(new Coord(6, 5));
            var blocked = new HashSet<Coord> { new Coord(4, 5) };

            var reach = Pathfinding.FloodFill(grid, new Coord(5, 5), 1, blocked);

            var coords = new List<Coord>();
            foreach (var r in reach) coords.Add(r.coord);
            CollectionAssert.DoesNotContain(coords, new Coord(6, 5)); // 장애물
            CollectionAssert.DoesNotContain(coords, new Coord(4, 5)); // 유닛 점유
            Assert.AreEqual(2, reach.Count); // 상하만 남음
        }

        [Test]
        public void FindPath_StraightLine()
        {
            var path = Pathfinding.FindPath(grid, new Coord(0, 0), new Coord(3, 0), 4, null);

            Assert.IsNotNull(path);
            CollectionAssert.AreEqual(
                new[] { new Coord(1, 0), new Coord(2, 0), new Coord(3, 0) }, path);
        }

        [Test]
        public void FindPath_AroundObstacle()
        {
            grid.SetObstacle(new Coord(1, 0));
            var path = Pathfinding.FindPath(grid, new Coord(0, 0), new Coord(2, 0), 4, null);

            Assert.IsNotNull(path);
            Assert.AreEqual(4, path.Count); // 우회로 2 + 직선 2
            Assert.AreEqual(new Coord(2, 0), path[path.Count - 1]);
            CollectionAssert.DoesNotContain(path, new Coord(1, 0));
        }

        [Test]
        public void FindPath_BeyondMaxDist_ReturnsNull()
        {
            Assert.IsNull(Pathfinding.FindPath(grid, new Coord(0, 0), new Coord(5, 0), 4, null));
        }

        [Test]
        public void FindPath_Unreachable_ReturnsNull()
        {
            // (1,1)을 장애물로 포위
            grid.SetObstacle(new Coord(0, 1));
            grid.SetObstacle(new Coord(2, 1));
            grid.SetObstacle(new Coord(1, 0));
            grid.SetObstacle(new Coord(1, 2));

            Assert.IsNull(Pathfinding.FindPath(grid, new Coord(5, 5), new Coord(1, 1), 20, null));
        }

        [Test]
        public void FindPath_SameStartGoal_ReturnsNull()
        {
            Assert.IsNull(Pathfinding.FindPath(grid, new Coord(3, 3), new Coord(3, 3), 4, null));
        }

        [Test]
        public void FindPath_Deterministic()
        {
            grid.SetObstacle(new Coord(3, 3));
            var a = Pathfinding.FindPath(grid, new Coord(1, 1), new Coord(5, 5), 10, null);
            var b = Pathfinding.FindPath(grid, new Coord(1, 1), new Coord(5, 5), 10, null);

            CollectionAssert.AreEqual(a, b);
        }
    }
}
