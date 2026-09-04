using System.Collections.Generic;
using NUnit.Framework;

namespace SeoYuGi.Battle.Tests
{
    /// <summary>탱고파이브식 실시간 이동: 파랑=게이지 이내, 노랑=초과(쿨타임), 회복 지연.</summary>
    public class MoveSystemTests
    {
        MoveConfig config;
        MoveProfile profile;
        BattleState battle;
        MoveSystem move;

        [SetUp]
        public void SetUp()
        {
            profile = new MoveProfile
            {
                freeRange = 2,
                maxRange = 4,
                gaugeRegenPerSecond = 1f,
                regenDelaySeconds = 1f,
                yellowCooldownSeconds = 2.5f
            };
            config = new MoveConfig { defaultProfile = profile };
            battle = new BattleState(new GridModel(new GridConfig()));
            battle.AddUnit(new UnitState(1, team: 0, new Coord(5, 5)) { profile = profile }); // 클래스 기본값 대신 테스트 수치 고정
            battle.AddUnit(new UnitState(2, team: 1, new Coord(0, 0)) { profile = profile });
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
        public void RegenPaused_RightAfterMove()
        {
            move.TryMove(1, new Coord(5, 6)); // 파랑 1칸 → 게이지 1, 회복 지연 1초

            move.Tick(1f); // 지연 소화 — 회복 없음

            Assert.AreEqual(1f, battle.GetUnit(1).moveGauge, 0.001f);
        }

        [Test]
        public void Gauge_RegensAfterDelay()
        {
            move.TryMove(1, new Coord(5, 7)); // 게이지 0, 지연 1초

            move.Tick(1f); // 지연 소화
            move.Tick(1f); // +1

            Assert.AreEqual(1f, battle.GetUnit(1).moveGauge, 0.001f);
            var (blue, _) = Ranges(1);
            Assert.AreEqual(4, blue.Count); // 1칸 거리만 파랑
        }

        [Test]
        public void YellowMove_SetsCooldown_AndLocksMovement()
        {
            var result = move.TryMove(1, new Coord(5, 8)); // 3칸 > 게이지 2 → 노랑

            Assert.IsTrue(result.success);
            Assert.IsTrue(result.isYellow);
            var unit = battle.GetUnit(1);
            Assert.AreEqual(profile.yellowCooldownSeconds, unit.moveCooldown, 0.001f);

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
            Assert.AreEqual(profile.freeRange, unit.moveGauge, 0.001f);
            var (blue, yellow) = Ranges(1);
            Assert.Greater(blue.Count, 0);   // 파랑+노랑 다 복귀
            Assert.Greater(yellow.Count, 0);
        }

        [Test]
        public void Gauge_ClampedAtFreeRange()
        {
            move.Tick(100f);
            Assert.AreEqual(profile.freeRange, battle.GetUnit(1).moveGauge, 0.001f);
        }

        [Test]
        public void PerUnitProfile_Overrides()
        {
            // 러너형: 파랑 3칸 / 최대 5칸
            var runner = new MoveProfile { freeRange = 3, maxRange = 5, gaugeRegenPerSecond = 1f };
            battle.AddUnit(new UnitState(7, team: 0, new Coord(9, 9)) { profile = runner });
            var sys = new MoveSystem(battle, config);

            var blue = new List<Coord>();
            var yellow = new List<Coord>();
            sys.GetRanges(7, blue, yellow);

            foreach (var c in blue) Assert.LessOrEqual(Coord.Manhattan(new Coord(9, 9), c), 3);
            foreach (var c in yellow)
            {
                Assert.Greater(Coord.Manhattan(new Coord(9, 9), c), 3);
                Assert.LessOrEqual(Coord.Manhattan(new Coord(9, 9), c), 5);
            }
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
