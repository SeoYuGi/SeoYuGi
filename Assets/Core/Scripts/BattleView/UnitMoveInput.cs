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
        [SerializeField] Color aimRangeColor = new Color(1f, 0.6f, 0.15f);      // 조준 가능 칸 (주황)
        [SerializeField] Color aimImpactColor = new Color(1f, 0.12f, 0.08f);    // 발사 시 맞는 칸 (진빨강)

        Camera rayCamera; // Camera.main 자동 연결

        MoveSystem moveSystem;
        CombatSystem combat;
        GridView gridView;
        UnitViewRegistry views;

        enum AimMode { None, Attack, Skill }

        int selectedUnitId = -1;
        AimMode aim;

        /// <summary>HUD용 — 내 유닛이 선택돼 조작 가능한 상태인가.</summary>
        public bool HasSelection => selectedUnitId != -1;
        readonly List<Coord> blue = new List<Coord>();
        readonly List<Coord> yellow = new List<Coord>();
        readonly List<Coord> aimRange = new List<Coord>();
        readonly List<Coord> aimImpact = new List<Coord>();
        readonly List<Coord> cells = new List<Coord>();
        readonly List<Color> colors = new List<Color>();

        int playerUnitId = -1; // 조작 가능한 유닛 (기획서 §04: 내 캐릭터 1기뿐)
        int playerTeam;
        System.Func<Coord, bool> isCellVisible; // 내 팀 시야 — 적 예고 필터 (세부기획 B)

        public void Init(MoveSystem moveSystem, CombatSystem combat, GridView gridView, UnitViewRegistry views,
            int playerUnitId, System.Func<Coord, bool> isCellVisible = null)
        {
            this.moveSystem = moveSystem;
            this.combat = combat;
            this.gridView = gridView;
            this.views = views;
            this.playerUnitId = playerUnitId;
            this.isCellVisible = isCellVisible;
            playerTeam = moveSystem.State.GetUnit(playerUnitId).team;
            selectedUnitId = -1; // 라운드 재시작 — 이전 라운드 선택은 무효
            aim = AimMode.None;
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
            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                if (aim != AimMode.None) aim = AimMode.None; // 조준 중 우클릭 = 조준만 취소
                else Deselect();
            }

            // 전투 입력: A=공격 조준 토글, S=스킬 조준 토글, D=방어 즉발, ESC=조준 취소
            if (Keyboard.current != null && selectedUnitId != -1)
            {
                if (Keyboard.current.aKey.wasPressedThisFrame)
                    aim = aim == AimMode.Attack ? AimMode.None : AimMode.Attack;
                if (Keyboard.current.sKey.wasPressedThisFrame)
                    aim = aim == AimMode.Skill ? AimMode.None : AimMode.Skill;
                if (Keyboard.current.dKey.wasPressedThisFrame)
                    Log(combat.TryGuard(selectedUnitId), "방어");
                if (Keyboard.current.escapeKey.wasPressedThisFrame)
                    aim = AimMode.None;
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
            // 조준 중 좌클릭 = 발사 (이동 아님). 성공하면 조준 해제.
            if (aim != AimMode.None && selectedUnitId != -1)
            {
                if (TryHoverCell(out var target))
                {
                    var result = aim == AimMode.Attack
                        ? combat.TryAttack(selectedUnitId, target)
                        : combat.TrySkill(selectedUnitId, target);
                    Log(result, aim == AimMode.Attack ? "공격" : "스킬");
                    if (result == ActDenied.None) aim = AimMode.None;
                }
                return;
            }

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
            aim = AimMode.None;
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

            if (selectedUnitId != -1 && aim != AimMode.None)
            {
                // 조준 모드: 이동 범위 대신 조준 가능 칸(주황) + 발사 시 맞는 칸(진빨강)
                if (aim == AimMode.Attack) combat.GetAttackRange(selectedUnitId, aimRange);
                else combat.GetSkillRange(selectedUnitId, aimRange);
                foreach (var c in aimRange) { cells.Add(c); colors.Add(aimRangeColor); }

                if (TryHoverCell(out var hover))
                {
                    bool valid = aim == AimMode.Attack
                        ? combat.GetAttackImpact(selectedUnitId, hover, aimImpact)
                        : combat.GetSkillImpact(selectedUnitId, hover, aimImpact);
                    if (valid)
                        foreach (var c in aimImpact) { cells.Add(c); colors.Add(aimImpactColor); }
                }
            }
            else if (selectedUnitId != -1)
            {
                moveSystem.GetRanges(selectedUnitId, blue, yellow);
                foreach (var c in blue) { cells.Add(c); colors.Add(blueRangeColor); }
                foreach (var c in yellow) { cells.Add(c); colors.Add(yellowRangeColor); }
            }

            // 설치 공격 예고 표시 — 같은 칸이면 빨강이 이김 (나중 쓰기 우선). 펄스로 깜빡임.
            // 적 예고는 내 팀 시야 안의 칸만 보인다 — 안개 속 예측 설치가 서프라이즈로 남게.
            var pulse = Color.Lerp(telegraphColor, Color.white, Mathf.PingPong(Time.time * 2.5f, 0.4f));
            foreach (var strike in combat.ActiveStrikes)
            {
                bool mine = strike.team == playerTeam;
                foreach (var c in strike.cells)
                {
                    if (!mine && isCellVisible != null && !isCellVisible(c)) continue;
                    cells.Add(c);
                    colors.Add(pulse);
                }
            }

            gridView.SetHighlights(cells, colors);
        }
    }
}
