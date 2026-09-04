using System;
using System.Collections.Generic;

namespace SeoYuGi.Battle
{
    public enum ActDenied
    {
        None,
        Dead,      // 유닛 없음/사망
        NoAp,      // AP 부족
        BadTarget, // 사거리 밖 / 벽 / 잘못된 지정
        Cooldown,  // 스킬 쿨타임
        Locked     // 스턴·비행 중 행동 불가
    }

    /// <summary>설치 공격 예고 — 예고 후 판정, 즉발 없음(기획서 §03).</summary>
    public class TelegraphStrike
    {
        public int id;             // 매치 내 고유 — 네트워크 복제·판정 매칭용
        public int attackerId;
        public int team;
        public List<Coord> cells = new List<Coord>();
        public float impactTime;   // BattleState.time 기준 판정 시각
        public int damage;
        public Coord pushDir;      // 밀침 방향 (Zero = 없음)
        public int pushCells;
        public int wallBonusDamage; // 벽/맵 경계에 밀려 부딪히면 추가 피해
        public float stunSeconds;   // 비명 교란: 판정 시 스턴 부여
    }

    /// <summary>
    /// AP 경제 + 설치형 공격 + 클래스 스킬 2개 체제 (캐릭터 기획 v1.7).
    /// 순수 C# — 외부에서 Tick(dt) 호출. BattleState.time은 이 시스템이 단독 전진.
    /// 판정 순서 = 설치 순서 (결정론). 팀킬 없음. 방어는 기획 삭제됨.
    /// 즉발은 이동기(돌파·도약·넉백샷)만 — 타격·CC는 반드시 예고.
    /// </summary>
    public class CombatSystem
    {
        public BattleState State { get; }
        public CombatConfig Config { get; }
        public IReadOnlyList<TelegraphStrike> ActiveStrikes => strikes;

        public event Action<TelegraphStrike> OnTelegraph;
        public event Action<TelegraphStrike, bool> OnStrikeResolved; // (strike, hitAnything)
        public event Action<int, int, Coord> OnUnitDamaged;          // (unitId, damage, hitDir — Zero면 방향 없음)
        public event Action<int> OnUnitDied;
        public event Action<int, SkillKind> OnSkillCast; // (unitId, kind) — 성공 시
        public event Action<int, float> OnStunned;       // (unitId, seconds)
        public event Action<int> OnWallCrash;            // 밀침으로 벽/맵 경계 충돌

        readonly List<TelegraphStrike> strikes = new List<TelegraphStrike>();

        public CombatSystem(BattleState state, CombatConfig config)
        {
            State = state;
            Config = config;
            foreach (var unit in state.Units)
                unit.ap = config.apMax;
        }

        public void Tick(float deltaTime)
        {
            State.time += deltaTime;

            foreach (var unit in State.Units)
                if (unit.alive)
                    unit.ap = Math.Min(Config.apMax, unit.ap + Config.apRegenPerSecond * deltaTime);

            // 판정 시각 도달한 예고를 설치 순서대로 해석
            for (int i = 0; i < strikes.Count;)
            {
                if (strikes[i].impactTime <= State.time)
                {
                    var s = strikes[i];
                    strikes.RemoveAt(i);
                    Resolve(s);
                }
                else i++;
            }
        }

        // ── 상태 질의 (UI·AI 공용) ────────────────────────────────

        /// <summary>스턴·비행으로 행동 불가인가 (MoveSystem도 이걸 확인).</summary>
        public bool IsLocked(UnitState unit) =>
            unit.stunnedUntil >= State.time || unit.flyingUntil >= State.time;

        public bool IsFlying(UnitState unit) => unit.flyingUntil >= State.time;

        public float SkillCooldownRemaining(int unitId, int skillIndex)
        {
            var unit = State.GetUnit(unitId);
            if (unit == null) return 0f;
            return Math.Max(0f, unit.skillReadyAt[skillIndex] - State.time);
        }

        /// <summary>기본공격 사거리 판정 — 클래스별 모양 (범위 다이어그램 원본).</summary>
        public static bool InAttackShape(AttackShape shape, Coord from, Coord to)
        {
            int dx = Math.Abs(to.x - from.x), dy = Math.Abs(to.y - from.y);
            if (dx == 0 && dy == 0) return false;
            int cheb = Math.Max(dx, dy);
            switch (shape)
            {
                case AttackShape.Melee8: return cheb == 1;
                case AttackShape.Circle2: return cheb <= 2 && !(dx == 2 && dy == 2);
                case AttackShape.Square2: return cheb <= 2;
                default: return false;
            }
        }

        // ── 일반공격 ──────────────────────────────────────────────

        /// <summary>일반공격: 클래스별 모양 안 1칸 지정 → 예고 후 판정. 도약 버프 시 피해 +1.</summary>
        public ActDenied TryAttack(int unitId, Coord target)
        {
            var unit = State.GetUnit(unitId);
            if (unit == null || !unit.alive) return ActDenied.Dead;
            if (IsLocked(unit)) return ActDenied.Locked;
            if (unit.ap < Config.costAttack) return ActDenied.NoAp;
            var def = ClassCatalog.Get(unit.unitClass);
            if (!InAttackShape(def.attackShape, unit.pos, target)) return ActDenied.BadTarget;
            if (!State.Grid.IsWalkableTerrain(target)) return ActDenied.BadTarget;

            unit.ap -= Config.costAttack;
            int damage = Config.attackDamage
                + (unit.attackBuffUntil > State.time ? Config.blinkBuffBonus : 0);
            Place(new TelegraphStrike
            {
                attackerId = unitId,
                team = unit.team,
                cells = { target },
                impactTime = State.time + Config.attackTelegraphSeconds,
                damage = damage
            });
            return ActDenied.None;
        }

        // ── 스킬 (2개 체제: 인덱스 0/1, AP + 쿨타임 병행) ─────────

        /// <summary>구 API 호환 — 스킬1.</summary>
        public ActDenied TrySkill(int unitId, Coord target) => TrySkill(unitId, 0, target);

        public ActDenied TrySkill(int unitId, int skillIndex, Coord target)
        {
            var unit = State.GetUnit(unitId);
            if (unit == null || !unit.alive) return ActDenied.Dead;
            if (IsLocked(unit)) return ActDenied.Locked;

            var skill = ClassCatalog.Get(unit.unitClass).skills[skillIndex];
            if (unit.skillReadyAt[skillIndex] > State.time) return ActDenied.Cooldown;
            if (unit.ap < skill.apCost) return ActDenied.NoAp;

            ActDenied result;
            switch (skill.kind)
            {
                case SkillKind.ShieldPush: result = CastMeleeStrike(unit, target, skill, pushCells: 2, wallBonus: 1); break;
                case SkillKind.Smash: result = CastMeleeStrike(unit, target, skill, pushCells: 1, wallBonus: 0); break;
                case SkillKind.Dash: result = CastDash(unit, target, skill); break;
                case SkillKind.Scream: result = CastScream(unit, skill); break;
                case SkillKind.Blink: result = CastBlink(unit, target, skill); break;
                case SkillKind.Claw: result = CastMeleeStrike(unit, target, skill, pushCells: 0, wallBonus: 0); break;
                case SkillKind.Burst: result = CastBurst(unit, target, skill); break;
                case SkillKind.BombDeliver: result = CastBombDeliver(unit, target, skill); break;
                case SkillKind.KnockShot: result = CastKnockShot(unit, target, skill); break;
                case SkillKind.Snipe: result = CastSnipe(unit, target, skill); break;
                default: result = ActDenied.BadTarget; break;
            }
            if (result == ActDenied.None)
            {
                unit.ap -= skill.apCost;
                unit.skillReadyAt[skillIndex] = State.time + skill.cooldownSeconds;
                OnSkillCast?.Invoke(unitId, skill.kind);
            }
            return result;
        }

        // ── 스킬 구현 ─────────────────────────────────────────────

        /// <summary>인접8 단일 칸 예고 타격 — 방패밀기(밀침2·벽꿍), 강타(밀침1), 발톱(순수 딜).</summary>
        ActDenied CastMeleeStrike(UnitState unit, Coord target, SkillDef skill, int pushCells, int wallBonus)
        {
            if (!InAttackShape(AttackShape.Melee8, unit.pos, target)) return ActDenied.BadTarget;
            if (!State.Grid.IsWalkableTerrain(target)) return ActDenied.BadTarget;

            var d = target - unit.pos;
            Place(new TelegraphStrike
            {
                attackerId = unit.id,
                team = unit.team,
                cells = { target },
                impactTime = State.time + skill.telegraphSeconds,
                damage = skill.damage,
                pushDir = pushCells > 0 ? new Coord(Math.Sign(d.x), Math.Sign(d.y)) : Coord.Zero,
                pushCells = pushCells,
                wallBonusDamage = wallBonus
            });
            return ActDenied.None;
        }

        ActDenied CastDash(UnitState unit, Coord target, SkillDef skill)
        {
            var dir = UnitDir(unit.pos, target, skill.range);
            if (dir == Coord.Zero) return ActDenied.BadTarget;

            // 즉발 대시: 벽만 못 뚫고 유닛은 통과. 경로의 적은 피해 + 1칸 밀침(유닛당 1회).
            // 착지는 통과한 칸 중 가장 먼 빈 칸.
            var hitIds = new HashSet<int>();
            var landing = unit.pos;
            var probe = unit.pos;
            for (int step = 0; step < skill.range; step++)
            {
                var next = probe + dir;
                if (!State.Grid.IsWalkableTerrain(next)) break; // 벽·경계 정지

                int occupantId = State.Grid.GetUnitAt(next);
                if (occupantId != Cell.NoUnit)
                {
                    var occupant = State.GetUnit(occupantId);
                    if (occupant.team != unit.team && !hitIds.Contains(occupantId) && !IsFlying(occupant))
                    {
                        hitIds.Add(occupantId);
                        Damage(occupant, skill.damage, dir);
                        if (occupant.alive) Push(occupant, dir, 1, 0);
                    }
                    probe = next;
                    if (State.Grid.GetUnitAt(next) == Cell.NoUnit) landing = next; // 밀려나 비면 착지 후보
                    continue; // 통과
                }
                probe = next;
                landing = next;
            }
            if (landing == unit.pos && hitIds.Count == 0) return ActDenied.BadTarget; // 아무 일도 없음

            if (landing != unit.pos)
            {
                State.Grid.MoveOccupant(unit.pos, landing);
                unit.pos = landing;
            }
            return ActDenied.None;
        }

        ActDenied CastScream(UnitState unit, SkillDef skill)
        {
            // 인접8 전부 예고 (벽 칸 제외 — 벽은 통과 못함). 판정 시 적에게 스턴.
            var strike = new TelegraphStrike
            {
                attackerId = unit.id,
                team = unit.team,
                impactTime = State.time + skill.telegraphSeconds,
                damage = skill.damage,
                stunSeconds = skill.stunSeconds
            };
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                var c = new Coord(unit.pos.x + dx, unit.pos.y + dy);
                if (State.Grid.IsWalkableTerrain(c)) strike.cells.Add(c);
            }
            if (strike.cells.Count == 0) return ActDenied.BadTarget;
            Place(strike);
            return ActDenied.None;
        }

        ActDenied CastBlink(UnitState unit, Coord target, SkillDef skill)
        {
            // 5×5 (체비쇼프 2) 내 아무 빈 칸으로 점멸 — 범위 다이어그램 원본
            var d = target - unit.pos;
            if (Math.Max(Math.Abs(d.x), Math.Abs(d.y)) > skill.range || d == Coord.Zero) return ActDenied.BadTarget;
            if (!State.Grid.IsWalkable(target)) return ActDenied.BadTarget;

            State.Grid.MoveOccupant(unit.pos, target); // 점멸 — 벽 무시 순간이동
            unit.pos = target;
            unit.attackBuffUntil = State.time + Config.blinkBuffSeconds;
            return ActDenied.None;
        }

        ActDenied CastBurst(UnitState unit, Coord target, SkillDef skill)
        {
            // 5×5 (체비쇼프 2, 자기 칸 포함 가능 — 자폭 피해 없음) 지정 → 십자 5칸
            var d = target - unit.pos;
            if (Math.Max(Math.Abs(d.x), Math.Abs(d.y)) > skill.range) return ActDenied.BadTarget;
            if (!State.Grid.IsWalkableTerrain(target)) return ActDenied.BadTarget;

            var strike = new TelegraphStrike
            {
                attackerId = unit.id,
                team = unit.team,
                impactTime = State.time + skill.telegraphSeconds,
                damage = skill.damage
            };
            strike.cells.Add(target);
            foreach (var dir in Coord.Directions4)
            {
                var c = target + dir;
                if (State.Grid.IsWalkableTerrain(c)) strike.cells.Add(c);
            }
            Place(strike);
            return ActDenied.None;
        }

        ActDenied CastBombDeliver(UnitState unit, Coord target, SkillDef skill)
        {
            // 맨해튼 4 내 빈 칸으로 비행 이동 + 착지 십자 폭격. 비행 중 무적·행동 불가.
            if (Coord.Manhattan(unit.pos, target) > skill.range || target == unit.pos) return ActDenied.BadTarget;
            if (!State.Grid.IsWalkable(target)) return ActDenied.BadTarget;

            State.Grid.MoveOccupant(unit.pos, target); // 착지 칸 즉시 점유(충돌 방지) — 비행 연출은 뷰 소관
            unit.pos = target;
            unit.flyingUntil = State.time + skill.telegraphSeconds;

            var strike = new TelegraphStrike
            {
                attackerId = unit.id,
                team = unit.team,
                impactTime = State.time + skill.telegraphSeconds,
                damage = skill.damage
            };
            strike.cells.Add(target);
            foreach (var dir in Coord.Directions4)
            {
                var c = target + dir;
                if (State.Grid.IsWalkableTerrain(c)) strike.cells.Add(c);
            }
            Place(strike);
            return ActDenied.None;
        }

        ActDenied CastKnockShot(UnitState unit, Coord target, SkillDef skill)
        {
            // 인접8 적에게 즉발 피해 + 본인이 반대 방향 2칸 후퇴 (벽 막힘, 낙하 자기 부담)
            if (!InAttackShape(AttackShape.Melee8, unit.pos, target)) return ActDenied.BadTarget;
            int victimId = State.Grid.GetUnitAt(target);
            if (victimId == Cell.NoUnit) return ActDenied.BadTarget;
            var victim = State.GetUnit(victimId);
            if (victim.team == unit.team || IsFlying(victim)) return ActDenied.BadTarget;

            var d = target - unit.pos;
            var dir = new Coord(Math.Sign(d.x), Math.Sign(d.y));
            Damage(victim, skill.damage, dir);
            unit.ap = Math.Min(Config.apMax, unit.ap + Config.hitRefund); // 즉발 명중도 예측 성공 취급
            Push(unit, new Coord(-dir.x, -dir.y), 2, 0); // 셀프 넉백 — Push가 낙하·막힘 처리
            return ActDenied.None;
        }

        ActDenied CastSnipe(UnitState unit, Coord target, SkillDef skill)
        {
            var dir = UnitDir(unit.pos, target, skill.range);
            if (dir == Coord.Zero) return ActDenied.BadTarget;

            var strike = new TelegraphStrike
            {
                attackerId = unit.id,
                team = unit.team,
                impactTime = State.time + skill.telegraphSeconds,
                damage = skill.damage
            };
            foreach (var c in SnipeLine(unit, dir, skill.range))
                strike.cells.Add(c);
            if (strike.cells.Count == 0) return ActDenied.BadTarget;

            Place(strike);
            return ActDenied.None;
        }

        // ── 조준 미리보기 (상태 변경 없음 — 뷰 전용 쿼리, AP 검사 안 함) ──

        /// <summary>일반공격 조준 가능 칸 — 클래스별 모양.</summary>
        public void GetAttackRange(int unitId, List<Coord> cells)
        {
            cells.Clear();
            var unit = State.GetUnit(unitId);
            if (unit == null || !unit.alive) return;
            var shape = ClassCatalog.Get(unit.unitClass).attackShape;
            for (int dx = -2; dx <= 2; dx++)
            for (int dy = -2; dy <= 2; dy++)
            {
                var c = new Coord(unit.pos.x + dx, unit.pos.y + dy);
                if (InAttackShape(shape, unit.pos, c) && State.Grid.IsWalkableTerrain(c))
                    cells.Add(c);
            }
        }

        /// <summary>일반공격을 hover로 발사하면 맞는 칸. 유효 조준이면 true.</summary>
        public bool GetAttackImpact(int unitId, Coord hover, List<Coord> cells)
        {
            cells.Clear();
            var unit = State.GetUnit(unitId);
            if (unit == null || !unit.alive) return false;
            var shape = ClassCatalog.Get(unit.unitClass).attackShape;
            if (!InAttackShape(shape, unit.pos, hover) || !State.Grid.IsWalkableTerrain(hover)) return false;
            cells.Add(hover);
            return true;
        }

        /// <summary>구 API 호환 — 스킬1 조준 범위.</summary>
        public void GetSkillRange(int unitId, List<Coord> cells) => GetSkillRange(unitId, 0, cells);

        /// <summary>스킬 조준 가능 칸 — SkillKind별 사거리 모양.</summary>
        public void GetSkillRange(int unitId, int skillIndex, List<Coord> cells)
        {
            cells.Clear();
            var unit = State.GetUnit(unitId);
            if (unit == null || !unit.alive) return;
            var skill = ClassCatalog.Get(unit.unitClass).skills[skillIndex];
            switch (skill.kind)
            {
                case SkillKind.ShieldPush:
                case SkillKind.Smash:
                case SkillKind.Claw:
                case SkillKind.Scream:
                case SkillKind.KnockShot:
                    for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var c = new Coord(unit.pos.x + dx, unit.pos.y + dy);
                        if (State.Grid.IsWalkableTerrain(c)) cells.Add(c);
                    }
                    break;
                case SkillKind.Dash:
                    foreach (var dir in Coord.Directions4)
                        for (int i = 1; i <= skill.range; i++)
                        {
                            var c = unit.pos + new Coord(dir.x * i, dir.y * i);
                            if (!State.Grid.IsWalkableTerrain(c)) break;
                            cells.Add(c);
                        }
                    break;
                case SkillKind.Blink:
                case SkillKind.Burst:
                    for (int dx = -skill.range; dx <= skill.range; dx++)
                    for (int dy = -skill.range; dy <= skill.range; dy++)
                    {
                        var c = new Coord(unit.pos.x + dx, unit.pos.y + dy);
                        if (c == unit.pos && skill.kind == SkillKind.Blink) continue;
                        bool ok = skill.kind == SkillKind.Blink
                            ? State.Grid.IsWalkable(c)
                            : State.Grid.IsWalkableTerrain(c);
                        if (ok) cells.Add(c);
                    }
                    break;
                case SkillKind.BombDeliver:
                    for (int dx = -skill.range; dx <= skill.range; dx++)
                    for (int dy = -skill.range; dy <= skill.range; dy++)
                    {
                        if (Math.Abs(dx) + Math.Abs(dy) > skill.range || (dx == 0 && dy == 0)) continue;
                        var c = new Coord(unit.pos.x + dx, unit.pos.y + dy);
                        if (State.Grid.IsWalkable(c)) cells.Add(c);
                    }
                    break;
                case SkillKind.Snipe:
                    foreach (var dir in Coord.Directions4)
                        cells.AddRange(SnipeLine(unit, dir, skill.range));
                    break;
            }
        }

        /// <summary>구 API 호환 — 스킬1 임팩트.</summary>
        public bool GetSkillImpact(int unitId, Coord hover, List<Coord> cells) => GetSkillImpact(unitId, 0, hover, cells);

        /// <summary>스킬을 hover로 발사하면 실제 맞는(닿는) 칸들. 유효 조준이면 true.</summary>
        public bool GetSkillImpact(int unitId, int skillIndex, Coord hover, List<Coord> cells)
        {
            cells.Clear();
            var unit = State.GetUnit(unitId);
            if (unit == null || !unit.alive) return false;
            var skill = ClassCatalog.Get(unit.unitClass).skills[skillIndex];
            switch (skill.kind)
            {
                case SkillKind.ShieldPush:
                case SkillKind.Smash:
                case SkillKind.Claw:
                case SkillKind.KnockShot:
                    if (!InAttackShape(AttackShape.Melee8, unit.pos, hover) || !State.Grid.IsWalkableTerrain(hover)) return false;
                    cells.Add(hover);
                    return true;
                case SkillKind.Scream:
                    // 조준 불필요 — 자기 주변 8칸이 곧 임팩트
                    for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var c = new Coord(unit.pos.x + dx, unit.pos.y + dy);
                        if (State.Grid.IsWalkableTerrain(c)) cells.Add(c);
                    }
                    return cells.Count > 0;
                case SkillKind.Dash:
                {
                    var dir = UnitDir(unit.pos, hover, skill.range);
                    if (dir == Coord.Zero) return false;
                    var pos = unit.pos;
                    for (int i = 0; i < skill.range; i++)
                    {
                        var next = pos + dir;
                        if (!State.Grid.IsWalkableTerrain(next)) break;
                        cells.Add(next);
                        pos = next;
                    }
                    return cells.Count > 0;
                }
                case SkillKind.Blink:
                {
                    var d = hover - unit.pos;
                    if (Math.Max(Math.Abs(d.x), Math.Abs(d.y)) > skill.range || !State.Grid.IsWalkable(hover)) return false;
                    cells.Add(hover);
                    return true;
                }
                case SkillKind.Burst:
                {
                    var d = hover - unit.pos;
                    if (Math.Max(Math.Abs(d.x), Math.Abs(d.y)) > skill.range || !State.Grid.IsWalkableTerrain(hover)) return false;
                    cells.Add(hover);
                    foreach (var dir in Coord.Directions4)
                        if (State.Grid.IsWalkableTerrain(hover + dir)) cells.Add(hover + dir);
                    return true;
                }
                case SkillKind.BombDeliver:
                    if (Coord.Manhattan(unit.pos, hover) > skill.range || !State.Grid.IsWalkable(hover)) return false;
                    cells.Add(hover);
                    foreach (var dir in Coord.Directions4)
                        if (State.Grid.IsWalkableTerrain(hover + dir)) cells.Add(hover + dir);
                    return true;
                case SkillKind.Snipe:
                {
                    var dir = UnitDir(unit.pos, hover, skill.range);
                    if (dir == Coord.Zero) return false;
                    cells.AddRange(SnipeLine(unit, dir, skill.range));
                    return cells.Count > 0;
                }
                default: return false;
            }
        }

        // ── 내부 ──────────────────────────────────────────────────

        /// <summary>저격 직선의 타격 칸들 (최대 maxRange칸). 벽: 평지 사수는 정지, 고지대 사수는 넘겨 쏨. 구덩이: 탄이 지나간다(칸 제외).</summary>
        List<Coord> SnipeLine(UnitState unit, Coord dir, int maxRange)
        {
            var cells = new List<Coord>();
            bool elevated = State.Grid.IsHighland(unit.pos);
            int dist = 0;
            for (var c = unit.pos + dir; State.Grid.InBounds(c) && dist < maxRange; c += dir)
            {
                dist++;
                var type = State.Grid.GetCell(c).type;
                if (type == CellType.Obstacle)
                {
                    if (elevated) continue;
                    break;
                }
                if (type == CellType.Void) continue;
                cells.Add(c);
            }
            return cells;
        }

        /// <summary>target이 pos에서 직선(상하좌우) maxDist 이내면 단위 방향, 아니면 Zero.</summary>
        static Coord UnitDir(Coord pos, Coord target, int maxDist)
        {
            var d = target - pos;
            if (d == Coord.Zero) return Coord.Zero;
            if (d.x != 0 && d.y != 0) return Coord.Zero;
            int dist = Math.Abs(d.x + d.y);
            if (dist > maxDist) return Coord.Zero;
            return new Coord(Math.Sign(d.x), Math.Sign(d.y));
        }

        int nextStrikeId; // 예고 고유 id — 네트워크 판정 매칭용

        void Place(TelegraphStrike strike)
        {
            strike.id = ++nextStrikeId;
            strikes.Add(strike);
            OnTelegraph?.Invoke(strike);
        }

        // ── 멀티 클라이언트 전용 — 호스트가 릴레이한 예고를 미러에 주입 ──
        // 클라는 Tick을 안 돌리므로 이 경로 외엔 strikes가 채워지지 않는다.
        // OnTelegraph/OnStrikeResolved를 그대로 발화 — 기존 연출 배선이 무수정으로 동작.

        /// <summary>클라 — 호스트 예고 주입. 예고 렌더·경고 링이 로컬과 동일하게 뜬다.</summary>
        public void InjectRemoteStrike(TelegraphStrike strike)
        {
            strikes.Add(strike);
            OnTelegraph?.Invoke(strike);
        }

        /// <summary>클라 — 호스트 판정 통보. 목록에서 제거 + 해소 연출 발화.</summary>
        public void ResolveRemoteStrike(int strikeId, bool hit)
        {
            for (int i = 0; i < strikes.Count; i++)
                if (strikes[i].id == strikeId)
                {
                    var s = strikes[i];
                    strikes.RemoveAt(i);
                    OnStrikeResolved?.Invoke(s, hit);
                    return;
                }
        }

        void Resolve(TelegraphStrike strike)
        {
            var attacker = State.GetUnit(strike.attackerId);
            bool hit = false;

            foreach (var cell in strike.cells)
            {
                int unitId = State.Grid.GetUnitAt(cell);
                if (unitId == Cell.NoUnit) continue;
                var unit = State.GetUnit(unitId);
                if (unit.team == strike.team) continue;   // 팀킬 없음
                if (IsFlying(unit)) continue;             // 비행 중 무적

                var hitDir = attacker != null
                    ? new Coord(Math.Sign(unit.pos.x - attacker.pos.x), Math.Sign(unit.pos.y - attacker.pos.y))
                    : Coord.Zero;
                Damage(unit, strike.damage, hitDir);
                hit = true;
                if (strike.stunSeconds > 0f && unit.alive)
                {
                    unit.stunnedUntil = State.time + strike.stunSeconds;
                    OnStunned?.Invoke(unit.id, strike.stunSeconds);
                }
                if (strike.pushCells > 0 && unit.alive)
                    Push(unit, strike.pushDir, strike.pushCells, strike.wallBonusDamage);
            }

            // 적중 = 예측 성공 → AP 환급
            if (hit && attacker != null && attacker.alive)
                attacker.ap = Math.Min(Config.apMax, attacker.ap + Config.hitRefund);

            OnStrikeResolved?.Invoke(strike, hit);
        }

        void Damage(UnitState unit, int amount, Coord hitDir = default)
        {
            if (amount <= 0) return;
            if (IsFlying(unit)) return; // 비행 중 무적
            unit.hp -= amount;
            OnUnitDamaged?.Invoke(unit.id, amount, hitDir);
            if (unit.hp <= 0)
            {
                unit.alive = false;
                State.Grid.RemoveUnit(unit.pos);
                OnUnitDied?.Invoke(unit.id);
            }
        }

        void Push(UnitState unit, Coord dir, int cells, int wallBonusDamage)
        {
            if (dir == Coord.Zero) return;
            for (int i = 0; i < cells; i++)
            {
                var next = unit.pos + dir;
                // 평지 → 고지대 밀침은 단면 충돌(벽꿍과 동일) — 밀어서 올려주는 건 없다
                bool uphill = State.Grid.IsHighland(next) && !State.Grid.IsHighland(unit.pos);
                if (!State.Grid.IsWalkableTerrain(next) || uphill)
                {
                    OnWallCrash?.Invoke(unit.id);
                    if (wallBonusDamage > 0) Damage(unit, wallBonusDamage, dir);
                    return;
                }
                if (State.Grid.GetUnitAt(next) != Cell.NoUnit) return; // 유닛에 막힘 — 추가 피해 없음

                bool falls = State.Grid.IsHighland(unit.pos) && !State.Grid.IsHighland(next);
                State.Grid.MoveOccupant(unit.pos, next);
                unit.pos = next;
                if (falls)
                {
                    Damage(unit, Config.fallDamage); // 고지대 낙하
                    if (!unit.alive) return;
                }
            }
        }
    }
}
