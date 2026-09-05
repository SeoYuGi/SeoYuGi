using System;
using System.Collections.Generic;

namespace SeoYuGi.Battle
{
    /// <summary>유닛이 어디를 향할지. 전투 방식이 아니라 목적지만 정한다.</summary>
    public enum OrderGoal
    {
        Free = 0,    // 명령 없음 — 기존 자율 판단 그대로
        Zone,        // 지정 거점으로 (zoneIndex)
        Highland,    // 가까운 고지대 선점
        Regroup,     // 지휘관(플레이어) 곁으로 — "모여"
        Fallback     // 아군 스폰 쪽으로 물러남 — "빠져"
    }

    /// <summary>
    /// 교전 자세. 사거리·스킬·조준은 건드리지 않는다 —
    /// "적을 찾아 나설 것인가"와 "먼저 쏠 것인가"만 정한다.
    /// </summary>
    public enum OrderStance
    {
        Normal = 0,
        Aggressive,  // 적극 교전 — 적을 향해 가고 쿨 돌면 바로 쏜다
        Evasive      // 교전 회피 — 먼저 쏘지 않고 목적지 수행에 집중
    }

    /// <summary>유닛 1기에게 내린 상시 명령. 다음 명령이 오거나 라운드가 끝날 때까지 유지된다.</summary>
    [Serializable]
    public struct UnitOrder
    {
        public int unitId;
        public OrderGoal goal;
        public int zoneIndex;     // goal == Zone 일 때만 의미 있음
        public OrderStance stance;
        public int focusEnemyId;  // 이 유닛이 우선 노릴 적 unitId. -1 = 없음. 시야 규칙은 그대로 — 보여야 쏜다.
        public bool persistent;   // true = "매치 내내" — 라운드가 바뀌어도 유지. false(기본) = 이번 라운드만.

        public static UnitOrder Free(int unitId) =>
            new UnitOrder { unitId = unitId, goal = OrderGoal.Free, zoneIndex = -1, stance = OrderStance.Normal, focusEnemyId = -1, persistent = false };
    }

    /// <summary>
    /// 한 번의 무전으로 내려간 명령 묶음.
    /// understood == false 면 유닛은 자유의지로 둔다 — 알아듣지 못한 명령을 억지로 실행하지 않는다.
    /// </summary>
    public class SquadOrders
    {
        public bool understood = true;
        public bool refused;                          // 분대가 명령을 거부했다 (understood=false와 함께) — 연출·로그 구분용
        public string ack = "";                       // 무전 응답 한 줄 (프리셋은 고정 문구, 자유 서술은 LLM이 쓴다)
        public List<UnitOrder> orders = new List<UnitOrder>();

        public static SquadOrders NotUnderstood(string reason) =>
            new SquadOrders { understood = false, ack = reason };
    }

    /// <summary>
    /// 지휘 상태 — 유닛별 상시 명령 보관소. 순수 C#.
    /// 명령이 없는 유닛은 항상 Free이므로, 지휘를 한 번도 안 해도 기존 동작과 100% 같다.
    /// </summary>
    public class CommandState
    {
        readonly Dictionary<int, UnitOrder> byUnit = new Dictionary<int, UnitOrder>();

        /// <summary>마지막 무전 응답 — HUD 표시용.</summary>
        public string LastAck { get; private set; } = "";

        /// <summary>핑 포커스 — 지휘관이 핑 찍은 적. AiBrain이 타겟 선정에서 최우선한다. -1 = 없음.</summary>
        public int FocusEnemyId { get; private set; } = -1;

        /// <summary>포커스 만료 시각 (전투 시계 기준).</summary>
        public float FocusUntil { get; private set; }

        public void SetFocus(int enemyId, float until)
        {
            FocusEnemyId = enemyId;
            FocusUntil = until;
        }

        public UnitOrder Get(int unitId) =>
            byUnit.TryGetValue(unitId, out var o) ? o : UnitOrder.Free(unitId);

        public bool HasOrder(int unitId) =>
            byUnit.TryGetValue(unitId, out var o) && o.goal != OrderGoal.Free;

        public void Apply(SquadOrders squad)
        {
            LastAck = squad.ack;
            if (!squad.understood) return; // 못 알아들었다 — 기존 명령도 건드리지 않는다
            foreach (var o in squad.orders) byUnit[o.unitId] = o;
        }

        /// <summary>라운드 시작 = 백지. 단 "매치 내내"(persistent) 명령은 라운드를 넘어 유지된다.</summary>
        public void Clear()
        {
            var keep = new List<UnitOrder>();
            foreach (var kv in byUnit)
                if (kv.Value.persistent) keep.Add(kv.Value);
            byUnit.Clear();
            foreach (var o in keep) byUnit[o.unitId] = o;
            LastAck = "";
            FocusEnemyId = -1;
            FocusUntil = 0f;
        }
    }
}
