using System.Collections.Generic;

namespace SeoYuGi.Battle
{
    /// <summary>
    /// 무전 프리셋 — 버튼 한 번으로 나가는 상시 명령.
    ///
    /// 프리셋은 LLM을 거치지 않는다. 즉시 실행되고, 비용이 없고, 오프라인에서도 동작한다.
    /// 자유 서술 명령이 실패하거나 네트워크가 없어도 지휘관 모드 전체가 프리셋만으로 성립한다.
    /// 자유 서술은 프리셋으로 표현할 수 없는 상황에만 쓰는 확장이다.
    /// </summary>
    public static class OrderPresets
    {
        public struct Preset
        {
            public string label;   // 버튼에 찍히는 말
            public string ack;     // 무전 응답
        }

        /// <summary>버튼 순서 = 이 배열 순서. 인덱스로 Build를 호출한다.</summary>
        public static readonly Preset[] All =
        {
            new Preset { label = "흩어져!",        ack = "산개합니다." },
            new Preset { label = "모여!",          ack = "지휘관께 붙습니다." },
            new Preset { label = "각 거점으로!",    ack = "거점별로 전개합니다." },
            new Preset { label = "고지대를 먹어!",  ack = "고지대로 올라갑니다." },
            new Preset { label = "교전하지 마!",    ack = "교전을 피하겠습니다." },
            new Preset { label = "적극적으로 붙어!", ack = "교전에 들어갑니다." },
        };

        /// <summary>
        /// 프리셋을 실제 명령으로 편다.
        /// squad = 지휘 대상 유닛 id (플레이어 본인 제외), zoneCount = 이 맵의 거점 수.
        /// </summary>
        public static SquadOrders Build(int index, IReadOnlyList<int> squad, int zoneCount)
        {
            var s = new SquadOrders();
            if (index < 0 || index >= All.Length) return SquadOrders.NotUnderstood("알 수 없는 명령입니다.");
            s.ack = All[index].ack;

            for (int i = 0; i < squad.Count; i++)
            {
                var o = UnitOrder.Free(squad[i]);
                switch (index)
                {
                    case 0: // 흩어져 — 서로 다른 거점으로 나눠 보낸다
                        o.goal = OrderGoal.Zone;
                        o.zoneIndex = zoneCount > 0 ? i % zoneCount : 0;
                        o.stance = OrderStance.Normal;
                        break;
                    case 1: // 모여 — 지휘관 곁으로
                        o.goal = OrderGoal.Regroup;
                        o.stance = OrderStance.Normal;
                        break;
                    case 2: // 각 거점으로 — 흩어져와 목적은 같지만 교전을 피하며 점거 우선
                        o.goal = OrderGoal.Zone;
                        o.zoneIndex = zoneCount > 0 ? i % zoneCount : 0;
                        o.stance = OrderStance.Evasive;
                        break;
                    case 3: // 고지대
                        o.goal = OrderGoal.Highland;
                        o.stance = OrderStance.Normal;
                        break;
                    case 4: // 교전 회피 — 목적지는 그대로 두고 자세만 바꾼다
                        o.goal = OrderGoal.Free;
                        o.stance = OrderStance.Evasive;
                        break;
                    case 5: // 적극 교전
                        o.goal = OrderGoal.Free;
                        o.stance = OrderStance.Aggressive;
                        break;
                }
                s.orders.Add(o);
            }
            return s;
        }
    }
}
