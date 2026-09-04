namespace SeoYuGi.Battle
{
    /// <summary>
    /// 매치 = 라운드 × 5, 3선승. 학습 커브(관찰→적용→조건부)는 R3에서 만렙 — R4~5는 만렙 AI 유지.
    /// 라운드 사이 브리핑·Predictor.SetRound는 바깥(러너)이 처리 — 여기는 스코어만.
    /// </summary>
    public class MatchSystem
    {
        public const int MaxRounds = 5;
        public const int WinsNeeded = 3;

        /// <summary>진행 중인 라운드 번호 1..3. 라운드 결과 기록 시 자동 전진.</summary>
        public int CurrentRound { get; private set; } = 1;

        /// <summary>-1 = 진행 중. 0/1 = 매치 승리 팀.</summary>
        public int MatchWinner { get; private set; } = -1;

        public bool IsOver => MatchWinner != -1;

        readonly int[] wins = new int[2];

        public int GetWins(int team) => wins[team];

        /// <summary>라운드 승리 팀 기록. 2선승 달성 시 즉시 매치 종료.</summary>
        public bool RecordRoundResult(int winnerTeam)
        {
            if (IsOver) return true;
            wins[winnerTeam]++;
            if (wins[winnerTeam] >= WinsNeeded)
                MatchWinner = winnerTeam;
            else
                CurrentRound++;
            return IsOver;
        }
    }
}
