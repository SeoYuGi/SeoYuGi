using System.Collections.Generic;
using NUnit.Framework;

namespace SeoYuGi.Battle.Tests
{
    /// <summary>클래스 5종 스탯 (캐릭터 기획 v1.7: HP 15/10/8/8/5, 시야 3/4/4/4/5).</summary>
    public class UnitClassTests
    {
        [TestCase(UnitClass.Tank, 15, 3)]
        [TestCase(UnitClass.Balance, 10, 4)]
        [TestCase(UnitClass.Assassin, 8, 4)]
        [TestCase(UnitClass.Grenadier, 8, 4)]
        [TestCase(UnitClass.Sniper, 5, 5)]
        public void Catalog_MatchesSpec(UnitClass cls, int hp, int sight)
        {
            var def = ClassCatalog.Get(cls);
            Assert.AreEqual(hp, def.maxHp);
            Assert.AreEqual(sight, def.sightRange);
            Assert.IsNotNull(def.move);
        }

        [Test]
        public void UnitState_InitializedFromClass()
        {
            var unit = new UnitState(1, 0, new Coord(0, 0), UnitClass.Tank);

            Assert.AreEqual(UnitClass.Tank, unit.unitClass);
            Assert.AreEqual(15, unit.hp);
            Assert.AreEqual(15, unit.maxHp);
            Assert.AreEqual(3, unit.sightRange);
            Assert.AreSame(ClassCatalog.Get(UnitClass.Tank).move, unit.profile);
        }

        [Test]
        public void ClassProfile_DrivesMoveRanges()
        {
            var battle = new BattleState(new GridModel(new GridConfig()));
            battle.AddUnit(new UnitState(1, 0, new Coord(5, 5), UnitClass.Tank));     // 파랑 1 / 최대 3
            battle.AddUnit(new UnitState(2, 0, new Coord(0, 0), UnitClass.Assassin)); // 파랑 3 / 최대 5
            var move = new MoveSystem(battle, new MoveConfig());

            var blue = new List<Coord>();
            var yellow = new List<Coord>();

            move.GetRanges(1, blue, yellow);
            foreach (var c in blue) Assert.LessOrEqual(Coord.Manhattan(new Coord(5, 5), c), 1);
            foreach (var c in yellow) Assert.LessOrEqual(Coord.Manhattan(new Coord(5, 5), c), 3);

            move.GetRanges(2, blue, yellow);
            foreach (var c in blue) Assert.LessOrEqual(Coord.Manhattan(new Coord(0, 0), c), 3);
            foreach (var c in yellow) Assert.LessOrEqual(Coord.Manhattan(new Coord(0, 0), c), 5);
        }
    }
}
