using System.Collections.Generic;
using SeoYuGi.Battle;
using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 임시 HUD (OnGUI) — 탱고파이브 리로디드 배치 파쿠리. 아트 UI 프리팹이 붙으면 통째로 교체.
    /// 상단: 생존 ● + 매치 스코어 | 남은시간 | A/B/C 거점 칩.
    /// 중앙 상단: 상황 안내 배너 ("공격할 대상을 선택하세요" 식).
    /// 하단: 통합 바 — 콜사인·HP | 이동/공격/스킬 슬롯(키·쿨타임) | 해킹 궁게이지.
    /// 오버레이: 라운드 결과 화면 · 매치 종료. (AI 학습 브리핑은 2026-09-05 컨셉 선회로 폐기 — lines=null로 온다)
    /// hudScale로 전체 크기 조절 (GUI.matrix).
    /// </summary>
    public class BattleHud : MonoBehaviour
    {
        enum Overlay { None, Briefing, MatchEnd }

        [SerializeField] float hudScale = 1.6f; // 1080p 기준 배율 — 해상도는 아래서 자동 보정

        BattleState battle;
        RoundSystem round;
        CombatConfig combatConfig;
        MatchSystem match;
        int playerUnitId;
        int playerTeam;
        string playerName;
        Color allyColor = Color.cyan, enemyColor = Color.red;

        Overlay overlay;
        int briefingRound;   // 방금 끝난 라운드 번호
        int briefingWinner;
        string[] briefingLines;
        int[] briefingZones;          // 거점별 소유 팀 (-1 = 중립)
        int briefingAliveMine, briefingAliveEnemy;
        string[] briefingStatsMine, briefingStatsFoe; // 개인 전적 줄 "콜사인  K n / D n" (2026-09-05)

        /// <summary>라운드 전적 주입 — ShowBriefing 직전에 러너가 채운다.</summary>
        public void SetBriefingStats(string[] mineLines, string[] foeLines)
        {
            briefingStatsMine = mineLines;
            briefingStatsFoe = foeLines;
        }

        // 관제 AI 보이스 자막 (영어 보이스 + 한글 자막)
        string subtitleText;
        float subtitleUntil;

        // 큰 중앙 공지 (거점 점령 등) — 자막과 별개, 상단 중앙에 크게
        string announceText;
        float announceUntil;
        Color announceColor = Color.white;

        // 빠른채팅 로그 — 좌하단에 최근 3줄. 자막(중앙)과 자리가 겹치지 않는다.
        struct ChatEntry { public string text; public Color color; public float until; }
        readonly List<ChatEntry> chatLog = new List<ChatEntry>();
        GUIStyle chatStyle; // 채팅 로그 전용 — labelStyle + 줄바꿈
        const int ChatLogMax = 3;

        // 킬피드 — 우상단에 최근 5줄. "킬러 ⚔ 피해자".
        readonly List<ChatEntry> killFeed = new List<ChatEntry>();
        const int KillFeedMax = 5;

        /// <summary>Tab 홀드 중 프리셋 치트시트 표시 — BattleRunner가 매 프레임 갱신.</summary>
        public bool ShowChatCheatsheet { get; set; }

        /// <summary>무전 패널의 문구 클릭 — lineId. 전송 경로는 러너가 배선.</summary>
        public event System.Action<int> OnChatClicked;
        /// <summary>해킹 게이지 클릭 — H키와 동일 경로. 러너가 배선.</summary>
        public event System.Action OnHackClicked;
        bool chatPanelOpen; // [무전] 토글 — 마우스로도 보낼 수 있게
        float idleHintUntil; // 기본 조작 안내(타일 클릭 = 이동)는 진입 후 15초만 — 그 뒤엔 화면 중앙을 비운다

        GUIStyle timerStyle, timerLabelStyle, dotStyle, chipStyle, roundStyle;
        GUIStyle bannerTextStyle, labelStyle, bannerStyle, briefTitleStyle, briefLineStyle, killStyle, announceStyle;
        GUIStyle keyStyle, slotNameStyle, slotCostStyle, slotCoolStyle, bigNumStyle, subStyle, subtitleStyle;
        bool stylesReady;
        Texture2D iconMove, iconAttack, iconGuard, iconSkill1, iconSkill2, panelBriefing; // Resources/UI — 없으면 무시
        Texture2D texSlot, texPanel, texInfo, texChip, texBanner;           // 프레임류 — 없으면 GUI.Box 폴백
        Texture2D texTimer, texGauge, texResultBanner, texCheatsheet;       // UI 리마스터 2차분 (2026-09-05)
        UnitMoveInput moveInput; // 선택 상태 조회용 — 같은 GO에서 자동 연결

        // 해상도 대응: 세로 1080 기준 비례 스케일 × hudScale — 어느 기기든 화면 대비 같은 크기
        float UiScale => Screen.height / 1080f * hudScale;

        // 스케일 적용 후 논리 화면 크기
        float W => Screen.width / UiScale;
        float H => Screen.height / UiScale;

        System.Func<float> hackCharge; // 내 유닛 해킹 게이지 0..1 — 러너가 주입 (HUD는 코어 비의존)

        public void Init(BattleState battle, RoundSystem round, CombatConfig combatConfig, MatchSystem match,
            int playerUnitId, Color[] teamColors, string playerName = null, System.Func<float> hackCharge = null)
        {
            this.battle = battle;
            this.round = round;
            this.combatConfig = combatConfig;
            this.match = match;
            this.playerUnitId = playerUnitId;
            this.playerName = playerName;
            this.hackCharge = hackCharge;
            playerTeam = battle.GetUnit(playerUnitId).team;
            idleHintUntil = Time.time + 15f;
            allyColor = teamColors[playerTeam];
            enemyColor = teamColors[1 - playerTeam];
            overlay = Overlay.None;
            if (moveInput == null) moveInput = GetComponent<UnitMoveInput>();

            iconMove = LoadKeyed("UI/Icon_Move"); // 검정 배경 키잉 — 알파 없는 생성 아이콘이 검은 사각형으로 붙는 것 방지 (2026-09-05)
            iconAttack = Resources.Load<Texture2D>("UI/Icon_Attack");
            iconGuard = Resources.Load<Texture2D>("UI/Icon_Guard");
            var myCls = battle.GetUnit(playerUnitId).unitClass;
            iconSkill1 = LoadSkillIcon(ClassCatalog.Get(myCls).skills[0].kind); // 스킬별 아이콘 — 10종 전부 아트 확보 (2026-09-05)
            iconSkill2 = LoadSkillIcon(ClassCatalog.Get(myCls).skills[1].kind);
            panelBriefing = Resources.Load<Texture2D>("UI/Panel_Briefing");
            texSlot = LoadKeyed("UI/Frame_Slot");
            texPanel = LoadKeyed("UI/Frame_Panel");
            texInfo = LoadKeyed("UI/Frame_Info");
            texChip = LoadKeyed("UI/Frame_Chip");
            texBanner = LoadKeyed("UI/Frame_Banner");
            texTimer = LoadKeyed("UI/Panel_Timer");
            texGauge = LoadKeyedTealSwap("UI/Gauge_Hack"); // 아트는 보라 — 색 언어(보라=예측)에 맞춰 틸로 채널 스왑
            texResultBanner = LoadKeyed("UI/Banner_Result");
            texCheatsheet = LoadKeyed("UI/Panel_Cheatsheet");
        }

        static readonly Dictionary<string, Texture2D> keyedCache = new Dictionary<string, Texture2D>();

        /// <summary>
        /// 생성 이미지의 검정 배경을 투명 처리해서 로드 (프레임류는 plain black 위에 생성됨).
        /// 순수 검정(합 &lt; 30)만 제거, 30~60은 페더 — 건메탈 아트(합 130+)는 안전.
        /// Read/Write 꺼져 있으면 원본 그대로 (검정 배경 노출 폴백). 타이틀 매칭 링 등 외부도 사용.
        /// </summary>
        public static Texture2D LoadKeyed(string path)
        {
            if (keyedCache.TryGetValue(path, out var cached)) return cached;
            var src = Resources.Load<Texture2D>(path);
            Texture2D result = src;
            if (src != null)
            {
                try
                {
                    var px = src.GetPixels32();
                    for (int i = 0; i < px.Length; i++)
                    {
                        int lum = px[i].r + px[i].g + px[i].b;
                        if (lum < 30) px[i].a = 0;
                        else if (lum < 60) px[i].a = (byte)((lum - 30) * 255 / 30);
                    }

                    // 내용 바운딩 박스 크롭 — 생성 이미지의 캔버스 여백 때문에
                    // 프레임이 rect보다 작게 그려져 텍스트가 뚫고 나가는 문제 해결
                    int w = src.width, h = src.height;
                    int minX = w, minY = h, maxX = -1, maxY = -1;
                    for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        if (px[y * w + x].a > 12)
                        {
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }

                    if (maxX <= minX || maxY <= minY) { minX = 0; minY = 0; maxX = w - 1; maxY = h - 1; }
                    int cw = maxX - minX + 1, ch = maxY - minY + 1;
                    var cropped = new Color32[cw * ch];
                    for (int y = 0; y < ch; y++)
                    for (int x = 0; x < cw; x++)
                        cropped[y * cw + x] = px[(y + minY) * w + (x + minX)];

                    var tex = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
                    tex.SetPixels32(cropped);
                    tex.Apply();
                    result = tex;
                }
                catch (UnityException) { /* isReadable=0 — 원본 사용 */ }
            }
            keyedCache[path] = result;
            return result;
        }

        /// <summary>프레임 텍스처가 있으면 이미지, 없으면 네온 패널 폴백.</summary>
        void DrawFrame(Rect r, Texture2D tex)
        {
            if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.StretchToFill);
            else NeonPanel(r, HudCyan);
        }

        // ── 네온 킷 — 유니티 기본 회색 박스(IO게임 감성) 대체용 절차 드로잉 ──

        static readonly Color HudCyan = new Color(0.45f, 1f, 0.95f);

        static readonly Dictionary<uint, Texture2D> solidCache = new Dictionary<uint, Texture2D>();

        /// <summary>단색 1×1 텍스처 — GUIStyle 배경용 (스타일은 GUI.color 틴트를 못 받는다).</summary>
        static Texture2D Solid(Color c)
        {
            uint key = ((uint)(c.r * 255) << 24) | ((uint)(c.g * 255) << 16) | ((uint)(c.b * 255) << 8) | (uint)(c.a * 255);
            if (solidCache.TryGetValue(key, out var t) && t != null) return t;
            t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, c);
            t.Apply();
            solidCache[key] = t;
            return t;
        }

        static void Fill(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        /// <summary>1px 외곽선.</summary>
        static void Edge(Rect r, Color c)
        {
            Fill(new Rect(r.x, r.y, r.width, 1f), c);
            Fill(new Rect(r.x, r.yMax - 1f, r.width, 1f), c);
            Fill(new Rect(r.x, r.y, 1f, r.height), c);
            Fill(new Rect(r.xMax - 1f, r.y, 1f, r.height), c);
        }

        /// <summary>어두운 판 + 액센트 외곽선 + 네 모서리 틱 — 민짜 검정 사각형 대체.</summary>
        static void NeonPanel(Rect r, Color accent, float alpha = 1f)
        {
            Fill(r, new Color(0.02f, 0.04f, 0.09f, 0.8f * alpha));
            Edge(r, new Color(accent.r, accent.g, accent.b, 0.4f * alpha));
            var tick = new Color(accent.r, accent.g, accent.b, 0.9f * alpha);
            const float T = 2f, L = 9f;
            Fill(new Rect(r.x, r.y, L, T), tick); Fill(new Rect(r.x, r.y, T, L), tick);
            Fill(new Rect(r.xMax - L, r.y, L, T), tick); Fill(new Rect(r.xMax - T, r.y, T, L), tick);
            Fill(new Rect(r.x, r.yMax - T, L, T), tick); Fill(new Rect(r.x, r.yMax - L, T, L), tick);
            Fill(new Rect(r.xMax - L, r.yMax - T, L, T), tick); Fill(new Rect(r.xMax - T, r.yMax - L, T, L), tick);
        }

        // ── 가장자리 사건 화살표 — 프레임 밖에서 터진 킬 등을 방향으로 알림 (가시성 패스 D) ──

        struct EdgePing { public Vector3 world; public Color color; public float until; }
        readonly List<EdgePing> edgePings = new List<EdgePing>();

        /// <summary>worldPos가 화면 밖이면 가장자리에 방향 화살표를 seconds 동안 표시.</summary>
        public void PingEdge(Vector3 worldPos, Color color, float seconds = 2.2f)
        {
            edgePings.Add(new EdgePing { world = worldPos, color = color, until = Time.time + seconds });
        }

        void DrawEdgePings()
        {
            var cam = Camera.main;
            if (cam == null) return;
            for (int i = edgePings.Count - 1; i >= 0; i--)
            {
                if (Time.time >= edgePings[i].until) { edgePings.RemoveAt(i); continue; }
                var vp = cam.WorldToViewportPoint(edgePings[i].world);
                if (vp.z > 0f && vp.x > 0.04f && vp.x < 0.96f && vp.y > 0.06f && vp.y < 0.94f)
                    continue; // 화면 안 — 화살표 불필요
                if (vp.z < 0f) { vp.x = 1f - vp.x; vp.y = 1f - vp.y; } // 카메라 뒤 — 방향 뒤집기

                // 화면 중심→사건 방향으로 가장자리에 클램프 (GUI 좌표는 y가 아래로 증가)
                var dir = new Vector2(vp.x - 0.5f, -(vp.y - 0.5f));
                if (dir.sqrMagnitude < 0.0001f) continue;
                dir.Normalize();
                var pos = new Vector2(W / 2f, H / 2f) + new Vector2(
                    dir.x * (W / 2f - 46f), dir.y * (H / 2f - 46f));

                float a = Mathf.Clamp01(edgePings[i].until - Time.time);
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                var saved = GUI.matrix;
                GUIUtility.RotateAroundPivot(angle, pos); // GUI.matrix 스케일 위에서 논리 좌표 기준 회전
                var c = edgePings[i].color;
                ShadowLabel(new Rect(pos.x - 22f, pos.y - 16f, 44f, 32f), "▶",
                    subtitleStyle, new Color(c.r, c.g, c.b, a));
                GUI.matrix = saved;
            }
        }

        // ── 핑 휠 (휠 꾹) — 러너가 상태 주입, 그리기만 담당 ────────

        bool pingWheelOn;
        Vector2 pingWheelScreen; // 실제 스크린 px (GUI 상단 원점)
        int pingWheelSel;

        /// <summary>screenPos = 마우스 스크린 px(하단 원점). sel = 0 ▼ / 1 ! / 2 ?.</summary>
        public void SetPingWheel(bool on, Vector2 screenPos, int sel)
        {
            pingWheelOn = on;
            pingWheelScreen = new Vector2(screenPos.x, Screen.height - screenPos.y);
            pingWheelSel = sel;
        }

        void DrawPingWheel()
        {
            if (!pingWheelOn) return;
            var p = pingWheelScreen / UiScale;
            // 배치: 오른쪽 ▼(디폴트) / 위 ! / 왼쪽 ? — 러너의 방향 판정과 1:1
            (string glyph, Vector2 off)[] opts =
            {
                ("▼", new Vector2(64f, 0f)),
                ("!", new Vector2(0f, -64f)),
                ("?", new Vector2(-64f, 0f)),
            };
            for (int i = 0; i < opts.Length; i++)
            {
                var c = p + opts[i].off;
                var r = new Rect(c.x - 24f, c.y - 24f, 48f, 48f);
                bool sel = i == pingWheelSel;
                Fill(r, sel ? new Color(0.08f, 0.16f, 0.26f, 0.95f) : new Color(0.02f, 0.04f, 0.09f, 0.85f));
                Edge(r, sel ? HudCyan : new Color(HudCyan.r, HudCyan.g, HudCyan.b, 0.35f));
                ShadowLabel(r, opts[i].glyph, subtitleStyle, sel ? Color.white : new Color(0.7f, 0.78f, 0.88f));
            }
        }

        /// <summary>그림자 딸린 라벨 — 밝은 맵 위에서도 글자가 뜬다.</summary>
        // 파이 마스크 텍스처 캐시 — 0..PieSteps 단계 (흰 알파 마스크, GUI.color로 팀 색). 12시부터 시계 방향.
        const int PieSteps = 36;
        static readonly Texture2D[] pieTex = new Texture2D[PieSteps + 1];

        static Texture2D PieTex(int step)
        {
            step = Mathf.Clamp(step, 0, PieSteps);
            if (pieTex[step] != null) return pieTex[step];
            const int N = 40; float c = (N - 1) * 0.5f, r = N * 0.5f - 1f;
            float sweep = step / (float)PieSteps * Mathf.PI * 2f;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = x - c, dy = y - c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float edge = Mathf.Clamp01(r - d); // 가장자리 1px 안티에일리어싱
                    float ang = Mathf.Atan2(dx, dy); // 12시(+y) 기준, 시계 방향 양수 (텍스처 y는 위가 +)
                    if (ang < 0f) ang += Mathf.PI * 2f;
                    bool inside = step >= PieSteps || ang <= sweep;
                    byte a = (byte)(inside ? edge * 255f : 0f);
                    px[y * N + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            pieTex[step] = tex;
            return tex;
        }

        static void ShadowLabel(Rect r, string text, GUIStyle style, Color color)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.8f * color.a);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
            GUI.color = color;
            GUI.Label(r, text, style);
            GUI.color = Color.white;
        }

        static Texture2D LoadSkillIcon(SkillKind kind)
        {
            var t = Resources.Load<Texture2D>("UI/Icon_Skill_" + kind);
            return t != null ? t : Resources.Load<Texture2D>("UI/Icon_Skill_Generic");
        }

        /// <summary>LoadKeyed + R↔G 채널 스왑 — 보라 계열 아트를 팀 틸로. (해킹 게이지 전용)</summary>
        static Texture2D LoadKeyedTealSwap(string path)
        {
            var src = LoadKeyed(path);
            if (src == null) return null;
            try
            {
                var px = src.GetPixels32();
                for (int i = 0; i < px.Length; i++) { var r = px[i].r; px[i].r = px[i].g; px[i].g = r; }
                var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
                tex.SetPixels32(px);
                tex.Apply();
                return tex;
            }
            catch (UnityException) { return src; }
        }

        /// <summary>라운드 사이 결과 화면. lines는 폐기된 브리핑 잔재 — null로 온다.</summary>
        /// <summary>
        /// 라운드 결과 화면. 문구 대신 전황을 보여준다 — 거점 A/B/C를 누가 쥐었는지와 양 팀 생존 수.
        /// zoneOwners[i] = 그 거점의 소유 팀(-1 중립), aliveMine/aliveEnemy = 라운드 종료 시점 생존 수.
        /// </summary>
        public void ShowBriefing(int endedRound, int roundWinnerTeam, string[] lines,
            int[] zoneOwners = null, int aliveMine = 0, int aliveEnemy = 0)
        {
            briefingRound = endedRound;
            briefingWinner = roundWinnerTeam;
            briefingLines = lines;
            briefingZones = zoneOwners;
            briefingAliveMine = aliveMine;
            briefingAliveEnemy = aliveEnemy;
            overlay = Overlay.Briefing;
        }

        public void ShowMatchEnd()
        {
            overlay = Overlay.MatchEnd;
            matchEndAt = -1f; // 등장 애니메이션 리셋
        }

        /// <summary>픽 단계(맵/클래스 선택) — 인게임 HUD 전부 숨김. 다음 Init까지 유지.</summary>
        public void Hide()
        {
            battle = null; // OnGUI 조기 리턴
            overlay = Overlay.None;
            subtitleText = null;
            countdownNum = 0;
        }

        int countdownNum; // 라운드 시작 3·2·1 — 0이면 숨김

        /// <summary>라운드 시작 카운트다운 — 중앙 대형 숫자. 0 = 숨김.</summary>
        public void SetCountdown(int num) => countdownNum = num;

        void DrawCountdown()
        {
            if (countdownNum <= 0) return;
            ShadowLabel(new Rect(0, H / 2f - 90, W, 100), countdownNum.ToString(), bannerStyle, new Color(1f, 0.85f, 0.25f));
        }

        float threatRemain = -1f, threatUntil; // 내 칸 피격 예고 — 러너가 매 프레임 갱신, 0.15초 안 오면 꺼짐

        /// <summary>내 칸에 적 예고가 떨어진다 — 상단 붉은 배너. remain = 판정까지 남은 초.</summary>
        public void ShowThreat(float remain)
        {
            threatRemain = remain;
            threatUntil = Time.time + 0.15f;
        }

        void DrawThreat()
        {
            if (Time.time >= threatUntil) return;
            float urgency = 1f - Mathf.Clamp01(threatRemain / 0.8f);
            float beat = 0.5f + 0.5f * Mathf.Sin(Time.time * (6f + 14f * urgency));
            var box = new Rect(W / 2f - 190, 96, 380, 48);
            var red = new Color(1f, 0.25f, 0.18f);
            NeonPanel(box, red, 0.6f + 0.4f * beat);
            Fill(new Rect(box.x + 1, box.y + 1, box.width - 2, box.height - 2), new Color(0.6f, 0.05f, 0.02f, 0.25f + 0.2f * beat));
            ShadowLabel(new Rect(box.x, box.y + 6, box.width, 36),
                $"피격 예고 {Mathf.Max(0f, threatRemain):0.0}s. 피하세요!", subtitleStyle,
                Color.Lerp(Color.white, red, 0.25f * beat));
        }

        /// <summary>보이스 재생 동안 하단에 한글 자막 표시.</summary>
        public void ShowSubtitle(string text, float seconds)
        {
            subtitleText = text;
            subtitleUntil = Time.time + seconds;
        }

        /// <summary>큰 중앙 공지 — 거점 점령 등 "지금 이거 봐" 급. 팀 색으로.</summary>
        public void ShowAnnounce(string text, Color color, float seconds)
        {
            announceText = text;
            announceColor = color;
            announceUntil = Time.time + seconds;
        }

        /// <summary>빠른채팅 수신 — 좌하단 로그에 한 줄 추가. 팀 필터는 호출부 담당.</summary>
        /// <summary>킬피드 한 줄 — 우상단. color = 가해자 팀 색. killer=null이면 환경사("처치됨").</summary>
        public void AddKill(string killer, string victim, Color color)
        {
            string text = string.IsNullOrEmpty(killer) ? $"{victim} 처치됨" : $"{killer}: {victim} 처치";
            killFeed.Add(new ChatEntry { text = text, color = color, until = Time.time + 6f });
            if (killFeed.Count > KillFeedMax) killFeed.RemoveAt(0);
        }

        public void AddChatLine(string callsign, string text, Color color)
        {
            chatLog.Add(new ChatEntry { text = $"[{callsign}] {text}", color = color, until = Time.time + 8f }); // 문장이 길어져 6 → 8초
            if (chatLog.Count > ChatLogMax) chatLog.RemoveAt(0);
        }

        void OnGUI()
        {
            if (battle == null) return;
            EnsureStyles();
            float s = UiScale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));

            DrawTopBar();
            if (overlay == Overlay.None)
            {
                DrawBanner();
                DrawBottomBar();
                DrawChatLog();
                DrawKillFeed();
                DrawChatPanel();
                DrawAnnounce();
                DrawCountdown();
                DrawThreat();
                DrawEdgePings();
                DrawPingWheel();
            }
            if (overlay == Overlay.Briefing) DrawBriefing();
            else if (overlay == Overlay.MatchEnd) DrawMatchEnd();
            DrawSubtitle();
            DrawHeroCard(); // 오버레이 위에도 보이게 마지막에

            GUI.matrix = Matrix4x4.identity;
        }

        void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;
            timerStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            timerLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            dotStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleCenter };
            // 유니티 기본 회색 박스 대신 어두운 판 + 흰 글자 — 호버 시 살짝 밝게
            chipStyle = new GUIStyle(GUI.skin.box) { fontSize = 14, fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleCenter };
            chipStyle.normal.background = Solid(new Color(0.04f, 0.07f, 0.13f, 0.92f));
            chipStyle.normal.textColor = new Color(0.85f, 0.92f, 1f);
            chipStyle.hover.background = Solid(new Color(0.08f, 0.14f, 0.24f, 0.95f));
            chipStyle.hover.textColor = Color.white;
            chipStyle.active.background = Solid(new Color(0.1f, 0.2f, 0.3f, 0.95f));
            chipStyle.active.textColor = HudCyan;
            roundStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            bannerTextStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleCenter };
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            killStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleRight };
            announceStyle = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            bannerStyle = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            briefTitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            briefLineStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft, wordWrap = true };
            keyStyle = new GUIStyle(GUI.skin.box) { fontSize = 12, fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleCenter };
            keyStyle.normal.background = Solid(new Color(0.06f, 0.1f, 0.18f, 0.95f)); // 키캡 — 회색 박스 대체
            keyStyle.normal.textColor = HudCyan;
            slotNameStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
            slotCostStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            slotCoolStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleCenter };
            bigNumStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            subStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            subtitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleCenter, wordWrap = true };

            // 폰트: 어그로체 = 타이틀·배너·자막 임팩트, SUIT = HUD 전반
            GameFonts.Apply(bannerStyle, GameFonts.Title);      // 매치 승/패 배너
            GameFonts.Apply(subtitleStyle, GameFonts.Title);    // 관제 자막
            GameFonts.Apply(bannerTextStyle, GameFonts.Title);  // 안내 배너
            GameFonts.Apply(briefTitleStyle, GameFonts.Title);  // 브리핑 제목
            GameFonts.Apply(timerStyle, GameFonts.HudHeavy);    // 남은시간 숫자
            GameFonts.Apply(bigNumStyle, GameFonts.HudHeavy);   // AP 숫자
            GameFonts.Apply(timerLabelStyle, GameFonts.Hud);
            GameFonts.Apply(dotStyle, GameFonts.Hud);
            GameFonts.Apply(chipStyle, GameFonts.HudHeavy);
            GameFonts.Apply(roundStyle, GameFonts.Hud);
            GameFonts.Apply(labelStyle, GameFonts.Hud);
            GameFonts.Apply(killStyle, GameFonts.HudHeavy);
            GameFonts.Apply(announceStyle, GameFonts.Title);
            GameFonts.Apply(briefLineStyle, GameFonts.Hud);
            GameFonts.Apply(keyStyle, GameFonts.Hud);
            GameFonts.Apply(slotNameStyle, GameFonts.Hud);
            GameFonts.Apply(slotCostStyle, GameFonts.Hud);
            GameFonts.Apply(slotCoolStyle, GameFonts.HudHeavy);
            GameFonts.Apply(subStyle, GameFonts.Hud);
        }

        // ── 상단 바: 생존·스코어 | 남은시간 | 거점 칩 ─────────────────

        void DrawTopBar()
        {
            // 남은시간 (탱고파이브 중앙 타이머) — 프레임 아트의 테두리 여백만큼 텍스트를 안쪽에
            var timerBox = new Rect(W / 2f - 84, 2, 168, 62);
            DrawFrame(timerBox, texTimer != null ? texTimer : texInfo);
            if (round.Overtime)
            {
                GUI.color = new Color(1f, 0.78f, 0.25f); // 호박색 — 빨강은 적 위협 전용
                GUI.Label(new Rect(timerBox.x, timerBox.y + 12, timerBox.width, 38), "추가시간", timerStyle);
                GUI.color = Color.white;
            }
            else
            {
                float remain = Mathf.Max(0f, round.Config.roundSeconds - battle.time);
                GUI.Label(new Rect(timerBox.x, timerBox.y + 9, timerBox.width, 14), "남은시간", timerLabelStyle);
                // 긴급도 색 — 30초 이하 호박색, 10초 이하 빨강 맥동
                var timerColor = Color.white;
                if (remain <= 10f) timerColor = Color.Lerp(new Color(1f, 0.35f, 0.3f), Color.white, 0.5f + 0.5f * Mathf.Sin(Time.time * 8f));
                else if (remain <= 30f) timerColor = new Color(1f, 0.78f, 0.25f);
                ShadowLabel(new Rect(timerBox.x, timerBox.y + 23, timerBox.width, 30),
                    $"{(int)remain / 60}:{(int)remain % 60:00}", timerStyle, timerColor);
            }

            // 좌 = 아군 생존 ● + 매치 승수, 우 = 적군 (탱고파이브 해골 카운터 자리)
            DrawTeamStatus(new Rect(W / 2f - 250, 10, 170, 40), playerTeam, allyColor, rightAlign: false);
            DrawTeamStatus(new Rect(W / 2f + 80, 10, 170, 40), 1 - playerTeam, enemyColor, rightAlign: true);

            // 거점 칩 A/B/C (탱고파이브 상단 ABC)
            var zones = round.Zones;
            float chipW = 34f, gap = 6f;
            float x0 = W / 2f - (zones.Count * chipW + (zones.Count - 1) * gap) / 2f;
            string[] letters = { "A", "B", "C" };
            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                var chipRect = new Rect(x0 + i * (chipW + gap), 70, chipW, 24);
                GUI.color = z.owner == -1 ? new Color(0.55f, 0.55f, 0.6f)
                    : Color.Lerp(z.owner == playerTeam ? allyColor : enemyColor, Color.white, 0.25f);
                if (texChip != null)
                    GUI.DrawTexture(chipRect, texChip, ScaleMode.StretchToFill);
                else
                    GUI.Box(chipRect, "", chipStyle);

                // 점거 파이 (2026-09-06) — 누가 얼마나 먹어가는지 상단에서 실시간으로. 점거 팀 색, 시계 방향으로 찬다.
                float capSec = round.Config.captureSeconds;
                if (z.capturingTeam >= 0 && z.progress > 0f && capSec > 0f)
                {
                    float frac = Mathf.Clamp01(z.progress / capSec);
                    var pieRect = new Rect(chipRect.x + chipRect.width - 20f, chipRect.y + 2f, 20f, 20f);
                    GUI.color = new Color(0f, 0f, 0f, 0.45f);
                    GUI.DrawTexture(pieRect, PieTex(PieSteps), ScaleMode.StretchToFill);
                    var pc = z.capturingTeam == playerTeam ? allyColor : enemyColor;
                    GUI.color = new Color(pc.r, pc.g, pc.b, 0.95f);
                    GUI.DrawTexture(pieRect, PieTex(Mathf.RoundToInt(frac * PieSteps)), ScaleMode.StretchToFill);
                }
                GUI.color = Color.white;
                var letterRect = new Rect(chipRect.x - 4f, chipRect.y, chipRect.width, chipRect.height);
                GUI.Label(letterRect, i < letters.Length ? letters[i] : "?", slotNameStyle);
            }

            // 판세 스코어 (2026-09-05) — "지금 누가 이기고 있나"를 칩 색만으로 못 읽던 문제
            int myZ = 0, enZ = 0;
            foreach (var z in round.Zones)
            {
                if (z.owner == playerTeam) myZ++;
                else if (z.owner == 1 - playerTeam) enZ++;
            }
            var zsStyle = new GUIStyle(dotStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 15 };
            ShadowLabel(new Rect(W / 2f - 100, 97, 88, 18), $"거점 {myZ}", zsStyle, allyColor);
            ShadowLabel(new Rect(W / 2f - 12, 97, 24, 18), ":", zsStyle, new Color(0.6f, 0.65f, 0.72f));
            ShadowLabel(new Rect(W / 2f + 12, 97, 88, 18), $"{enZ} 적", zsStyle, enemyColor);
            GUI.Label(new Rect(W / 2f - 100, 114, 200, 14),
                $"ROUND {match.CurrentRound}/{MatchSystem.MaxRounds}", roundStyle);
            if (!string.IsNullOrEmpty(roundRuleChip))
            {
                // 라운드 변형 규칙 상시 칩 (2026-09-05) — 시작 자막을 놓치면 "점령 안 되는 버그"가 된다
                var chipStyle2 = new GUIStyle(roundStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 13 };
                var chipRect = new Rect(W / 2f - 110, 131, 220, 20);
                Fill(chipRect, new Color(0.04f, 0.06f, 0.1f, 0.85f));
                Edge(chipRect, new Color(1f, 0.78f, 0.25f, 0.5f));
                ShadowLabel(chipRect, $"규칙: {roundRuleChip}", chipStyle2, new Color(1f, 0.85f, 0.45f));
            }
        }

        void DrawTeamStatus(Rect r, int team, Color color, bool rightAlign)
        {
            int alive = 0, total = 0;
            foreach (var u in battle.Units)
                if (u.team == team)
                {
                    total++;
                    if (u.alive) alive++;
                }

            // 생존 핍 — 텍스트 ●○ 대신 드로잉 (산 유닛 = 팀색 채움 + 밝은 윗변, 죽은 유닛 = 어두운 슬롯)
            var style = new GUIStyle(dotStyle) { alignment = rightAlign ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft };
            const float pipW = 16f, pipH = 8f, pipGap = 4f;
            float pipsW = total * pipW + (total - 1) * pipGap;
            float labelW = 46f;
            float pipX = rightAlign ? r.xMax - labelW - 8f - pipsW : r.x + labelW + 8f;
            ShadowLabel(new Rect(rightAlign ? r.xMax - labelW : r.x, r.y, labelW, 20),
                team == playerTeam ? "아군" : "적군", new GUIStyle(style) { alignment = rightAlign ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft }, color);
            for (int i = 0; i < total; i++)
            {
                var pip = new Rect(pipX + i * (pipW + pipGap), r.y + 6f, pipW, pipH);
                if (i < alive)
                {
                    Fill(pip, color);
                    Fill(new Rect(pip.x, pip.y, pip.width, 2f), Color.Lerp(color, Color.white, 0.55f)); // 윗변 하이라이트
                }
                else
                {
                    Fill(pip, new Color(0.05f, 0.07f, 0.12f, 0.85f));
                    Edge(pip, new Color(color.r, color.g, color.b, 0.35f));
                }
            }

            string winsText = $"승리 {match.GetWins(team)}/{MatchSystem.WinsNeeded}"; // 3판 2선승
            GUI.Label(new Rect(r.x, r.y + 20, r.width, 16),
                winsText, new GUIStyle(roundStyle) { alignment = rightAlign ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft });
        }

        // ── 상황 안내 배너 (탱고파이브 "공격할 대상을 선택하세요") ────

        bool skipHint, skipActive; // 싱글 사망 후 빨리감기 안내 — 러너가 매 프레임 갱신

        public void SetSkipHint(bool show, bool active)
        {
            skipHint = show;
            skipActive = active;
        }

        // 상단 배너 = 전황 로그 (2026-09-05 유저: 조작 안내는 의미를 모르겠다 → 킬·거점 이벤트만).
        // 최근 이벤트 하나를 6초간. 내가 죽었을 땐 빨리감기 안내가 우선.

        /// <summary>안내 한 줄 — 튜토리얼 힌트, 이탈, 명령 거부. 이벤트 배너는 은퇴(2026-09-06 "중앙 공지와 둘 다 있어 불편")하고 자막 레인으로 보낸다. 색은 자막이 정한다.</summary>
        public void PushEvent(string text, Color color) => ShowSubtitle(text, 3.5f);

        void DrawBanner()
        {
            var u = battle.GetUnit(playerUnitId);
            string msg;
            var bannerColor = new Color(0.2f, 0.75f, 0.85f, 0.85f); // 기본 = 탱고파이브 시안
            if ((u == null || !u.alive) && skipHint)
            {
                msg = skipActive ? "빨리감기 중 (6배속). SPACE: 해제"
                    : GameModeState.IsCommander ? "격파됨. Enter 무전, 숫자키로 분대 지휘 계속. SPACE 빨리감기"
                    : "격파됨. SPACE: 라운드 결과까지 빨리감기";
                if (skipActive) bannerColor = new Color(1f, 0.78f, 0.25f, 0.9f); // 호박색 — 비정상 속도 표시
            }
            else return; // 배너는 전사 후 빨리감기 안내 전용 — 전황 이벤트 로그는 은퇴 (2026-09-06)

            // 프레임 사선 컷 여백만큼 텍스트를 안쪽에 — 텍스트가 프레임을 뚫지 않게
            var box = new Rect(W / 2f - 240, 158, 480, 34); // 118 → 158: ROUND 라벨·규칙 칩(131)과 겹침 (2026-09-05)
            GUI.color = bannerColor;
            if (texBanner != null)
            {
                GUI.DrawTexture(box, texBanner, ScaleMode.StretchToFill);
                GUI.color = new Color(0.85f, 0.98f, 1f); // 어두운 프레임 위 밝은 텍스트
            }
            else
            {
                GUI.DrawTexture(box, Texture2D.whiteTexture);
                GUI.color = new Color(0.05f, 0.15f, 0.2f);
            }
            GUI.Label(new Rect(box.x + 30, box.y + 2, box.width - 60, box.height - 4), msg, bannerTextStyle);
            GUI.color = Color.white;
        }

        string DisplayName() => string.IsNullOrEmpty(playerName)
            ? battle.GetUnit(playerUnitId).unitClass.ToString() : playerName;

        // ── 하단 통합 바: HP | 슬롯 4개 | 해킹 궁게이지 ──────────────

        void DrawBottomBar()
        {
            var u = battle.GetUnit(playerUnitId);
            if (u == null || !u.alive) return;

            const float slotW = 108f, slotH = 64f, gap = 8f, segW = 190f; // 새 프레임 테두리가 두꺼워 150은 HP 칸이 좁았다
            float totalW = segW + gap + 4 * slotW + 3 * gap + gap + segW;
            float x0 = W / 2f - totalW / 2f;
            float y = H - slotH - 18f;

            DrawFrame(new Rect(x0 - 34, y - 14, totalW + 68, slotH + 30), texPanel); // 바 배경 — 장식 테두리가 내용 밖에 오도록 여유

            // HP 세그먼트 (탱고파이브 좌측 캐릭터 정보 자리)
            var hpSeg = new Rect(x0, y, segW, slotH);
            GUI.color = Color.Lerp(allyColor, Color.white, 0.4f);
            GUI.Label(new Rect(hpSeg.x, hpSeg.y + 2, hpSeg.width, 18), $"{DisplayName()} / {u.unitClass}", slotNameStyle);
            GUI.color = Color.white;
            Bar(new Rect(hpSeg.x + 14, hpSeg.y + 26, hpSeg.width - 28, 12), u.hp / (float)u.maxHp,
                new Color(0.3f, 0.9f, 0.4f), segments: u.maxHp); // HP 칸 눈금 — 이산 수치가 읽힌다
            GUI.Label(new Rect(hpSeg.x, hpSeg.y + 40, hpSeg.width, 18), $"HP {u.hp}/{u.maxHp}", slotCostStyle);

            // 슬롯 4개
            float sx = x0 + segW + gap;
            float moveCoolFrac = u.moveCooldown > 0f && u.profile.yellowCooldownSeconds > 0f
                ? u.moveCooldown / u.profile.yellowCooldownSeconds : 0f;
            DrawSlot(new Rect(sx, y, slotW, slotH), "좌클릭", "이동",
                $"게이지 {(int)u.moveGauge}", u.moveCooldown <= 0f, u.moveCooldown, moveCoolFrac, false, iconMove);

            var aim = moveInput != null ? moveInput.CurrentAim : UnitMoveInput.AimMode.None;
            float atkCool = Mathf.Max(0f, u.attackReadyAt - battle.time);
            DrawSlot(new Rect(sx + (slotW + gap), y, slotW, slotH), "A", "일반공격",
                $"쿨 {combatConfig.attackCooldownSeconds:0}s", atkCool <= 0f,
                atkCool, combatConfig.attackCooldownSeconds > 0f ? atkCool / combatConfig.attackCooldownSeconds : 0f,
                aim == UnitMoveInput.AimMode.Attack, iconAttack,
                () => moveInput?.ToggleAim(UnitMoveInput.AimMode.Attack));

            var s1 = ClassCatalog.Get(u.unitClass).skills[0];
            float s1Cool = Mathf.Max(0f, u.skillReadyAt[0] - battle.time);
            DrawSlot(new Rect(sx + (slotW + gap) * 2, y, slotW, slotH), "S", SkillName(u.unitClass, 0),
                $"쿨 {s1.cooldownSeconds:0}s", s1Cool <= 0f,
                s1Cool, s1.cooldownSeconds > 0f ? s1Cool / s1.cooldownSeconds : 0f,
                aim == UnitMoveInput.AimMode.Skill, iconSkill1,
                () => moveInput?.ToggleAim(UnitMoveInput.AimMode.Skill));

            var s2 = ClassCatalog.Get(u.unitClass).skills[1];
            float s2Cool = Mathf.Max(0f, u.skillReadyAt[1] - battle.time);
            DrawSlot(new Rect(sx + (slotW + gap) * 3, y, slotW, slotH), "D", SkillName(u.unitClass, 1),
                $"쿨 {s2.cooldownSeconds:0}s", s2Cool <= 0f,
                s2Cool, s2.cooldownSeconds > 0f ? s2Cool / s2.cooldownSeconds : 0f,
                aim == UnitMoveInput.AimMode.Skill2, iconSkill2,
                () => moveInput?.ToggleAim(UnitMoveInput.AimMode.Skill2));

            // 해킹 궁게이지 세그먼트 (구 AP 탄약 카운터 자리) — 만충 시 H 발동
            var hackSeg = new Rect(x0 + totalW - segW, y, segW, slotH);
            if (GUI.Button(hackSeg, GUIContent.none, GUIStyle.none)) OnHackClicked?.Invoke(); // 클릭 = H키
            float charge = hackCharge != null ? Mathf.Clamp01(hackCharge()) : 0f;
            bool hackReady = charge >= 1f;
            // 색 언어: 핑크·보라(마법소녀 톤) 대신 내 팀 틸 — 준비 완료면 밝게 맥동
            var hackColor = hackReady
                ? Color.Lerp(new Color(0.45f, 1f, 0.95f), Color.white, 0.5f + 0.5f * Mathf.Sin(Time.time * 6f))
                : new Color(0.3f, 0.75f, 0.72f);
            if (texGauge != null) // 게이지 링 아트 — 숫자 뒤 장식, 충전량은 알파로
            {
                GUI.color = new Color(1f, 1f, 1f, 0.35f + 0.65f * charge);
                GUI.DrawTexture(new Rect(hackSeg.xMax - 58, hackSeg.y + 3, 56, 56), texGauge, ScaleMode.ScaleToFit);
            }
            GUI.color = hackColor;
            GUI.Label(new Rect(hackSeg.x, hackSeg.y + 2, hackSeg.width, 30), $"{charge * 100f:0}%", bigNumStyle);
            GUI.color = Color.white;
            GUI.Label(new Rect(hackSeg.x, hackSeg.y + 30, hackSeg.width, 14),
                hackReady ? "시야해킹 준비 완료: H" : "시야해킹 게이지", subStyle);
            Bar(new Rect(hackSeg.x + 14, hackSeg.y + 48, hackSeg.width - 28, 8), charge, hackColor, segments: 4);
        }

        void DrawSlot(Rect r, string key, string name, string cost, bool enabled, float coolRemain, float coolFrac,
            bool active = false, Texture2D icon = null, System.Action onClick = null)
        {
            // 슬롯 클릭 = 단축키와 동일 (2026-09-05) — 투명 버튼이 히트박스만 담당
            if (onClick != null && GUI.Button(r, GUIContent.none, GUIStyle.none)) onClick();
            if (active)
            {
                // 조준 중인 슬롯 — 틸 프레임 (노랑은 이동 색이라 금지). 조준 칸 틴트와 같은 색이라 연결이 읽힌다
                GUI.color = new Color(0.45f, 1f, 0.95f);
                GUI.DrawTexture(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), Texture2D.whiteTexture);
            }
            GUI.color = enabled ? Color.white : new Color(1f, 1f, 1f, 0.4f);
            DrawFrame(r, texSlot);
            if (icon != null)
                GUI.DrawTexture(new Rect(r.x + r.width - 30, r.y + 4, 26, 26), icon, ScaleMode.ScaleToFit);
            GUI.Box(new Rect(r.x + 5, r.y + 5, key.Length > 2 ? 46 : 22, 18), key, keyStyle);
            GUI.Label(new Rect(r.x, r.y + 25, r.width, 18), name, slotNameStyle);
            GUI.Label(new Rect(r.x, r.y + 43, r.width, 16), cost, slotCostStyle);
            GUI.color = Color.white;

            if (coolRemain > 0f)
            {
                float fh = r.height * Mathf.Clamp01(coolFrac);
                GUI.color = new Color(0f, 0f, 0f, 0.65f);
                GUI.DrawTexture(new Rect(r.x, r.y + r.height - fh, r.width, fh), Texture2D.whiteTexture);
                GUI.color = Color.white; // 쿨타임 숫자 — 주황은 적 색이라 흰색으로
                GUI.Label(new Rect(r.x, r.y, r.width, r.height), $"{coolRemain:0.0}s", slotCoolStyle);
                GUI.color = Color.white;
            }
        }

        static string SkillName(UnitClass cls, int idx)
        {
            switch (ClassCatalog.Get(cls).skills[idx].kind)
            {
                case SkillKind.ShieldPush: return "방패 밀어붙이기";
                case SkillKind.Smash: return "던져버리기";
                case SkillKind.Dash: return "돌파";
                case SkillKind.Scream: return "비명 교란";
                case SkillKind.Blink: return "그림자 도약";
                case SkillKind.Claw: return "발톱 쥐어짜기";
                case SkillKind.Burst: return "파열탄";
                case SkillKind.BombDeliver: return "폭탄 배달";
                case SkillKind.Snatch: return "낚아채기";
                case SkillKind.KnockShot: return "넉백샷";
                case SkillKind.Snipe: return "조준 사격";
                default: return "스킬";
            }
        }

        // ── 오버레이 ──────────────────────────────────────────────

        /// <summary>
        /// 라운드 결과의 알맹이 — 거점을 누가 쥐었나와 양 팀이 몇 명 남았나.
        /// 색이 소속을 말한다: 파랑=우리, 빨강=적, 회색=중립 (전투 화면의 팀 색 언어와 같다).
        /// </summary>
        void DrawBriefingStatus(Rect box)
        {
            // 하드코딩 파랑/빨강 폐지 (2026-09-05 "팀 바꿨는데 결과화면은 파랑이 내 팀") —
            // 진짜 팀색을 쓴다. 내 팀이 빨강이면 왼쪽 줄도 빨강.
            var mine = Color.Lerp(allyColor, Color.white, 0.15f);
            var foe = Color.Lerp(enemyColor, Color.white, 0.15f);
            var neutral = new Color(0.45f, 0.5f, 0.58f);

            float y = box.y + 112f;
            GUI.color = new Color(0.6f, 0.68f, 0.78f);
            GUI.Label(new Rect(box.x, y, box.width, 20f), "거점",
                new GUIStyle(subStyle) { alignment = TextAnchor.MiddleCenter });
            GUI.color = Color.white;
            y += 24f;

            int zoneN = briefingZones != null ? briefingZones.Length : 0;
            var zoneStyle = new GUIStyle(briefTitleStyle) { fontSize = 34 };
            const float ZoneW = 78f;
            float zx = box.x + box.width / 2f - zoneN * ZoneW / 2f;
            for (int i = 0; i < zoneN; i++)
            {
                int owner = briefingZones[i];
                GUI.color = owner < 0 ? neutral : (owner == playerTeam ? mine : foe);
                GUI.Label(new Rect(zx + i * ZoneW, y, ZoneW, 40f),
                    OrderPresets.ZoneName(i), zoneStyle);
            }
            GUI.color = Color.white;
            y += 56f;

            GUI.color = new Color(0.6f, 0.68f, 0.78f);
            GUI.Label(new Rect(box.x, y, box.width, 20f), "생존",
                new GUIStyle(subStyle) { alignment = TextAnchor.MiddleCenter });
            GUI.color = Color.white;
            y += 26f;

            // 좌우 고정 배치 (2026-09-05 "파란팀 왼쪽·빨간팀 오른쪽"): 팀당 3칸을 항상 같은 자리에.
            // 한쪽이 전멸해도 자리가 흐린 원으로 남아 "누가 몇 명 잃었나"가 즉독된다.
            const float Dot = 18f, DotGap = 8f, SideGap = 44f;
            const int SlotsPerTeam = 3;
            float sideW = SlotsPerTeam * (Dot + DotGap) - DotGap;
            float cx = box.x + box.width / 2f;
            float leftX = cx - SideGap / 2f - sideW;
            float rightX = cx + SideGap / 2f;

            for (int i = 0; i < SlotsPerTeam; i++)
            {
                var lr = new Rect(leftX + i * (Dot + DotGap), y, Dot, Dot);
                Dish(lr, i < briefingAliveMine ? mine : new Color(mine.r, mine.g, mine.b, 0.18f));
                var rr = new Rect(rightX + i * (Dot + DotGap), y, Dot, Dot);
                Dish(rr, i < briefingAliveEnemy ? foe : new Color(foe.r, foe.g, foe.b, 0.18f));
            }
            y += Dot + 22f;

            // 전적 (2026-09-05) — 개인 킬/데스. 왼쪽 우리 파랑, 오른쪽 적 빨강 (좌우 규칙 동일)
            GUI.color = new Color(0.6f, 0.68f, 0.78f);
            GUI.Label(new Rect(box.x, y, box.width, 20f), "전적",
                new GUIStyle(subStyle) { alignment = TextAnchor.MiddleCenter });
            GUI.color = Color.white;
            y += 24f;

            var statStyle = new GUIStyle(labelStyle) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
            var statStyleR = new GUIStyle(labelStyle) { fontSize = 14, alignment = TextAnchor.MiddleRight };
            int rows = Mathf.Max(briefingStatsMine?.Length ?? 0, briefingStatsFoe?.Length ?? 0);
            for (int i = 0; i < rows; i++)
            {
                if (briefingStatsMine != null && i < briefingStatsMine.Length)
                    ShadowLabel(new Rect(box.x + 70f, y + i * 22f, box.width / 2f - 90f, 20f),
                        briefingStatsMine[i], statStyle, mine);
                if (briefingStatsFoe != null && i < briefingStatsFoe.Length)
                    ShadowLabel(new Rect(box.x + box.width / 2f + 20f, y + i * 22f, box.width / 2f - 90f, 20f),
                        briefingStatsFoe[i], statStyleR, foe);
            }
        }

        /// <summary>동그라미 하나 — 원형 텍스처가 없어 사각형을 겹쳐 둥글게 낸다.</summary>
        void Dish(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x + r.width * 0.22f, r.y, r.width * 0.56f, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y + r.height * 0.22f, r.width, r.height * 0.56f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x + r.width * 0.11f, r.y + r.height * 0.11f, r.width * 0.78f, r.height * 0.78f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void DrawBriefing()
        {
            bool myWin = briefingWinner == playerTeam;
            // 전황 세 줄(거점·생존·전적) 고정 높이 — 문구 브리핑을 걷어낸 뒤(2026-09-05) 실측이 필요 없어졌다
            const float boxH = 470f;
            var box = new Rect(W / 2f - 270, H / 2f - boxH / 2f, 540, boxH);
            briefingBottomY = box.yMax; // 자막 겹침 방지용 — DrawSubtitle이 참조
            NeonPanel(box, myWin ? new Color(0.4f, 1f, 0.6f) : new Color(1f, 0.45f, 0.35f));
            if (panelBriefing != null)
                GUI.DrawTexture(box, panelBriefing, ScaleMode.StretchToFill); // 관제 터미널 배경

            // 결과 배너 아트 (날개 프레임) — 중립색 아트를 승/패 색으로 틴트. 없으면 구 백킹 폴백.
            if (texResultBanner != null)
            {
                GUI.color = myWin ? new Color(0.55f, 1f, 0.7f) : new Color(1f, 0.55f, 0.45f);
                GUI.DrawTexture(new Rect(box.x + 40, box.y + 22, box.width - 80, 74), texResultBanner, ScaleMode.ScaleToFit);
            }
            else
            {
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(box.x + 50, box.y + 44, box.width - 100, 30), Texture2D.whiteTexture);
            }
            GUI.color = myWin ? new Color(0.4f, 1f, 0.6f) : new Color(1f, 0.45f, 0.35f);
            GUI.Label(new Rect(box.x, box.y + 46, box.width, 26),
                $"ROUND {briefingRound}  {(myWin ? "승리" : "패배")}", briefTitleStyle);
            if (!string.IsNullOrEmpty(briefingReason))
            {
                GUI.color = Color.white;
                ShadowLabel(new Rect(box.x, box.y + 96f, box.width, 18f), briefingReason,
                    new GUIStyle(roundStyle) { fontSize = 14, alignment = TextAnchor.MiddleCenter },
                    myWin ? new Color(0.6f, 1f, 0.75f) : new Color(1f, 0.6f, 0.5f));
            }
            GUI.color = Color.white;

            DrawBriefingStatus(box);

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(box.x + 50, box.y + box.height - 60, box.width - 100, 24), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.85f, 0.25f);
            string footer = readyTotal > 1
                ? $"SPACE: 다음 라운드 동의  ({readyCount}/{readyTotal})"
                : "SPACE: 다음 라운드";
            GUI.Label(new Rect(box.x, box.y + box.height - 58, box.width, 22),
                footer, new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter });
            GUI.color = Color.white;
        }

        // 다음 라운드 동의 현황 (멀티) — 러너가 갱신
        int readyCount, readyTotal;
        float briefingBottomY; // 브리핑 패널 하단 y — 자막이 패널을 뚫지 않게

        public void SetReadyCount(int ready, int total)
        {
            readyCount = ready;
            readyTotal = total;
        }

        /// <summary>상단 중앙 큰 공지 — 거점 점령 등. 등장 팝 + 마지막 0.5초 페이드.</summary>
        void DrawAnnounce()
        {
            if (string.IsNullOrEmpty(announceText) || Time.time >= announceUntil) return;

            float remain = announceUntil - Time.time;
            float a = Mathf.Clamp01(remain / 0.5f); // 마지막 0.5초 페이드아웃

            // 박스 크기 = 텍스트 실측 — 긴 문구(시야해킹 안내 등)가 잘리지 않게. 화면 폭 초과 시 줄바꿈.
            var content = new GUIContent(announceText);
            float boxW = Mathf.Min(W - 40f, Mathf.Max(640f, announceStyle.CalcSize(content).x + 80f));
            float textH = announceStyle.CalcHeight(content, boxW - 60f);
            float boxH = Mathf.Max(66f, textH + 26f);
            var box = new Rect(W / 2f - boxW / 2f, H * 0.24f, boxW, boxH);
            GUI.color = new Color(0f, 0f, 0f, 0.6f * a);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            // 팀 색 상·하 액센트 바
            GUI.color = new Color(announceColor.r, announceColor.g, announceColor.b, a);
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(box.x, box.y + box.height - 3f, box.width, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            ShadowLabel(new Rect(box.x + 30f, box.y + (boxH - textH) / 2f, box.width - 60f, textH),
                announceText, announceStyle,
                new Color(announceColor.r, announceColor.g, announceColor.b, a));
        }

        // ── 히어로 카드 — 라운드 결정타 주인공 (2026-09-05 "자막 간지나게") ──
        string heroName, heroReason;
        float heroStart = -99f, heroUntil = -99f;

        public void ShowHeroCard(string name, string reason, float seconds)
        {
            heroName = name;
            heroReason = reason;
            heroStart = Time.unscaledTime;
            heroUntil = Time.unscaledTime + seconds;
        }

        void DrawHeroCard()
        {
            if (Time.unscaledTime >= heroUntil || string.IsNullOrEmpty(heroName)) return;
            float age = Time.unscaledTime - heroStart;
            float slide = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / 0.28f));       // 왼쪽에서 미끄러져 들어옴
            float a = Mathf.Min(slide, Mathf.Clamp01((heroUntil - Time.unscaledTime) / 0.3f)); // 끝에서 페이드

            var gold = new Color(1f, 0.85f, 0.3f);
            float bw = 560f, bh = 84f;
            float bx = W / 2f - bw / 2f - (1f - slide) * 140f;
            float by = H - 196f; // 포커싱된 캐릭터 아래·하단 조작 바 위 사이 (2026-09-05)

            var mtx = GUI.matrix;
            GUIUtility.RotateAroundPivot(-1.6f, new Vector2(W / 2f, by + bh / 2f)); // 살짝 기울여 — 정적인 자막 탈피

            // 방사 광원 + 다크 밴드 + 금 라인 + 좌측 금 블록
            GUI.color = new Color(gold.r, gold.g, gold.b, 0.2f * a);
            GUI.DrawTexture(new Rect(bx - 70f, by - 46f, bw + 140f, bh + 92f), RadialTex());
            GUI.color = Color.white;
            Fill(new Rect(bx, by, bw, bh), new Color(0.02f, 0.04f, 0.08f, 0.88f * a));
            Fill(new Rect(bx, by, bw * a, 2f), new Color(gold.r, gold.g, gold.b, 0.9f * a));
            Fill(new Rect(bx + bw * (1f - a), by + bh - 2f, bw * a, 2f), new Color(gold.r, gold.g, gold.b, 0.9f * a));
            Fill(new Rect(bx, by, 6f, bh), new Color(gold.r, gold.g, gold.b, 0.85f * a));

            var nameStyle = new GUIStyle(bannerStyle) { fontSize = 27, alignment = TextAnchor.MiddleLeft };
            GUI.color = new Color(gold.r, gold.g, gold.b, 0.3f * a); // 타이트 글로우 한 겹
            GUI.Label(new Rect(bx + 26f, by + 9f, bw - 40f, 36f), $"★  {heroName}의 결정타", nameStyle);
            GUI.color = Color.white;
            ShadowLabel(new Rect(bx + 24f, by + 8f, bw - 40f, 36f), $"★  {heroName}의 결정타", nameStyle,
                new Color(1f, 0.95f, 0.75f, a));
            ShadowLabel(new Rect(bx + 26f, by + 48f, bw - 40f, 24f), heroReason,
                new GUIStyle(subStyle) { fontSize = 17, alignment = TextAnchor.MiddleLeft },
                new Color(0.85f, 0.9f, 0.98f, a));

            GUI.matrix = mtx;
        }

        void DrawSubtitle()
        {
            if (string.IsNullOrEmpty(subtitleText) || Time.time >= subtitleUntil) return;

            // 박스 = 텍스트 실측 (가로 여백 60, 줄바꿈 대응) — 긴 자막이 잘리던 문제 (2026-09-05)
            var content = new GUIContent(subtitleText);
            float boxW = Mathf.Min(W - 40f, Mathf.Max(400f, subtitleStyle.CalcSize(content).x + 60f));
            float textH = subtitleStyle.CalcHeight(content, boxW - 40f);
            float boxH = textH + 22f;
            float boxY = H - 100f - boxH;
            if (overlay == Overlay.Briefing) // 브리핑 패널과 겹침 방지 — 패널 바로 아래로 (2026-09-05)
                boxY = Mathf.Min(H - boxH - 10f, briefingBottomY + 12f);
            else if (overlay == Overlay.MatchEnd) // MVP 페이지 문구와 겹침 방지 — 화면 맨 아래로 (2026-09-05)
                boxY = H - boxH - 16f;
            var box = new Rect(W / 2f - boxW / 2f, boxY, boxW, boxH);
            NeonPanel(box, new Color(0.55f, 0.95f, 1f));
            GUI.color = new Color(0.55f, 0.95f, 1f);
            GUI.Label(new Rect(box.x + 20f, box.y + 14f, box.width - 40f, textH), subtitleText, subtitleStyle);
            GUI.color = Color.white;
        }

        /// <summary>빠른채팅 로그 — 좌하단. 오래된 줄부터 위로 밀려 사라진다.</summary>
        void DrawChatLog()
        {
            for (int i = chatLog.Count - 1; i >= 0; i--)
                if (Time.time >= chatLog[i].until) chatLog.RemoveAt(i);
            if (chatLog.Count == 0) return;

            const float boxW = 480f; // 무전 문장(35자 안팎)이 한 줄에 들어가는 폭. 넘치면 줄바꿈 (2026-09-06 "말이 다 잘림")
            if (chatStyle == null) chatStyle = new GUIStyle(labelStyle) { wordWrap = true, fontSize = 17 }; // 13 → 17, "대사가 잘 안 보인다" (2026-09-06)
            float total = 0f;
            var hs = new float[chatLog.Count];
            for (int i = 0; i < chatLog.Count; i++)
            {
                hs[i] = Mathf.Max(26f, chatStyle.CalcHeight(new GUIContent(chatLog[i].text), boxW - 16f));
                total += hs[i];
            }
            // 무전창이 열려 있으면 채팅 로그가 입력줄 바로 위에 붙는다 — 롤 채팅처럼 한 덩어리 (2026-09-06 "따로 노는 느낌")
            // RadioWindow와 같은 스케일·x·폭으로 맞춘다 (그쪽은 Screen 픽셀, 여기는 HUD 단위 → UiScale로 환산)
            float logX = 12f, logW = boxW;
            float y;
            if (RadioWindow.TextInputActive)
            {
                float rs = Mathf.Max(1f, Screen.height / 1080f) * 1.25f;
                float radioW = Mathf.Min(560f * rs, Screen.width * 0.5f), pad = 10f * rs;
                float radioTopPx = Screen.height - (RadioWindow.FieldBottom + 30f) * rs - pad; // 입력줄 + 상단 안내줄(30) + 패드
                logX = (24f * rs - pad) / UiScale;
                logW = (radioW + pad * 2f) / UiScale;
                y = radioTopPx / UiScale - 6f - total;
            }
            else y = H - 108f - total;

            var logBox = new Rect(logX, y - 4, logW, total + 8);
            Fill(logBox, new Color(0.02f, 0.04f, 0.09f, 0.6f));
            Fill(new Rect(logBox.x, logBox.y, 2f, logBox.height), new Color(allyColor.r, allyColor.g, allyColor.b, 0.8f)); // 좌측 팀색 액센트

            float yy = y;
            for (int i = 0; i < chatLog.Count; i++)
            {
                // 마지막 1초는 서서히 옅어짐 — 사라지는 게 툭 끊기지 않게
                float remain = chatLog[i].until - Time.time;
                var c = chatLog[i].color;
                c.a = Mathf.Clamp01(remain);
                GUI.color = c;
                GUI.Label(new Rect(logX + 8f, yy, logW - 16, hs[i]), chatLog[i].text, chatStyle);
                yy += hs[i];
            }
            GUI.color = Color.white;
        }

        /// <summary>킬피드 — 우상단(빠른채팅 토글 아래). 최근 킬 5줄, 가해자 팀 색, 마지막 1초 페이드.</summary>
        void DrawKillFeed()
        {
            for (int i = killFeed.Count - 1; i >= 0; i--)
                if (Time.time >= killFeed[i].until) killFeed.RemoveAt(i);
            if (killFeed.Count == 0) return;

            const float lineH = 28f, boxW = 280f;
            float x = W - boxW - 12f, y0 = 46f; // 빠른채팅 토글(y=12,h=26) 아래

            for (int i = 0; i < killFeed.Count; i++)
            {
                float y = y0 + i * lineH;
                float remain = killFeed[i].until - Time.time;
                float a = Mathf.Clamp01(remain);
                var c = killFeed[i].color;

                // 진한 배경 + 우측 팀색 액센트 바 — 어느 맵 위에서도 읽히게
                GUI.color = new Color(0f, 0f, 0f, 0.72f * a);
                GUI.DrawTexture(new Rect(x, y, boxW, lineH - 3f), Texture2D.whiteTexture);
                GUI.color = new Color(c.r, c.g, c.b, a);
                GUI.DrawTexture(new Rect(x + boxW - 3f, y, 3f, lineH - 3f), Texture2D.whiteTexture);

                c.a = a;
                GUI.color = c;
                GUI.Label(new Rect(x + 8f, y, boxW - 18f, lineH - 3f), killFeed[i].text, killStyle);
            }
            GUI.color = Color.white;
        }

        /// <summary>
        /// 무전 패널 — 우상단 [무전] 토글로 아래로 펼치고, 문구 클릭 = 전송 (단축키 병기).
        /// Tab 홀드 중에도 임시로 펼쳐진다 (읽기 + 클릭 둘 다 가능).
        /// </summary>
        void DrawChatPanel()
        {
            const float btnW = 96f, btnH = 26f, rowH = 30f, panelW = 170f;
            float x0 = W - panelW - 12f, toggleY = 12f;

            if (GUI.Button(new Rect(W - btnW - 12f, toggleY, btnW, btnH), chatPanelOpen ? "빠른채팅 닫기" : "빠른채팅 열기", chipStyle))
                chatPanelOpen = !chatPanelOpen;

            if (!chatPanelOpen && !ShowChatCheatsheet) return;

            var lines = SeoYuGi.Chat.QuickChat.Lines;
            float panelH = lines.Length * rowH + 30f;
            float y0 = toggleY + btnH + 6f;

            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(new Rect(x0, y0, panelW, panelH), Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (texCheatsheet != null) // 치트시트 프레임 아트 — 반투명 백킹 위에 겹침
                GUI.DrawTexture(new Rect(x0 - 6, y0 - 6, panelW + 12, panelH + 12), texCheatsheet, ScaleMode.StretchToFill);
            GUI.color = new Color(0.55f, 0.95f, 1f);
            GUI.Label(new Rect(x0, y0 + 4, panelW, 20), "빠른채팅: 클릭 또는 숫자키", subStyle);
            GUI.color = Color.white;

            for (int i = 0; i < lines.Length; i++)
            {
                var r = new Rect(x0 + 8f, y0 + 26f + i * rowH, panelW - 16f, rowH - 4f);
                if (GUI.Button(r, $"[{(i + 1) % 10}] {lines[i]}", chipStyle)) // 10번째 = 0키
                    OnChatClicked?.Invoke(i);
            }
        }

        string roundRuleChip; // 라운드 변형 규칙 상시 표시 — null이면 평범한 라운드
        string briefingReason; // 라운드 종료 사유 한 줄 — 헤더 아래 (2026-09-05 "왜 이겼는지")
        public void SetBriefingReason(string r) => briefingReason = r;
        string matchEndReason; // 최종 종료 사유 (2026-09-05)
        public void SetMatchEndReason(string r) => matchEndReason = r;
        public void SetRoundRule(string title) => roundRuleChip = title;

        /// <summary>매치엔드 통계 한 줄 — 초상+닉네임+K/D. 러너가 매치 종료 직전 채운다.</summary>
        public struct MatchStatEntry
        {
            public string name; public int cls; public int team;
            public int kills, deaths;
            public int damage;      // 가한 피해 합 — 화력 부문 (2026-09-05)
            public float captureSec; // 점령 기여 초 — 점령 부문
        }
        MatchStatEntry[] matchStats;
        public void SetMatchStats(MatchStatEntry[] entries) => matchStats = entries;

        static readonly string[] CardArtNames = { "Card_Tank", "Card_Balance", "Card_Assassin", "Card_Grenadier", "Card_Sniper" };
        static readonly Texture2D[] cardArts = new Texture2D[5];
        static Texture2D CardArt(int cls)
        {
            if (cls < 0 || cls >= 5) return null;
            if (cardArts[cls] == null) cardArts[cls] = Resources.Load<Texture2D>("UI/" + CardArtNames[cls]);
            return cardArts[cls];
        }

        static int ArgBest(MatchStatEntry[] arr, System.Func<MatchStatEntry, float> key)
        {
            int best = -1; float bv = float.MinValue;
            for (int i = 0; i < arr.Length; i++)
                if (key(arr[i]) > bv) { bv = key(arr[i]); best = i; }
            return best;
        }

        static float Frac01(float x) { x = Mathf.Sin(x) * 43758.5453f; return x - Mathf.Floor(x); }

        float matchEndAt = -1f; // 오버레이 진입 시각 — 등장 애니메이션 기준

        static Texture2D radialTex;
        /// <summary>부드러운 방사형 광원 — 매치엔드 타이틀 글로우용 (제곱 감쇠, 가장자리 0).</summary>
        static Texture2D RadialTex()
        {
            if (radialTex != null) return radialTex;
            const int n = 96;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a = Mathf.Clamp01(1f - d);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            t.Apply();
            return radialTex = t;
        }

        void DrawMatchEnd()
        {
            if (matchEndAt < 0f) matchEndAt = Time.unscaledTime;
            float age = Time.unscaledTime - matchEndAt;
            bool myWin = match.MatchWinner == playerTeam;
            var accent = myWin ? new Color(0.35f, 1f, 0.65f) : new Color(1f, 0.4f, 0.32f);

            // 전체 딤 — 승패 색조로
            Fill(new Rect(0, 0, W, H), new Color(accent.r * 0.12f, accent.g * 0.12f, accent.b * 0.12f, 0.55f));

            // 상·하 팀색 밴드가 중앙에서 바깥으로 열린다 (0.4초)
            float open = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / 0.4f));
            float bandH = 196f; // 초상 통계 스트립이 들어가며 재확장 (2026-09-05)
            float cy = H / 2f;
            Fill(new Rect(0, cy - bandH, W * open, 3f), accent);
            Fill(new Rect(W - W * open, cy + bandH - 3f, W * open, 3f), accent);
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(0, cy - bandH + 3f, W, bandH * 2f - 6f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // 작전 보고 톤 (2026-09-05 "너무 AI같음" — 별·색종이 폐기): 콜사인 EN 라벨 + 큰 국문 + 임무 한 줄
            float pop = Mathf.SmoothStep(1.5f, 1f, Mathf.Clamp01(age / 0.3f));

            float subA = Mathf.Clamp01((age - 0.15f) / 0.3f);
            ShadowLabel(new Rect(0, cy - 78f, W, 22f), myWin ? "M I S S I O N   C O M P L E T E" : "M I S S I O N   F A I L E D",
                new GUIStyle(roundStyle) { fontSize = 15, alignment = TextAnchor.MiddleCenter },
                new Color(accent.r, accent.g, accent.b, subA));

            var titleStyle = new GUIStyle(bannerStyle) { fontSize = Mathf.RoundToInt(62 * pop) };
            string title = myWin ? "승리" : "패배";

            // 네온 글로우 (2026-09-05 "IO게임같아"): 방사형 광원 웅덩이 + 8방향 텍스트 번짐 + 밝은 코어
            float breathe = 0.8f + 0.2f * Mathf.Sin(age * 2.6f); // 은은한 호흡
            GUI.color = new Color(accent.r, accent.g, accent.b, 0.34f * breathe);
            GUI.DrawTexture(new Rect(W / 2f - 340f, cy - 130f, 680f, 240f), RadialTex());
            GUI.color = new Color(1f, 1f, 1f, 0.18f * breathe); // 중심은 흰 광원 — 색만 쌓이면 탁해진다
            GUI.DrawTexture(new Rect(W / 2f - 170f, cy - 80f, 340f, 140f), RadialTex());

            for (int ring = 3; ring >= 1; ring--) // 바깥 겹일수록 멀고 옅게 — IMGUI식 블러
            {
                float off = ring * 2.6f;
                GUI.color = new Color(accent.r, accent.g, accent.b, (0.08f + 0.05f * (3 - ring)) * breathe);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (dx != 0 || dy != 0)
                            GUI.Label(new Rect(dx * off, cy - 50f + dy * off, W, 80f), title, titleStyle);
            }
            GUI.color = Color.white;
            ShadowLabel(new Rect(0, cy - 50f, W, 80f), title, titleStyle, Color.Lerp(Color.white, accent, 0.2f));

            // 타이틀 아래 액센트 언더라인 — 밴드와 같은 속도로 열리고, 글로우를 두른다
            float underW = 240f * open;
            Fill(new Rect(W / 2f - underW / 2f, cy + 28f, underW, 6f), new Color(accent.r, accent.g, accent.b, 0.25f));
            Fill(new Rect(W / 2f - underW / 2f, cy + 30f, underW, 2f), Color.Lerp(accent, Color.white, 0.3f));

            // 스코어 + 임무 문구 (조금 늦게 페이드인)
            float scoreA = Mathf.Clamp01((age - 0.4f) / 0.4f);
            ShadowLabel(new Rect(0, cy + 40f, W, 30f),
                $"{match.GetWins(playerTeam)}  :  {match.GetWins(1 - playerTeam)}",
                new GUIStyle(timerStyle) { fontSize = 28 }, new Color(1f, 1f, 1f, scoreA));
            // 페이지 전환 (2026-09-05 "다음 페이지에서 MVP"): 4초간 결과 페이지 → MVP 전체 화면
            float mvpT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - 4f) / 0.5f));

            if (matchStats != null && mvpT < 1f)
            {
                // ── 1페이지: 초상 통계 스트립 — 내 팀 왼쪽 원본색 / 기계팀 회색 틴트
                float stripA = scoreA * (1f - mvpT);
                const float P = 58f, Gap = 16f, GroupGap = 60f;
                int mineN = 0, foeN = 0;
                foreach (var e in matchStats) { if (e.team == playerTeam) mineN++; else foeN++; }
                float totalW = (mineN + foeN) * (P + Gap) - Gap + GroupGap;
                float x = W / 2f - totalW / 2f;
                float py = cy + 72f;

                var nameStyle2 = new GUIStyle(roundStyle) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int i = 0; i < matchStats.Length; i++)
                    {
                        var e = matchStats[i];
                        if ((pass == 0) != (e.team == playerTeam)) continue;
                        var art = MatchPortrait(e.cls, e.team, out bool tintGray);
                        var pr = new Rect(x, py, P, P);
                        Fill(new Rect(pr.x - 2, pr.y - 2, P + 4, P + 4), new Color(0.03f, 0.05f, 0.09f, 0.9f * stripA));
                        if (art != null)
                        {
                            GUI.color = tintGray ? new Color(0.5f, 0.52f, 0.56f, stripA) : new Color(1f, 1f, 1f, stripA);
                            GUI.DrawTextureWithTexCoords(pr, art, new Rect(0.22f, 0.5f, 0.56f, 0.42f));
                            GUI.color = Color.white;
                        }
                        Edge(pr, new Color(accent.r, accent.g, accent.b, 0.35f * stripA));
                        ShadowLabel(new Rect(pr.x - 6, pr.yMax + 1f, P + 12, 14f), e.name, nameStyle2,
                            new Color(0.92f, 0.95f, 1f, stripA));
                        ShadowLabel(new Rect(pr.x - 6, pr.yMax + 15f, P + 12, 13f), $"{e.kills}킬 {e.deaths}데스",
                            new GUIStyle(nameStyle2) { fontSize = 11 }, new Color(0.62f, 0.68f, 0.78f, stripA));
                        x += P + Gap;
                    }
                    x += GroupGap;
                }
                string closing = myWin ? "도시 관리 AI 소탕 완료" : "작전 실패. 재정비하라";
                if (!string.IsNullOrEmpty(matchEndReason)) closing = $"{matchEndReason}. {closing}"; // 왜인지 (2026-09-05)
                ShadowLabel(new Rect(0, cy + 168f, W, 24f),
                    closing,
                    new GUIStyle(roundStyle) { fontSize = 15, alignment = TextAnchor.MiddleCenter },
                    new Color(0.75f, 0.8f, 0.88f, scoreA * (1f - mvpT)));
            }

            if (matchStats != null && matchStats.Length > 0 && mvpT > 0f)
            {
                // ── 2페이지: MVP 시네마틱 (2026-09-05 "간지나게") — 스포트라이트 + 대각 빔 + 브래킷 + 광택 스윕
                // 가중 MVP (2026-09-05 다부문): 킬·피해·점령 기여를 합산 — 킬 없는 점령왕도 MVP가 될 수 있다
                float Score(MatchStatEntry e2) => e2.kills * 100f + e2.damage * 10f + e2.captureSec * 9f - e2.deaths * 25f;
                int mvp = 0;
                for (int i = 1; i < matchStats.Length; i++)
                    if (Score(matchStats[i]) > Score(matchStats[mvp])) mvp = i;
                var m = matchStats[mvp];
                var gold = new Color(1f, 0.85f, 0.3f);
                float rise = (1f - mvpT) * 46f; // 등장 — 아래에서 떠오르며 정착

                Fill(new Rect(0, 0, W, H), new Color(0.01f, 0.02f, 0.04f, 0.92f * mvpT));

                // 대각 금빛 빔 두 줄 — 무대 조명
                var mtx = GUI.matrix;
                GUIUtility.RotateAroundPivot(-16f, new Vector2(W / 2f, H / 2f));
                Fill(new Rect(W / 2f - 460f, -100f, 150f, H + 200f), new Color(gold.r, gold.g, gold.b, 0.05f * mvpT));
                Fill(new Rect(W / 2f + 290f, -100f, 110f, H + 200f), new Color(gold.r, gold.g, gold.b, 0.04f * mvpT));
                GUI.matrix = mtx;

                // 떠오르는 금빛 불씨 — 은은하게 (결정적 난수, 매 프레임 같은 궤적)
                for (int i = 0; i < 22; i++)
                {
                    float seed = i * 7.31f;
                    float ex = W * 0.5f + (Frac01(seed) - 0.5f) * 560f;
                    float speed = 26f + Frac01(seed * 1.9f) * 34f;
                    float ey = H - ((age * speed + Frac01(seed * 3.7f) * H) % (H * 0.9f));
                    float ea = (0.05f + 0.2f * Frac01(seed * 5.1f)) * mvpT;
                    Fill(new Rect(ex, ey, 3f, 3f), new Color(gold.r, gold.g, gold.b, ea));
                }

                ShadowLabel(new Rect(0, H * 0.085f + rise * 0.4f, W, 40f), "M  V  P",
                    new GUIStyle(bannerStyle) { fontSize = 34 }, new Color(gold.r, gold.g, gold.b, mvpT));
                float ulW = 210f * mvpT;
                Fill(new Rect(W / 2f - ulW / 2f, H * 0.085f + 46f + rise * 0.4f, ulW, 2f), new Color(gold.r, gold.g, gold.b, 0.85f * mvpT));

                // 대형 일러스트 — 금빛 스포트라이트 + 이중 프레임 + 코너 브래킷
                const float IW = 320f, IH = 380f;
                var big = new Rect(W / 2f - IW / 2f, H * 0.175f + rise, IW, IH);
                GUI.color = new Color(gold.r, gold.g, gold.b, 0.3f * mvpT);
                GUI.DrawTexture(new Rect(big.x - 130f, big.y - 90f, IW + 260f, IH + 200f), RadialTex());
                GUI.color = new Color(1f, 1f, 1f, 0.12f * mvpT); // 중심 흰 광원 — 금색만 쌓이면 탁해진다
                GUI.DrawTexture(new Rect(big.x - 30f, big.y - 20f, IW + 60f, IH + 60f), RadialTex());
                GUI.color = Color.white;

                Fill(new Rect(big.x - 5, big.y - 5, IW + 10, IH + 10), new Color(0.02f, 0.03f, 0.06f, 0.95f * mvpT));
                var bart = MatchPortrait(m.cls, m.team, out bool bigGray);
                if (bart != null)
                {
                    GUI.color = bigGray ? new Color(0.55f, 0.57f, 0.6f, mvpT) : new Color(1f, 1f, 1f, mvpT);
                    GUI.DrawTextureWithTexCoords(big, bart, new Rect(0.13f, 0.14f, 0.74f, 0.8f));
                    GUI.color = Color.white;
                }
                // 광택 스윕 — 3초마다 빛의 띠가 초상을 훑는다
                float sweep = (age % 3f) / 3f;
                if (sweep < 0.5f)
                {
                    float sx = big.x - 80f + (IW + 160f) * (sweep * 2f);
                    GUI.color = new Color(1f, 1f, 1f, 0.13f * mvpT);
                    GUI.DrawTexture(new Rect(sx, big.y, 70f, IH), RadialTex());
                    GUI.color = Color.white;
                }
                Edge(new Rect(big.x - 2, big.y - 2, IW + 4, IH + 4), new Color(gold.r, gold.g, gold.b, 0.9f * mvpT));
                Edge(new Rect(big.x - 7, big.y - 7, IW + 14, IH + 14), new Color(gold.r, gold.g, gold.b, 0.25f * mvpT));
                // 코너 브래킷 — 군용 조준 프레임
                const float B = 26f, Bt = 3f;
                var bc = new Color(gold.r, gold.g, gold.b, 0.95f * mvpT);
                Fill(new Rect(big.x - 12, big.y - 12, B, Bt), bc); Fill(new Rect(big.x - 12, big.y - 12, Bt, B), bc);
                Fill(new Rect(big.xMax + 12 - B, big.y - 12, B, Bt), bc); Fill(new Rect(big.xMax + 12 - Bt, big.y - 12, Bt, B), bc);
                Fill(new Rect(big.x - 12, big.yMax + 12 - Bt, B, Bt), bc); Fill(new Rect(big.x - 12, big.yMax + 12 - B, Bt, B), bc);
                Fill(new Rect(big.xMax + 12 - B, big.yMax + 12 - Bt, B, Bt), bc); Fill(new Rect(big.xMax + 12 - Bt, big.yMax + 12 - B, Bt, B), bc);

                // 이름 — 크게, 금빛 글로우 한 겹 + 역할 EN 라벨
                var nameStyleBig = new GUIStyle(bannerStyle) { fontSize = 34 };
                GUI.color = new Color(gold.r, gold.g, gold.b, 0.3f * mvpT);
                GUI.Label(new Rect(0, big.yMax + 20f + 2f + rise * 0.3f, W, 44f), m.name, nameStyleBig);
                GUI.color = Color.white;
                ShadowLabel(new Rect(0, big.yMax + 20f + rise * 0.3f, W, 44f), m.name, nameStyleBig,
                    new Color(1f, 1f, 1f, mvpT));
                string[] roleEn = { "TANKER", "RUNNER", "ASSASSIN", "GRENADIER", "MARKSMAN" };
                string role = m.cls >= 0 && m.cls < 5 ? roleEn[m.cls] : "";
                ShadowLabel(new Rect(0, big.yMax + 62f + rise * 0.3f, W, 18f), role,
                    new GUIStyle(roundStyle) { fontSize = 13, alignment = TextAnchor.MiddleCenter },
                    new Color(gold.r, gold.g, gold.b, 0.75f * mvpT));

                // 사유 = MVP의 대표 부문 (부문별 1등 여부로 판단, 2026-09-05 다부문)
                int topK = 0, topD = 0; float topC = 0f;
                foreach (var e3 in matchStats)
                { topK = Mathf.Max(topK, e3.kills); topD = Mathf.Max(topD, e3.damage); topC = Mathf.Max(topC, e3.captureSec); }
                string why;
                if (m.kills > 0 && m.kills >= topK) why = $"매치 최다 처치. {m.kills}킬 {m.deaths}데스";
                else if (m.captureSec > 0f && m.captureSec >= topC) why = $"점령의 주역. 거점 기여 {Mathf.RoundToInt(m.captureSec)}초";
                else if (m.damage > 0 && m.damage >= topD) why = $"화력의 중심. 총 피해 {m.damage}";
                else why = $"팀의 기둥. {m.kills}킬 / 피해 {m.damage}";
                ShadowLabel(new Rect(0, big.yMax + 86f + rise * 0.3f, W, 20f), why,
                    new GUIStyle(roundStyle) { fontSize = 15, alignment = TextAnchor.MiddleCenter },
                    new Color(0.88f, 0.9f, 0.96f, mvpT));

                // 부문 수상 3종 — 학살 / 점령 / 화력 각 1등 (수치 0이면 생략)
                var awardStyle = new GUIStyle(roundStyle) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
                string[] awardTexts = new string[3];
                int ak = ArgBest(matchStats, e4 => e4.kills); if (ak >= 0 && matchStats[ak].kills > 0) awardTexts[0] = $"학살  {matchStats[ak].name} / {matchStats[ak].kills}킬";
                int ac = ArgBest(matchStats, e4 => e4.captureSec); if (ac >= 0 && matchStats[ac].captureSec > 1f) awardTexts[1] = $"점령  {matchStats[ac].name} / {Mathf.RoundToInt(matchStats[ac].captureSec)}초";
                int ad = ArgBest(matchStats, e4 => e4.damage); if (ad >= 0 && matchStats[ad].damage > 0) awardTexts[2] = $"화력  {matchStats[ad].name} / 피해 {matchStats[ad].damage}";
                float ax = W / 2f - 277f;
                for (int bi = 0; bi < 3; bi++)
                {
                    if (string.IsNullOrEmpty(awardTexts[bi])) continue;
                    var ar = new Rect(ax + bi * 190f, big.yMax + 112f + rise * 0.3f, 174f, 22f);
                    Fill(ar, new Color(0.04f, 0.06f, 0.1f, 0.85f * mvpT));
                    Edge(ar, new Color(gold.r, gold.g, gold.b, 0.35f * mvpT));
                    ShadowLabel(ar, awardTexts[bi], awardStyle, new Color(0.9f, 0.92f, 0.98f, mvpT));
                }

                string rHint = readyTotal > 1 ? $"R: 새 매치 동의  ({readyCount}/{readyTotal})" : "R: 새 매치";
                ShadowLabel(new Rect(0, big.yMax + 142f + rise * 0.3f, W, 20f), rHint,
                    new GUIStyle(roundStyle) { fontSize = 14, alignment = TextAnchor.MiddleCenter },
                    new Color(0.6f, 0.68f, 0.78f, (0.6f + 0.4f * Mathf.Sin(age * 3f)) * mvpT));
            }
        }

        /// <summary>매치엔드 초상 — 기계팀(팀1)은 전용 회색 아트(Card_*_M)가 있으면 그걸, 없으면 원본+회색 틴트.</summary>
        static Texture2D MatchPortrait(int cls, int team, out bool tintGray)
        {
            if (team == 1)
            {
                var m = Resources.Load<Texture2D>("UI/" + CardArtNames[Mathf.Clamp(cls, 0, 4)] + "_M");
                if (m != null) { tintGray = false; return m; }
                tintGray = true;
                return CardArt(cls);
            }
            tintGray = false;
            return CardArt(cls);
        }

        /// <summary>게이지 바 — 어두운 트랙 + 채움(윗변 하이라이트) + 끝단 캡 + 구간 눈금.
        /// segments &gt; 0이면 그 개수로 칸 나눔 (HP처럼 이산 수치 읽기용).</summary>
        static void Bar(Rect rect, float fraction, Color color, int segments = 0)
        {
            float f = Mathf.Clamp01(fraction);
            Fill(rect, new Color(0.03f, 0.05f, 0.09f, 0.92f));                       // 트랙
            Edge(rect, new Color(color.r, color.g, color.b, 0.3f));
            if (f > 0f)
            {
                var fill = new Rect(rect.x + 1f, rect.y + 1f, (rect.width - 2f) * f, rect.height - 2f);
                Fill(fill, color);
                Fill(new Rect(fill.x, fill.y, fill.width, Mathf.Max(1f, fill.height * 0.35f)),
                    Color.Lerp(color, Color.white, 0.35f));                          // 윗면 광
                Fill(new Rect(fill.xMax - 1f, rect.y, 2f, rect.height),
                    Color.Lerp(color, Color.white, 0.7f));                           // 끝단 캡 — 잔량이 또렷이 읽힌다
            }
            for (int i = 1; i < segments; i++)                                        // 구간 눈금
                Fill(new Rect(rect.x + rect.width * i / segments, rect.y + 1f, 1f, rect.height - 2f),
                    new Color(0f, 0f, 0f, 0.55f));
        }
    }
}
