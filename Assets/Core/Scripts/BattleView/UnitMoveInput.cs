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
        static readonly Color allyTelegraphColor = new Color(0.45f, 0.55f, 0.68f);   // 내 팀 예고 — 저휘도 회청 (파랑은 이동과 겹쳐 뜻이 두 개가 됐다 — 색 4규칙 2026-09-05)
        static readonly Color aimRangeColor = new Color(0.95f, 0.97f, 1f);           // 조준 가능 칸 — 흰색 (2026-09-05 요청; 구 틸은 팀색과 겹쳤다)
        static readonly Color aimImpactColor = new Color(1f, 1f, 1f);                // 발사 시 맞는 칸 — 순흰 (색 4규칙: 흰 = 내 공격. 시안 채널 폐지 2026-09-05)
        static readonly Color aimInvalidColor = new Color(0.35f, 0.6f, 1f);          // 타겟 아닌 호버 칸 (파랑) = 클릭하면 이동
        static readonly Color aimFillColor = new Color(0.85f, 0.9f, 1f);             // 범위기 판정 칸 채움 — 흰 계열 (내 공격 = 흰)

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
        PushArrow aimPushArrow;   // 조준 중 "여기 맞추면 저기로 밀린다" — 연계를 미리 짜라고 보여준다
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
                // 쿨타임 중인 행동은 조준 모드 자체를 안 연다 — 범위도 안 그리고 버저도 없이 조용히 무시.
                // (조준 → 클릭 → 거부 → 버저 루프가 "삐삐" 소음의 주범이었다.) 이미 켜진 조준을 끄는 건 항상 허용.
                var su = moveSystem.State.GetUnit(selectedUnitId);
                float now = moveSystem.State.time;
                if (Keyboard.current.aKey.wasPressedThisFrame && (aim == AimMode.Attack || su.attackReadyAt <= now))
                    aim = aim == AimMode.Attack ? AimMode.None : AimMode.Attack;
                if (Keyboard.current.sKey.wasPressedThisFrame && (aim == AimMode.Skill || su.skillReadyAt[0] <= now))
                    aim = aim == AimMode.Skill ? AimMode.None : AimMode.Skill;
                if (Keyboard.current.dKey.wasPressedThisFrame && (aim == AimMode.Skill2 || su.skillReadyAt[1] <= now))
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
            Deny();
        }

        float lastDenyTime = -10f;

        /// <summary>거부 버저 — 0.5초에 한 번만. 쿨타임 중 연타하면 매 클릭마다 울려 귀가 아팠다.</summary>
        void Deny()
        {
            if (Time.unscaledTime - lastDenyTime < 0.5f) return;
            lastDenyTime = Time.unscaledTime;
            OnActionDenied?.Invoke();
        }

        static readonly List<Coord> NoCells = new List<Coord>();
        readonly Dictionary<string, Texture2D> iconCache = new Dictionary<string, Texture2D>();

        /// <summary>조준 커서 아이콘 — 공격은 클래스별 Icon_Attack(BattleHud.AttackIconPath), 스킬은 Icon_Skill_<종류> (없으면 Generic). HUD 슬롯과 같은 그림.</summary>
        Texture2D AimIcon(int skillIdx)
        {
            var u = moveSystem.State.GetUnit(selectedUnitId);
            string path;
            if (aim == AimMode.Attack) path = BattleHud.AttackIconPath(u.unitClass);
            else
            {
                var skills = ClassCatalog.Get(u.unitClass).skills;
                path = "UI/Icon_Skill_" + (skillIdx < skills.Length ? skills[skillIdx].kind.ToString() : "Generic");
            }
            if (!iconCache.TryGetValue(path, out var tex))
            {
                tex = Resources.Load<Texture2D>(path) ?? Resources.Load<Texture2D>("UI/Icon_Skill_Generic");
                iconCache[path] = tex;
            }
            return tex;
        }

        /// <summary>마우스 아래 칸 — 휠클릭 핑 등 외부 조회용.</summary>
        public bool TryGetHoverCell(out Coord cell) => TryHoverCell(out cell);

        /// <summary>HUD 슬롯 클릭 = 단축키(A/S/D)와 동일한 조준 토글 — 쿨타임 게이트도 동일 (2026-09-05).</summary>
        float aimOpenedAt = -99f; // 조준 진입 시각 — 사거리 플래시(밝게 떴다 은은하게 정착)용

        public void ToggleAim(AimMode mode)
        {
            if (selectedUnitId == -1 || mode == AimMode.None) { aim = AimMode.None; return; }
            if (aim == mode) { aim = AimMode.None; return; } // 켜진 조준 끄기는 항상 허용
            var su = moveSystem.State.GetUnit(selectedUnitId);
            float now = moveSystem.State.time;
            bool ready = mode == AimMode.Attack ? su.attackReadyAt <= now
                : mode == AimMode.Skill ? su.skillReadyAt[0] <= now
                : su.skillReadyAt[1] <= now;
            if (ready) { aim = mode; aimOpenedAt = Time.time; } // 쿨 중이면 조용히 무시 — 키보드와 같은 규칙. 진입 시 사거리 플래시
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
            gridView.ClearRangeOutline();
        }

        void TryMove(Coord dest)
        {
            if (selectedUnitId == -1) return;
            var view = views.Get(selectedUnitId);
            if (view != null && view.IsMoving) return; // 연출 중 연타 방지

            var result = sink.Submit(BattleIntent.Move(selectedUnitId, dest));
            if (!result.accepted && !result.pending && result.moveDenied == MoveDenied.Locked)
            {
                Deny(); // 쿨타임 중 이동 시도 — 버저
                // 시전 잠금이면 이유를 말해준다 — 새 규칙(시전 중 이동 금지)이 버저만으론 안 읽힌다 (2026-09-05)
                if (moveSystem.IsCastingFn != null && moveSystem.IsCastingFn(selectedUnitId))
                {
                    var su2 = moveSystem.State.GetUnit(selectedUnitId);
                    if (su2 != null)
                        FloatingText.Spawn(gridView.CoordToWorld(su2.pos) + Vector3.up * 0.3f, "시전 중!",
                            new Color(1f, 0.78f, 0.25f), 0.9f, 0.7f);
                }
            }
            else if (result.accepted || result.pending)
                CellFlash.Spawn(gridView.CoordToWorld(dest), blueRangeColor, 0.45f, 0.9f); // 클릭 확인 — "여기로 간다"
            // 성공 시 연출은 MoveSystem.OnUnitMoved → BattleRunner가 재생
        }

        void RefreshHighlights()
        {
            cells.Clear();
            colors.Clear();

            if (selectedUnitId != -1 && aim != AimMode.None)
            {
                // 조준 모드 (2026-09-05 윤곽 전환): 사거리 = 흰 윤곽선, 판정 칸 = 밝은 윤곽선, 커서 칸 = 공격/스킬 아이콘.
                // 칸 채움(흰 네모)·펄스 마커는 폐지 — 거점 바닥 위에서 안 읽혔고 이동 구역 표시와 문법이 달랐다.
                int skillIdx = aim == AimMode.Skill2 ? 1 : 0;
                if (aim == AimMode.Attack) combat.GetAttackRange(selectedUnitId, aimRange);
                else combat.GetSkillRange(selectedUnitId, skillIdx, aimRange);
                // 진입 순간 0.3초 밝게 → 은은한 저휘도로 정착 (조준 중 사거리 판단 근거는 남긴다)
                float settle = Mathf.SmoothStep(1f, 0.55f, Mathf.Clamp01((Time.time - aimOpenedAt) / 0.3f));
                var rangeCol = new Color(aimRangeColor.r * settle, aimRangeColor.g * settle, aimRangeColor.b * settle, aimRangeColor.a);
                gridView.BeginOutlines();
                gridView.AddOutline(aimRange, rangeCol, 0.13f);
                UpdateMarkers(NoCells, aimImpactColor); // 구 마커 전부 숨김
                var icon = AimIcon(skillIdx);

                if (TryHoverCell(out var hover))
                {
                    bool valid = aim == AimMode.Attack
                        ? combat.GetAttackImpact(selectedUnitId, hover, aimImpact)
                        : combat.GetSkillImpact(selectedUnitId, skillIdx, hover, aimImpact);
                    if (valid)
                    {
                        gridView.AddOutline(aimImpact, aimImpactColor, 0.14f); // 맞는 칸들 — 밝은 윤곽
                        // 범위기(판정 2칸 이상)는 칸을 흰색으로 채운다 — 윤곽만으론 "1칸 때리는" 걸로 읽혔다 (2026-09-06).
                        // 단일 타겟은 그대로 윤곽 + 아이콘. 채움은 바닥 틴트라 아이콘·윤곽 밑에 깔린다.
                        if (aimImpact.Count > 1)
                            foreach (var c in aimImpact) { cells.Add(c); colors.Add(aimFillColor); }
                        gridView.ShowCursorIcon(hover, icon, aimImpactColor);
                        ShowPushPreview(hover);
                    }
                    else
                    {
                        aimImpact.Clear();
                        gridView.ShowCursorIcon(hover, icon, aimInvalidColor); // 못 쏘는 곳 — 아이콘만 파랗게(클릭하면 이동)
                        ClearPushPreview();
                    }
                }
                else
                {
                    aimImpact.Clear();
                    gridView.HideFootstep();
                    ClearPushPreview();
                }
                gridView.EndOutlines();
            }
            else
            {
                aimImpact.Clear();
                UpdateMarkers(aimImpact, aimImpactColor); // 비조준 — 마커 전부 숨김
                ClearPushPreview();

                if (selectedUnitId != -1)
                {
                    moveSystem.GetRanges(selectedUnitId, blue, yellow);
                    foreach (var c in blue) { cells.Add(c); colors.Add(blueRangeColor); }
                    foreach (var c in yellow) { cells.Add(c); colors.Add(yellowRangeColor); }

                    // 네온 테두리 두 겹 — 거점 바닥 위에서도 이동 구역이 읽힌다 (2026-09-05). 호버 칸엔 발자국.
                    gridView.SetRangeOutline(blue, yellow, blueRangeColor, yellowRangeColor);
                    if (TryHoverCell(out var hoverMove) && (blue.Contains(hoverMove) || yellow.Contains(hoverMove)))
                        gridView.ShowFootstep(hoverMove, yellow.Contains(hoverMove) ? yellowRangeColor : blueRangeColor);
                    else gridView.HideFootstep();
                }
                else gridView.ClearRangeOutline();
            }

            // 설치 공격 예고 표시 — 같은 칸이면 예고가 이김 (나중 쓰기 우선).
            // 초점 계층: 움직이는 건 "내 칸을 노리는 적 예고"뿐 — 펄스 + 판정 0.35초 전 급점멸.
            // 나머지 적 예고는 정적·살짝 어둡게(정보는 남기되 시선은 안 뺏게), 내 예고는 흰색 정적.
            // 적 예고는 내 팀 시야 안의 칸만 보인다 — 안개 속 예측 설치가 서프라이즈로 남게.
            var me = moveSystem.State.GetUnit(playerUnitId);
            float pulseK = Mathf.PingPong(Time.time * 2.5f, 0.4f);
            var threatPulse = Color.Lerp(telegraphColor, Color.white, pulseK);
            var calmEnemy = telegraphColor * 0.92f; // 0.7은 밝은 거점 바닥에서 묻혔다 (2026-09-05)
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
        /// <summary>
        /// 조준 중 밀침 미리보기 — 지금 겨눈 칸에 맞으면 누가 어디로 밀리는지 바닥 화살표로.
        /// 밀림 규격·도착 칸은 코어(PushSpecOf·PreviewPush)가 계산한다 — 실제 판정과 어긋나면 거짓말이 된다.
        /// </summary>
        void ShowPushPreview(Coord hover)
        {
            ClearPushPreview();
            if (aim == AimMode.None || aim == AimMode.Attack) return; // 평타는 밀침 없음

            var me = combat.State.GetUnit(selectedUnitId);
            if (me == null) return;
            var skills = ClassCatalog.Get(me.unitClass).skills;
            int skillIdx = aim == AimMode.Skill ? 0 : 1;
            if (skillIdx >= skills.Length) return;

            CombatSystem.PushSpecOf(skills[skillIdx].kind, out int pushCells, out _, out bool self);
            if (pushCells <= 0) return;

            var d = hover - me.pos;
            var dir = new Coord(System.Math.Sign(d.x), System.Math.Sign(d.y));
            if (dir == Coord.Zero) return;

            // 넉백샷은 시전자 본인이 반대 방향으로 후퇴한다 — 밀리는 주체가 다르다
            var from = self ? me.pos : hover;
            if (self) dir = new Coord(-dir.x, -dir.y);

            var dest = combat.PreviewPush(from, dir, pushCells, out bool crash);
            if (dest == from && !crash) return;

            var color = aimImpactColor; color.a = 0.9f; // 내 조준이 화면에서 가장 진해야 한다
            aimPushArrow = PushArrow.Create(transform, gridView.CoordToWorld(from),
                gridView.CoordToWorld(dest), color, crash, 1.2f);
        }

        void ClearPushPreview()
        {
            if (aimPushArrow == null) return;
            Destroy(aimPushArrow.gameObject);
            aimPushArrow = null;
        }

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
