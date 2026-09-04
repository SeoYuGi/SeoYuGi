using NUnit.Framework;

namespace SeoYuGi.Battle.Tests
{
    /// <summary>설치형 공격 + 스킬 2개 체제 (캐릭터 기획 v1.7). 방어·AP 삭제 — 평타는 쿨다운, 적중은 OnDamageDealt.</summary>
    public class CombatSystemTests
    {
        BattleState battle;
        CombatConfig config;

        CombatSystem NewCombat() => new CombatSystem(battle, config);

        UnitState Add(int id, int team, Coord pos, UnitClass cls = UnitClass.Balance)
        {
            var u = new UnitState(id, team, pos, cls);
            battle.AddUnit(u);
            return u;
        }

        [SetUp]
        public void SetUp()
        {
            battle = new BattleState(new GridModel(new GridConfig()));
            config = new CombatConfig();
        }

        [Test]
        public void Attack_Cooldown_BlocksUntilElapsed()
        {
            Add(1, 0, new Coord(5, 5));
            Add(2, 1, new Coord(5, 6));
            var combat = NewCombat();

            Assert.AreEqual(ActDenied.None, combat.TryAttack(1, new Coord(5, 6)));
            Assert.AreEqual(ActDenied.Cooldown, combat.TryAttack(1, new Coord(5, 6))); // 쿨 2초
            combat.Tick(2f); // 쿨 경과
            Assert.AreEqual(ActDenied.None, combat.TryAttack(1, new Coord(5, 6)));
        }

        [Test]
        public void Attack_TelegraphThenDamage_FiresDamageDealt()
        {
            Add(1, 0, new Coord(5, 5));
            var target = Add(2, 1, new Coord(5, 6));
            var combat = NewCombat();
            int dealtBy = -1, dealtSum = 0;
            combat.OnDamageDealt += (attackerId, dmg) => { dealtBy = attackerId; dealtSum += dmg; };

            Assert.AreEqual(ActDenied.None, combat.TryAttack(1, new Coord(5, 6)));
            Assert.AreEqual(1, combat.ActiveStrikes.Count); // 예고 중 — 아직 피해 없음
            Assert.AreEqual(10, target.hp);
            Assert.AreEqual(0, dealtSum);

            combat.Tick(0.5f); // 판정

            Assert.AreEqual(9, target.hp);
            Assert.AreEqual(0, combat.ActiveStrikes.Count);
            Assert.AreEqual(1, dealtBy);   // 적중 = 예측 성공 → 보상 훅 발화
            Assert.AreEqual(1, dealtSum);  // 가한 피해 합
        }

        [Test]
        public void Attack_DiagonalAdjacent_AllowedForMelee8()
        {
            Add(1, 0, new Coord(5, 5)); // Balance = Melee8 — 대각 인접 허용
            var target = Add(2, 1, new Coord(6, 6));
            var combat = NewCombat();

            Assert.AreEqual(ActDenied.None, combat.TryAttack(1, new Coord(6, 6)));
            combat.Tick(0.5f);
            Assert.AreEqual(9, target.hp);
        }

        [Test]
        public void Attack_EmptyCellAtImpact_NoDamageDealt()
        {
            Add(1, 0, new Coord(5, 5));
            var combat = NewCombat();
            int dealtSum = 0;
            combat.OnDamageDealt += (_, dmg) => dealtSum += dmg;

            combat.TryAttack(1, new Coord(5, 6)); // 빈 칸에 설치
            combat.Tick(0.5f);

            Assert.AreEqual(0, dealtSum); // 빗나감 — 보상 훅 없음
        }

        [Test]
        public void Attack_Invalid_Denied()
        {
            Add(1, 0, new Coord(5, 5));
            Add(2, 1, new Coord(5, 7));
            var combat = NewCombat();

            Assert.AreEqual(ActDenied.BadTarget, combat.TryAttack(1, new Coord(5, 7))); // Melee8 밖 (거리 2)
        }

        [Test]
        public void RangedAttack_Sniper_HitsAtRange2()
        {
            Add(1, 0, new Coord(5, 5), UnitClass.Sniper); // Square2 — 5×5 링
            var target = Add(2, 1, new Coord(7, 7));
            var combat = NewCombat();

            Assert.AreEqual(ActDenied.None, combat.TryAttack(1, new Coord(7, 7)));
            combat.Tick(0.5f);
            Assert.AreEqual(9, target.hp);
        }

        [Test]
        public void Flying_BlocksDamage_NoDamageDealt()
        {
            Add(1, 0, new Coord(5, 5));
            var defender = Add(2, 1, new Coord(5, 6));
            var combat = NewCombat();
            int dealtSum = 0;
            combat.OnDamageDealt += (_, dmg) => dealtSum += dmg;

            combat.TryAttack(1, new Coord(5, 6));
            defender.flyingUntil = 999f; // 비행 중 무적 (폭탄 배달)

            combat.Tick(0.5f);

            Assert.AreEqual(10, defender.hp); // 무효
            Assert.AreEqual(0, dealtSum);     // 보상 훅 없음
        }

        [Test]
        public void Scream_StunsAdjacent_AndStunRootsUnit()
        {
            Add(1, 0, new Coord(5, 5), UnitClass.Balance);
            var enemy = Add(2, 1, new Coord(5, 6));
            var combat = NewCombat();
            var move = new MoveSystem(battle, new MoveConfig());

            Assert.AreEqual(ActDenied.None, combat.TrySkill(1, 1, new Coord(5, 5))); // 비명 교란
            combat.Tick(0.4f); // 예고 → 판정

            Assert.Greater(enemy.stunnedUntil, battle.time);
            Assert.AreEqual(MoveDenied.Locked, move.TryMove(2, new Coord(5, 7)).denied);

            combat.Tick(1.1f); // 스턴 종료
            Assert.IsTrue(move.TryMove(2, new Coord(5, 7)).success);
        }

        [Test]
        public void LethalDamage_KillsAndClearsGrid()
        {
            Add(1, 0, new Coord(5, 5));
            var target = Add(2, 1, new Coord(5, 6));
            target.hp = 1;
            var combat = NewCombat();
            int died = -1;
            combat.OnUnitDied += id => died = id;

            combat.TryAttack(1, new Coord(5, 6));
            combat.Tick(0.5f);

            Assert.IsFalse(target.alive);
            Assert.AreEqual(2, died);
            Assert.AreEqual(Cell.NoUnit, battle.Grid.GetUnitAt(new Coord(5, 6)));
        }

        [Test]
        public void ShieldPush_PushesTwo_WallCollisionBonus()
        {
            Add(1, 0, new Coord(1, 5), UnitClass.Tank);
            var target = Add(2, 1, new Coord(0, 5), UnitClass.Balance); // 등 뒤가 맵 경계
            var combat = NewCombat();

            Assert.AreEqual(ActDenied.None, combat.TrySkill(1, 0, new Coord(0, 5)));
            combat.Tick(0.6f);

            Assert.AreEqual(10 - 1 - 1, target.hp); // 피해 1 + 벽 충돌 1
            Assert.AreEqual(new Coord(0, 5), target.pos); // 밀 곳 없음 — 제자리
        }

        [Test]
        public void ShieldPush_PushesTwoCells_WhenOpen()
        {
            Add(1, 0, new Coord(5, 5), UnitClass.Tank);
            var target = Add(2, 1, new Coord(5, 6));
            var combat = NewCombat();

            combat.TrySkill(1, 0, new Coord(5, 6));
            combat.Tick(0.6f);

            Assert.AreEqual(9, target.hp);                // 벽 충돌 없음
            Assert.AreEqual(new Coord(5, 8), target.pos); // 2칸 밀림
            Assert.AreEqual(2, battle.Grid.GetUnitAt(new Coord(5, 8)));
        }

        [Test]
        public void Smash_Damage2_PushesOne()
        {
            Add(1, 0, new Coord(5, 5), UnitClass.Tank);
            var target = Add(2, 1, new Coord(5, 6));
            var combat = NewCombat();

            Assert.AreEqual(ActDenied.None, combat.TrySkill(1, 1, new Coord(5, 6)));
            combat.Tick(0.6f);

            Assert.AreEqual(10 - 2, target.hp);
            Assert.AreEqual(new Coord(5, 7), target.pos); // 1칸 밀림
        }

        [Test]
        public void Skill_Cooldown_Denied()
        {
            Add(1, 0, new Coord(5, 5), UnitClass.Tank);
            Add(2, 1, new Coord(5, 6));
            var combat = NewCombat();

            Assert.AreEqual(ActDenied.None, combat.TrySkill(1, 0, new Coord(5, 6)));
            Assert.AreEqual(ActDenied.Cooldown, combat.TrySkill(1, 0, new Coord(5, 6))); // 쿨 진행 중
        }

        [Test]
        public void Dash_DamagesPushesAndAdvances()
        {
            var caster = Add(1, 0, new Coord(0, 0), UnitClass.Balance);
            var enemy = Add(2, 1, new Coord(1, 0));
            var combat = NewCombat();

            Assert.AreEqual(ActDenied.None, combat.TrySkill(1, 0, new Coord(2, 0))); // 직선 2칸

            Assert.AreEqual(9, enemy.hp);                 // 즉시 판정
            Assert.AreEqual(new Coord(2, 0), enemy.pos);  // 1칸 밀림
            Assert.AreEqual(new Coord(1, 0), caster.pos); // 밀린 자리로 전진
        }

        [Test]
        public void Blink_TeleportsAndBuffsAttack()
        {
            var caster = Add(1, 0, new Coord(5, 5), UnitClass.Assassin);
            var enemy = Add(2, 1, new Coord(5, 8));
            var combat = NewCombat();

            Assert.AreEqual(ActDenied.None, combat.TrySkill(1, 0, new Coord(5, 7))); // 2칸 점멸
            Assert.AreEqual(new Coord(5, 7), caster.pos);

            combat.TryAttack(1, new Coord(5, 8)); // 버프 중 일반공격 +1
            combat.Tick(0.5f);

            Assert.AreEqual(10 - 2, enemy.hp);
        }

        [Test]
        public void Claw_Damage3()
        {
            Add(1, 0, new Coord(5, 5), UnitClass.Assassin);
            var enemy = Add(2, 1, new Coord(5, 6));
            var combat = NewCombat();

            Assert.AreEqual(ActDenied.None, combat.TrySkill(1, 1, new Coord(5, 6)));
            combat.Tick(0.8f);

            Assert.AreEqual(10 - 3, enemy.hp);
        }

        [Test]
        public void Burst_HitsCrossCells()
        {
            Add(1, 0, new Coord(1, 0), UnitClass.Grenadier);
            var atCenter = Add(2, 1, new Coord(3, 0));
            var atArm = Add(3, 1, new Coord(3, 1));
            var outside = Add(4, 1, new Coord(3, 2));
            var combat = NewCombat();

            Assert.AreEqual(ActDenied.None, combat.TrySkill(1, 0, new Coord(3, 0))); // 5×5 내
            combat.Tick(0.8f);

            Assert.AreEqual(9, atCenter.hp);
            Assert.AreEqual(9, atArm.hp);
            Assert.AreEqual(10, outside.hp); // 십자 밖 — 무피해
        }

        [Test]
        public void BombDeliver_MovesFlies_ThenBlasts()
        {
            var caster = Add(1, 0, new Coord(0, 0), UnitClass.Grenadier);
            var enemy = Add(2, 1, new Coord(3, 1));
            var combat = NewCombat();

            Assert.AreEqual(ActDenied.None, combat.TrySkill(1, 1, new Coord(3, 0))); // 맨해튼 3 ≤ 4
            Assert.AreEqual(new Coord(3, 0), caster.pos); // 착지 칸 선점
            Assert.IsTrue(caster.flyingUntil > battle.time); // 비행 중 무적

            combat.Tick(1f); // 착지 + 폭발

            Assert.AreEqual(10 - 2, enemy.hp); // 십자 팔에 명중
            Assert.IsFalse(caster.flyingUntil > battle.time);
        }

        [Test]
        public void KnockShot_InstantDamage_SelfKnockback()
        {
            var caster = Add(1, 0, new Coord(5, 5), UnitClass.Sniper);
            var enemy = Add(2, 1, new Coord(5, 6));
            var combat = NewCombat();

            Assert.AreEqual(ActDenied.None, combat.TrySkill(1, 0, new Coord(5, 6)));

            Assert.AreEqual(9, enemy.hp);                 // 즉발 피해 1
            Assert.AreEqual(new Coord(5, 3), caster.pos); // 본인 2칸 후퇴
        }

        [Test]
        public void Snipe_PiercesLine_StopsAtWall_MaxRange5()
        {
            Add(1, 0, new Coord(0, 5), UnitClass.Sniper);
            var near = Add(2, 1, new Coord(2, 5));
            battle.Grid.SetObstacle(new Coord(3, 5));
            var behindWall = Add(3, 1, new Coord(4, 5));
            var farBeyond = Add(4, 1, new Coord(6, 5)); // 사거리 5 밖
            var combat = NewCombat();

            Assert.AreEqual(ActDenied.None, combat.TrySkill(1, 1, new Coord(1, 5))); // 방향 지정
            combat.Tick(0.8f);

            Assert.AreEqual(10 - 3, near.hp);   // 관통 피해 3
            Assert.AreEqual(10, behindWall.hp); // 벽 뒤 안전 (평지 사수)
            Assert.AreEqual(10, farBeyond.hp);  // 사거리 밖
        }

        [Test]
        public void KnockShot_InstantHit_FiresDamageDealt()
        {
            Add(1, 0, new Coord(5, 5), UnitClass.Sniper);
            Add(2, 1, new Coord(5, 6));
            var combat = NewCombat();
            int dealtBy = -1, dealtSum = 0;
            combat.OnDamageDealt += (attackerId, dmg) => { dealtBy = attackerId; dealtSum += dmg; };

            Assert.AreEqual(ActDenied.None, combat.TrySkill(1, 0, new Coord(5, 6)));

            Assert.AreEqual(1, dealtBy);  // 즉발 명중도 예측 성공 취급
            Assert.AreEqual(1, dealtSum);
        }
    }
}
