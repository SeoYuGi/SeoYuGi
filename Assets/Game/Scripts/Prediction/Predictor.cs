using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoYuGi.Prediction
{
    /// 학습형 예측기. 코어는 Observe/InjectDecoy로 데이터를 주고,
    /// PredictNextCells/GetBriefing으로 결과를 읽는다. UnityEngine 비의존.
    public class Predictor
    {
        private readonly PredictionConfig _cfg;
        private readonly Dictionary<int, ActorPattern> _actors = new Dictionary<int, ActorPattern>();
        private readonly Dictionary<int, TeamId> _teams = new Dictionary<int, TeamId>();
        private readonly Dictionary<int, int> _roundMoveCount = new Dictionary<int, int>();
        private readonly Dictionary<TeamId, float> _decoyUntil = new Dictionary<TeamId, float>();
        private float _clock;
        private int _round = 1;

        public Predictor(PredictionConfig config = null)
        {
            _cfg = config ?? new PredictionConfig();
        }

        public int Round => _round;

        /// 1=관찰만(예측 안 내놓음), 2=빈도 학습, 3=문맥 조건부. 라운드 종속 상태도 초기화.
        public void SetRound(int round)
        {
            _round = Math.Max(1, Math.Min(3, round));
            _clock = 0f;
            foreach (var a in _actors.Values) a.ResetRoundState();
            _roundMoveCount.Clear();
            _decoyUntil.Clear();
        }

        public void Observe(ActionEvent e)
        {
            _clock = Math.Max(_clock, e.Time);
            _teams[e.ActorId] = e.Team;
            var p = GetPattern(e.ActorId);

            if (IsDecoyActive(e.Team)) return; // 디코이 중 데이터는 오염됐다고 보고 버림

            if (e.Type == ActionType.Move)
            {
                _roundMoveCount.TryGetValue(e.ActorId, out int idx);
                p.ObserveMove(e.From, e.To, e.Time, idx);
                _roundMoveCount[e.ActorId] = idx + 1;
            }
            else
            {
                // 이동 외 행동은 위치 추적만 갱신 (공격 습관 학습은 이후 확장 지점)
                p.CurCell = e.From;
                p.HasPosition = true;
            }
        }

        public void InjectDecoy(int actorId, float durationSec)
        {
            if (!_teams.TryGetValue(actorId, out var team)) return;
            _decoyUntil[team] = _clock + durationSec;
        }

        /// actorId의 다음 이동 칸 top-N. R1이거나 데이터 부족이면 빈 목록(= UI는 "분석 중" 표시).
        public IReadOnlyList<CellProb> PredictNextCells(int actorId, int topN = 3)
        {
            var result = new List<CellProb>();
            if (_round < 2) return result;
            if (!_actors.TryGetValue(actorId, out var p) || !p.HasPosition || p.ObservedMoves < 4)
                return result;

            int ctx = _round >= 3 ? p.ContextOf(p.CurCell) : ActorPattern.CtxOpen;
            float decoyMul = _teams.TryGetValue(actorId, out var team) && IsDecoyActive(team)
                ? _cfg.DecoyProbMultiplier : 1f;

            var scores = new List<CellProb>();
            float total = 0f;
            for (int dir = 0; dir < ActorPattern.DirCount; dir++)
            {
                var target = ActorPattern.Step(p.CurCell, dir);
                if (!p.InMap(target)) continue;

                float markov = RowProb(p, ctx, p.LastDir, dir);
                float visit = VisitProb(p, target);
                float zone = ZonePull(p.CurCell, target);
                float s = _cfg.MarkovWeight * markov + _cfg.VisitWeight * visit + _cfg.ZonePullWeight * zone;
                scores.Add(new CellProb(target, s));
                total += s;
            }
            if (total <= 0f) return result;

            foreach (var s in scores.OrderByDescending(c => c.Prob).Take(topN))
                result.Add(new CellProb(s.Cell, s.Prob / total * decoyMul));
            return result;
        }

        /// 라운드 간 브리핑 화면용 분석 문구.
        public string[] GetBriefing(int actorId)
        {
            if (!_actors.TryGetValue(actorId, out var p) || p.ObservedMoves < 4)
                return new[] { "데이터 수집 중... 아직 당신을 모릅니다." };

            var lines = new List<string>();

            int third = LaneThird(p, out float lanePct);
            string[] laneNames = { "좌측", "중앙", "우측" };
            lines.Add($"{laneNames[third]} 경로 선호 {(int)(lanePct * 100)}% — 해당 경로에 화력을 배치합니다.");

            if (p.FirstZoneEntryTime >= 0f && p.FirstZoneEntryTime < 15f)
                lines.Add($"개막 {p.FirstZoneEntryTime:0}초 만에 거점 직행 — 선점 저격을 준비합니다.");

            float predictability = Predictability(p);
            if (predictability > 0.55f)
                lines.Add($"이동 패턴 일치율 {(int)(predictability * 100)}% — 당신의 다음 수가 보입니다.");
            else
                lines.Add("이동 패턴이 불규칙합니다 — 표본을 더 수집합니다.");

            var opening = p.OpeningCells.OrderByDescending(kv => kv.Value).FirstOrDefault();
            if (opening.Value >= 2)
                lines.Add($"오프닝 경유지 {opening.Key} 반복 감지 — 초반 설치를 조정합니다.");

            return lines.ToArray();
        }

        private ActorPattern GetPattern(int actorId)
        {
            if (!_actors.TryGetValue(actorId, out var p))
            {
                p = new ActorPattern(_cfg);
                _actors[actorId] = p;
            }
            return p;
        }

        private bool IsDecoyActive(TeamId team) =>
            _decoyUntil.TryGetValue(team, out float until) && _clock < until;

        private static float RowProb(ActorPattern p, int ctx, int prevDir, int dir)
        {
            float row = 0f;
            for (int j = 0; j < ActorPattern.DirCount; j++) row += p.Trans[ctx, prevDir, j];
            return row <= 0f ? 1f / ActorPattern.DirCount : p.Trans[ctx, prevDir, dir] / row;
        }

        private float VisitProb(ActorPattern p, Cell target)
        {
            float sum = 0f, max = 0f;
            for (int x = 0; x < _cfg.MapWidth; x++)
                for (int y = 0; y < _cfg.MapHeight; y++)
                { sum += p.Visits[x, y]; max = Math.Max(max, p.Visits[x, y]); }
            if (max <= 0f) return 0f;
            return p.Visits[target.X, target.Y] / max;
        }

        private float ZonePull(Cell from, Cell to)
        {
            // 거점에 가까워지는 수를 약하게 가산 (모두가 공유하는 일반 상식 항)
            float best = 0f;
            foreach (var z in _cfg.ZoneCells)
            {
                int dFrom = Math.Abs(z.X - from.X) + Math.Abs(z.Y - from.Y);
                int dTo = Math.Abs(z.X - to.X) + Math.Abs(z.Y - to.Y);
                if (dTo < dFrom) best = Math.Max(best, 1f);
            }
            return best;
        }

        private int LaneThird(ActorPattern p, out float pct)
        {
            float[] lane = new float[3];
            float sum = 0f;
            int w = _cfg.MapWidth;
            for (int x = 0; x < w; x++)
                for (int y = 0; y < _cfg.MapHeight; y++)
                {
                    int t = Math.Min(2, x * 3 / w);
                    lane[t] += p.Visits[x, y];
                    sum += p.Visits[x, y];
                }
            int best = 0;
            for (int i = 1; i < 3; i++) if (lane[i] > lane[best]) best = i;
            pct = sum <= 0f ? 0f : lane[best] / sum;
            return best;
        }

        private static float Predictability(ActorPattern p)
        {
            // 각 prevDir 행에서 최대 확률의 가중 평균 = "얼마나 뻔한가"
            float acc = 0f, weight = 0f;
            for (int ctx = 0; ctx < 2; ctx++)
                for (int i = 0; i < ActorPattern.DirCount; i++)
                {
                    float row = 0f, max = 0f;
                    for (int j = 0; j < ActorPattern.DirCount; j++)
                    { row += p.Trans[ctx, i, j]; max = Math.Max(max, p.Trans[ctx, i, j]); }
                    if (row <= 0f) continue;
                    acc += max / row * row;
                    weight += row;
                }
            return weight <= 0f ? 0f : acc / weight;
        }
    }
}
