using System.Collections.Generic;

namespace SeoYuGi.Battle
{
    /// <summary>
    /// 무전 프리셋 — 버튼 한 번으로 나가는 상시 명령.
    ///
    /// 프리셋은 LLM을 거치지 않는다. 즉시 실행되고, 비용이 없고, 오프라인에서도 동작한다.
    /// 자유 서술 명령이 실패하거나 네트워크가 없어도 지휘관 모드 전체가 프리셋만으로 성립한다.
    ///
    /// 거점 지목 프리셋("A 거점으로 모여")은 맵마다 거점 수가 달라 고정 배열로 둘 수 없다.
    /// For(zoneCount)가 그 맵에 맞는 목록을 만들어 준다.
    /// </summary>
    public static class OrderPresets
    {
        public enum Kind
        {
            Scatter,      // 흩어져 — 거점별로 갈라 보낸다
            RegroupOnMe,  // 나에게 모여
            EachZone,     // 각 거점으로 (교전 회피하며 점거 우선)
            Highland,     // 고지대 선점
            Avoid,        // 교전 회피 (목적지는 유지)
            Aggressive,   // 적극 교전 (목적지는 유지)
            GatherZone,   // 지정 거점으로 전원 집결
            Fallback      // 후퇴 — 지휘관 쪽으로 물러나며 교전 회피
        }

        public struct Preset
        {
            public Kind kind;
            public int zone;      // GatherZone 일 때만 의미 있음
            public string label;  // 버튼에 찍히는 말
            public string ack;    // 무전 응답
        }

        static readonly string[] ZoneLetters = { "A", "B", "C", "D", "E" };

        public static string ZoneName(int i) =>
            i >= 0 && i < ZoneLetters.Length ? ZoneLetters[i] : (i + 1).ToString();

        /// <summary>이 맵에서 낼 수 있는 프리셋 목록. 거점 지목은 거점 수만큼 생긴다.</summary>
        public static List<Preset> For(int zoneCount)
        {
            var list = new List<Preset>
            {
                new Preset { kind = Kind.RegroupOnMe, label = "나에게 모여!",     ack = "지휘관께 붙습니다." },
                new Preset { kind = Kind.Scatter,     label = "흩어져!",          ack = "산개합니다." },
                new Preset { kind = Kind.EachZone,    label = "각 거점으로!",     ack = "거점별로 전개합니다." },
                new Preset { kind = Kind.Highland,    label = "고지대를 먹어!",   ack = "고지대로 올라갑니다." },
                new Preset { kind = Kind.Avoid,       label = "교전하지 마!",     ack = "교전을 피하겠습니다." },
                new Preset { kind = Kind.Aggressive,  label = "적극적으로 붙어!", ack = "교전에 들어갑니다." },
            };
            for (int z = 0; z < zoneCount; z++)
                list.Add(new Preset
                {
                    kind = Kind.GatherZone,
                    zone = z,
                    label = $"{ZoneName(z)} 거점으로 모여!",
                    ack = $"{ZoneName(z)} 거점으로 집결합니다."
                });
            return list;
        }

        /// <summary>프리셋을 실제 명령으로 편다. squad = 지휘 대상 유닛 id(플레이어 본인 제외).</summary>
        public static SquadOrders Build(Preset p, IReadOnlyList<int> squad, int zoneCount)
        {
            var s = new SquadOrders { ack = p.ack };
            for (int i = 0; i < squad.Count; i++)
            {
                var o = UnitOrder.Free(squad[i]);
                switch (p.kind)
                {
                    case Kind.Scatter:
                        o.goal = OrderGoal.Zone;
                        o.zoneIndex = zoneCount > 0 ? i % zoneCount : 0;
                        o.stance = OrderStance.Normal;
                        break;
                    case Kind.RegroupOnMe:
                        o.goal = OrderGoal.Regroup;
                        o.stance = OrderStance.Normal;
                        break;
                    case Kind.EachZone:
                        o.goal = OrderGoal.Zone;
                        o.zoneIndex = zoneCount > 0 ? i % zoneCount : 0;
                        o.stance = OrderStance.Evasive;
                        break;
                    case Kind.Highland:
                        o.goal = OrderGoal.Highland;
                        o.stance = OrderStance.Normal;
                        break;
                    case Kind.Avoid:   // 목적지는 그대로 두고 자세만 바꾼다
                        o.goal = OrderGoal.Free;
                        o.stance = OrderStance.Evasive;
                        break;
                    case Kind.Aggressive:
                        o.goal = OrderGoal.Free;
                        o.stance = OrderStance.Aggressive;
                        break;
                    case Kind.GatherZone:
                        o.goal = OrderGoal.Zone;
                        o.zoneIndex = p.zone;
                        o.stance = OrderStance.Normal;
                        break;
                    case Kind.Fallback:
                        o.goal = OrderGoal.Fallback;
                        o.stance = OrderStance.Evasive;
                        break;
                }
                s.orders.Add(o);
            }
            return s;
        }
    }
}
