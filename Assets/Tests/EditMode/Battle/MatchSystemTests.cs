using NUnit.Framework;

namespace SeoYuGi.Battle.Tests
{
    /// <summary>매치 = 항상 3라운드 완주, 다수승 (R3 학습 절정 보장).</summary>
    public class MatchSystemTests
    {
        [Test]
        public void StartsAtRoundOne_NoWinner()
        {
            var m = new MatchSystem();
            Assert.AreEqual(1, m.CurrentRound);
            Assert.AreEqual(-1, m.MatchWinner);
            Assert.IsFalse(m.IsOver);
        }

        [Test]
        public void TwoStraightWins_StillPlaysRoundThree()
        {
            var m = new MatchSystem();
            Assert.IsFalse(m.RecordRoundResult(0));
            Assert.IsFalse(m.RecordRoundResult(0)); // 2:0이어도 매치 계속 — R3 보장
            Assert.AreEqual(3, m.CurrentRound);

            Assert.IsTrue(m.RecordRoundResult(1));  // 3라운드 종료 — 2:1 다수승
            Assert.AreEqual(0, m.MatchWinner);
        }

        [Test]
        public void SplitRounds_MajorityDecidesAfterThree()
        {
            var m = new MatchSystem();
            m.RecordRoundResult(0);
            m.RecordRoundResult(1);
            Assert.AreEqual(3, m.CurrentRound);
            Assert.IsFalse(m.IsOver);

            Assert.IsTrue(m.RecordRoundResult(1));
            Assert.AreEqual(1, m.MatchWinner);
            Assert.AreEqual(1, m.GetWins(0));
            Assert.AreEqual(2, m.GetWins(1));
        }

        [Test]
        public void Sweep_WinsThreeZero()
        {
            var m = new MatchSystem();
            m.RecordRoundResult(1);
            m.RecordRoundResult(1);
            Assert.IsTrue(m.RecordRoundResult(1));
            Assert.AreEqual(1, m.MatchWinner);
            Assert.AreEqual(3, m.GetWins(1));
        }

        [Test]
        public void RecordAfterOver_ChangesNothing()
        {
            var m = new MatchSystem();
            m.RecordRoundResult(0);
            m.RecordRoundResult(0);
            m.RecordRoundResult(0);

            Assert.IsTrue(m.RecordRoundResult(1)); // 이미 끝난 매치
            Assert.AreEqual(0, m.MatchWinner);
            Assert.AreEqual(0, m.GetWins(1));
        }
    }
}
