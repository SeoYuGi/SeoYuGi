using System.Collections.Generic;
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

        /// <summary>거점을 2칸 패치로 — 1칸 거점은 한 칸에 한 명뿐이라 경합(추가시간)이 성립하지 않는다.</summary>
        RoundSystem NewWideRound()
        {
            var groups = new List<IEnumerable<Coord>>
            {
                new[] { zoneL, zoneL + Coord.Up },
                new[] { zoneM, zoneM + Coord.Up },
                new[] { zoneR, zoneR + Coord.Up },
            };
            return round = new RoundSystem(battle, config, groups);
        }

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
        public void Vacate_DecaysProgress()
        {
            // captureSeconds 2, decaySeconds 4 → 감소 속도 0.5/s
            var u = Add(1, 0, zoneM);
            Add(2, 1, new Coord(0, 0));
            NewRound();

            round.Tick(1.5f);
            MoveTo(u, zoneM + Coord.Up); // 이탈 — 즉시 리셋 아님, 서서히 감소
            round.Tick(4f);              // 1.5 게이지는 3초면 전부 빠짐
            Assert.AreEqual(0f, round.Zones[1].progress);
            Assert.AreEqual(-1, round.Zones[1].capturingTeam);

            MoveTo(u, zoneM);            // 복귀 — 처음부터 다시
            round.Tick(1.9f);
            Assert.AreEqual(-1, round.Zones[1].owner);
            round.Tick(0.2f);
            Assert.AreEqual(0, round.Zones[1].owner);
        }

        [Test]
        public void EnemyGauge_NeutralizedBeforeRecapture()
        {
            var a = Add(1, 0, zoneM);
            var b = Add(2, 1, new Coord(0, 0));
            NewRound();

            round.Tick(1.0f);            // 팀0 게이지 1.0
            MoveTo(a, new Coord(0, 1));
            MoveTo(b, zoneM);            // 팀1 진입 — 먼저 중화

            round.Tick(0.9f);            // 잔여 0.1 남음 — 아직 팀1 게이지 아님
            Assert.AreEqual(-1, round.Zones[1].owner);
            Assert.AreEqual(0, round.Zones[1].capturingTeam);

            round.Tick(1.2f);            // 0.1 중화 + 1.1 적립
            Assert.AreEqual(1, round.Zones[1].capturingTeam);
            Assert.AreEqual(-1, round.Zones[1].owner);

            round.Tick(1.0f);            // 누적 2.1 ≥ 2 — 탈환
            Assert.AreEqual(1, round.Zones[1].owner);
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
        public void AllZones_EnemyStandingOnZone_OvertimeHoldsRound()
        {
            Add(1, 0, zoneL);
            Add(2, 0, zoneM);
            var c = Add(3, 0, new Coord(7, 6)); // 우측 거점 옆에서 대기
            var e = Add(4, 1, new Coord(0, 0));
            NewWideRound();

            bool overtime = false;
            round.OnOvertime += on => overtime = on;

            round.Tick(2.1f);              // 좌·중 점거 (거점 2개)
            MoveTo(e, zoneL + Coord.Up);   // 적이 우리 거점 진입 — 경합으로 동결, 소유는 유지
            MoveTo(c, zoneR);
            round.Tick(2.1f);              // 우측 점거 → 독점이지만 적이 아직 거점 위

            Assert.AreEqual(0, round.Zones[0].owner); // 경합 중에도 소유는 그대로
            Assert.AreEqual(-1, round.Winner);        // 라운드 유지
            Assert.IsTrue(round.Overtime);
            Assert.IsTrue(overtime);
        }

        [Test]
        public void AllZones_EnemyLeavesZone_WinsImmediately()
        {
            Add(1, 0, zoneL);
            Add(2, 0, zoneM);
            var c = Add(3, 0, new Coord(7, 6));
            var e = Add(4, 1, new Coord(0, 0));
            NewWideRound();

            round.Tick(2.1f);
            MoveTo(e, zoneL + Coord.Up);
            MoveTo(c, zoneR);
            round.Tick(2.1f);
            Assert.IsTrue(round.Overtime);

            MoveTo(e, new Coord(0, 0)); // 발을 뗀다 → 즉시 승부
            round.Tick(0.1f);

            Assert.AreEqual(0, round.Winner);
            Assert.IsFalse(round.Overtime);
        }

        [Test]
        public void Overtime_Annihilation_StillWinsImmediately()
        {
            Add(1, 0, zoneL);
            Add(2, 0, zoneM);
            var c = Add(3, 0, new Coord(7, 6));
            var e = Add(4, 1, new Coord(0, 0));
            NewWideRound();

            round.Tick(2.1f);
            MoveTo(e, zoneL + Coord.Up);
            MoveTo(c, zoneR);
            round.Tick(2.1f);
            Assert.IsTrue(round.Overtime);

            e.alive = false;              // 거점 위에서 격파 — 추가시간에 막히면 안 된다
            battle.Grid.RemoveUnit(e.pos);
            round.Tick(0.1f);

            Assert.AreEqual(0, round.Winner);
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
        public void Patch_AnyCellCaptures()
        {
            var patch = new[] { new Coord(4, 4), new Coord(5, 4), new Coord(4, 5), new Coord(5, 5) };
            Add(1, 0, new Coord(5, 5)); // 패치 구석 칸
            Add(2, 1, new Coord(0, 0));
            round = new RoundSystem(battle, config, new[] { patch });

            round.Tick(2.1f);
            Assert.AreEqual(0, round.Zones[0].owner);
        }

        [Test]
        public void Patch_Contested_PausesProgress()
        {
            var patch = new[] { new Coord(4, 4), new Coord(5, 4) };
            var a = Add(1, 0, patch[0]);
            var b = Add(2, 1, new Coord(0, 0));
            round = new RoundSystem(battle, config, new[] { patch });

            round.Tick(1.5f);           // 팀0 진행 1.5
            MoveTo(b, patch[1]);        // 적 진입 — 경합
            round.Tick(5f);
            Assert.AreEqual(-1, round.Zones[0].owner); // 경합 중 탈환 없음

            MoveTo(b, new Coord(0, 0)); // 적 이탈 — 진행 재개 (리셋 아님)
            round.Tick(0.6f);
            Assert.AreEqual(0, round.Zones[0].owner);
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

        /// <summary>
        /// 중화 후 남은 시간 이월은 그 거점 안에서만 일어나야 한다.
        /// (회귀) 이월 계산이 Tick의 deltaTime 파라미터를 덮어써서, 앞 순번 거점이 중화되면
        /// 같은 프레임의 뒤 순번 거점들이 축소된 dt로 진행되던 버그.
        /// </summary>
        [Test]
        public void Neutralize_DoesNotStealTimeFromOtherZones()
        {
            var a = Add(1, 0, zoneL);            // 팀0 — 좌측 점거 중
            Add(2, 1, zoneM);                    // 팀1 — 중앙 점거 중
            var c = Add(3, 1, new Coord(0, 0));  // 팀1 — 나중에 좌측으로 진입
            NewRound();

            round.Tick(1f); // 좌/중앙 각각 1초 적립 (captureSeconds = 2)
            Assert.AreEqual(1f, round.Zones[0].progress, 1e-4f);
            Assert.AreEqual(1f, round.Zones[1].progress, 1e-4f);

            // 팀0이 좌측을 비우고 팀1이 진입 → 좌측이 '중화 후 이월' 경로를 탄다
            MoveTo(a, new Coord(8, 8));
            MoveTo(c, zoneL);

            round.Tick(1.5f);

            // 좌측에서 무슨 일이 있든 중앙은 온전한 1.5초를 받아 2초를 채워야 한다
            Assert.AreEqual(1, round.Zones[1].owner, "중앙 거점이 온전한 dt를 받지 못했다");
        }
    }
}
