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
        // ── 색 언어 (가독성 패스 2026-09-05) — 한 색 = 한 뜻. 인스펙터 튜닝 대신 코드 단일 원천.
        //   빨강   = 적 위협 (적 예고) — 이 색은 다른 어떤 것에도 쓰지 않는다
        //   흰색   = 내 공격 (내 예고·발사 시 맞는 칸)
        //   파랑/노랑 = 내가 갈 수 있는 곳 (게이지 이내 / 과부하) — 노랑은 이동 전용
        //   틸     = 내가 쏠 수 있는 곳 (조준 모드) — 내 공격 = 틸 네온과 같은 계열. 이동 노랑과 절대 안 겹친다
        //   적 팀색(주황)은 유닛·HP바에만 — 바닥 틴트엔 안 쓴다
        static readonly Color blueRangeColor = new Color(0.3f, 0.6f, 1f);
        static readonly Color yellowRangeColor = new Color(1f, 0.85f, 0.2f);
        static readonly Color telegraphColor = new Color(0.91f, 0.25f, 0.12f);      // 적 예고 — 유일한 빨강
        static readonly Color allyTelegraphColor = new Color(0.7f, 1f, 0.95f);       // 내 예고 — 틸-흰 (아군 = 틸 계열, 기획서 §06)
        static readonly Color aimRangeColor = new Color(0.3f, 0.8f, 0.75f);          // 조준 가능 칸 — 틸 (구 연노랑: 이동 노랑과 헷갈렸다)
        static readonly Color aimImpactColor = new Color(0.8f, 1f, 0.97f);           // 발사 시 맞는 칸 — 틸-흰 (구 진빨강: 적 위협과 혼동)
        static readonly Color aimInvalidColor = new Color(0.35f, 0.6f, 1f);          // 타겟 아닌 호버 칸 (파랑) = 클릭하면 이동

        Camera rayCamera; // Camera.main 자동 연결

        MoveSystem moveSystem;
        CombatSystem combat;
        IIntentSink sink;
        GridView gridView;
        UnitViewRegistry views;

        public enum AimMode { None, Attack, Skill, Skill2 }

        int selectedUnitId = -1;
        AimMode aim;

        /// <summary>HUD용 — 내 유닛이 선택돼 조작 가능한 상태인가.</summary>
        public bool HasSelection => selectedUnitId != -1;
        /// <summary>HUD용 — 현재 조준 모드 (배너·슬롯 하이라이트).</summary>
        public AimMode CurrentAim => aim;

        /// <summary>플레이어 행동 거부(AP 부족·쿨타임 등) — 버저 SFX용.</summary>
        public event System.Action OnActionDenied;
        readonly List<Coord> blue = new List<Coord>();
        readonly List<Coord> yellow = new List<Coord>();
        readonly List<Coord> aimRange = new List<Coord>();
        readonly List<Coord> aimImpact = new List<Coord>();
        readonly List<Transform> markers = new List<Transform>(); // 조준 마커 쿼드 풀
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        MaterialPropertyBlock markerMpb;
        readonly List<Coord> cells = new List<Coord>();
        readonly List<Color> colors = new List<Color>();

        int playerUnitId = -1; // 조작 가능한 유닛 (기획서 §04: 내 캐릭터 1기뿐)
        int playerTeam;
        System.Func<Coord, bool> isCellVisible; // 내 팀 시야 — 적 예고 필터 (세부기획 B)

        /// <summary>
        /// sink = 행동 제출 통로 (싱글=즉시 실행, 멀티 클라=RPC).
        /// moveSystem/combat은 조준 미리보기·범위 표시 등 읽기 쿼리 전용으로 유지.
        /// </summary>
        public void Init(MoveSystem moveSystem, CombatSystem combat, GridView gridView, UnitViewRegistry views,
            int playerUnitId, IIntentSink sink, System.Func<Coord, bool> isCellVisible = null)
        {
            this.moveSystem = moveSystem;
            this.combat = combat;
            this.sink = sink;
            this.gridView = gridView;
            this.views = views;
            this.playerUnitId = playerUnitId;
            this.isCellVisible = isCellVisible;
            playerTeam = moveSystem.State.GetUnit(playerUnitId).team;
            selectedUnitId = -1; // 라운드 재시작 — 이전 라운드 선택은 무효
            aim = AimMode.None;
            rayCamera = Camera.main;
            Select(playerUnitId); // 시작부터 내 유닛 선택 — 이동 그리드(파랑/노랑) 즉시 표시
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

            // 내 유닛은 살아있는 한 항상 선택 유지 — 선택이 풀려 "이동이 안 되는" 상태 자체를 없앤다
            if (selectedUnitId == -1)
            {
                var mine = moveSystem.State.GetUnit(playerUnitId);
                if (mine != null && mine.alive) Select(playerUnitId);
            }

            if (Mouse.current.leftButton.wasPressedThisFrame) HandleClick();
            if (Mouse.current.rightButton.wasPressedThisFrame)
                aim = AimMode.None; // 우클릭 = 조준 취소만 — 선택 해제 없음 (1유닛 게임)

            // 전투 입력: A=공격 조준, S=스킬1 조준, D=스킬2 조준 (토글), ESC=취소. 방어는 기획 삭제.
            if (Keyboard.current != null && selectedUnitId != -1)
            {
                if (Keyboard.current.aKey.wasPressedThisFrame)
                    aim = aim == AimMode.Attack ? AimMode.None : AimMode.Attack;
                if (Keyboard.current.sKey.wasPressedThisFrame)
                    aim = aim == AimMode.Skill ? AimMode.None : AimMode.Skill;
                if (Keyboard.current.dKey.wasPressedThisFrame)
                    aim = aim == AimMode.Skill2 ? AimMode.None : AimMode.Skill2;
                if (Keyboard.current.escapeKey.wasPressedThisFrame)
                    aim = AimMode.None;
            }

            RefreshHighlights(); // 게이지·예고가 실시간이라 매 프레임 갱신
        }

        void Log(IntentResult result, string action)
        {
            if (result.accepted || result.pending) return; // Pending = 네트워크 제출 — 일단 받아들여진 걸로
            Debug.Log($"{action} 불가: {result.actDenied}");
            OnActionDenied?.Invoke();
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
            // 조준 중 좌클릭: 유효 타겟(빨간 마커) = 발사, 그 외 칸 = 조준 풀고 그냥 이동.
            if (aim != AimMode.None && selectedUnitId != -1)
            {
                if (TryHoverCell(out var target))
                {
                    int skillIdx = aim == AimMode.Skill2 ? 1 : 0;
                    bool validTarget = aim == AimMode.Attack
                        ? combat.GetAttackImpact(selectedUnitId, target, aimImpact)
                        : combat.GetSkillImpact(selectedUnitId, skillIdx, target, aimImpact);
                    if (validTarget)
                    {
                        var result = sink.Submit(aim == AimMode.Attack
                            ? BattleIntent.Attack(selectedUnitId, target)
                            : BattleIntent.Skill(selectedUnitId, target, skillIdx));
                        Log(result, aim == AimMode.Attack ? "공격" : "스킬");
                        if (result.accepted || result.pending) aim = AimMode.None;
                    }
                    else
                    {
                        aim = AimMode.None; // 이동하고 싶었던 것 — 조준이 이동을 막지 않게
                        TryMove(target);
                    }
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

            var result = sink.Submit(BattleIntent.Move(selectedUnitId, dest));
            if (!result.accepted && !result.pending && result.moveDenied == MoveDenied.Locked)
                OnActionDenied?.Invoke(); // 쿨타임 중 이동 시도 — 버저
            // 성공 시 연출은 MoveSystem.OnUnitMoved → BattleRunner가 재생
        }

        void RefreshHighlights()
        {
            cells.Clear();
            colors.Clear();

            if (selectedUnitId != -1 && aim != AimMode.None)
            {
                // 조준 모드: 이동 범위 대신 조준 가능 칸(틸) + 발사 시 맞는 칸(틸-흰)
                int skillIdx = aim == AimMode.Skill2 ? 1 : 0;
                if (aim == AimMode.Attack) combat.GetAttackRange(selectedUnitId, aimRange);
                else combat.GetSkillRange(selectedUnitId, skillIdx, aimRange);
                foreach (var c in aimRange) { cells.Add(c); colors.Add(aimRangeColor); }

                if (TryHoverCell(out var hover))
                {
                    bool valid = aim == AimMode.Attack
                        ? combat.GetAttackImpact(selectedUnitId, hover, aimImpact)
                        : combat.GetSkillImpact(selectedUnitId, skillIdx, hover, aimImpact);
                    if (valid)
                    {
                        foreach (var c in aimImpact) { cells.Add(c); colors.Add(aimImpactColor); }
                        UpdateMarkers(aimImpact, aimImpactColor);
                    }
                    else
                    {
                        // 못 쏘는 곳 — 호버 칸에 회색 마커로 "여긴 안 됨" 표시
                        aimImpact.Clear();
                        aimImpact.Add(hover);
                        UpdateMarkers(aimImpact, aimInvalidColor);
                    }
                }
                else
                {
                    aimImpact.Clear();
                    UpdateMarkers(aimImpact, aimImpactColor);
                }
            }
            else
            {
                aimImpact.Clear();
                UpdateMarkers(aimImpact, aimImpactColor); // 비조준 — 마커 전부 숨김

                if (selectedUnitId != -1)
                {
                    moveSystem.GetRanges(selectedUnitId, blue, yellow);
                    foreach (var c in blue) { cells.Add(c); colors.Add(blueRangeColor); }
                    foreach (var c in yellow) { cells.Add(c); colors.Add(yellowRangeColor); }
                }
            }

            // 설치 공격 예고 표시 — 같은 칸이면 예고가 이김 (나중 쓰기 우선).
            // 초점 계층: 움직이는 건 "내 칸을 노리는 적 예고"뿐 — 펄스 + 판정 0.35초 전 급점멸.
            // 나머지 적 예고는 정적·살짝 어둡게(정보는 남기되 시선은 안 뺏게), 내 예고는 흰색 정적.
            // 적 예고는 내 팀 시야 안의 칸만 보인다 — 안개 속 예측 설치가 서프라이즈로 남게.
            var me = moveSystem.State.GetUnit(playerUnitId);
            float pulseK = Mathf.PingPong(Time.time * 2.5f, 0.4f);
            var threatPulse = Color.Lerp(telegraphColor, Color.white, pulseK);
            var calmEnemy = telegraphColor * 0.7f;
            calmEnemy.a = 1f;
            foreach (var strike in combat.ActiveStrikes)
            {
                bool mine = strike.team == playerTeam;
                bool threatensMe = false;
                if (!mine && me != null && me.alive)
                    foreach (var c in strike.cells)
                        if (c == me.pos) { threatensMe = true; break; }

                Color c2 = mine ? allyTelegraphColor : (threatensMe ? threatPulse : calmEnemy);
                float remain = strike.impactTime - combat.State.time;
                if (threatensMe && remain < 0.35f)
                {
                    bool on = Mathf.Sin(Time.time * 45f) > 0f;
                    c2 = on ? Color.Lerp(telegraphColor, Color.white, 0.5f) : telegraphColor * 0.45f;
                    c2.a = 1f;
                }
                foreach (var c in strike.cells)
                {
                    if (!mine && isCellVisible != null && !isCellVisible(c)) continue;
                    cells.Add(c);
                    colors.Add(c2);
                }
            }

            gridView.SetHighlights(cells, colors);
        }

        // ── 조준 마커 (틴트 위에 뜬 밝은 쿼드 — 풀 재사용) ─────────

        /// <summary>마커를 cellList에 맞춰 배치. 빈 리스트면 전부 숨김. 펄스로 두근거림.</summary>
        void UpdateMarkers(List<Coord> cellList, Color color)
        {
            if (markerMpb == null) markerMpb = new MaterialPropertyBlock();
            while (markers.Count < cellList.Count) markers.Add(CreateMarker());

            float s = 0.6f + Mathf.PingPong(Time.time * 1.8f, 0.18f);
            for (int i = 0; i < markers.Count; i++)
            {
                bool on = i < cellList.Count;
                if (markers[i].gameObject.activeSelf != on) markers[i].gameObject.SetActive(on);
                if (!on) continue;
                markers[i].position = gridView.CoordToWorld(cellList[i]) + Vector3.up * 0.14f;
                markers[i].localScale = Vector3.one * s;
                markerMpb.SetColor(BaseColorId, color);
                markers[i].GetComponent<Renderer>().SetPropertyBlock(markerMpb);
            }
        }

        Transform CreateMarker()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "AimMarker";
            Destroy(go.GetComponent<Collider>()); // 클릭 레이캐스트 방해 금지
            go.transform.SetParent(transform);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // 바닥에 눕힘
            go.SetActive(false);
            return go.transform;
        }

        void OnDisable()
        {
            // 라운드 종료(브리핑 중) 입력 꺼짐 — 마커가 화면에 남지 않게
            foreach (var m in markers)
                if (m != null) m.gameObject.SetActive(false);
        }
    }
}
