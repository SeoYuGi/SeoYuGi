using NUnit.Framework;

namespace SeoYuGi.Battle.Tests
{
    /// <summary>AP 경제 + 설치형 공격 + 방어 + 스킬 5종.</summary>
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
        public void ApRegen_ClampedAtMax()
        {
            var u = Add(1, 0, new Coord(5, 5));
            var combat = NewCombat();

            Assert.AreEqual(5f, u.ap, 0.001f); // 시작 = 최대
            u.ap = 2f;
            combat.Tick(1f);
            Assert.AreEqual(3f, u.ap, 0.001f); // 초당 1 회복
            combat.Tick(100f);
            Assert.AreEqual(5f, u.ap, 0.001f); // 클램프
        }

        [Test]
        public void Attack_TelegraphThenDamage_WithRefund()
        {
            var attacker = Add(1, 0, new Coord(5, 5));
            var target = Add(2, 1, new Coord(5, 6));
            var combat = NewCombat();

            Assert.AreEqual(ActDenied.None, combat.TryAttack(1, new Coord(5, 6)));
            Assert.AreEqual(3f, attacker.ap, 0.001f);       // 비용 2
            Assert.AreEqual(1, combat.ActiveStrikes.Count); // 예고 중 — 아직 피해 없음
            Assert.AreEqual(4, target.hp);

            combat.Tick(0.5f); // 판정

            Assert.AreEqual(3, target.hp);
            Assert.AreEqual(0, combat.ActiveStrikes.Count);
            Assert.AreEqual(3f + 0.5f + 1f, attacker.ap, 0.001f); // 회복 0.5 + 적중 환급 1
        }

        [Test]
        public void Attack_EmptyCellAtImpact_NoRefund()
        {
            var attacker = Add(1, 0, new Coord(5, 5));
            var combat = NewCombat();

            combat.TryAttack(1, new Coord(5, 6)); // 빈 칸에 설치
            combat.Tick(0.5f);

            Assert.AreEqual(3f + 0.5f, attacker.ap, 0.001f); // 환급 없음
        }

        [Test]
        public void Attack_Invalid_Denied()
        {
            var attacker = Add(1, 0, new Coord(5, 5));
            Add(2, 1, new Coord(5, 7));
            var combat = NewCombat();

            Assert.AreEqual(ActDenied.BadTarget, combat.TryAttack(1, new Coord(5, 7))); // 비인접
            attacker.ap = 1f;
            Assert.AreEqual(ActDenied.NoAp, combat.TryAttack(1, new Coord(5, 6)));
        }

        [Test]
        public void Guard_BlocksDamage_NoRefund()
        {
            var attacker = Add(1, 0, new Coord(5, 5));
            var defender = Add(2, 1, new Coord(5, 6));
            var combat = NewCombat();

            combat.TryAttack(1, new Coord(5, 6));
            combat.TryGuard(2); // 0.5초 무효 — 판정 시각(+0.5)까지 커버

            combat.Tick(0.5f);

            Assert.AreEqual(4, defender.hp); // 무효
            Assert.AreEqual(3f + 0.5f, attacker.ap, 0.001f); // 환급 없음
        }

        [Test]
        public void Guard_RootsUnit()
        {
            var u = Add(1, 0, new Coord(5, 5));
            var combat = NewCombat();
            var move = new MoveSystem(battle, new MoveConfig());

            combat.TryGuard(1);

            Assert.AreEqual(MoveDenied.Locked, move.TryMove(1, new Coord(5, 6)).denied);
            combat.Tick(0.6f); // 방어 종료
            Assert.IsTrue(move.TryMove(1, new Coord(5, 6)).success);
        }

        [Test]
        public void LethalDamage_KillsAndClearsGrid()
        {
            Add(1, 0, new Coord(5, 5));
            var target = Add(2, 1, new Coord(5, 6), UnitClass.Sniper); // HP 2
            var combat = NewCombat();
            int died = -1;
            combat.OnUnitDied += id => died = id;

            combat.TryAttack(1, new Coord(5, 6));
            combat.Tick(0.5f);
            combat.TryAttack(1, new Coord(5, 6));
            combat.Tick(0.5f);

            Assert.IsFalse(target.alive);
            Assert.AreEqual(2, died);
            Assert.AreEqual(Cell.NoUnit, battle.Grid.GetUnitAt(new Coord(5, 6)));
        }

        [Test]
        public void Smash_PushesTwo_WallCollisionBonus()
        {
            Add(1, 0, new Coord(1, 5), UnitClass.Tank);
            var target = Add(2, 1, new Coord(0, 5), UnitClass.Balance); // 등 뒤가 맵 경계
            var combat = NewCombat();

            Assert.AreEqual(ActDenied.None, combat.TrySkill(1, new Coord(0, 5)));
            combat.Tick(0.6f);

            Assert.AreEqual(4 - 1 - 1, target.hp); // 피해 1 + 벽 충돌 1
            Assert.AreEqual(new Coord(0, 5), target.pos); // 밀 곳 없음 — 제자리
        }

        [Test]
        public void Smash_PushesTwoCells_WhenOpen()
        {
            Add(1, 0, new Coord(5, 5), UnitClass.Tank);
            var target = Add(2, 1, new Coord(5, 6));
            var combat = NewCombat();

            combat.TrySkill(1, new Coord(5, 6));
            combat.Tick(0.6f);

            Assert.AreEqual(3, target.hp);              // 벽 충돌 없음
            Assert.AreEqual(new Coord(5, 8), target.pos); // 2칸 밀림
            Assert.AreEqual(2, battle.Grid.GetUnitAt(new Coord(5, 8)));
        }

        [Test]
        public void Dash_DamagesPushesAndAdvances()
        {
            var caster = Add(1, 0, new Coord(0, 0), UnitClass.Balance);
            var enemy = Add(2, 1, new Coord(1, 0));
            var combat = NewCombat();

            Assert.AreEqual(ActDenied.None, combat.TrySkill(1, new Coord(2, 0))); // 직선 2칸

            Assert.AreEqual(3, enemy.hp);                 // 즉시 판정
            Assert.AreEqual(new Coord(2, 0), enemy.pos);  // 1칸 밀림
            Assert.AreEqual(new Coord(1, 0), caster.pos); // 밀린 자리로 전진, 그 앞에서 정지
        }

        [Test]
        public void Blink_TeleportsAndBuffsAttack()
        {
            var caster = Add(1, 0, new Coord(5, 5), UnitClass.Assassin);
            var enemy = Add(2, 1, new Coord(5, 8));
            var combat = NewCombat();

            Assert.AreEqual(ActDenied.None, combat.TrySkill(1, new Coord(5, 7))); // 2칸 점멸
            Assert.AreEqual(new Coord(5, 7), caster.pos);

            combat.TryAttack(1, new Coord(5, 8)); // 버프 중 일반공격 +1
            combat.Tick(0.5f);

            Assert.AreEqual(4 - 2, enemy.hp);
        }

        [Test]
        public void Burst_HitsCrossCells()
        {
            Add(1, 0, new Coord(0, 0), UnitClass.Grenadier);
            var atCenter = Add(2, 1, new Coord(3, 0));
            var atArm = Add(3, 1, new Coord(3, 1));
            var outside = Add(4, 1, new Coord(3, 2));
            var combat = NewCombat();

            Assert.AreEqual(ActDenied.None, combat.TrySkill(1, new Coord(3, 0))); // 3칸 거리
            combat.Tick(0.8f);

            Assert.AreEqual(3, atCenter.hp);
            Assert.AreEqual(3, atArm.hp);
            Assert.AreEqual(4, outside.hp); // 십자 밖 — 무피해
        }

        [Test]
        public void Snipe_PiercesWholeLine_StopsAtWall()
        {
            Add(1, 0, new Coord(0, 5), UnitClass.Sniper);
            var near = Add(2, 1, new Coord(3, 5));
            var far = Add(3, 1, new Coord(7, 5));
            battle.Grid.SetObstacle(new Coord(9, 5));
            var behindWall = Add(4, 1, new Coord(10, 5));
            var combat = NewCombat();

            Assert.AreEqual(ActDenied.None, combat.TrySkill(1, new Coord(1, 5))); // 방향 지정
            combat.Tick(0.8f);

            Assert.AreEqual(4 - 2, near.hp);  // 관통 피해 2
            Assert.AreEqual(4 - 2, far.hp);
            Assert.AreEqual(4, behindWall.hp); // 벽 뒤 안전
        }

        [Test]
        public void Skill_NoAp_Denied()
        {
            var u = Add(1, 0, new Coord(5, 5), UnitClass.Tank);
            Add(2, 1, new Coord(5, 6));
            var combat = NewCombat();

            u.ap = 2f;
            Assert.AreEqual(ActDenied.NoAp, combat.TrySkill(1, new Coord(5, 6)));
            Assert.AreEqual(2f, u.ap, 0.001f); // 실패 시 소모 없음
        }
    }
}
