using System;
using System.Collections.Generic;

namespace SeoYuGi.Battle
{
    public enum ActDenied
    {
        None,
        Dead,      // 유닛 없음/사망
        NoAp,      // AP 부족
        BadTarget  // 사거리 밖 / 벽 / 잘못된 지정
    }

    /// <summary>설치 공격 예고 — 예고 후 판정, 즉발 없음(기획서 §03).</summary>
    public class TelegraphStrike
    {
        public int attackerId;
        public int team;
        public List<Coord> cells = new List<Coord>();
        public float impactTime;   // BattleState.time 기준 판정 시각
        public int damage;
        public Coord pushDir;      // 밀침 방향 (Zero = 없음)
        public int pushCells;
        public int wallBonusDamage; // 벽/맵 경계에 밀려 부딪히면 추가 피해
    }

    /// <summary>
    /// AP 경제 + 설치형 공격 + 방어 + 클래스 스킬 5종. 순수 C# — 외부에서 Tick(dt) 호출.
    /// BattleState.time은 이 시스템이 단독 전진. 판정 순서 = 설치 순서 (결정론).
    /// 팀킬 없음. 방어 중인 유닛은 피해 무효.
    /// </summary>
    public class CombatSystem
    {
        public BattleState State { get; }
        public CombatConfig Config { get; }
        public IReadOnlyList<TelegraphStrike> ActiveStrikes => strikes;

        public event Action<TelegraphStrike> OnTelegraph;
        public event Action<TelegraphStrike, bool> OnStrikeResolved; // (strike, hitAnything)
        public event Action<int, int> OnUnitDamaged;                 // (unitId, damage)
        public event Action<int> OnUnitDied;
        public event Action<int> OnGuard;
        public event Action<int, SkillKind> OnSkillCast; // (unitId, kind) — 성공 시
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

        /// <summary>일반공격: 인접 십자 1칸 지정 → 예고 후 판정. 도약 버프 시 피해 +1.</summary>
        public ActDenied TryAttack(int unitId, Coord target)
        {
            var unit = State.GetUnit(unitId);
            if (unit == null || !unit.alive) return ActDenied.Dead;
            if (unit.ap < Config.costAttack) return ActDenied.NoAp;
            if (Coord.Manhattan(unit.pos, target) != 1) return ActDenied.BadTarget;
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

        /// <summary>방어: 0.5초 피해 무효 + 제자리 고정 (MoveSystem이 guardUntil 확인).</summary>
        public ActDenied TryGuard(int unitId)
        {
            var unit = State.GetUnit(unitId);
            if (unit == null || !unit.alive) return ActDenied.Dead;
            if (unit.ap < Config.costGuard) return ActDenied.NoAp;

            unit.ap -= Config.costGuard;
            unit.guardUntil = State.time + Config.guardDurationSeconds;
            OnGuard?.Invoke(unitId);
            return ActDenied.None;
        }

        /// <summary>클래스 스킬 (AP 3). 종류별 판정은 SkillKind 참조.</summary>
        public ActDenied TrySkill(int unitId, Coord target)
        {
            var unit = State.GetUnit(unitId);
            if (unit == null || !unit.alive) return ActDenied.Dead;
            if (unit.ap < Config.costSkill) return ActDenied.NoAp;

            var skill = ClassCatalog.Get(unit.unitClass).skill;
            ActDenied result;
            switch (skill.kind)
            {
                case SkillKind.Smash: result = CastSmash(unit, target, skill); break;
                case SkillKind.Dash: result = CastDash(unit, target, skill); break;
                case SkillKind.Blink: result = CastBlink(unit, target, skill); break;
                case SkillKind.Burst: result = CastBurst(unit, target, skill); break;
                case SkillKind.Snipe: result = CastSnipe(unit, target, skill); break;
                default: result = ActDenied.BadTarget; break;
            }
            if (result == ActDenied.None)
            {
                unit.ap -= Config.costSkill;
                OnSkillCast?.Invoke(unitId, skill.kind);
            }
            return result;
        }

        // ── 스킬 5종 ──────────────────────────────────────────────

        ActDenied CastSmash(UnitState unit, Coord target, SkillDef skill)
        {
            if (Coord.Manhattan(unit.pos, target) != 1) return ActDenied.BadTarget;
            if (!State.Grid.IsWalkableTerrain(target)) return ActDenied.BadTarget;

            Place(new TelegraphStrike
            {
                attackerId = unit.id,
                team = unit.team,
                cells = { target },
                impactTime = State.time + skill.telegraphSeconds,
                damage = skill.damage,
                pushDir = target - unit.pos,
                pushCells = 2,
                wallBonusDamage = 1
            });
            return ActDenied.None;
        }

        ActDenied CastDash(UnitState unit, Coord target, SkillDef skill)
        {
            var dir = UnitDir(unit.pos, target, skill.range);
            if (dir == Coord.Zero) return ActDenied.BadTarget;

            // 즉시 발동: 최대 2칸 전진. 경로의 적은 피해 + 1칸 밀침(유닛당 1회), 밀려나면 계속 전진.
            var hitIds = new HashSet<int>();
            for (int step = 0; step < skill.range; step++)
            {
                var next = unit.pos + dir;
                if (!State.Grid.IsWalkableTerrain(next)) break;

                int occupantId = State.Grid.GetUnitAt(next);
                if (occupantId != Cell.NoUnit)
                {
                    if (hitIds.Contains(occupantId)) break; // 이미 때린 적을 또 만남 — 그 앞에서 정지
                    var occupant = State.GetUnit(occupantId);
                    if (occupant.team == unit.team) break; // 아군은 뚫지 않음
                    if (occupant.guardUntil < State.time)
                    {
                        hitIds.Add(occupantId);
                        Damage(occupant, skill.damage);
                        if (occupant.alive) Push(occupant, dir, 1, 0);
                    }
                    if (State.Grid.GetUnitAt(next) != Cell.NoUnit) break; // 안 밀렸으면 정지
                }
                State.Grid.MoveOccupant(unit.pos, next);
                unit.pos = next;
            }
            return ActDenied.None;
        }

        ActDenied CastBlink(UnitState unit, Coord target, SkillDef skill)
        {
            if (Coord.Manhattan(unit.pos, target) > skill.range) return ActDenied.BadTarget;
            if (!State.Grid.IsWalkable(target)) return ActDenied.BadTarget;

            State.Grid.MoveOccupant(unit.pos, target); // 점멸 — 벽 무시 순간이동
            unit.pos = target;
            unit.attackBuffUntil = State.time + Config.blinkBuffSeconds;
            return ActDenied.None;
        }

        ActDenied CastBurst(UnitState unit, Coord target, SkillDef skill)
        {
            if (Coord.Manhattan(unit.pos, target) > skill.range) return ActDenied.BadTarget;
            if (!State.Grid.IsWalkableTerrain(target)) return ActDenied.BadTarget;

            var strike = new TelegraphStrike
            {
                attackerId = unit.id,
                team = unit.team,
                impactTime = State.time + skill.telegraphSeconds,
                damage = skill.damage
            };
            strike.cells.Add(target); // 십자 5칸
            foreach (var dir in Coord.Directions4)
            {
                var c = target + dir;
                if (State.Grid.IsWalkableTerrain(c)) strike.cells.Add(c);
            }
            Place(strike);
            return ActDenied.None;
        }

        ActDenied CastSnipe(UnitState unit, Coord target, SkillDef skill)
        {
            var dir = UnitDir(unit.pos, target, int.MaxValue);
            if (dir == Coord.Zero) return ActDenied.BadTarget;

            // 직선 1열: 벽/맵 끝까지 전부 예고. 유닛은 관통.
            var strike = new TelegraphStrike
            {
                attackerId = unit.id,
                team = unit.team,
                impactTime = State.time + skill.telegraphSeconds,
                damage = skill.damage
            };
            for (var c = unit.pos + dir; State.Grid.IsWalkableTerrain(c); c += dir)
                strike.cells.Add(c);
            if (strike.cells.Count == 0) return ActDenied.BadTarget;

            Place(strike);
            return ActDenied.None;
        }

        // ── 조준 미리보기 (상태 변경 없음 — 뷰 전용 쿼리, AP 검사 안 함) ──

        /// <summary>일반공격 조준 가능 칸 (인접 4칸 중 지형 가능).</summary>
        public void GetAttackRange(int unitId, List<Coord> cells)
        {
            cells.Clear();
            var unit = State.GetUnit(unitId);
            if (unit == null || !unit.alive) return;
            foreach (var dir in Coord.Directions4)
            {
                var c = unit.pos + dir;
                if (State.Grid.IsWalkableTerrain(c)) cells.Add(c);
            }
        }

        /// <summary>일반공격을 hover로 발사하면 맞는 칸. 유효 조준이면 true.</summary>
        public bool GetAttackImpact(int unitId, Coord hover, List<Coord> cells)
        {
            cells.Clear();
            var unit = State.GetUnit(unitId);
            if (unit == null || !unit.alive) return false;
            if (Coord.Manhattan(unit.pos, hover) != 1 || !State.Grid.IsWalkableTerrain(hover)) return false;
            cells.Add(hover);
            return true;
        }

        /// <summary>스킬 조준 가능 칸 — SkillKind별 사거리 모양.</summary>
        public void GetSkillRange(int unitId, List<Coord> cells)
        {
            cells.Clear();
            var unit = State.GetUnit(unitId);
            if (unit == null || !unit.alive) return;
            var skill = ClassCatalog.Get(unit.unitClass).skill;
            switch (skill.kind)
            {
                case SkillKind.Smash:
                    foreach (var dir in Coord.Directions4)
                        if (State.Grid.IsWalkableTerrain(unit.pos + dir)) cells.Add(unit.pos + dir);
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
                        if (Math.Abs(dx) + Math.Abs(dy) > skill.range || c == unit.pos) continue;
                        // 점멸은 빈 칸이어야 착지(IsWalkable), 파열탄은 지형만 보면 됨
                        bool ok = skill.kind == SkillKind.Blink
                            ? State.Grid.IsWalkable(c)
                            : State.Grid.IsWalkableTerrain(c);
                        if (ok) cells.Add(c);
                    }
                    break;
                case SkillKind.Snipe:
                    foreach (var dir in Coord.Directions4)
                        for (var c = unit.pos + dir; State.Grid.IsWalkableTerrain(c); c += dir)
                            cells.Add(c);
                    break;
            }
        }

        /// <summary>스킬을 hover로 발사하면 실제 맞는(닿는) 칸들. 유효 조준이면 true.</summary>
        public bool GetSkillImpact(int unitId, Coord hover, List<Coord> cells)
        {
            cells.Clear();
            var unit = State.GetUnit(unitId);
            if (unit == null || !unit.alive) return false;
            var skill = ClassCatalog.Get(unit.unitClass).skill;
            switch (skill.kind)
            {
                case SkillKind.Smash:
                    if (Coord.Manhattan(unit.pos, hover) != 1 || !State.Grid.IsWalkableTerrain(hover)) return false;
                    cells.Add(hover);
                    return true;
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
                    if (Coord.Manhattan(unit.pos, hover) > skill.range || !State.Grid.IsWalkable(hover)) return false;
                    cells.Add(hover);
                    return true;
                case SkillKind.Burst:
                    if (Coord.Manhattan(unit.pos, hover) > skill.range || !State.Grid.IsWalkableTerrain(hover)) return false;
                    cells.Add(hover);
                    foreach (var dir in Coord.Directions4)
                        if (State.Grid.IsWalkableTerrain(hover + dir)) cells.Add(hover + dir);
                    return true;
                case SkillKind.Snipe:
                {
                    var dir = UnitDir(unit.pos, hover, int.MaxValue);
                    if (dir == Coord.Zero) return false;
                    for (var c = unit.pos + dir; State.Grid.IsWalkableTerrain(c); c += dir)
                        cells.Add(c);
                    return cells.Count > 0;
                }
                default: return false;
            }
        }

        // ── 내부 ──────────────────────────────────────────────────

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

        void Place(TelegraphStrike strike)
        {
            strikes.Add(strike);
            OnTelegraph?.Invoke(strike);
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
                if (unit.team == strike.team) continue;          // 팀킬 없음
                if (unit.guardUntil >= State.time) continue;     // 방어 성공

                Damage(unit, strike.damage);
                hit = true;
                if (strike.pushCells > 0 && unit.alive)
                    Push(unit, strike.pushDir, strike.pushCells, strike.wallBonusDamage);
            }

            // 적중 = 예측 성공 → AP 환급
            if (hit && attacker != null && attacker.alive)
                attacker.ap = Math.Min(Config.apMax, attacker.ap + Config.hitRefund);

            OnStrikeResolved?.Invoke(strike, hit);
        }

        void Damage(UnitState unit, int amount)
        {
            if (amount <= 0) return;
            unit.hp -= amount;
            OnUnitDamaged?.Invoke(unit.id, amount);
            if (unit.hp <= 0)
            {
                unit.alive = false;
                State.Grid.RemoveUnit(unit.pos);
                OnUnitDied?.Invoke(unit.id);
            }
        }

        void Push(UnitState unit, Coord dir, int cells, int wallBonusDamage)
        {
            for (int i = 0; i < cells; i++)
            {
                var next = unit.pos + dir;
                if (!State.Grid.IsWalkableTerrain(next))
                {
                    // 벽/맵 경계 충돌
                    OnWallCrash?.Invoke(unit.id);
                    if (wallBonusDamage > 0) Damage(unit, wallBonusDamage);
                    return;
                }
                if (State.Grid.GetUnitAt(next) != Cell.NoUnit) return; // 유닛에 막힘 — 추가 피해 없음

                State.Grid.MoveOccupant(unit.pos, next);
                unit.pos = next;
            }
        }
    }
}
