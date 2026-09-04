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
        [SerializeField] Color blueRangeColor = new Color(0.3f, 0.6f, 1f);
        [SerializeField] Color yellowRangeColor = new Color(1f, 0.85f, 0.2f);
        [SerializeField] Color telegraphColor = new Color(0.91f, 0.25f, 0.12f); // 설치 공격 예고

        Camera rayCamera; // Camera.main 자동 연결

        MoveSystem moveSystem;
        CombatSystem combat;
        GridView gridView;
        UnitViewRegistry views;

        int selectedUnitId = -1;
        readonly List<Coord> blue = new List<Coord>();
        readonly List<Coord> yellow = new List<Coord>();
        readonly List<Coord> cells = new List<Coord>();
        readonly List<Color> colors = new List<Color>();

        int playerUnitId = -1; // 조작 가능한 유닛 (기획서 §04: 내 캐릭터 1기뿐)

        public void Init(MoveSystem moveSystem, CombatSystem combat, GridView gridView, UnitViewRegistry views, int playerUnitId)
        {
            this.moveSystem = moveSystem;
            this.combat = combat;
            this.gridView = gridView;
            this.views = views;
            this.playerUnitId = playerUnitId;
            rayCamera = Camera.main;
        }

        void Update()
        {
            if (moveSystem == null || Mouse.current == null) return;

            // 선택 유닛이 죽었으면 해제
            if (selectedUnitId != -1)
            {
                var u = moveSystem.State.GetUnit(selectedUnitId);
                if (u == null || !u.alive) Deselect();
            }

            if (Mouse.current.leftButton.wasPressedThisFrame) HandleClick();
            if (Mouse.current.rightButton.wasPressedThisFrame) Deselect();

            // 전투 입력: A=일반공격(마우스 칸), S=스킬(마우스 칸), D=방어
            if (Keyboard.current != null && selectedUnitId != -1)
            {
                if (Keyboard.current.aKey.wasPressedThisFrame && TryHoverCell(out var atk))
                    Log(combat.TryAttack(selectedUnitId, atk), "공격");
                if (Keyboard.current.sKey.wasPressedThisFrame && TryHoverCell(out var skl))
                    Log(combat.TrySkill(selectedUnitId, skl), "스킬");
                if (Keyboard.current.dKey.wasPressedThisFrame)
                    Log(combat.TryGuard(selectedUnitId), "방어");
            }

            RefreshHighlights(); // 게이지·예고가 실시간이라 매 프레임 갱신
        }

        static void Log(ActDenied result, string action)
        {
            if (result != ActDenied.None) Debug.Log($"{action} 불가: {result}");
        }

        bool TryHoverCell(out Coord cell)
        {
            cell = default;
            var ray = rayCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out var hit, 500f)) return false;

            var unitView = hit.collider.GetComponentInParent<UnitView>();
            cell = unitView != null
                ? moveSystem.State.GetUnit(unitView.UnitId).pos // 유닛을 겨누면 그 유닛 칸
                : gridView.WorldToCoord(hit.point);
            return true;
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
            if (unitId != playerUnitId) return; // 내 캐릭터만 조작
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
            cells.Clear();
            colors.Clear();

            if (selectedUnitId != -1)
            {
                moveSystem.GetRanges(selectedUnitId, blue, yellow);
                foreach (var c in blue) { cells.Add(c); colors.Add(blueRangeColor); }
                foreach (var c in yellow) { cells.Add(c); colors.Add(yellowRangeColor); }
            }

            // 설치 공격 예고는 항상 표시 — 같은 칸이면 빨강이 이김 (나중 쓰기 우선)
            foreach (var strike in combat.ActiveStrikes)
            foreach (var c in strike.cells)
            {
                cells.Add(c);
                colors.Add(telegraphColor);
            }

            gridView.SetHighlights(cells, colors);
        }
    }
}
