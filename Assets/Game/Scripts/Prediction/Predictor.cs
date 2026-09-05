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
                CheckDetections(e.ActorId, p);
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
            InjectDecoy(team, durationSec);
        }

        /// <summary>관찰 이력이 없는 유닛(아군 AI 등)도 팀 단위로 교란을 걸 수 있게.</summary>
        public void InjectDecoy(TeamId team, float durationSec)
        {
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
                return new[] { "아직 당신을 잘 모르겠습니다. 조금 더 지켜보겠습니다." };

            // 사람 말로 — "무엇을 봤고, 그래서 다음 판에 뭘 할 건지" 한 문장씩. 수치·좌표·분류 용어 금지 (2026-09-05).
            var lines = new List<string>();

            var style = GetStyle(actorId);
            if (style == PlayStyle.ZoneRusher)
                lines.Add("당신은 거점으로 곧장 달려드는 편이더군요. 다음 판엔 높은 곳에서 내려다보며 쏘겠습니다.");
            else if (style == PlayStyle.HighlandHolder)
                lines.Add("높은 자리를 좋아하시는군요. 다음 판엔 그 자리에 제가 먼저 가 있겠습니다.");

            int third = LaneThird(p, out float lanePct);
            string[] laneNames = { "왼쪽", "가운데", "오른쪽" };
            int outOfTen = Math.Max(1, (int)Math.Round(lanePct * 10));
            if (lanePct >= 0.5f)
                lines.Add($"열 번 중 {outOfTen}번은 {laneNames[third]} 길로 오셨습니다. 거기서 기다리겠습니다.");
            else
                lines.Add("길은 골고루 쓰시네요. 어디로 올지 아직 못 정했습니다.");

            if (p.FirstZoneEntryTime >= 0f && p.FirstZoneEntryTime < 15f)
                lines.Add($"시작 {p.FirstZoneEntryTime:0}초 만에 거점에 들어오셨습니다. 다음엔 들어오는 길목부터 겨누겠습니다.");

            float predictability = Predictability(p);
            if (predictability > 0.55f)
                lines.Add("움직임이 꽤 규칙적입니다. 다음에 어느 칸으로 갈지 대충 보입니다.");
            else
                lines.Add("움직임이 들쭉날쭉해서 아직 읽기 어렵습니다.");

            var opening = p.OpeningCells.OrderByDescending(kv => kv.Value).FirstOrDefault();
            if (opening.Value >= 2)
                lines.Add("시작할 때마다 같은 칸을 지나가시더군요. 거기에 미리 깔아두겠습니다.");

            return lines.ToArray();
        }

        // ── 스타일 분류 + 카운터 전술 API (R2+ "습성 조건부 대응") ──────

        /// HUD 학습 게이지용 0..1 — 표본 수(20수에 만충) 60% + 이동 패턴 일치율 40%.
        /// "AI가 나를 학습한다"를 상시 숫자로 보이게 (2026-09-05).
        public float LearningProgress(int actorId)
        {
            if (!_actors.TryGetValue(actorId, out var p)) return 0f;
            float samples = Math.Min(1f, p.ObservedMoves / 20f);
            return samples * 0.6f + Predictability(p) * 0.4f;
        }

        /// 유저 플레이 스타일. 표본 6수 미만이면 Unknown.
        public PlayStyle GetStyle(int actorId)
        {
            if (!_actors.TryGetValue(actorId, out var p) || p.ObservedMoves < 6)
                return PlayStyle.Unknown;
            // 고지 성향이 러시 성향보다 뚜렷하면 고지형 — 둘 다면 고지 우선 (더 특이한 습관)
            if (p.HighlandEntries >= 3 && p.HighlandEntries >= p.ZoneEntries)
                return PlayStyle.HighlandHolder;
            if ((p.FirstZoneEntryTime >= 0f && p.FirstZoneEntryTime < 15f) ||
                p.ZoneEntries / (float)p.ObservedMoves > 0.2f)
                return PlayStyle.ZoneRusher;
            return PlayStyle.Unknown;
        }

        /// 개막 반복 경유지 (2회 이상 반복된 첫 3수 칸). 없으면 false.
        public bool TryGetOpeningCell(int actorId, out Cell cell)
        {
            cell = default;
            if (!_actors.TryGetValue(actorId, out var p)) return false;
            int best = 1;
            foreach (var kv in p.OpeningCells)
                if (kv.Value > best) { best = kv.Value; cell = kv.Key; }
            return best >= 2;
        }

        /// 유저가 가장 자주 밟은 고지대. 표본 없으면 false.
        public bool TryGetFavoriteHighland(int actorId, out Cell cell)
        {
            cell = default;
            if (!_actors.TryGetValue(actorId, out var p)) return false;
            float best = 0.5f;
            foreach (var h in _cfg.HighlandCells)
            {
                if (!p.InMap(h)) continue;
                float v = p.Visits[h.X, h.Y];
                if (v > best) { best = v; cell = h; }
            }
            return best > 0.5f;
        }

        // ── 실시간 패턴 감지 (자막 연출용) — 감지 종류당 매치 1회 ──────

        private readonly HashSet<string> _firedDetections = new HashSet<string>();
        private readonly Queue<string> _detections = new Queue<string>();

        public bool TryDequeueDetection(out string message)
        {
            message = null;
            if (_detections.Count == 0) return false;
            message = _detections.Dequeue();
            return true;
        }

        private void CheckDetections(int actorId, ActorPattern p)
        {
            if (p.ObservedMoves < 6) return;

            if (p.FirstZoneEntryTime >= 0f && p.FirstZoneEntryTime < 15f)
                Fire(actorId, "rush", "거점으로 바로 달려드시는군요. 기억해 두겠습니다.");
            if (p.HighlandEntries >= 3)
                Fire(actorId, "high", "높은 자리를 좋아하시네요. 기억해 두겠습니다.");
            int third = LaneThird(p, out float lanePct);
            if (lanePct > 0.6f && p.ObservedMoves >= 10)
            {
                string[] laneNames = { "왼쪽", "가운데", "오른쪽" };
                Fire(actorId, "lane", $"자꾸 {laneNames[third]} 길로 오시네요. 기억해 두겠습니다.");
            }
        }

        private void Fire(int actorId, string key, string message)
        {
            if (_firedDetections.Add(actorId + ":" + key))
                _detections.Enqueue(message);
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
