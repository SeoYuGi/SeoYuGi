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
        private float _lastDecoyTime = -999f;

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
            if (cmd.Type != CommandType.None)
                _nextDecisionTime = world.Time + _cfg.MinDecisionInterval + _cfg.AggressionDelay;
            return cmd;
        }

        private AiCommand Decide(IWorldView world, ActorState me, float ap)
        {
            // 1) 회피 — 내 칸에 곧 떨어지는 적 예고가 있으면 비키거나 막는다
            if (IsThreatened(world, me.Team, me.Pos))
            {
                var dodge = FindDodgeCell(world, me);
                if (dodge.HasValue && ap >= _cfg.CostMove)
                    return AiCommand.Of(CommandType.Move, dodge.Value);
                if (ap >= _cfg.CostGuard)
                    return AiCommand.Of(CommandType.Guard, me.Pos);
            }

            var target = PickTarget(world, me);

            // 2) 클래스 스킬
            if (target.HasValue)
            {
                var aim = AimCell(target.Value); // 예측 칸(학습 전이면 현재 칸)
                var skill = TrySkill(world, me, ap, target.Value, aim);
                if (skill.Type != CommandType.None) return skill;

                // 3) 일반공격 — 예측 칸이 내 십자 인접이면 깐다
                if (ap >= _cfg.CostAttack + _cfg.ReserveAp && IsOrthoAdjacent(me.Pos, aim))
                    return AiCommand.Of(CommandType.Attack, aim);
            }

            // 4) 거점 이동
            var step = StepTowardBestZone(world, me);
            if (step.HasValue && ap >= _cfg.CostMove + _cfg.ReserveAp)
                return AiCommand.Of(CommandType.Move, step.Value);

            // 5) 비축
            return AiCommand.None;
        }

        private AiCommand TrySkill(IWorldView world, ActorState me, float ap, ActorState target, Cell aim)
        {
            if (ap < _cfg.CostHeavy + _cfg.ReserveAp && me.Class != ClassId.Jammer) return AiCommand.None;

            switch (me.Class)
            {
                case ClassId.Sniper:
                    // 예측 칸과 행/열이 정렬됐을 때만 조준 — 맞히는 것 자체가 예측
                    if ((aim.X == me.Pos.X || aim.Y == me.Pos.Y) &&
                        Chebyshev(me.Pos, aim) <= _cfg.SnipeRange && !aim.Equals(me.Pos))
                        return AiCommand.Of(CommandType.Heavy, aim);
                    break;

                case ClassId.Runner:
                    // 돌파: 타겟이 같은 행/열 2칸 이내면 대시로 접촉
                    if ((target.Pos.X == me.Pos.X || target.Pos.Y == me.Pos.Y) &&
                        Chebyshev(me.Pos, target.Pos) <= 2)
                        return AiCommand.Of(CommandType.Heavy, target.Pos);
                    break;

                case ClassId.Jammer:
                    // 디코이: R2부터, 쿨다운마다 — 상대 학습이 유효해진 뒤에만 가치가 있음
                    if (world.Round >= 2 && ap >= _cfg.CostDecoy + _cfg.ReserveAp &&
                        world.Time - _lastDecoyTime >= _cfg.DecoyCooldown)
                    {
                        _lastDecoyTime = world.Time;
                        return AiCommand.Of(CommandType.Decoy, me.Pos);
                    }
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
                float score = -Manhattan(me.Pos, a.Pos);
                if (a.IsHuman) score += _cfg.HumanTargetBonus;
                score += (3 - a.Hp) * 0.5f; // 마무리 우선
                if (score > bestScore) { bestScore = score; best = a; }
            }
            return best;
        }

        private Cell AimCell(ActorState target)
        {
            if (_predictor != null)
            {
                var preds = _predictor.PredictNextCells(target.Id, 1);
                if (preds.Count > 0) return preds[0].Cell;
            }
            return target.Pos;
        }

        private Cell? StepTowardBestZone(IWorldView world, ActorState me)
        {
            ZoneState? goal = null;
            float bestScore = float.MinValue;
            foreach (var z in world.Zones)
            {
                bool ours = z.HasOwner && z.Owner == me.Team;
                float score = -Manhattan(me.Pos, z.Cell) + (ours ? -5f : 0f); // 미소유 우선
                if (score > bestScore) { bestScore = score; goal = z; }
            }
            if (!goal.HasValue || goal.Value.Cell.Equals(me.Pos)) return null;

            Cell? best = null;
            int bestDist = Manhattan(me.Pos, goal.Value.Cell);
            foreach (var n in OrthoNeighbors(me.Pos))
            {
                if (!world.IsWalkable(n) || IsThreatened(world, me.Team, n)) continue;
                int d = Manhattan(n, goal.Value.Cell);
                if (d < bestDist) { bestDist = d; best = n; }
            }
            return best;
        }

        private Cell? FindDodgeCell(IWorldView world, ActorState me)
        {
            foreach (var n in OrthoNeighbors(me.Pos))
                if (world.IsWalkable(n) && !IsThreatened(world, me.Team, n))
                    return n;
            return null;
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

        private static bool IsOrthoAdjacent(Cell a, Cell b) => Manhattan(a, b) == 1;

        private static int Manhattan(Cell a, Cell b) =>
            Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        private static int Chebyshev(Cell a, Cell b) =>
            Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }
}
