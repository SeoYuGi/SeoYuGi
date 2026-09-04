using NUnit.Framework;

namespace SeoYuGi.Battle.Tests
{
    /// <summary>매치 = 3라운드 2선승 (기획서 §05).</summary>
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
        public void TwoStraightWins_EndsMatchInTwoRounds()
        {
            var m = new MatchSystem();
            Assert.IsFalse(m.RecordRoundResult(0));
            Assert.AreEqual(2, m.CurrentRound);
            Assert.IsTrue(m.RecordRoundResult(0)); // 2:0 — 즉시 종료
            Assert.AreEqual(0, m.MatchWinner);
            Assert.AreEqual(2, m.CurrentRound);    // 매치 종료 — 더 전진 안 함
        }

        [Test]
        public void SplitRounds_GoToRoundThree_ThenDecide()
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
        public void RecordAfterOver_ChangesNothing()
        {
            var m = new MatchSystem();
            m.RecordRoundResult(0);
            m.RecordRoundResult(0);

            Assert.IsTrue(m.RecordRoundResult(1)); // 이미 끝난 매치
            Assert.AreEqual(0, m.MatchWinner);
            Assert.AreEqual(0, m.GetWins(1));
        }
    }
}
