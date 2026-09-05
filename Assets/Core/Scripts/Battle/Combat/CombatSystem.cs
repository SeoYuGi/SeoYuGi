using System;
using System.Collections.Generic;

namespace SeoYuGi.Battle
{
    public enum ActDenied
    {
        None,
        Dead,      // 유닛 없음/사망
        BadTarget, // 사거리 밖 / 벽 / 잘못된 지정
        Cooldown,  // 일반공격·스킬 쿨타임
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
        public SkillKind kind;      // 이 예고가 무슨 스킬인가 — 예고 아이콘용. 평타는 SkillKind.BasicAttack
        public Coord aimCell;       // 시전자가 지정한 칸 — 연출용(조준경·공격선). 광역은 중심, 자기중심 스킬은 시전자 칸
    }

    /// <summary>
    /// 설치형 공격 + 클래스 스킬 2개 체제 (캐릭터 기획 v1.7). AP는 삭제됨(2026-09-05) —
    /// 일반공격은 쿨다운으로 제한하고, 적중 보상은 OnDamageDealt를 구독한 해킹 게이지가 받는다.
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
        public event Action<int, int> OnDamageDealt;                 // (attackerId, 가한 피해 합) — 적중 = 예측 성공 보상 훅
        public event Action<int> OnUnitDied;
        public event Action<int, int> OnUnitKilled;                  // (deadId, killerId — NoUnit이면 환경사) — 킬로그용
        public event Action<int, SkillKind> OnSkillCast; // (unitId, kind) — 성공 시
        public event Action<int, float> OnStunned;       // (unitId, seconds)
        public event Action<int> OnWallCrash;            // 밀침으로 벽/맵 경계 충돌

        readonly List<TelegraphStrike> strikes = new List<TelegraphStrike>();

        public CombatSystem(BattleState state, CombatConfig config)
        {
            State = state;
            Config = config;
        }

        public void Tick(float deltaTime)
        {
            State.time += deltaTime;

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

        public float AttackCooldownRemaining(int unitId)
        {
            var unit = State.GetUnit(unitId);
            if (unit == null) return 0f;
            return Math.Max(0f, unit.attackReadyAt - State.time);
        }

        /// <summary>기본공격 사거리 판정 — 클래스별 모양 (범위 다이어그램 원본).</summary>
        /// <summary>bonus = 고지대 사거리 연장(+2). 모양을 유지한 채 반경만 커진다.</summary>
        /// <summary>
        /// 근접 스킬 사거리(체비셰프 반경) — 방패밀기·강타·발톱·비명 교란·넉백샷.
        /// 반경은 SkillDef.range가 정한다. 고지대 보너스는 붙지 않는다 — 자기중심·인접 모양
        /// 스킬은 지형으로 늘어나지 않는다는 기존 규칙(EffRange 주석) 유지.
        /// 기본공격은 별도 모양 표(InAttackShape)를 쓴다.
        /// </summary>
        public static bool InMeleeRange(Coord from, Coord to, int range)
        {
            int dx = Math.Abs(to.x - from.x), dy = Math.Abs(to.y - from.y);
            return (dx != 0 || dy != 0) && dx <= range && dy <= range;
        }

        public static bool InAttackShape(AttackShape shape, Coord from, Coord to, int bonus = 0)
        {
            int dx = Math.Abs(to.x - from.x), dy = Math.Abs(to.y - from.y);
            if (dx == 0 && dy == 0) return false;
            int cheb = Math.Max(dx, dy);
            switch (shape)
            {
                case AttackShape.Melee8: return cheb <= 2 + bonus;
                case AttackShape.Circle2: { int r = 3 + bonus; return cheb <= r && !(dx == r && dy == r); }
                case AttackShape.Square2: return cheb <= 3 + bonus;
                default: return false;
            }
        }

        /// <summary>고지대 위 유닛의 일반공격 사거리 연장 — EffRange와 같은 규칙(+2).</summary>
        int AttackBonus(UnitState unit) => State.Grid.IsHighland(unit.pos) ? 2 : 0;

        // ── 일반공격 ──────────────────────────────────────────────

        /// <summary>일반공격: 클래스별 모양 안 1칸 지정 → 예고 후 판정. 도약 버프 시 피해 +1.</summary>
        public ActDenied TryAttack(int unitId, Coord target)
        {
            var unit = State.GetUnit(unitId);
            if (unit == null || !unit.alive) return ActDenied.Dead;
            if (IsLocked(unit)) return ActDenied.Locked;
            if (unit.attackReadyAt > State.time) return ActDenied.Cooldown;
            var def = ClassCatalog.Get(unit.unitClass);
            if (!InAttackShape(def.attackShape, unit.pos, target, AttackBonus(unit))) return ActDenied.BadTarget;
            if (!State.Grid.IsWalkableTerrain(target)) return ActDenied.BadTarget;

            unit.attackReadyAt = State.time + Config.attackCooldownSeconds;
            int damage = Config.attackDamage
                + (unit.attackBuffUntil > State.time ? Config.blinkBuffBonus : 0);
            Place(new TelegraphStrike
            {
                attackerId = unitId,
                kind = SkillKind.BasicAttack,
                team = unit.team,
                cells = { target },
                aimCell = target,
                impactTime = State.time + Config.attackTelegraphSeconds,
                damage = damage
            });
            return ActDenied.None;
        }

        // ── 스킬 (2개 체제: 인덱스 0/1, 쿨타임 제한) ──────────────

        /// <summary>구 API 호환 — 스킬1.</summary>
        public ActDenied TrySkill(int unitId, Coord target) => TrySkill(unitId, 0, target);

        public ActDenied TrySkill(int unitId, int skillIndex, Coord target)
        {
            var unit = State.GetUnit(unitId);
            if (unit == null || !unit.alive) return ActDenied.Dead;
            if (IsLocked(unit)) return ActDenied.Locked;

            var skill = ClassCatalog.Get(unit.unitClass).skills[skillIndex];
            if (unit.skillReadyAt[skillIndex] > State.time) return ActDenied.Cooldown;

            ActDenied result;
            switch (skill.kind)
            {
                case SkillKind.ShieldPush:
                case SkillKind.Smash:
                case SkillKind.Claw:
                    PushSpecOf(skill.kind, out int meleePush, out int meleeWall, out _);
                    result = CastMeleeStrike(unit, target, skill, meleePush, meleeWall);
                    break;
                case SkillKind.Dash: result = CastDash(unit, target, skill); break;
                case SkillKind.Scream: result = CastScream(unit, skill); break;
                case SkillKind.Blink: result = CastBlink(unit, target, skill); break;
                case SkillKind.Burst: result = CastBurst(unit, target, skill); break;
                case SkillKind.BombDeliver: result = CastBombDeliver(unit, target, skill); break;
                case SkillKind.KnockShot: result = CastKnockShot(unit, target, skill); break;
                case SkillKind.Snipe: result = CastSnipe(unit, target, skill); break;
                default: result = ActDenied.BadTarget; break;
            }
            if (result == ActDenied.None)
            {
                unit.skillReadyAt[skillIndex] = State.time + skill.cooldownSeconds;
                OnSkillCast?.Invoke(unitId, skill.kind);
            }
            return result;
        }

        // ── 스킬 구현 ─────────────────────────────────────────────

        /// <summary>고지대 위 유닛은 사거리형 스킬 +2 — 시야 +2와 짝 (2026-09-05).
        /// 인접8 모양 스킬(근접기·비명)은 모양 고정이라 해당 없음 (일반공격은 AttackBonus가 처리).</summary>
        int EffRange(UnitState unit, SkillDef skill) =>
            skill.range + (State.Grid.IsHighland(unit.pos) ? 2 : 0);

        /// <summary>인접8 단일 칸 예고 타격 — 방패밀기(밀침2·벽꿍), 강타(밀침1), 발톱(순수 딜).</summary>
        ActDenied CastMeleeStrike(UnitState unit, Coord target, SkillDef skill, int pushCells, int wallBonus)
        {
            if (!InMeleeRange(unit.pos, target, skill.range)) return ActDenied.BadTarget; // 근접 스킬 사거리 = 표(SkillDef.range)
            if (!State.Grid.IsWalkableTerrain(target)) return ActDenied.BadTarget;

            var d = target - unit.pos;
            Place(new TelegraphStrike
            {
                attackerId = unit.id,
                kind = skill.kind,
                team = unit.team,
                cells = { target },
                aimCell = target,
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
            int range = EffRange(unit, skill);
            var dir = UnitDir(unit.pos, target, range);
            if (dir == Coord.Zero) return ActDenied.BadTarget;

            // 즉발 대시: 벽만 못 뚫고 유닛은 통과. 경로의 적은 피해 + 1칸 밀침(유닛당 1회).
            // 착지는 통과한 칸 중 가장 먼 빈 칸.
            var hitIds = new HashSet<int>();
            int dealt = 0;
            var landing = unit.pos;
            var probe = unit.pos;
            for (int step = 0; step < range; step++)
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
                        Damage(occupant, skill.damage, dir, unit.id);
                        dealt += skill.damage;
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
            if (dealt > 0) OnDamageDealt?.Invoke(unit.id, dealt);
            return ActDenied.None;
        }

        ActDenied CastScream(UnitState unit, SkillDef skill)
        {
            // 인접8 전부 예고 (벽 칸 제외 — 벽은 통과 못함). 판정 시 적에게 스턴.
            var strike = new TelegraphStrike
            {
                attackerId = unit.id,
                kind = skill.kind,
                team = unit.team,
                aimCell = unit.pos, // 자기 중심 광역
                impactTime = State.time + skill.telegraphSeconds,
                damage = skill.damage,
                stunSeconds = skill.stunSeconds
            };
            int r = skill.range; // 인접8 고정이 아니라 표를 따른다 — 사거리 +1이 광역에도 반영된다
            for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
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
            // 5×5 (체비쇼프 2) 내 아무 빈 칸으로 점멸 — 범위 다이어그램 원본. 고지대 위 +1
            var d = target - unit.pos;
            if (Math.Max(Math.Abs(d.x), Math.Abs(d.y)) > EffRange(unit, skill) || d == Coord.Zero) return ActDenied.BadTarget;
            if (!State.Grid.IsWalkable(target)) return ActDenied.BadTarget;

            State.Grid.MoveOccupant(unit.pos, target); // 점멸 — 벽 무시 순간이동
            unit.pos = target;
            unit.attackBuffUntil = State.time + Config.blinkBuffSeconds;
            return ActDenied.None;
        }

        ActDenied CastBurst(UnitState unit, Coord target, SkillDef skill)
        {
            // 5×5 (체비쇼프 2, 자기 칸 포함 가능 — 자폭 피해 없음) 지정 → 십자 5칸. 고지대 위 +1
            var d = target - unit.pos;
            if (Math.Max(Math.Abs(d.x), Math.Abs(d.y)) > EffRange(unit, skill)) return ActDenied.BadTarget;
            if (!State.Grid.IsWalkableTerrain(target)) return ActDenied.BadTarget;

            var strike = new TelegraphStrike
            {
                attackerId = unit.id,
                kind = skill.kind,
                team = unit.team,
                aimCell = target,
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
            // 맨해튼 4 내 칸에 폭탄 배달 후 원위치 복귀 — 시뮬 위치는 출발 칸 그대로
            // (왕복 비행은 뷰 소관). 비행 중 무적·행동 불가. 적 점유 칸도 지정 가능.
            if (Coord.Manhattan(unit.pos, target) > EffRange(unit, skill) || target == unit.pos) return ActDenied.BadTarget;
            if (!State.Grid.IsWalkableTerrain(target)) return ActDenied.BadTarget;

            unit.flyingUntil = State.time + skill.telegraphSeconds;

            var strike = new TelegraphStrike
            {
                attackerId = unit.id,
                kind = skill.kind,
                team = unit.team,
                aimCell = target,
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
            if (!InMeleeRange(unit.pos, target, skill.range)) return ActDenied.BadTarget; // 근접 스킬 사거리 = 표(SkillDef.range)
            int victimId = State.Grid.GetUnitAt(target);
            if (victimId == Cell.NoUnit) return ActDenied.BadTarget;
            var victim = State.GetUnit(victimId);
            if (victim.team == unit.team || IsFlying(victim)) return ActDenied.BadTarget;

            var d = target - unit.pos;
            var dir = new Coord(Math.Sign(d.x), Math.Sign(d.y));
            Damage(victim, skill.damage, dir, unit.id);
            OnDamageDealt?.Invoke(unit.id, skill.damage); // 즉발 명중도 예측 성공 취급
            PushSpecOf(SkillKind.KnockShot, out int knockCells, out _, out _);
            Push(unit, new Coord(-dir.x, -dir.y), knockCells, 0); // 셀프 넉백 — Push가 낙하·막힘 처리
            return ActDenied.None;
        }

        /// <summary>조준 사격 — 다이아(맨해튼 range-1) + 십자 끝 range칸 안의 한 칸을 지정 (기획 다이어그램 2026-09-05, RangeTemplates.SnipeRange와 동일 모양).
        /// 벽 LOS 필요 — 고지대 사수는 벽을 넘겨 쏜다. 구 직선 관통은 폐기.</summary>
        ActDenied CastSnipe(UnitState unit, Coord target, SkillDef skill)
        {
            if (!CanSnipe(unit, target, EffRange(unit, skill))) return ActDenied.BadTarget;

            var strike = new TelegraphStrike
            {
                attackerId = unit.id,
                kind = skill.kind,
                team = unit.team,
                aimCell = target,
                cells = { target },
                impactTime = State.time + skill.telegraphSeconds,
                damage = skill.damage
            };
            Place(strike);
            return ActDenied.None;
        }

        // ── 조준 미리보기 (상태 변경 없음 — 뷰 전용 쿼리, 쿨타임 검사 안 함) ──

        /// <summary>일반공격 조준 가능 칸 — 클래스별 모양.</summary>
        public void GetAttackRange(int unitId, List<Coord> cells)
        {
            cells.Clear();
            var unit = State.GetUnit(unitId);
            if (unit == null || !unit.alive) return;
            var shape = ClassCatalog.Get(unit.unitClass).attackShape;
            int bonus = AttackBonus(unit);
            int r = 3 + bonus; // 가장 넓은 모양(Square2) 기준 탐색 반경 — 사거리 +1과 함께 넓혔다
            for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
            {
                var c = new Coord(unit.pos.x + dx, unit.pos.y + dy);
                if (InAttackShape(shape, unit.pos, c, bonus) && State.Grid.IsWalkableTerrain(c))
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
            if (!InAttackShape(shape, unit.pos, hover, AttackBonus(unit)) || !State.Grid.IsWalkableTerrain(hover)) return false;
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
                {
                    int r = skill.range; // 근접 스킬 사거리 = 표. 고지대 보너스 없음(InMeleeRange와 같은 규칙)
                    for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var c = new Coord(unit.pos.x + dx, unit.pos.y + dy);
                        if (State.Grid.IsWalkableTerrain(c)) cells.Add(c);
                    }
                    break;
                }
                case SkillKind.Dash:
                {
                    int range = EffRange(unit, skill); // 고지대 위 +1
                    foreach (var dir in Coord.Directions8) // 대각 대시 허용 (2026-09-05)
                        for (int i = 1; i <= range; i++)
                        {
                            var c = unit.pos + new Coord(dir.x * i, dir.y * i);
                            if (!State.Grid.IsWalkableTerrain(c)) break;
                            cells.Add(c);
                        }
                    break;
                }
                case SkillKind.Blink:
                case SkillKind.Burst:
                {
                    int range = EffRange(unit, skill);
                    for (int dx = -range; dx <= range; dx++)
                    for (int dy = -range; dy <= range; dy++)
                    {
                        var c = new Coord(unit.pos.x + dx, unit.pos.y + dy);
                        if (c == unit.pos && skill.kind == SkillKind.Blink) continue;
                        bool ok = skill.kind == SkillKind.Blink
                            ? State.Grid.IsWalkable(c)
                            : State.Grid.IsWalkableTerrain(c);
                        if (ok) cells.Add(c);
                    }
                    break;
                }
                case SkillKind.BombDeliver:
                {
                    int range = EffRange(unit, skill);
                    for (int dx = -range; dx <= range; dx++)
                    for (int dy = -range; dy <= range; dy++)
                    {
                        if (Math.Abs(dx) + Math.Abs(dy) > range || (dx == 0 && dy == 0)) continue;
                        var c = new Coord(unit.pos.x + dx, unit.pos.y + dy);
                        if (State.Grid.IsWalkableTerrain(c)) cells.Add(c); // 점유 칸도 폭격 가능 — 착지 안 하므로
                    }
                    break;
                }
                case SkillKind.Snipe:
                {
                    int range = EffRange(unit, skill);
                    for (int dx = -range; dx <= range; dx++)
                    for (int dy = -range; dy <= range; dy++)
                    {
                        var c = new Coord(unit.pos.x + dx, unit.pos.y + dy);
                        if (CanSnipe(unit, c, range)) cells.Add(c);
                    }
                    break;
                }
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
                    if (!InMeleeRange(unit.pos, hover, skill.range) || !State.Grid.IsWalkableTerrain(hover)) return false;
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
                    int range = EffRange(unit, skill); // 고지대 위 +1
                    var dir = UnitDir(unit.pos, hover, range);
                    if (dir == Coord.Zero) return false;
                    var pos = unit.pos;
                    for (int i = 0; i < range; i++)
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
                    if (Math.Max(Math.Abs(d.x), Math.Abs(d.y)) > EffRange(unit, skill) || !State.Grid.IsWalkable(hover)) return false;
                    cells.Add(hover);
                    return true;
                }
                case SkillKind.Burst:
                {
                    var d = hover - unit.pos;
                    if (Math.Max(Math.Abs(d.x), Math.Abs(d.y)) > EffRange(unit, skill) || !State.Grid.IsWalkableTerrain(hover)) return false;
                    cells.Add(hover);
                    foreach (var dir in Coord.Directions4)
                        if (State.Grid.IsWalkableTerrain(hover + dir)) cells.Add(hover + dir);
                    return true;
                }
                case SkillKind.BombDeliver:
                    if (Coord.Manhattan(unit.pos, hover) > EffRange(unit, skill) || hover == unit.pos ||
                        !State.Grid.IsWalkableTerrain(hover)) return false;
                    cells.Add(hover);
                    foreach (var dir in Coord.Directions4)
                        if (State.Grid.IsWalkableTerrain(hover + dir)) cells.Add(hover + dir);
                    return true;
                case SkillKind.Snipe:
                    if (!CanSnipe(unit, hover, EffRange(unit, skill))) return false;
                    cells.Add(hover);
                    return true;
                default: return false;
            }
        }

        // ── 내부 ──────────────────────────────────────────────────

        /// <summary>조준 사격 지정 가능 칸인가 — 모양(다이아 range-1 + 십자 끝 range) + 지형(벽·구덩이 불가) + 벽 LOS(고지대 사수 면제).</summary>
        bool CanSnipe(UnitState unit, Coord target, int range)
        {
            int dx = Math.Abs(target.x - unit.pos.x), dy = Math.Abs(target.y - unit.pos.y);
            if (dx == 0 && dy == 0) return false;
            bool inShape = dx + dy <= range - 1 || (dx == 0 && dy == range) || (dy == 0 && dx == range);
            if (!inShape || !State.Grid.IsWalkableTerrain(target)) return false;
            return State.Grid.IsHighland(unit.pos) || VisionSystem.HasLineOfSight(State.Grid, unit.pos, target);
        }

        /// <summary>target이 pos에서 직선(상하좌우) 또는 정대각 maxDist 이내면 단위 방향, 아니면 Zero.</summary>
        static Coord UnitDir(Coord pos, Coord target, int maxDist)
        {
            var d = target - pos;
            if (d == Coord.Zero) return Coord.Zero;
            bool straight = d.x == 0 || d.y == 0;
            bool diagonal = Math.Abs(d.x) == Math.Abs(d.y);
            if (!straight && !diagonal) return Coord.Zero;
            int dist = Math.Max(Math.Abs(d.x), Math.Abs(d.y)); // 대각 1스텝 = 1칸 (체비쇼프)
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
            int dealt = 0;

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
                Damage(unit, strike.damage, hitDir, strike.attackerId);
                hit = true;
                dealt += strike.damage;
                if (strike.stunSeconds > 0f && unit.alive)
                {
                    unit.stunnedUntil = State.time + strike.stunSeconds;
                    OnStunned?.Invoke(unit.id, strike.stunSeconds);
                }
                if (strike.pushCells > 0 && unit.alive)
                    Push(unit, strike.pushDir, strike.pushCells, strike.wallBonusDamage);
            }

            // 적중 = 예측 성공 → 보상 훅 (해킹 게이지 충전 등은 구독자 소관)
            if (dealt > 0 && attacker != null && attacker.alive)
                OnDamageDealt?.Invoke(attacker.id, dealt);

            OnStrikeResolved?.Invoke(strike, hit);
        }

        void Damage(UnitState unit, int amount, Coord hitDir = default, int attackerId = Cell.NoUnit)
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
                OnUnitKilled?.Invoke(unit.id, attackerId); // 킬러 귀속 (환경사 = NoUnit)
            }
        }

        /// <summary>
        /// 스킬의 밀침 규격 — 칸수, 벽꿍 추가 피해, 그리고 밀리는 대상(self=true면 시전자 본인).
        /// TrySkill과 조준 미리보기가 같은 표를 쓰게 하는 단일 출처 — 둘이 어긋나면 연계 설계가 거짓말이 된다.
        /// </summary>
        public static void PushSpecOf(SkillKind kind, out int cells, out int wallBonus, out bool self)
        {
            switch (kind)
            {
                case SkillKind.ShieldPush: cells = 2; wallBonus = 1; self = false; return;
                case SkillKind.Smash: cells = 1; wallBonus = 0; self = false; return;
                case SkillKind.KnockShot: cells = 2; wallBonus = 0; self = true; return;   // 본인이 반대로 후퇴
                default: cells = 0; wallBonus = 0; self = false; return;
            }
        }

        /// <summary>
        /// 밀침 결과 미리보기 — 실제 Push와 같은 규칙으로 도착 칸을 계산한다(상태 변경 없음).
        /// 연계 설계의 토대: "여기 맞추면 저기로 밀린다"를 예고·조준에 그리려면 코어와 답이 같아야 한다.
        /// </summary>
        public Coord PreviewPush(Coord from, Coord dir, int cells, out bool wallCrash)
        {
            wallCrash = false;
            if (dir == Coord.Zero || cells <= 0) return from;
            var pos = from;
            for (int i = 0; i < cells; i++)
            {
                var next = pos + dir;
                bool uphill = State.Grid.IsHighland(next) && !State.Grid.IsHighland(pos);
                if (!State.Grid.IsWalkableTerrain(next) || uphill) { wallCrash = true; return pos; }
                if (State.Grid.GetUnitAt(next) != Cell.NoUnit) return pos; // 유닛에 막힘 — 추가 피해 없음
                pos = next;
            }
            return pos;
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
