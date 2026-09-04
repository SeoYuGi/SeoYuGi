using NUnit.Framework;

namespace SeoYuGi.Battle.Tests
{
    /// <summary>거점 점유·탈환 + 라운드 승패 (기획서 §03 ZN).</summary>
    public class RoundSystemTests
    {
        BattleState battle;
        RoundConfig config;
        RoundSystem round;
        Coord zoneL, zoneM, zoneR;

        [SetUp]
        public void SetUp()
        {
            battle = new BattleState(new GridModel(new GridConfig { width = 9, height = 9 }));
            config = new RoundConfig { captureSeconds = 2f, roundSeconds = 120f };

            var cells = RoundSystem.DefaultZoneCells(9, 9);
            zoneL = cells[0]; zoneM = cells[1]; zoneR = cells[2]; // (1,4)(4,4)(7,4)
        }

        RoundSystem NewRound() => round = new RoundSystem(battle, config);

        UnitState Add(int id, int team, Coord pos)
        {
            var u = new UnitState(id, team, pos);
            battle.AddUnit(u);
            return u;
        }

        void MoveTo(UnitState u, Coord to)
        {
            battle.Grid.MoveOccupant(u.pos, to);
            u.pos = to;
        }

        [Test]
        public void Capture_AfterHoldingTwoSeconds()
        {
            var u = Add(1, 0, zoneM);
            Add(2, 1, new Coord(0, 0));
            NewRound();

            round.Tick(1.9f);
            Assert.AreEqual(-1, round.Zones[1].owner); // 아직

            round.Tick(0.2f);
            Assert.AreEqual(0, round.Zones[1].owner); // 탈환
            Assert.AreEqual(-1, round.Winner);        // 1개론 승리 아님
        }

        [Test]
        public void Vacate_ResetsProgress()
        {
            var u = Add(1, 0, zoneM);
            Add(2, 1, new Coord(0, 0));
            NewRound();

            round.Tick(1.5f);
            MoveTo(u, zoneM + Coord.Up); // 이탈
            round.Tick(0.1f);
            MoveTo(u, zoneM);            // 복귀 — 처음부터 다시

            round.Tick(1.9f);
            Assert.AreEqual(-1, round.Zones[1].owner);
            round.Tick(0.2f);
            Assert.AreEqual(0, round.Zones[1].owner);
        }

        [Test]
        public void Owner_KeepsZoneAfterLeaving()
        {
            var u = Add(1, 0, zoneM);
            Add(2, 1, new Coord(0, 0));
            NewRound();

            round.Tick(2.1f);
            MoveTo(u, zoneM + Coord.Up);
            round.Tick(5f);

            Assert.AreEqual(0, round.Zones[1].owner); // 비워도 소유 유지
        }

        [Test]
        public void Enemy_CanRecapture()
        {
            var a = Add(1, 0, zoneM);
            var b = Add(2, 1, new Coord(0, 0));
            NewRound();

            round.Tick(2.1f); // 팀0 소유
            MoveTo(a, zoneM + Coord.Up);
            MoveTo(b, zoneM);
            round.Tick(2.1f);

            Assert.AreEqual(1, round.Zones[1].owner);
        }

        [Test]
        public void AllThreeZones_WinsImmediately()
        {
            Add(1, 0, zoneL);
            Add(2, 0, zoneM);
            Add(3, 0, zoneR);
            Add(4, 1, new Coord(0, 0));
            NewRound();

            int winner = -1;
            round.OnRoundEnd += t => winner = t;
            round.Tick(2.1f);

            Assert.AreEqual(0, round.Winner);
            Assert.AreEqual(0, winner);
        }

        [Test]
        public void Annihilation_Wins()
        {
            Add(1, 0, new Coord(0, 0));
            var enemy = Add(2, 1, new Coord(8, 8));
            NewRound();

            enemy.alive = false;
            battle.Grid.RemoveUnit(enemy.pos);
            round.Tick(0.1f);

            Assert.AreEqual(0, round.Winner);
        }

        [Test]
        public void Timeout_MoreZones_Wins()
        {
            Add(1, 0, zoneL);
            Add(2, 1, new Coord(0, 0));
            NewRound();

            round.Tick(2.1f); // 팀0이 거점 1개
            battle.time = 120f;
            round.Tick(0.1f);

            Assert.AreEqual(0, round.Winner);
        }

        [Test]
        public void Timeout_ZoneTie_AliveDecides()
        {
            Add(1, 0, new Coord(0, 0));
            Add(2, 0, new Coord(2, 0));
            Add(3, 1, new Coord(8, 8));
            NewRound();

            battle.time = 120f;
            round.Tick(0.1f);

            Assert.AreEqual(0, round.Winner); // 거점 0:0, 생존 2:1
        }

        [Test]
        public void Timeout_FullTie_SuddenDeath_NextCaptureWins()
        {
            var a = Add(1, 0, new Coord(0, 0));
            Add(2, 1, new Coord(8, 8));
            NewRound();

            battle.time = 120f;
            round.Tick(0.1f);
            Assert.IsTrue(round.SuddenDeath);
            Assert.AreEqual(-1, round.Winner);

            MoveTo(a, zoneL);
            round.Tick(2.1f); // 서든데스 탈환 = 즉시 승리

            Assert.AreEqual(0, round.Winner);
        }

        [Test]
        public void SuddenDeath_KillWins()
        {
            Add(1, 0, new Coord(0, 0));
            Add(2, 0, new Coord(2, 0));
            var e1 = Add(3, 1, new Coord(8, 8));
            Add(4, 1, new Coord(6, 8));
            NewRound();

            battle.time = 120f;
            round.Tick(0.1f);
            Assert.IsTrue(round.SuddenDeath);

            e1.alive = false; // 킬 발생 (전멸 아님 — 팀1에 1기 남음)
            battle.Grid.RemoveUnit(e1.pos);
            round.Tick(0.1f);

            Assert.AreEqual(0, round.Winner);
        }
    }
}
