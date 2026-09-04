using System.Collections.Generic;
using SeoYuGi.Battle;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 실시간 이동 입력 (탱고파이브식):
    /// 유닛 좌클릭 → 선택 + 범위 표시(파랑=게이지 이내, 노랑=쿨타임 발생).
    /// 범위 타일 좌클릭 → 즉시 이동. 우클릭 = 선택 해제.
    /// 게이지가 실시간 회복되므로 선택 중엔 매 프레임 범위를 갱신한다.
    /// </summary>
    public class UnitMoveInput : MonoBehaviour
    {
        [SerializeField] Camera rayCamera; // 비우면 Camera.main
        [SerializeField] Color blueRangeColor = new Color(0.3f, 0.6f, 1f);
        [SerializeField] Color yellowRangeColor = new Color(1f, 0.85f, 0.2f);

        MoveSystem moveSystem;
        GridView gridView;
        UnitViewRegistry views;

        int selectedUnitId = -1;
        readonly List<Coord> blue = new List<Coord>();
        readonly List<Coord> yellow = new List<Coord>();
        readonly List<Coord> cells = new List<Coord>();
        readonly List<Color> colors = new List<Color>();

        public void Init(MoveSystem moveSystem, GridView gridView, UnitViewRegistry views)
        {
            this.moveSystem = moveSystem;
            this.gridView = gridView;
            this.views = views;
            if (rayCamera == null) rayCamera = Camera.main;
        }

        void Update()
        {
            if (moveSystem == null || Mouse.current == null) return;

            if (Mouse.current.leftButton.wasPressedThisFrame) HandleClick();
            if (Mouse.current.rightButton.wasPressedThisFrame) Deselect();

            RefreshHighlights(); // 게이지 회복/쿨타임 종료가 실시간이라 매 프레임 갱신
        }

        void HandleClick()
        {
            var ray = rayCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out var hit, 500f)) return;

            var unitView = hit.collider.GetComponentInParent<UnitView>();
            if (unitView != null)
            {
                Select(unitView.UnitId);
                return;
            }
            TryMove(gridView.WorldToCoord(hit.point));
        }

        void Select(int unitId)
        {
            if (selectedUnitId == unitId) return;
            Deselect();
            selectedUnitId = unitId;
            views.Get(unitId)?.SetSelected(true);
        }

        void Deselect()
        {
            if (selectedUnitId != -1)
                views.Get(selectedUnitId)?.SetSelected(false);
            selectedUnitId = -1;
            gridView.ClearHighlights();
        }

        void TryMove(Coord dest)
        {
            if (selectedUnitId == -1) return;
            var view = views.Get(selectedUnitId);
            if (view != null && view.IsMoving) return; // 연출 중 연타 방지

            moveSystem.TryMove(selectedUnitId, dest);
            // 성공 시 연출은 MoveSystem.OnUnitMoved → BattleRunner가 재생
        }

        void RefreshHighlights()
        {
            if (selectedUnitId == -1) return;

            moveSystem.GetRanges(selectedUnitId, blue, yellow);
            cells.Clear();
            colors.Clear();
            foreach (var c in blue) { cells.Add(c); colors.Add(blueRangeColor); }
            foreach (var c in yellow) { cells.Add(c); colors.Add(yellowRangeColor); }
            gridView.SetHighlights(cells, colors);
        }
    }
}
