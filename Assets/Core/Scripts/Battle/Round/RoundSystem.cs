using System;
using System.Collections.Generic;

namespace SeoYuGi.Battle
{
    [Serializable]
    public class RoundConfig
    {
        public float captureSeconds = 5f;  // 거점 점거 완료까지 시간
        public float captureStackBonus = 0.5f; // 패치 위 아군 1기 추가마다 점거 속도 +50% (2기 1.5배, 3기 2배). 경합 동결은 그대로
        public float decaySeconds = 4f;    // 비웠을 때 풀 게이지가 전부 빠지는 시간
        public float roundSeconds = 120f;  // 라운드 제한 (초과 시 판정)
    }

    /// <summary>거점 1개 — 다중 칸 패치 (탱고파이브식 넓은 거점). owner -1 = 미소유.</summary>
    public class Zone
    {
        public List<Coord> cells = new List<Coord>();
        public Coord Center;
        public int owner = -1;
        public int capturingTeam = -1;
        public float progress; // 0..captureSeconds
    }

    /// <summary>
    /// 거점(맵당 1~3개, 홀수) + 라운드 승패 (기획서 §03 ZN).
    /// 승리: ① 상대팀 전멸(즉시) ② 모든 거점 독점 — 단 상대가 거점을 밟고 있으면 추가시간으로 유지.
    /// 거점 1개 맵은 첫 점거가 곧 라운드(밟고 있던 상대는 이미 밀려난 상태).
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

        /// <summary>거점 독점했지만 상대가 아직 거점을 밟고 있어 라운드가 유지되는 중.</summary>
        public bool Overtime { get; private set; }

        public event Action<Zone> OnZoneCaptured;
        public event Action<bool> OnSuddenDeath; // true 고정 — HUD 연출용
        public event Action<bool> OnOvertime;    // 추가시간 진입/해제 — HUD 연출용
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

        /// <param name="zoneCellGroups">거점별 칸 목록 (좌→우). null이면 DefaultZoneCells 1칸 거점 3개.</param>
        public RoundSystem(BattleState state, RoundConfig config, IEnumerable<IEnumerable<Coord>> zoneCellGroups = null)
        {
            State = state;
            Config = config;
            if (zoneCellGroups == null)
            {
                var groups = new List<IEnumerable<Coord>>();
                foreach (var c in DefaultZoneCells(state.Grid.Width, state.Grid.Height))
                    groups.Add(new[] { c });
                zoneCellGroups = groups;
            }
            foreach (var group in zoneCellGroups)
            {
                var z = new Zone();
                int sx = 0, sy = 0;
                foreach (var c in group)
                {
                    z.cells.Add(c);
                    sx += c.x;
                    sy += c.y;
                }
                z.Center = new Coord(sx / z.cells.Count, sy / z.cells.Count);
                zones.Add(z);
            }

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
                // 이 거점이 쓸 진행량. 아래 '중화 후 이월'이 깎아도 다음 거점에 새면 안 되므로
                // deltaTime(프레임 전체 몫)을 건드리지 않고 거점마다 사본을 쓴다.
                float step = deltaTime;

                // 패치 위 팀별 주둔 수 — 여럿이 밟으면 더 빨리 찬다 (뭉치기 보상)
                int count0 = 0, count1 = 0;
                foreach (var cell in z.cells)
                {
                    int unitId = State.Grid.GetUnitAt(cell);
                    if (unitId == Cell.NoUnit) continue;
                    if (State.GetUnit(unitId).team == 0) count0++;
                    else count1++;
                }
                bool team0 = count0 > 0, team1 = count1 > 0;

                if (team0 && team1) continue; // 경합 — 진행 동결 (탱고파이브식)

                if (!team0 && !team1)
                {
                    Decay(z, step); // 비우면 즉시 리셋이 아니라 서서히 감소
                    continue;
                }

                int team = team0 ? 0 : 1;
                if (team == z.owner)
                {
                    Decay(z, step); // 주인이 지키면 적의 잔여 게이지가 빠진다
                    continue;
                }

                step *= 1f + (Math.Max(count0, count1) - 1) * Config.captureStackBonus; // 중화·점거 둘 다 인원 비례

                // 상대 잔여 게이지가 남아 있으면 먼저 중화 — 0이 된 뒤 내 게이지가 찬다
                if (z.capturingTeam != team && z.progress > 0f)
                {
                    z.progress -= step;
                    if (z.progress > 0f) continue;
                    step = -z.progress; // 남은 시간만큼 내 게이지로 이월
                }
                if (z.capturingTeam != team)
                {
                    z.capturingTeam = team;
                    z.progress = 0f;
                }
                z.progress += step;

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

        /// <summary>주둔자 없음/주인 방어 시 게이지 감소. 0이 되면 점거 팀 표시 해제.</summary>
        void Decay(Zone z, float deltaTime)
        {
            if (z.progress <= 0f) return;
            float rate = Config.decaySeconds > 0f ? Config.captureSeconds / Config.decaySeconds : float.MaxValue;
            z.progress -= deltaTime * rate;
            if (z.progress <= 0f)
            {
                z.progress = 0f;
                z.capturingTeam = -1;
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

            // 거점 독점 — 상대가 아직 거점을 밟고 있으면 추가시간: 발을 뗄 때까지 라운드 유지.
            // 교착은 스스로 풀린다 — 혼자 밟으면 그 거점을 뺏어 독점이 깨지고, 같이 밟으면 교전,
            // 그마저 길어지면 roundSeconds 타임아웃이 거점 수로 잘라준다.
            var owned = new int[2];
            foreach (var z in zones)
                if (z.owner >= 0) owned[z.owner]++;

            int monopoly = owned[0] == zones.Count ? 0 : owned[1] == zones.Count ? 1 : -1;
            if (monopoly >= 0 && !EnemyOnAnyZone(monopoly)) { SetOvertime(false); EndRound(monopoly); return; }
            SetOvertime(monopoly >= 0);

            // 시간 초과 판정: 거점 수 → 생존 수 → 서든데스
            if (!SuddenDeath && State.time >= Config.roundSeconds)
            {
                if (owned[0] != owned[1]) { EndRound(owned[0] > owned[1] ? 0 : 1); return; }
                if (alive[0] != alive[1]) { EndRound(alive[0] > alive[1] ? 0 : 1); return; }
                SuddenDeath = true;
                OnSuddenDeath?.Invoke(true);
            }
        }

        /// <summary>독점 팀의 상대가 거점 패치를 밟고 있나 — 추가시간 유지 조건.</summary>
        bool EnemyOnAnyZone(int team)
        {
            foreach (var z in zones)
                foreach (var cell in z.cells)
                {
                    int unitId = State.Grid.GetUnitAt(cell);
                    if (unitId == Cell.NoUnit) continue;
                    if (State.GetUnit(unitId).team != team) return true;
                }
            return false;
        }

        void SetOvertime(bool on)
        {
            if (Overtime == on) return;
            Overtime = on;
            OnOvertime?.Invoke(on);
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
