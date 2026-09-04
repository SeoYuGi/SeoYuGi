namespace SeoYuGi.Battle
{
    /// <summary>
    /// 매치 = 항상 라운드 3판 완주, 다수승 (기획서 §05 변형 — R3 학습 절정을 매번 보여준다).
    /// 라운드 사이 브리핑·Predictor.SetRound는 바깥(러너)이 처리 — 여기는 스코어만.
    /// </summary>
    public class MatchSystem
    {
        public const int MaxRounds = 3;
        public const int WinsNeeded = 2; // 다수승 기준 (표시용)

        /// <summary>진행 중인 라운드 번호 1..3. 라운드 결과 기록 시 자동 전진.</summary>
        public int CurrentRound { get; private set; } = 1;

        /// <summary>-1 = 진행 중. 0/1 = 매치 승리 팀.</summary>
        public int MatchWinner { get; private set; } = -1;

        public bool IsOver => MatchWinner != -1;

        readonly int[] wins = new int[2];

        public int GetWins(int team) => wins[team];

        /// <summary>라운드 승리 팀 기록. 3라운드를 모두 치른 뒤에만 매치 종료 (다수승).</summary>
        public bool RecordRoundResult(int winnerTeam)
        {
            if (IsOver) return true;
            wins[winnerTeam]++;
            if (CurrentRound >= MaxRounds)
                MatchWinner = wins[0] > wins[1] ? 0 : 1;
            else
                CurrentRound++;
            return IsOver;
        }
    }
}
