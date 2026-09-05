using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoYuGi.Prediction
{
    /// 학습형 예측기. 코어는 Observe/InjectDecoy로 데이터를 주고,
    /// PredictNextCells로 결과를 읽는다. UnityEngine 비의존.
    /// (브리핑·감지 자막 등 학습 서사 출력은 2026-09-05 컨셉 선회로 제거 — 예측 사격용 코어만 남음)
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

        // ── 스타일 분류 + 카운터 전술 API (R2+ "습성 조건부 대응") ──────

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

    }
}
