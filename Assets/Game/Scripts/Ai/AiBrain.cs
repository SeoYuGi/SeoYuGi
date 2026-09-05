using System;
using System.Collections.Generic;
using SeoYuGi.Prediction;

namespace SeoYuGi.Ai
{
    /// 슬롯 하나를 조종하는 뇌. 적팀 3기 + 아군 백필 팀원이 전부 이 클래스를 쓴다.
    /// 차이는 predictor 유무뿐 — 적팀 뇌에만 Predictor를 주면 "나를 학습하는 AI"가 되고,
    /// 아군 팀원 뇌는 null을 받아 순수 역할 스크립트로 돈다.
    /// 우선순위: 회피 > 클래스 스킬 > 일반공격 > 거점 이동. (AP 삭제 — 페이싱은 쿨다운·AttackInterval)
    public class AiBrain
    {
        private readonly int _actorId;
        private readonly AiConfig _cfg;
        private readonly Predictor _predictor; // 적팀 뇌만 보유, 아군 팀원은 null
        private float _nextDecisionTime;
        private float _nextAttackTime;         // 공격·스킬 페이싱 — 연타 방지
        private float _dodgeReadyTime;         // 회피 쿨타임 — 옆걸음 무한 반복 방지 (근접전이 성립하게)
        private float _nextMoveTime;           // 이동 페이싱 — 한 걸음 뒤 잠깐 서서 판단 (제자리 왕복 방지)
        private bool _dodgeMove;               // 이번 판단의 Move가 회피인가 — 회피는 페이싱을 안 탄다
        private readonly Random _rng;          // 페이싱 지터 — 슬롯마다 다른 시드. 셋이 같은 박자로 기술 쏘는 모양새 방지
        private readonly Cell[] _recent = new Cell[3]; // 최근 밟은 칸 3개 — 옆걸음·배회가 되돌아가지 않게
        private int _recentCount;
        private float _lastActiveTime;         // 마지막으로 뭔가 한 시각 — 프리징 감지
        private Cell _prevPos;                 // 직전 위치 — 옆걸음 왕복 방지
        private bool _hasPrevPos;

        // 핑 지휘 (2026-09-05 연계 패스) — 아군 인간의 휠클릭 핑에 잠시 복종한다.
        // ▼(0)=집결 이동, !(1)=그 근처 적 집중 타겟. ?(2)는 명령 아님.
        private Cell _pingCell;
        private int _pingType = -1;
        private float _pingUntil = -1f;
        const float PingObeySeconds = 6f;

        public void CommandPing(Cell cell, int type, float now)
        {
            if (type == 2) return; // ? = 정보 공유일 뿐
            _pingCell = cell;
            _pingType = type;
            _pingUntil = now + PingObeySeconds;
        }

        // 지휘관 모드에서 플레이어가 내린 상시 명령. null이면 지휘 없음 = 완전 자율(기존 동작).
        private readonly SeoYuGi.Battle.CommandState _orders;

        public AiBrain(int actorId, AiConfig config, Predictor predictor = null,
            SeoYuGi.Battle.CommandState orders = null)
        {
            _actorId = actorId;
            _cfg = config;
            _predictor = predictor;
            _orders = orders;
            // 개막 위상 분산 — 전원 0초에 준비돼서 동시에 첫 기술을 쏘던 것. 슬롯별로 0~1.5×간격 만큼 엇갈려 시작.
            _rng = new Random(actorId * 7919 + 17);
            _nextAttackTime = (float)_rng.NextDouble() * config.AttackInterval * 1.5f;
            _nextDecisionTime = (float)_rng.NextDouble() * config.MinDecisionInterval;
        }

        /// 코어가 매 프레임(또는 주기적으로) 호출. None이면 아무것도 하지 않는다.
        public AiCommand Tick(IWorldView world)
        {
            if (world.Time < _nextDecisionTime) return AiCommand.None;

            var me = FindActor(world, _actorId);
            if (!me.Alive) return AiCommand.None;

            var cmd = Decide(world, me);

            // 프리징 워치독 (2026-09-05): 3초 넘게 아무것도(공격 포함) 안 했고 거점 위도 아니고 적도 안 붙었으면
            // 페이싱·고지 사수 다 무시하고 미소유 거점으로 한 걸음. "멀뚱히 서 있는 AI"를 구조적으로 없앤다.
            // 고지대에서 실제로 쏘고 있는 저격수는 공격이 활동으로 잡혀 여기 안 걸린다.
            if (cmd.Type == CommandType.None && world.Time - _lastActiveTime > 3f &&
                !OnAnyZonePatch(world, me.Pos) && !EnemyAdjacent(world, me))
            {
                var forced = StepTowardBestZone(world, me, ZoneDeficit(world, me.Team) > 0);
                if (forced.HasValue)
                {
                    cmd = AiCommand.Of(CommandType.Move, forced.Value);
                    _dodgeMove = true; // 이동 페이싱 면제
                }
            }

            // 프리징 방지: 한동안 무행동 + 거점·고지대 위도 아니면 배회 한 걸음
            // (고지대 홀드는 카운터 전술의 자리 사수 — 배회로 새면 안 됨)
            if (cmd.Type == CommandType.None &&
                world.Time - _lastActiveTime > _cfg.IdleWanderAfter &&
                !OnAnyZonePatch(world, me.Pos) && !OnHighland(world, me.Pos) && !EnemyAdjacent(world, me))
            {
                var wander = WanderStep(world, me);
                if (wander.HasValue) cmd = AiCommand.Of(CommandType.Move, wander.Value);
            }

            // 이동 페이싱 — 회피가 아닌 이동은 한 걸음마다 MoveInterval 쉰다. 매 판단마다 걸으면
            // 위협 칸이 켜졌다 꺼졌다 할 때 두 칸 사이를 왕복한다 (같은 자리 왔다갔다의 주범).
            if (cmd.Type == CommandType.Move)
            {
                if (!_dodgeMove && world.Time < _nextMoveTime) cmd = AiCommand.None;
                else _nextMoveTime = world.Time + _cfg.MoveInterval;
            }
            _dodgeMove = false;

            if (cmd.Type != CommandType.None)
            {
                _lastActiveTime = world.Time;
                _nextDecisionTime = world.Time + _cfg.MinDecisionInterval + _cfg.AggressionDelay;
            }

            if (!_hasPrevPos || !_prevPos.Equals(me.Pos))
            {
                _prevPos = me.Pos; // 다음 판단에서 "방금 있던 칸" 회피용
                _hasPrevPos = true;
                _recent[_recentCount % _recent.Length] = me.Pos;
                _recentCount++;
            }
            return cmd;
        }

        private AiCommand Decide(IWorldView world, ActorState me)
        {
            // 1) 회피 — 내 칸에 곧 떨어지는 적 예고. 반응 하한 + 클래스별 확률 + 쿨타임 (완벽 회피 금지)
            //    쿨타임 중엔 아예 안 피한다 — 첫 공격은 흘려도 연속 공격은 맞아야 근접전이 성립한다.
            if (world.Time >= _dodgeReadyTime && ShouldDodge(world, me))
            {
                var dodge = FindDodgeCell(world, me);
                if (dodge.HasValue)
                {
                    _dodgeReadyTime = world.Time + _cfg.DodgeCooldown;
                    _dodgeMove = true; // 회피는 이동 페이싱 면제 — 살아야 하니까
                    return AiCommand.Of(CommandType.Move, dodge.Value);
                }
                if (world.Round >= 2 && world.HasDecoy(_actorId))
                    return AiCommand.Of(CommandType.Decoy, me.Pos); // 해킹 — 게이지 만충 시
                // 방어는 기획에서 삭제(2026-09-05) — 못 피하면 그냥 맞는다
            }

            // 1.5) 개막 카운터 (E) — 러시 습관 감지 시 시작 6초 안에 반복 진입로에 선제 설치 (R2+)
            if (_predictor != null && world.Round >= 2 && world.Time < 6f &&
                world.Time >= _nextAttackTime)
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

            var order = _orders != null ? _orders.Get(_actorId) : SeoYuGi.Battle.UnitOrder.Free(_actorId);
            var target = PickTarget(world, me);

            // 지휘: "교전하지 마" — 먼저 쏘지 않는다. 회피·스킬 자체는 그대로라 맞으면 반응은 한다.
            if (order.stance == SeoYuGi.Battle.OrderStance.Evasive) target = null;

            // 2~3) 공격·스킬 — AttackInterval 페이싱 (연타 방지, 인간적 템포)
            if (target.HasValue && world.Time >= _nextAttackTime)
            {
                var aim = AimCell(world, target.Value, out bool predictedAim); // 반응 지연을 먹인 조준 칸
                bool predicted = predictedAim && !aim.Equals(target.Value.Pos); // 현재 칸과 다를 때만 "통수"
                var skill = TrySkill(world, me, target.Value, aim);
                if (skill.Type != CommandType.None)
                {
                    _nextAttackTime = world.Time + EffectiveAttackInterval(world);
                    skill.Predicted = predicted;
                    return skill;
                }

                // 일반공격 — 쿨다운 준비됐고 예측 칸이 내 클래스 공격 모양 안이면 깐다.
                if (world.CanAttack(_actorId) && RangeTemplates.Contains(RangeTemplates.BasicAttack(me.Class), me.Pos, aim))
                {
                    _nextAttackTime = world.Time + EffectiveAttackInterval(world);
                    return AiCommand.Of(CommandType.Attack, aim, predicted);
                }
            }

            // 패배 긴급 — 적이 거점을 더 쥐고 있으면 이대로 시간이 가면 진다. 대치·고지 사수·힐팩 다 접고 거점으로.
            // (거점 다 털리는데 근접 대치로 멀뚱히 서 있거나 저격수가 고지대에 눌러앉던 문제.)
            bool losing = ZoneDeficit(world, me.Team) > 0;

            // 3.1) 거리 유지 — 원거리 클래스는 적이 KeepDistance 안으로 붙으면 한 걸음 물러난다 (몸 사림).
            //      공격·스킬(넉백샷 포함)이 위에서 먼저 나가고, 쿨이면 물러난다. 패배 긴급이면 생략.
            if (!losing && _cfg.KeepDistance > 0)
            {
                var near = NearestEnemy(world, me, out int nearDist);
                if (near.HasValue && nearDist <= _cfg.KeepDistance)
                {
                    var away = StepAway(world, me, near.Value.Pos);
                    if (away.HasValue) return AiCommand.Of(CommandType.Move, away.Value);
                }
            }

            // 3.2) 근접 대치 — 적이 붙어 있으면 근접 클래스는 자리를 지킨다. 매 판단마다 거점으로 걸어 나가면
            //      플레이어가 쫓아다니는 술래잡기가 된다. 공격은 위 2~3단계가 쿨다운 돌 때 나간다.
            if (!losing && target.HasValue && IsMelee(me.Class) && IsOrthoAdjacent(me.Pos, target.Value.Pos))
                return AiCommand.None;

            // 3.5) 습성 카운터 전술 (D) — 스타일 파악되면 통수 포지셔닝 (R2+)
            if (!losing && _predictor != null && world.Round >= 2 && TryCounterTactic(world, me, out var counterStep))
            {
                if (counterStep.HasValue)
                    return AiCommand.Of(CommandType.Move, counterStep.Value);
                return AiCommand.None; // 자리 사수 — 공격은 상위 우선순위가
            }

            // 3.6) 핑 지휘 — ▼ 집결: 지휘가 힐 욕심보다 앞선다 (도착권 2칸이면 대기)
            if (!losing && _pingType == 0 && world.Time < _pingUntil && Chebyshev(me.Pos, _pingCell) > 2)
            {
                var pingStep = GreedyStep(world, me, _pingCell);
                if (pingStep.HasValue) return AiCommand.Of(CommandType.Move, pingStep.Value);
            }

            // 3.7) 힐팩 — HP가 상했고 근처에 있을 때만. 거점 플레이보다 앞서지만 회피·공격보다는 뒤.
            // "전술적으로 안 먹기"는 두 문턱으로: 손상(HealSeekMissingHp) + 거리(HealSeekRadius).
            var healStep = losing ? null : StepTowardHealPack(world, me);
            if (healStep.HasValue) return AiCommand.Of(CommandType.Move, healStep.Value);

            // 지휘: 목적지 — 명령이 있으면 거점 자동 선택 대신 명령을 따른다.
            // 경로 탐색·위협 회피는 GreedyStep(BFS)이 그대로 처리한다.
            if (order.goal != SeoYuGi.Battle.OrderGoal.Free)
            {
                var ordered = OrderedStep(world, me, order);
                if (ordered.HasValue) return AiCommand.Of(CommandType.Move, ordered.Value);
                return AiCommand.None; // 도착했거나 갈 수 없다 — 자리를 지킨다
            }

            // 4) 거점 이동
            var step = StepTowardBestZone(world, me, losing);
            if (step.HasValue)
                return AiCommand.Of(CommandType.Move, step.Value);

            // 5) 대기
            return AiCommand.None;
        }

        // ── R1 = 살짝 바보 (기준선) — R2부터 본색. 학습의 낙차를 만드는 대비 장치 ──

        private float EffectiveDodgeChance(IWorldView world) =>
            _cfg.DodgeChance * (world.Round <= 1 ? 0.6f : 1f);

        /// 공격 간격 ±30% 지터 — 고정 박자면 쿨이 같은 슬롯끼리 다시 동기화된다.
        private float EffectiveAttackInterval(IWorldView world) =>
            _cfg.AttackInterval * (world.Round <= 1 ? 1.15f : 1f) * (0.7f + (float)_rng.NextDouble() * 0.6f);

        /// <summary>
        /// 상시 명령이 가리키는 곳으로 한 걸음. 목적지 계산만 하고, 걷는 방법은 GreedyStep(BFS)에 맡긴다.
        /// 이미 도착했으면 null — 호출부가 그 자리를 지킨다.
        /// </summary>
        private Cell? OrderedStep(IWorldView world, ActorState me, SeoYuGi.Battle.UnitOrder order)
        {
            Cell? dest = null;
            switch (order.goal)
            {
                case SeoYuGi.Battle.OrderGoal.Zone:
                {
                    int i = 0;
                    foreach (var z in world.Zones)
                    {
                        if (i++ != order.zoneIndex) continue;
                        dest = z.Cell;
                        break;
                    }
                    break;
                }
                case SeoYuGi.Battle.OrderGoal.Highland:
                    dest = NearestHighland(world, me.Pos);
                    break;
                case SeoYuGi.Battle.OrderGoal.Regroup:
                {
                    var human = FindHuman(world);       // 지휘관 = 내가 조종하는 유닛
                    if (human.HasValue) dest = human.Value.Pos;
                    break;
                }
                case SeoYuGi.Battle.OrderGoal.Fallback:
                {
                    var human = FindHuman(world);       // 후퇴도 지휘관 쪽으로 — 별도 스폰 좌표를 뷰가 안 넘긴다
                    if (human.HasValue) dest = human.Value.Pos;
                    break;
                }
            }
            if (!dest.HasValue || dest.Value.Equals(me.Pos)) return null;
            return GreedyStep(world, me, dest.Value);
        }

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
                    if (RangeTemplates.Contains(RangeTemplates.SnipeRange, me.Pos, cell)) // 다이아 + 십자 끝 (코어 CanSnipe와 동일 모양)
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

        /// 목표 칸으로 한 걸음 — BFS 실제 경로 우선(벽·소품을 돌아간다), 경로 없으면 그리디 옆걸음 폴백.
        /// 맨해튼 그리디만 쓰던 시절엔 오목한 벽 앞에서 좌우 왕복만 하며 다음 거점에 영영 못 갔다 (2026-09-05).
        private Cell? GreedyStep(IWorldView world, ActorState me, Cell targetCell)
        {
            var path = PathStep(world, me.Pos, targetCell);
            if (path.HasValue)
                return IsThreatened(world, me.Team, path.Value) ? (Cell?)null : path; // 첫 걸음이 위협 칸이면 한 박자 대기

            int curDist = Manhattan(me.Pos, targetCell);
            Cell? best = null;
            Cell? sidestep = null;
            int bestDist = curDist;
            foreach (var n in OrthoNeighbors(me.Pos))
            {
                if (!world.IsWalkable(n) || IsThreatened(world, me.Team, n)) continue;
                int d = Manhattan(n, targetCell);
                if (d < bestDist) { bestDist = d; best = n; }
                else if (d == curDist && sidestep == null && !IsRecent(n))
                    sidestep = n;
            }
            return best ?? sidestep;
        }

        private readonly Dictionary<Cell, Cell> _bfsParent = new Dictionary<Cell, Cell>();
        private readonly Queue<Cell> _bfsQueue = new Queue<Cell>();

        /// from→to 최단 경로(BFS, 십자 이동)의 첫 걸음. 점유·벽은 막힘(목표 칸은 예외). 600칸 확장 상한.
        /// 경로 없으면 null.
        private Cell? PathStep(IWorldView world, Cell from, Cell to)
        {
            if (from.Equals(to)) return null;
            _bfsParent.Clear();
            _bfsQueue.Clear();
            _bfsParent[from] = from;
            _bfsQueue.Enqueue(from);
            int expanded = 0;
            while (_bfsQueue.Count > 0 && expanded++ < 600)
            {
                var cur = _bfsQueue.Dequeue();
                foreach (var n in OrthoNeighbors(cur))
                {
                    if (_bfsParent.ContainsKey(n)) continue;
                    if (!n.Equals(to) && !world.IsWalkable(n)) continue;
                    _bfsParent[n] = cur;
                    if (n.Equals(to))
                    {
                        var c = n;
                        while (!_bfsParent[c].Equals(from)) c = _bfsParent[c];
                        return c;
                    }
                    _bfsQueue.Enqueue(n);
                }
            }
            return null;
        }

        private AiCommand TrySkill(IWorldView world, ActorState me, ActorState target, Cell aim)
        {
            // 스킬 2개 체제 — 각(角)은 뇌가, 쿨은 world.CanSkill로 미리 확인. 쿨 중인 스킬을 던졌다 거부당하면
            // AttackInterval을 통째로 쉬어서 "굼뜬 AI"가 됐다 (2026-09-05). 준비된 스킬 없으면 일반공격으로 넘어간다.
            bool s1 = world.CanSkill(_actorId, 0), s2 = world.CanSkill(_actorId, 1);
            switch (me.Class)
            {
                case ClassId.Tank:
                    if (Chebyshev(me.Pos, aim) == 1) // 인접8 — 강타(스킬2, 피해2) 우선, 쿨이면 방패밀기(스킬1)
                    {
                        if (s2) return AiCommand.Of(CommandType.Heavy, aim, skillIndex: 1);
                        if (s1) return AiCommand.Of(CommandType.Heavy, aim, skillIndex: 0);
                    }
                    break;

                case ClassId.Balance:
                    // 비명 교란(스킬2): 인접8에 적이 2기 이상이면 광역 스턴
                    if (s2 && CountAdjacentEnemies(world, me) >= 2)
                        return AiCommand.Of(CommandType.Heavy, me.Pos, skillIndex: 1);
                    // 돌파(스킬1): 타겟이 같은 행/열 2칸 이내면 대시로 접촉
                    if (s1 && (target.Pos.X == me.Pos.X || target.Pos.Y == me.Pos.Y) &&
                        Chebyshev(me.Pos, target.Pos) <= 2)
                        return AiCommand.Of(CommandType.Heavy, target.Pos, skillIndex: 0);
                    break;

                case ClassId.Assassin:
                    // 발톱(스킬2): 이미 인접8이면 최고 딜
                    if (s2 && Chebyshev(me.Pos, aim) == 1)
                        return AiCommand.Of(CommandType.Heavy, aim, skillIndex: 1);
                    // 그림자 도약(스킬1): 적 시야 밖일 때만 — 고스트를 남기지 않고 파고든다
                    if (s1 && Chebyshev(me.Pos, target.Pos) <= 3 &&
                        !world.IsVisibleTo(EnemyOf(me.Team), me.Pos))
                    {
                        var dest = BlinkCellToward(world, me.Pos, target.Pos);
                        if (dest.HasValue)
                            return AiCommand.Of(CommandType.Heavy, dest.Value, skillIndex: 0);
                    }
                    break;

                case ClassId.Grenadier:
                    // 폭탄 배달(스킬2): 멀리 있는 예측 칸으로 비행 폭격 (진입 겸용)
                    if (s2 && Manhattan(me.Pos, aim) is > 2 and <= 4)
                        return AiCommand.Of(CommandType.Heavy, aim, skillIndex: 1);
                    // 파열탄(스킬1): 5×5 내 십자 폭격
                    if (s1 && Chebyshev(me.Pos, aim) <= 2 && !aim.Equals(me.Pos))
                        return AiCommand.Of(CommandType.Heavy, aim, skillIndex: 0);
                    break;

                case ClassId.Sniper:
                    // 넉백샷(스킬1): 붙으면 때리고 물러난다 — 카이팅
                    if (s1 && Chebyshev(me.Pos, target.Pos) == 1)
                        return AiCommand.Of(CommandType.Heavy, target.Pos, skillIndex: 0);
                    // 조준 사격(스킬2): 예측 칸이 다이아(4)+십자 끝(5) 안이고 내 팀 시야 안이면 (벽 LOS 근사)
                    if (s2 && RangeTemplates.Contains(RangeTemplates.SnipeRange, me.Pos, aim) && world.IsVisibleTo(me.Team, aim))
                        return AiCommand.Of(CommandType.Heavy, aim, skillIndex: 1);
                    break;
            }
            return AiCommand.None;
        }

        static int CountAdjacentEnemies(IWorldView world, ActorState me)
        {
            int n = 0;
            foreach (var a in world.Actors)
                if (a.Alive && a.Team != me.Team && Chebyshev(me.Pos, a.Pos) == 1) n++;
            return n;
        }

        private ActorState? PickTarget(IWorldView world, ActorState me)
        {
            var myOrder = _orders != null ? _orders.Get(_actorId) : SeoYuGi.Battle.UnitOrder.Free(_actorId);
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
                score += (3 - a.Hp) * _cfg.LowHpTargetWeight; // 마무리 우선
                score += (6 - a.MaxHp) * _cfg.FragileTargetWeight; // 유리몸 우선 (암살자)
                if (_cfg.RangedTargetBonus > 0f && (a.Class == ClassId.Sniper || a.Class == ClassId.Grenadier))
                    score += _cfg.RangedTargetBonus + (OnHighland(world, a.Pos) ? 1f : 0f); // 후방 저격·폭격, 고지대 위면 더
                if (a.Stunned) score += 4f; // 연계 — 스턴 걸린 적을 다 같이 두들긴다 (스턴 콤보 +1과 세트)
                if (_pingType == 1 && world.Time < _pingUntil && Chebyshev(a.Pos, _pingCell) <= 3)
                    score += 5f; // ! 핑 — 지휘관이 찍은 근처의 적 집중
                // 지휘관 핑 포커스 — 지목된 적은 보이는 한 최우선 (지휘관 모드 전용, _orders 없으면 무시)
                if (_orders != null && a.Id == _orders.FocusEnemyId && world.Time < _orders.FocusUntil)
                    score += 100f;
                // 무전 지명 타겟 ("저격수부터 노려") — 상시 명령이라 다음 명령·라운드 끝까지 유지
                if (a.Id == myOrder.focusEnemyId)
                    score += 100f;
                if (score > bestScore) { bestScore = score; best = a; }
            }
            return best;
        }

        private Cell AimCell(IWorldView world, ActorState target, out bool predicted)
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
            return LaggedPos(world, target); // 즉시 추적 금지 — 이게 회피 창을 만든다
        }

        // 조준 기억 — (조준에 반영된 칸, 대상의 현재 칸, 그 칸으로 옮긴 시각)
        private readonly Dictionary<int, (Cell aimed, Cell latest, float since)> _aimMemory
            = new Dictionary<int, (Cell, Cell, float)>();

        /// <summary>
        /// 반응 지연을 먹인 조준 칸. 대상이 움직이면 AimReactionDelay 만큼 지난 뒤에야 조준이 따라간다.
        /// 이게 없으면 플레이어가 착지하는 프레임에 그 칸으로 예고가 깔려 회피 자체가 성립하지 않는다.
        /// </summary>
        private Cell LaggedPos(IWorldView world, ActorState target)
        {
            if (!_aimMemory.TryGetValue(target.Id, out var m))
            {
                _aimMemory[target.Id] = (target.Pos, target.Pos, world.Time);
                return target.Pos;
            }
            if (!m.latest.Equals(target.Pos))
                m = (m.aimed, target.Pos, world.Time); // 방금 움직였다 — 지연 시계 재시작
            if (world.Time - m.since >= _cfg.AimReactionDelay)
                m.aimed = m.latest;                    // 지연이 지나면 조준이 따라잡는다
            _aimMemory[target.Id] = m;
            return m.aimed;
        }

        private Cell? StepTowardBestZone(IWorldView world, ActorState me, bool losing = false)
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
                if (TeammateNear(world, me, z.Cell, 3)) score += _cfg.CohesionBonus; // 뭉치기 — 아군이 붙은 거점을 선호 (클래스별: 서포터 높고 암살자 낮음)
                if (score > bestScore) { bestScore = score; goal = z; }
            }
            if (!goal.HasValue) return null;

            // 저격수 성향 — 거점을 직접 밟는 대신 그 근처(3칸) 고지대에 앉아 내려다본다. 지고 있을 땐 내려와 밟는다.
            if (_cfg.PreferHighlandPerch && !losing)
            {
                Cell? perch = null;
                int pd = int.MaxValue;
                foreach (var h in world.Highlands)
                {
                    if (Chebyshev(h, goal.Value.Cell) > 3) continue;
                    if (!h.Equals(me.Pos) && !world.IsWalkable(h)) continue; // 남이 앉아 있으면 제외
                    int d = Manhattan(me.Pos, h);
                    if (d < pd) { pd = d; perch = h; }
                }
                if (perch.HasValue)
                    return perch.Value.Equals(me.Pos) ? null : GreedyStep(world, me, perch.Value); // 도착했으면 자리 사수
            }

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

            return GreedyStep(world, me, targetCell.Value); // BFS 경로 — 벽을 돌아간다
        }

        /// 힐팩 추구: HP 손상이 문턱 이상이고 반경 안에 활성 힐팩이 있으면 가장 가까운 쪽으로 한 걸음.
        /// 조건 불충족(비활성 성향·풀피 근처·팩 멀거나 없음)이면 null → 상위가 거점 플레이로 넘어간다.
        private Cell? StepTowardHealPack(IWorldView world, ActorState me)
        {
            if (_cfg.HealSeekMissingHp <= 0) return null;
            if (me.MaxHp - me.Hp < _cfg.HealSeekMissingHp) return null; // 아직 멀쩡 — 안 먹는다
            if (world.HealPacks.Count == 0) return null;

            Cell? nearest = null;
            int bestD = int.MaxValue;
            foreach (var pack in world.HealPacks)
            {
                int d = Manhattan(me.Pos, pack);
                if (d > _cfg.HealSeekRadius) continue; // 너무 멀다 — 거점 플레이 우선
                if (d < bestD) { bestD = d; nearest = pack; }
            }
            if (!nearest.HasValue) return null;
            if (me.Pos.Equals(nearest.Value)) return null; // 이미 팩 위 — 코어가 회복 처리

            return GreedyStep(world, me, nearest.Value); // 위협 칸 회피 포함 한 걸음
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
                if (IsRecent(n)) { fallback = n; continue; } // 방금 왔던 길들은 최후순위
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

        /// 최근 밟은 칸(최대 3)인가 — 옆걸음·배회 왕복 방지.
        private bool IsRecent(Cell c)
        {
            int n = System.Math.Min(_recentCount, _recent.Length);
            for (int i = 0; i < n; i++)
                if (_recent[i].Equals(c)) return true;
            return false;
        }

        /// 가장 가까운 살아있는 적 (체비쇼프 거리). 없으면 null.
        private static ActorState? NearestEnemy(IWorldView world, ActorState me, out int dist)
        {
            ActorState? best = null;
            dist = int.MaxValue;
            foreach (var a in world.Actors)
            {
                if (!a.Alive || a.Team == me.Team) continue;
                int d = Chebyshev(me.Pos, a.Pos);
                if (d < dist) { dist = d; best = a; }
            }
            return best;
        }

        /// from 적에게서 멀어지는 이웃 한 걸음 — 걸을 수 있고 위협 없는 칸 중 거리가 늘어나는 곳. 없으면 null.
        private Cell? StepAway(IWorldView world, ActorState me, Cell from)
        {
            int cur = Manhattan(me.Pos, from);
            Cell? best = null;
            int bd = cur;
            foreach (var n in OrthoNeighbors(me.Pos))
            {
                if (!world.IsWalkable(n) || IsThreatened(world, me.Team, n)) continue;
                int d = Manhattan(n, from);
                if (d > bd) { bd = d; best = n; }
            }
            return best;
        }

        /// 살아있는 아군(나 제외)이 cell에서 radius 이내에 있는가 — 뭉치기 판단.
        private static bool TeammateNear(IWorldView world, ActorState me, Cell cell, int radius)
        {
            foreach (var a in world.Actors)
                if (a.Alive && a.Id != me.Id && a.Team == me.Team && Manhattan(a.Pos, cell) <= radius) return true;
            return false;
        }

        private static bool IsMelee(ClassId c) =>
            c == ClassId.Tank || c == ClassId.Balance || c == ClassId.Assassin;

        /// 적 소유 거점 수 − 내 팀 소유 거점 수. 양수면 타임아웃 판정에서 지는 쪽.
        private static int ZoneDeficit(IWorldView world, TeamId team)
        {
            int deficit = 0;
            foreach (var z in world.Zones)
                if (z.HasOwner) deficit += z.Owner == team ? -1 : 1;
            return deficit;
        }

        /// 살아있는 적이 십자 인접 칸에 있는가 — 근접 대치 판정.
        private static bool EnemyAdjacent(IWorldView world, ActorState me)
        {
            foreach (var a in world.Actors)
                if (a.Alive && a.Team != me.Team && IsOrthoAdjacent(me.Pos, a.Pos)) return true;
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
