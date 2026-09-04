using System.Collections.Generic;
using NUnit.Framework;

namespace SeoYuGi.Battle.Tests
{
    /// <summary>탱고파이브식 실시간 이동: 파랑=게이지 이내, 노랑=초과(쿨타임), 회복.</summary>
    public class MoveSystemTests
    {
        MoveConfig config;
        BattleState battle;
        MoveSystem move;

        [SetUp]
        public void SetUp()
        {
            config = new MoveConfig
            {
                freeRange = 2,
                maxRange = 4,
                gaugeRegenPerSecond = 1f,
                yellowCooldownSeconds = 2.5f
            };
            battle = new BattleState(new GridModel(new GridConfig()));
            battle.AddUnit(new UnitState(1, team: 0, new Coord(5, 5)));
            battle.AddUnit(new UnitState(2, team: 1, new Coord(0, 0)));
            move = new MoveSystem(battle, config);
        }

        (List<Coord> blue, List<Coord> yellow) Ranges(int unitId)
        {
            var blue = new List<Coord>();
            var yellow = new List<Coord>();
            move.GetRanges(unitId, blue, yellow);
            return (blue, yellow);
        }

        [Test]
        public void FreshUnit_RangesSplitAtFreeRange()
        {
            var (blue, yellow) = Ranges(1);

            Assert.AreEqual(12, blue.Count);   // 맨해튼 1~2: 4 + 8
            Assert.AreEqual(28, yellow.Count); // 맨해튼 3~4: 12 + 16
            foreach (var c in blue) Assert.LessOrEqual(Coord.Manhattan(new Coord(5, 5), c), 2);
            foreach (var c in yellow) Assert.Greater(Coord.Manhattan(new Coord(5, 5), c), 2);
        }

        [Test]
        public void BlueMove_ConsumesGauge_NoCooldown()
        {
            var result = move.TryMove(1, new Coord(5, 7)); // 2칸 = 게이지 전부

            Assert.IsTrue(result.success);
            Assert.IsFalse(result.isYellow);
            var unit = battle.GetUnit(1);
            Assert.AreEqual(new Coord(5, 7), unit.pos);
            Assert.AreEqual(0f, unit.moveGauge, 0.001f);
            Assert.AreEqual(0f, unit.moveCooldown);
            Assert.AreEqual(1, battle.Grid.GetUnitAt(new Coord(5, 7)));
            Assert.AreEqual(Cell.NoUnit, battle.Grid.GetUnitAt(new Coord(5, 5)));
        }

        [Test]
        public void AfterGaugeSpent_AllRangesYellow()
        {
            move.TryMove(1, new Coord(5, 7)); // 게이지 소진

            var (blue, yellow) = Ranges(1);
            Assert.AreEqual(0, blue.Count);
            Assert.Greater(yellow.Count, 0); // 전부 노랑
        }

        [Test]
        public void YellowMove_SetsCooldown_AndLocksMovement()
        {
            var result = move.TryMove(1, new Coord(5, 8)); // 3칸 > 게이지 2 → 노랑

            Assert.IsTrue(result.success);
            Assert.IsTrue(result.isYellow);
            var unit = battle.GetUnit(1);
            Assert.AreEqual(config.yellowCooldownSeconds, unit.moveCooldown, 0.001f);

            // 쿨타임 중: 범위 없음 + 이동 거부
            var (blue, yellow) = Ranges(1);
            Assert.AreEqual(0, blue.Count + yellow.Count);
            Assert.AreEqual(MoveDenied.Locked, move.TryMove(1, new Coord(5, 9)).denied);
        }

        [Test]
        public void CooldownExpires_FullGaugeReturns()
        {
            move.TryMove(1, new Coord(5, 8)); // 노랑 → 쿨타임 2.5초

            move.Tick(2.6f);

            var unit = battle.GetUnit(1);
            Assert.AreEqual(0f, unit.moveCooldown);
            Assert.AreEqual(config.freeRange, unit.moveGauge, 0.001f);
            var (blue, yellow) = Ranges(1);
            Assert.Greater(blue.Count, 0);   // 파랑+노랑 다 복귀
            Assert.Greater(yellow.Count, 0);
        }

        [Test]
        public void Gauge_RegensOverTime()
        {
            move.TryMove(1, new Coord(5, 7)); // 게이지 0
            move.Tick(1f);                    // +1

            Assert.AreEqual(1f, battle.GetUnit(1).moveGauge, 0.001f);
            var (blue, _) = Ranges(1);
            Assert.AreEqual(4, blue.Count); // 1칸 거리만 파랑
        }

        [Test]
        public void Gauge_ClampedAtFreeRange()
        {
            move.Tick(100f);
            Assert.AreEqual(config.freeRange, battle.GetUnit(1).moveGauge, 0.001f);
        }

        [Test]
        public void MoveToOccupiedCell_Denied()
        {
            battle.AddUnit(new UnitState(3, team: 0, new Coord(5, 6)));

            Assert.AreEqual(MoveDenied.Unreachable, move.TryMove(1, new Coord(5, 6)).denied);
        }

        [Test]
        public void MoveBeyondMaxRange_Denied()
        {
            Assert.AreEqual(MoveDenied.Unreachable, move.TryMove(1, new Coord(5, 10)).denied); // 5칸
        }

        [Test]
        public void PathRoutesAroundUnitsAndObstacles()
        {
            battle.Grid.SetObstacle(new Coord(6, 5));
            battle.AddUnit(new UnitState(3, team: 1, new Coord(5, 6)));

            var result = move.TryMove(1, new Coord(7, 5)); // 직선 2칸이지만 우회 필요 → 4칸 노랑

            Assert.IsTrue(result.success);
            Assert.IsTrue(result.isYellow);
            Assert.AreEqual(4, result.path.Count);
        }
    }
}
