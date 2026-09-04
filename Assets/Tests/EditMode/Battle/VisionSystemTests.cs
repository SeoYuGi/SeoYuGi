using NUnit.Framework;

namespace SeoYuGi.Battle.Tests
{
    /// <summary>팀 공유 시야 + 벽 LOS 차단 + 고스트 마커 (세부기획 B).</summary>
    public class VisionSystemTests
    {
        BattleState battle;
        GridModel grid;

        [SetUp]
        public void SetUp()
        {
            grid = new GridModel(new GridConfig { width = 9, height = 9 });
            battle = new BattleState(grid);
        }

        UnitState Add(int id, int team, Coord pos, UnitClass cls = UnitClass.Balance)
        {
            var u = new UnitState(id, team, pos, cls);
            battle.AddUnit(u);
            return u;
        }

        void MoveTo(UnitState u, Coord to)
        {
            battle.Grid.MoveOccupant(u.pos, to);
            u.pos = to;
        }

        [Test]
        public void SightRange_IsChebyshev()
        {
            Add(1, 0, new Coord(4, 4)); // Balance 시야 4
            var vision = new VisionSystem(battle);

            // 대각 (8,8)은 체비쇼프 4 (맨해튼 8) — 대각도 시야 반경 안
            Assert.IsTrue(vision.IsVisibleTo(0, new Coord(8, 8)));
            Assert.IsTrue(vision.IsVisibleTo(0, new Coord(4, 0)));
        }

        [Test]
        public void OutOfRange_NotVisible()
        {
            Add(1, 0, new Coord(0, 0)); // Balance 시야 4
            var vision = new VisionSystem(battle);

            Assert.IsFalse(vision.IsVisibleTo(0, new Coord(5, 0))); // 체비쇼프 5
            Assert.IsFalse(vision.IsVisibleTo(0, new Coord(0, 5)));
        }

        [Test]
        public void Wall_BlocksLineOfSight_ButWallItselfVisible()
        {
            grid.SetObstacle(new Coord(2, 0));
            Add(1, 0, new Coord(0, 0));
            var vision = new VisionSystem(battle);

            Assert.IsTrue(vision.IsVisibleTo(0, new Coord(2, 0)));  // 벽 자체는 보임
            Assert.IsFalse(vision.IsVisibleTo(0, new Coord(3, 0))); // 벽 뒤는 안 보임
            Assert.IsFalse(vision.IsVisibleTo(0, new Coord(4, 0)));
        }

        [Test]
        public void TeamVision_IsUnionOfMembers()
        {
            Add(1, 0, new Coord(0, 0));
            Add(2, 0, new Coord(8, 8));
            var vision = new VisionSystem(battle);

            Assert.IsTrue(vision.IsVisibleTo(0, new Coord(1, 1)));
            Assert.IsTrue(vision.IsVisibleTo(0, new Coord(7, 7)));
            Assert.IsFalse(vision.IsVisibleTo(1, new Coord(1, 1))); // 팀1은 유닛 없음 — 아무것도 안 보임
        }

        [Test]
        public void DeadUnit_ContributesNoVision()
        {
            var u = Add(1, 0, new Coord(0, 0));
            var vision = new VisionSystem(battle);
            Assert.IsTrue(vision.IsVisibleTo(0, new Coord(1, 1)));

            u.alive = false;
            grid.RemoveUnit(u.pos);
            vision.Tick();

            Assert.IsFalse(vision.IsVisibleTo(0, new Coord(1, 1)));
        }

        [Test]
        public void GhostMarker_RemembersLastSeenPosition()
        {
            Add(1, 0, new Coord(0, 0));                    // 관찰자 (시야 4)
            var enemy = Add(2, 1, new Coord(3, 0));        // 시야 안
            var vision = new VisionSystem(battle);

            Assert.IsTrue(vision.TryGetLastSeen(0, 2, out var seen));
            Assert.AreEqual(new Coord(3, 0), seen);

            MoveTo(enemy, new Coord(8, 8));                // 시야 밖으로 이탈
            vision.Tick();

            Assert.IsFalse(vision.IsVisibleTo(0, enemy.pos));
            Assert.IsTrue(vision.TryGetLastSeen(0, 2, out seen));
            Assert.AreEqual(new Coord(3, 0), seen);        // 잔상은 마지막 목격 위치에 남는다
        }

        [Test]
        public void NeverSeenEnemy_HasNoGhost()
        {
            Add(1, 0, new Coord(0, 0));
            Add(2, 1, new Coord(8, 8)); // 시야 밖에서 시작
            var vision = new VisionSystem(battle);

            Assert.IsFalse(vision.TryGetLastSeen(0, 2, out _));
        }
    }
}
