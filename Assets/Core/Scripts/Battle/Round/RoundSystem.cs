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

        /// <summary>거점 봉쇄 규칙으로 잠긴 거점은 false — 점령도 안 되고 승리 판정에서도 빠진다.</summary>
        public bool active = true;
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


        /// <summary>
        /// 추가시간 — 제한시간이 다 됐는데 거점 수도 생존 수도 같아 승패를 가릴 수 없을 때만 켜진다.
        /// 이 동안에는 다음 탈환이나 다음 킬이 곧바로 승부를 낸다.
        /// </summary>
        public bool Overtime { get; private set; }

        /// <summary>이번 라운드 규칙. null이면 평범한 라운드. 러너가 조립 때 꽂는다.</summary>
        public RoundRule Rule { get; set; }

        public event Action<Zone> OnZoneCaptured;
        public event Action<bool> OnOvertime;    // 추가시간 진입 — HUD 연출용
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
                if (!z.active) continue; // 봉쇄된 거점 — 밟아도 아무 일 없다
                if (z.owner >= 0 && Rule != null && Rule.NoTakebacks) continue; // 탈환 불가 — 주인이 굳었다

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
                    if (Rule != null) { Rule.OnZoneCaptured(zones.IndexOf(z)); SyncZoneActive(); }
                    OnZoneCaptured?.Invoke(z);
                    if (Overtime) { EndRound(team, EndReason.OvertimeCapture); return; } // 추가시간엔 탈환이 곧 승리
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

        /// <summary>라운드가 왜 끝났나 — 종료 연출·브리핑 문구용 (2026-09-05).</summary>
        public enum EndReason { None, Elimination, AllZones, TimeoutZones, TimeoutAlive, OvertimeKill, OvertimeCapture }
        public EndReason Reason { get; private set; } = EndReason.None;

        void CheckWin()
        {
            var alive = new int[2];
            CountAlive(alive);

            // 서든데스: 킬 즉시 승부
            if (Overtime) // 추가시간엔 킬이 곧 승부
            {
                if (alive[0] < prevAlive[0] && alive[1] >= prevAlive[1]) { EndRound(1, EndReason.OvertimeKill); return; }
                if (alive[1] < prevAlive[1] && alive[0] >= prevAlive[0]) { EndRound(0, EndReason.OvertimeKill); return; }
            }
            prevAlive[0] = alive[0];
            prevAlive[1] = alive[1];

            // 전멸
            if (alive[0] == 0) { EndRound(1, EndReason.Elimination); return; }
            if (alive[1] == 0) { EndRound(0, EndReason.Elimination); return; }

            // 거점 독점 — 활성 거점만 센다. 봉쇄된 거점을 세면 아무도 독점할 수 없어 라운드가 끝나지 않는다.
            var owned = new int[2];
            int activeZones = 0;
            foreach (var z in zones)
            {
                if (!z.active) continue;
                activeZones++;
                if (z.owner >= 0) owned[z.owner]++;
            }

            if (activeZones > 0 && owned[0] == activeZones) { EndRound(0, EndReason.AllZones); return; }
            if (activeZones > 0 && owned[1] == activeZones) { EndRound(1, EndReason.AllZones); return; }

            // 시간 초과 판정: 거점 수 → 생존 수 → 그래도 못 가리면 추가시간
            if (!Overtime && State.time >= Config.roundSeconds)
            {
                if (owned[0] != owned[1]) { EndRound(owned[0] > owned[1] ? 0 : 1, EndReason.TimeoutZones); return; }
                if (alive[0] != alive[1]) { EndRound(alive[0] > alive[1] ? 0 : 1, EndReason.TimeoutAlive); return; }

                // 제한시간이 다 됐는데 거점도 생존도 같다 = 무승부. 여기서만 연장한다.
                Overtime = true;
                OnOvertime?.Invoke(true);
            }
        }

        /// <summary>규칙의 활성 표를 거점에 반영. 봉쇄 규칙이 없으면 전부 활성.</summary>
        public void SyncZoneActive()
        {
            for (int i = 0; i < zones.Count; i++)
                zones[i].active = Rule == null || Rule.ZoneEnabled(i);
        }

        void CountAlive(int[] counts)
        {
            counts[0] = counts[1] = 0;
            foreach (var u in State.Units)
                if (u.alive) counts[u.team]++;
        }

        void EndRound(int team, EndReason reason = EndReason.None)
        {
            Reason = reason;
            Winner = team;
            OnRoundEnd?.Invoke(team);
        }
    }
}
