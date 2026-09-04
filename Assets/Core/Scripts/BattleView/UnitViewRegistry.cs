using System.Collections.Generic;
using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>unitId → UnitView 조회.</summary>
    public class UnitViewRegistry : MonoBehaviour
    {
        readonly Dictionary<int, UnitView> views = new Dictionary<int, UnitView>();

        public void Register(UnitView view) => views[view.UnitId] = view;

        public UnitView Get(int unitId) => views.TryGetValue(unitId, out var v) ? v : null;

        public IEnumerable<UnitView> All => views.Values;

        /// <summary>라운드 재시작 — 파괴된 뷰 참조 제거.</summary>
        public void Clear() => views.Clear();
    }
}
