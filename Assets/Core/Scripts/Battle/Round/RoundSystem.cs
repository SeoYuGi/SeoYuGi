using System;
using System.Collections.Generic;

namespace SeoYuGi.Battle
{
    [Serializable]
    public class RoundConfig
    {
        public float captureSeconds = 2f;  // 거점 칸 점거 유지 시간
        public float roundSeconds = 120f;  // 라운드 제한 (초과 시 판정)
    }

    /// <summary>거점 1개. owner -1 = 미소유.</summary>
    public class Zone
    {
        public Coord cell;
        public int owner = -1;
        public int capturingTeam = -1;
        public float progress; // 0..captureSeconds
    }

    /// <summary>
    /// 거점 3개 + 라운드 승패 (기획서 §03 ZN).
    /// 승리: ① 상대팀 전멸 ② 거점 3개 독점 (즉시 종료).
    /// 시간 초과: 거점 수 → 생존 수 → 서든데스(다음 탈환 or 킬 즉시 승부).
    /// 시계는 BattleState.time(CombatSystem이 전진), 점거 진행은 Tick의 dt로 계산.
    /// </summary>
    public class RoundSystem
    {
        public BattleState State { get; }
        public RoundConfig Config { get; }
        public IReadOnlyList<Zone> Zones => zones;

        /// <summary>-1 = 진행 중. 0/1 = 승리 팀.</summary>
        public int Winner { get; private set; } = -1;
        public bool SuddenDeath { get; private set; }

        public event Action<Zone> OnZoneCaptured;
        public event Action<bool> OnSuddenDeath; // true 고정 — HUD 연출용
        public event Action<int> OnRoundEnd;     // 승리 팀

        readonly List<Zone> zones = new List<Zone>();
        readonly int[] prevAlive = new int[2];

        /// <summary>거점 3자리: 중앙열 좌/중/우 — 맵 생성 시 벽 금지 예약에도 사용.</summary>
        public static List<Coord> DefaultZoneCells(int width, int height)
        {
            int midY = height / 2;
            return new List<Coord>
            {
                new Coord(1, midY),
                new Coord(width / 2, midY),
                new Coord(width - 2, midY)
            };
        }

        public RoundSystem(BattleState state, RoundConfig config)
        {
            State = state;
            Config = config;
            foreach (var c in DefaultZoneCells(state.Grid.Width, state.Grid.Height))
                zones.Add(new Zone { cell = c });

            CountAlive(prevAlive);
        }

        public void Tick(float deltaTime)
        {
            if (Winner != -1) return;
            TickZones(deltaTime);
            if (Winner != -1) return; // 서든데스 탈환 승리
            CheckWin();
        }

        void TickZones(float deltaTime)
        {
            foreach (var z in zones)
            {
                int unitId = State.Grid.GetUnitAt(z.cell);
                if (unitId == Cell.NoUnit)
                {
                    z.capturingTeam = -1; // 비우면 진행 리셋
                    z.progress = 0f;
                    continue;
                }

                int team = State.GetUnit(unitId).team;
                if (team == z.owner)
                {
                    z.capturingTeam = -1;
                    z.progress = 0f;
                    continue;
                }

                if (z.capturingTeam != team)
                {
                    z.capturingTeam = team;
                    z.progress = 0f;
                }
                z.progress += deltaTime;

                if (z.progress >= Config.captureSeconds)
                {
                    z.owner = team;
                    z.capturingTeam = -1;
                    z.progress = 0f;
                    OnZoneCaptured?.Invoke(z);
                    if (SuddenDeath) { EndRound(team); return; }
                }
            }
        }

        void CheckWin()
        {
            var alive = new int[2];
            CountAlive(alive);

            // 서든데스: 킬 즉시 승부
            if (SuddenDeath)
            {
                if (alive[0] < prevAlive[0] && alive[1] >= prevAlive[1]) { EndRound(1); return; }
                if (alive[1] < prevAlive[1] && alive[0] >= prevAlive[0]) { EndRound(0); return; }
            }
            prevAlive[0] = alive[0];
            prevAlive[1] = alive[1];

            // 전멸
            if (alive[0] == 0) { EndRound(1); return; }
            if (alive[1] == 0) { EndRound(0); return; }

            // 거점 3개 독점
            var owned = new int[2];
            foreach (var z in zones)
                if (z.owner >= 0) owned[z.owner]++;
            if (owned[0] == zones.Count) { EndRound(0); return; }
            if (owned[1] == zones.Count) { EndRound(1); return; }

            // 시간 초과 판정: 거점 수 → 생존 수 → 서든데스
            if (!SuddenDeath && State.time >= Config.roundSeconds)
            {
                if (owned[0] != owned[1]) { EndRound(owned[0] > owned[1] ? 0 : 1); return; }
                if (alive[0] != alive[1]) { EndRound(alive[0] > alive[1] ? 0 : 1); return; }
                SuddenDeath = true;
                OnSuddenDeath?.Invoke(true);
            }
        }

        void CountAlive(int[] counts)
        {
            counts[0] = counts[1] = 0;
            foreach (var u in State.Units)
                if (u.alive) counts[u.team]++;
        }

        void EndRound(int team)
        {
            Winner = team;
            OnRoundEnd?.Invoke(team);
        }
    }
}
