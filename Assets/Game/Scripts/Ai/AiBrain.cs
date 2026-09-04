using System;
using System.Collections.Generic;
using SeoYuGi.Prediction;

namespace SeoYuGi.Ai
{
    /// 슬롯 하나를 조종하는 뇌. 적팀 3기 + 아군 백필 팀원이 전부 이 클래스를 쓴다.
    /// 차이는 predictor 유무뿐 — 적팀 뇌에만 Predictor를 주면 "나를 학습하는 AI"가 되고,
    /// 아군 팀원 뇌는 null을 받아 순수 역할 스크립트로 돈다.
    /// 우선순위: 회피 > 클래스 스킬 > 일반공격 > 거점 이동 > AP 비축.
    public class AiBrain
    {
        private readonly int _actorId;
        private readonly AiConfig _cfg;
        private readonly Predictor _predictor; // 적팀 뇌만 보유, 아군 팀원은 null
        private float _nextDecisionTime;
        private float _nextAttackTime;         // 공격·스킬 페이싱 — AP 연타 방지
        private float _lastActiveTime;         // 마지막으로 뭔가 한 시각 — 프리징 감지
        private Cell _prevPos;                 // 직전 위치 — 옆걸음 왕복 방지
        private bool _hasPrevPos;

        public AiBrain(int actorId, AiConfig config, Predictor predictor = null)
        {
            _actorId = actorId;
            _cfg = config;
            _predictor = predictor;
        }

        /// 코어가 매 프레임(또는 주기적으로) 호출. None이면 아무것도 하지 않는다.
        public AiCommand Tick(IWorldView world)
        {
            if (world.Time < _nextDecisionTime) return AiCommand.None;

            var me = FindActor(world, _actorId);
            if (!me.Alive) return AiCommand.None;
            float ap = world.GetAp(_actorId);

            var cmd = Decide(world, me, ap);

            // 프리징 방지: 한동안 무행동 + 거점·고지대 위도 아니면 배회 한 걸음
            // (고지대 홀드는 카운터 전술의 자리 사수 — 배회로 새면 안 됨)
            if (cmd.Type == CommandType.None &&
                world.Time - _lastActiveTime > _cfg.IdleWanderAfter &&
                ap >= _cfg.CostMove && !OnAnyZonePatch(world, me.Pos) && !OnHighland(world, me.Pos))
            {
                var wander = WanderStep(world, me);
                if (wander.HasValue) cmd = AiCommand.Of(CommandType.Move, wander.Value);
            }

            if (cmd.Type != CommandType.None)
            {
                _lastActiveTime = world.Time;
                _nextDecisionTime = world.Time + _cfg.MinDecisionInterval + _cfg.AggressionDelay;
            }

            if (!_hasPrevPos || !_prevPos.Equals(me.Pos))
            {
                _prevPos = me.Pos; // 다음 판단에서 "방금 있던 칸" 회피용
                _hasPrevPos = true;
            }
            return cmd;
        }

        private AiCommand Decide(IWorldView world, ActorState me, float ap)
        {
            // 1) 회피 — 내 칸에 곧 떨어지는 적 예고. 반응 하한 + 클래스별 확률 (완벽 회피 금지)
            if (ShouldDodge(world, me))
            {
                var dodge = FindDodgeCell(world, me);
                if (dodge.HasValue && ap >= _cfg.CostMove)
                    return AiCommand.Of(CommandType.Move, dodge.Value);
                if (world.Round >= 2 && world.HasDecoy(_actorId))
                    return AiCommand.Of(CommandType.Decoy, me.Pos); // 장비라 AP 소모 없음
                if (ap >= _cfg.CostGuard)
                    return AiCommand.Of(CommandType.Guard, me.Pos);
            }

            // 1.5) 개막 카운터 (E) — 러시 습관 감지 시 시작 6초 안에 반복 진입로에 선제 설치 (R2+)
            if (_predictor != null && world.Round >= 2 && world.Time < 6f &&
                world.Time >= _nextAttackTime && ap >= _cfg.CostHeavy)
            {
                var human = FindHuman(world);
                if (human.HasValue && _predictor.GetStyle(human.Value.Id) == PlayStyle.ZoneRusher &&
                    _predictor.TryGetOpeningCell(human.Value.Id, out var openCell))
                {
                    var preCast = TryRangedCast(me, openCell);
                    if (preCast.Type != CommandType.None)
                    {
                        _nextAttackTime = world.Time + EffectiveAttackInterval(world);
                        return preCast;
                    }
                }
            }

            var target = PickTarget(world, me);

            // 2~3) 공격·스킬 — AttackInterval 페이싱 (AP를 한 번에 쏟아붓는 연타 방지)
            if (target.HasValue && world.Time >= _nextAttackTime)
            {
                var aim = AimCell(target.Value, out bool predictedAim); // 예측 칸(학습 전이면 현재 칸)
                bool predicted = predictedAim && !aim.Equals(target.Value.Pos); // 현재 칸과 다를 때만 "통수"
                var skill = TrySkill(world, me, ap, target.Value, aim);
                if (skill.Type != CommandType.None)
                {
                    _nextAttackTime = world.Time + EffectiveAttackInterval(world);
                    skill.Predicted = predicted;
                    return skill;
                }

                // 일반공격 — 예측 칸이 내 십자 인접이면 깐다.
                // 예비 AP는 스킬용 — 일반공격까지 막지 않는다 (근접 대치에서 수동적이 되는 문제)
                if (ap >= _cfg.CostAttack && IsOrthoAdjacent(me.Pos, aim))
                {
                    _nextAttackTime = world.Time + EffectiveAttackInterval(world);
                    return AiCommand.Of(CommandType.Attack, aim, predicted);
                }
            }

            // 3.5) 습성 카운터 전술 (D) — 스타일 파악되면 통수 포지셔닝 (R2+)
            if (_predictor != null && world.Round >= 2 && TryCounterTactic(world, me, out var counterStep))
            {
                if (counterStep.HasValue && ap >= _cfg.CostMove)
                    return AiCommand.Of(CommandType.Move, counterStep.Value);
                if (!counterStep.HasValue) return AiCommand.None; // 자리 사수 — 공격은 상위 우선순위가
            }

            // 4) 거점 이동
            var step = StepTowardBestZone(world, me);
            if (step.HasValue && ap >= _cfg.CostMove + _cfg.ReserveAp)
                return AiCommand.Of(CommandType.Move, step.Value);

            // 5) 비축
            return AiCommand.None;
        }

        // ── R1 = 살짝 바보 (기준선) — R2부터 본색. 학습의 낙차를 만드는 대비 장치 ──

        private float EffectiveDodgeChance(IWorldView world) =>
            _cfg.DodgeChance * (world.Round <= 1 ? 0.6f : 1f);

        private float EffectiveAttackInterval(IWorldView world) =>
            _cfg.AttackInterval * (world.Round <= 1 ? 1.35f : 1f);

        /// 습성 카운터: 러시형 유저 → 원거리가 고지대 선점해 점사.
        /// 고지형 유저 → 기동형이 유저 선호 고지대를 먼저 접수.
        /// true 반환 시: step=이동 한 걸음, step=null이면 현 위치 사수.
        private bool TryCounterTactic(IWorldView world, ActorState me, out Cell? step)
        {
            step = null;
            var human = FindHuman(world);
            if (!human.HasValue) return false;
            var style = _predictor.GetStyle(human.Value.Id);

            if (style == PlayStyle.ZoneRusher)
            {
                if (me.Class != ClassId.Sniper && me.Class != ClassId.Grenadier) return false;
                if (OnHighland(world, me.Pos)) return true; // 고지 점거 완료 — 점사 태세
                var high = NearestHighland(world, me.Pos);
                if (!high.HasValue) return false;
                step = GreedyStep(world, me, high.Value);
                return step.HasValue;
            }

            if (style == PlayStyle.HighlandHolder)
            {
                if (me.Class != ClassId.Assassin && me.Class != ClassId.Balance) return false;
                if (!_predictor.TryGetFavoriteHighland(human.Value.Id, out var fav)) return false;
                var favCell = new Cell(fav.X, fav.Y);
                if (me.Pos.Equals(favCell)) return true; // 유저 단골 고지 접수 완료 — 사수
                step = GreedyStep(world, me, favCell);
                return step.HasValue;
            }
            return false;
        }

        /// 원거리 계열이 지정 칸에 캐스팅 가능하면 예측 표식 달아서 반환.
        private AiCommand TryRangedCast(ActorState me, Cell cell)
        {
            if (cell.Equals(me.Pos)) return AiCommand.None;
            switch (me.Class)
            {
                case ClassId.Grenadier:
                    if (Manhattan(me.Pos, cell) <= _cfg.GrenadeRange)
                        return AiCommand.Of(CommandType.Heavy, cell, predicted: true);
                    break;
                case ClassId.Sniper:
                    if ((cell.X == me.Pos.X || cell.Y == me.Pos.Y) && Chebyshev(me.Pos, cell) <= _cfg.SnipeRange)
                        return AiCommand.Of(CommandType.Heavy, cell, predicted: true);
                    break;
            }
            return AiCommand.None;
        }

        private static ActorState? FindHuman(IWorldView world)
        {
            foreach (var a in world.Actors)
                if (a.IsHuman && a.Alive) return a;
            return null;
        }

        private bool OnHighland(IWorldView world, Cell pos)
        {
            foreach (var h in world.Highlands)
                if (h.Equals(pos)) return true;
            return false;
        }

        private Cell? NearestHighland(IWorldView world, Cell from)
        {
            Cell? best = null;
            int bd = int.MaxValue;
            foreach (var h in world.Highlands)
            {
                if (!world.IsWalkable(h) && !h.Equals(from)) continue; // 점유된 고지는 제외
                int d = Manhattan(from, h);
                if (d < bd) { bd = d; best = h; }
            }
            return best;
        }

        /// 범용 그리디 한 걸음 — 가까워지는 이웃 우선, 없으면 옆걸음(직전 칸 제외).
        private Cell? GreedyStep(IWorldView world, ActorState me, Cell targetCell)
        {
            int curDist = Manhattan(me.Pos, targetCell);
            Cell? best = null;
            Cell? sidestep = null;
            int bestDist = curDist;
            foreach (var n in OrthoNeighbors(me.Pos))
            {
                if (!world.IsWalkable(n) || IsThreatened(world, me.Team, n)) continue;
                int d = Manhattan(n, targetCell);
                if (d < bestDist) { bestDist = d; best = n; }
                else if (d == curDist && sidestep == null && !(_hasPrevPos && n.Equals(_prevPos)))
                    sidestep = n;
            }
            return best ?? sidestep;
        }

        private AiCommand TrySkill(IWorldView world, ActorState me, float ap, ActorState target, Cell aim)
        {
            if (ap < _cfg.CostHeavy) return AiCommand.None;

            switch (me.Class)
            {
                case ClassId.Tank:
                    // 강타: 예측 칸이 인접이면 후려친다 (밀침은 코어가 처리)
                    if (IsOrthoAdjacent(me.Pos, aim))
                        return AiCommand.Of(CommandType.Heavy, aim);
                    break;

                case ClassId.Balance:
                    // 돌파: 타겟이 같은 행/열 2칸 이내면 대시로 접촉
                    if ((target.Pos.X == me.Pos.X || target.Pos.Y == me.Pos.Y) &&
                        Chebyshev(me.Pos, target.Pos) <= 2)
                        return AiCommand.Of(CommandType.Heavy, target.Pos);
                    break;

                case ClassId.Assassin:
                    // 그림자 도약: 적 시야 밖일 때만 — 고스트를 남기지 않고 파고든다
                    if (Chebyshev(me.Pos, target.Pos) <= 3 &&
                        !world.IsVisibleTo(EnemyOf(me.Team), me.Pos))
                    {
                        var dest = BlinkCellToward(world, me.Pos, target.Pos);
                        if (dest.HasValue)
                            return AiCommand.Of(CommandType.Heavy, dest.Value);
                    }
                    break;

                case ClassId.Grenadier:
                    // 파열탄: 예측 칸이 사거리 안이면 십자 폭격
                    if (Manhattan(me.Pos, aim) <= _cfg.GrenadeRange && !aim.Equals(me.Pos))
                        return AiCommand.Of(CommandType.Heavy, aim);
                    break;

                case ClassId.Sniper:
                    // 조준 사격: 예측 칸과 행/열이 정렬됐을 때만 — 맞히는 것 자체가 예측
                    if ((aim.X == me.Pos.X || aim.Y == me.Pos.Y) &&
                        Chebyshev(me.Pos, aim) <= _cfg.SnipeRange && !aim.Equals(me.Pos))
                        return AiCommand.Of(CommandType.Heavy, aim);
                    break;
            }
            return AiCommand.None;
        }

        private ActorState? PickTarget(IWorldView world, ActorState me)
        {
            ActorState? best = null;
            float bestScore = float.MinValue;
            foreach (var a in world.Actors)
            {
                if (!a.Alive || a.Team == me.Team) continue;
                bool visible = world.IsVisibleTo(me.Team, a.Pos);
                if (!visible && !(a.IsHuman && _predictor != null)) continue; // 안개 속은 예측 가능한 인간만 노림
                float score = -Manhattan(me.Pos, a.Pos);
                if (a.IsHuman) score += _cfg.HumanTargetBonus;
                if (!visible) score -= 2f;
                score += (3 - a.Hp) * 0.5f; // 마무리 우선
                if (score > bestScore) { bestScore = score; best = a; }
            }
            return best;
        }

        private Cell AimCell(ActorState target, out bool predicted)
        {
            predicted = false;
            if (_predictor != null)
            {
                var preds = _predictor.PredictNextCells(target.Id, 1);
                if (preds.Count > 0)
                {
                    predicted = true;
                    return preds[0].Cell;
                }
            }
            return target.Pos;
        }

        private Cell? StepTowardBestZone(IWorldView world, ActorState me)
        {
            // 목표 거점: 미소유(중립·적) 우선 — 이미 딴 거점은 제외하고 다음으로 로테이션.
            // 전부 우리 것이면 가장 가까운 거점을 수비.
            bool anyNotOurs = false;
            foreach (var z in world.Zones)
                if (!(z.HasOwner && z.Owner == me.Team)) { anyNotOurs = true; break; }

            ZoneState? goal = null;
            float bestScore = float.MinValue;
            foreach (var z in world.Zones)
            {
                bool ours = z.HasOwner && z.Owner == me.Team;
                if (anyNotOurs && ours) continue; // 먹은 거점에 눌러앉지 말 것
                float score = -Manhattan(me.Pos, z.Cell);
                if (score > bestScore) { bestScore = score; goal = z; }
            }
            if (!goal.HasValue) return null;

            var patch = goal.Value.Cells ?? new[] { goal.Value.Cell };

            bool onPatch = false;
            foreach (var c in patch)
                if (c.Equals(me.Pos)) { onPatch = true; break; }

            if (onPatch)
            {
                // 경합(적도 패치 위) 중인데 공격각이 안 나오면 적에게 한 걸음 — 눌러앉기 교착 방지
                foreach (var a in world.Actors)
                {
                    if (!a.Alive || a.Team == me.Team) continue;
                    bool intruder = false;
                    foreach (var c in patch)
                        if (c.Equals(a.Pos)) { intruder = true; break; }
                    if (!intruder) continue;
                    if (IsOrthoAdjacent(me.Pos, a.Pos)) return null; // 이미 붙음 — 공격은 상위 우선순위 몫

                    Cell? approach = null;
                    int bd = Manhattan(me.Pos, a.Pos);
                    foreach (var n in OrthoNeighbors(me.Pos))
                    {
                        if (!world.IsWalkable(n) || IsThreatened(world, me.Team, n)) continue;
                        int d = Manhattan(n, a.Pos);
                        if (d < bd) { bd = d; approach = n; }
                    }
                    if (approach.HasValue) return approach;
                }
                return null; // 경합 없음 — 점거 유지
            }

            // 목표 = 비어 있는 가장 가까운 패치 칸 (아군끼리 분산 진입)
            Cell? targetCell = null;
            int bestD = int.MaxValue;
            foreach (var c in patch)
            {
                if (!world.IsWalkable(c)) continue; // 점유·벽 제외
                int d = Manhattan(me.Pos, c);
                if (d < bestD) { bestD = d; targetCell = c; }
            }
            if (!targetCell.HasValue) return null; // 패치 만석 — 밀치지 말고 대기

            // 더 가까워지는 이웃 우선. 없으면 같은 거리 옆걸음 — 오목한 벽 앞 영구 정지 방지.
            // 옆걸음은 직전 칸 제외 — 두 칸 왕복 진동 방지.
            int curDist = Manhattan(me.Pos, targetCell.Value);
            Cell? best = null;
            Cell? sidestep = null;
            int bestDist = curDist;
            foreach (var n in OrthoNeighbors(me.Pos))
            {
                if (!world.IsWalkable(n) || IsThreatened(world, me.Team, n)) continue;
                int d = Manhattan(n, targetCell.Value);
                if (d < bestDist) { bestDist = d; best = n; }
                else if (d == curDist && sidestep == null && !(_hasPrevPos && n.Equals(_prevPos)))
                    sidestep = n;
            }
            return best ?? sidestep;
        }

        private Cell? FindDodgeCell(IWorldView world, ActorState me)
        {
            foreach (var n in OrthoNeighbors(me.Pos))
                if (world.IsWalkable(n) && !IsThreatened(world, me.Team, n))
                    return n;
            return null;
        }

        /// 내 칸에 떨어질 예고 중 "반응 가능하고 + 주사위를 통과한" 게 있는가.
        /// 주사위는 (스트라이크, 액터)별 결정적 해시 — 같은 위협엔 항상 같은 판단 (판단 깜빡임 방지).
        private bool ShouldDodge(IWorldView world, ActorState me)
        {
            foreach (var t in world.Telegraphs)
            {
                if (t.Team == me.Team || !t.Cell.Equals(me.Pos)) continue;
                float lead = t.ImpactTime - world.Time;
                if (lead > _cfg.DodgeWindow) continue;      // 아직 여유 — 반응 안 함
                if (lead < _cfg.MinDodgeLead) continue;     // 너무 늦음 — 인간적 반응 한계
                uint h = (uint)(t.Cell.X * 73856093 ^ t.Cell.Y * 19349663
                                ^ (int)(t.ImpactTime * 997f) ^ _actorId * 83492791);
                if (h % 100 < EffectiveDodgeChance(world) * 100f) return true;
            }
            return false;
        }

        /// 목표가 없어 얼어붙었을 때 한 걸음 배회 — 시간 기반 의사난수로 방향 선택, 직전 칸은 회피.
        private Cell? WanderStep(IWorldView world, ActorState me)
        {
            Cell? fallback = null;
            int pick = (int)(world.Time * 3.7f) + _actorId;
            int seen = 0;
            foreach (var n in OrthoNeighbors(me.Pos))
            {
                if (!world.IsWalkable(n) || IsThreatened(world, me.Team, n)) continue;
                if (_hasPrevPos && n.Equals(_prevPos)) { fallback = n; continue; } // 왔던 길은 최후순위
                seen++;
                if (pick % seen == 0) fallback = n; // reservoir 흉내 — 결정적이면서 다양
                if (fallback == null) fallback = n;
            }
            return fallback;
        }

        private bool OnAnyZonePatch(IWorldView world, Cell pos)
        {
            foreach (var z in world.Zones)
            {
                var patch = z.Cells ?? new[] { z.Cell };
                foreach (var c in patch)
                    if (c.Equals(pos)) return true;
            }
            return false;
        }

        private bool IsThreatened(IWorldView world, TeamId myTeam, Cell cell)
        {
            foreach (var t in world.Telegraphs)
                if (t.Team != myTeam && t.Cell.Equals(cell) &&
                    t.ImpactTime - world.Time <= _cfg.DodgeWindow)
                    return true;
            return false;
        }

        private static ActorState FindActor(IWorldView world, int id)
        {
            foreach (var a in world.Actors)
                if (a.Id == id) return a;
            return default;
        }

        private static IEnumerable<Cell> OrthoNeighbors(Cell c)
        {
            yield return new Cell(c.X, c.Y + 1);
            yield return new Cell(c.X, c.Y - 1);
            yield return new Cell(c.X - 1, c.Y);
            yield return new Cell(c.X + 1, c.Y);
        }

        private Cell? BlinkCellToward(IWorldView world, Cell from, Cell target)
        {
            Cell? best = null;
            int bestDist = Manhattan(from, target);
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                {
                    if (Math.Abs(dx) + Math.Abs(dy) > 2 || (dx == 0 && dy == 0)) continue;
                    var c = new Cell(from.X + dx, from.Y + dy);
                    if (!world.IsWalkable(c)) continue;
                    int d = Manhattan(c, target);
                    if (d < bestDist) { bestDist = d; best = c; }
                }
            return best;
        }

        private static TeamId EnemyOf(TeamId team) =>
            team == TeamId.Human ? TeamId.Machine : TeamId.Human;

        private static bool IsOrthoAdjacent(Cell a, Cell b) => Manhattan(a, b) == 1;

        private static int Manhattan(Cell a, Cell b) =>
            Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        private static int Chebyshev(Cell a, Cell b) =>
            Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }
}
