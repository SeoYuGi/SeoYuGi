using NUnit.Framework;

namespace SeoYuGi.Battle.Tests
{
    /// <summary>매치 = 5라운드 3선승.</summary>
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
        public void ThreeStraightWins_EndsMatchInThreeRounds()
        {
            var m = new MatchSystem();
            Assert.IsFalse(m.RecordRoundResult(0));
            Assert.IsFalse(m.RecordRoundResult(0));
            Assert.AreEqual(3, m.CurrentRound);
            Assert.IsTrue(m.RecordRoundResult(0)); // 3:0 — 즉시 종료
            Assert.AreEqual(0, m.MatchWinner);
            Assert.AreEqual(3, m.CurrentRound);    // 매치 종료 — 더 전진 안 함
        }

        [Test]
        public void SplitRounds_GoToRoundFive_ThenDecide()
        {
            var m = new MatchSystem();
            m.RecordRoundResult(0);
            m.RecordRoundResult(1);
            m.RecordRoundResult(0);
            m.RecordRoundResult(1);
            Assert.AreEqual(5, m.CurrentRound); // 2:2 — 최종 라운드
            Assert.IsFalse(m.IsOver);

            Assert.IsTrue(m.RecordRoundResult(1));
            Assert.AreEqual(1, m.MatchWinner);
            Assert.AreEqual(2, m.GetWins(0));
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
