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
    /// 오버레이: 라운드 간 AI 분석 브리핑(기획서 §05 — 심사 핵심 어필) · 매치 종료.
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
        Texture2D iconMove, iconAttack, iconGuard, iconSkill, panelBriefing; // Resources/UI — 없으면 무시
        Texture2D texSlot, texPanel, texInfo, texChip, texBanner;           // 프레임류 — 없으면 GUI.Box 폴백
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

            iconMove = Resources.Load<Texture2D>("UI/Icon_Move");
            iconAttack = Resources.Load<Texture2D>("UI/Icon_Attack");
            iconGuard = Resources.Load<Texture2D>("UI/Icon_Guard");
            iconSkill = Resources.Load<Texture2D>("UI/" + SkillIconName(battle.GetUnit(playerUnitId).unitClass));
            panelBriefing = Resources.Load<Texture2D>("UI/Panel_Briefing");
            texSlot = LoadKeyed("UI/Frame_Slot");
            texPanel = LoadKeyed("UI/Frame_Panel");
            texInfo = LoadKeyed("UI/Frame_Info");
            texChip = LoadKeyed("UI/Frame_Chip");
            texBanner = LoadKeyed("UI/Frame_Banner");
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
        static void ShadowLabel(Rect r, string text, GUIStyle style, Color color)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.8f * color.a);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
            GUI.color = color;
            GUI.Label(r, text, style);
            GUI.color = Color.white;
        }

        static string SkillIconName(UnitClass cls)
        {
            switch (cls)
            {
                case UnitClass.Tank: return "Icon_Skill_Smash";
                case UnitClass.Balance: return "Icon_Skill_Dash";
                case UnitClass.Assassin: return "Icon_Skill_Blink";
                case UnitClass.Grenadier: return "Icon_Skill_Burst";
                case UnitClass.Sniper: return "Icon_Skill_Snipe";
                default: return "Icon_Skill_Generic";
            }
        }

        /// <summary>라운드 사이 — AI가 학습한 내용을 관제 로그 톤으로 보여준다.</summary>
        public void ShowBriefing(int endedRound, int roundWinnerTeam, string[] lines)
        {
            briefingRound = endedRound;
            briefingWinner = roundWinnerTeam;
            briefingLines = lines;
            overlay = Overlay.Briefing;
        }

        public void ShowMatchEnd()
        {
            overlay = Overlay.MatchEnd;
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
                $"⚠ 피격 예고  {Mathf.Max(0f, threatRemain):0.0}s — 피해!", subtitleStyle,
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
            string text = string.IsNullOrEmpty(killer) ? $"{victim} 처치됨" : $"{killer}  ⚔  {victim}";
            killFeed.Add(new ChatEntry { text = text, color = color, until = Time.time + 6f });
            if (killFeed.Count > KillFeedMax) killFeed.RemoveAt(0);
        }

        public void AddChatLine(string callsign, string text, Color color)
        {
            chatLog.Add(new ChatEntry { text = $"[{callsign}] {text}", color = color, until = Time.time + 6f });
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
            DrawSubtitle(); // 오버레이 위에도 보이게 마지막에

            GUI.matrix = Matrix4x4.identity;
        }

        void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;
            timerStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            timerLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            dotStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            // 유니티 기본 회색 박스 대신 어두운 판 + 흰 글자 — 호버 시 살짝 밝게
            chipStyle = new GUIStyle(GUI.skin.box) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            chipStyle.normal.background = Solid(new Color(0.04f, 0.07f, 0.13f, 0.92f));
            chipStyle.normal.textColor = new Color(0.85f, 0.92f, 1f);
            chipStyle.hover.background = Solid(new Color(0.08f, 0.14f, 0.24f, 0.95f));
            chipStyle.hover.textColor = Color.white;
            chipStyle.active.background = Solid(new Color(0.1f, 0.2f, 0.3f, 0.95f));
            chipStyle.active.textColor = HudCyan;
            roundStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            bannerTextStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            killStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            announceStyle = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            bannerStyle = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            briefTitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            briefLineStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft, wordWrap = true };
            keyStyle = new GUIStyle(GUI.skin.box) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            keyStyle.normal.background = Solid(new Color(0.06f, 0.1f, 0.18f, 0.95f)); // 키캡 — 회색 박스 대체
            keyStyle.normal.textColor = HudCyan;
            slotNameStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
            slotCostStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            slotCoolStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            bigNumStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            subStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            subtitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };

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
            DrawFrame(timerBox, texInfo);
            if (round.SuddenDeath)
            {
                GUI.color = new Color(1f, 0.4f, 0.3f);
                GUI.Label(new Rect(timerBox.x, timerBox.y + 12, timerBox.width, 38), "서든데스", timerStyle);
                GUI.color = Color.white;
            }
            else if (round.Overtime)
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
                {
                    GUI.DrawTexture(chipRect, texChip, ScaleMode.StretchToFill);
                    GUI.color = Color.white;
                    GUI.Label(chipRect, i < letters.Length ? letters[i] : "?", slotNameStyle);
                }
                else
                {
                    GUI.Box(chipRect, i < letters.Length ? letters[i] : "?", chipStyle);
                    GUI.color = Color.white;
                }
            }

            GUI.Label(new Rect(W / 2f - 100, 97, 200, 16),
                $"ROUND {match.CurrentRound}/{MatchSystem.MaxRounds}", roundStyle);
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
        string eventText;
        Color eventColor;
        float eventUntil;

        /// <summary>전황 이벤트 한 줄 — "알파가 델타 처치!", "아군이 B 거점 점령!" 등. 팀 색으로.</summary>
        public void PushEvent(string text, Color color)
        {
            eventText = text;
            eventColor = color;
            eventUntil = Time.time + 6f;
        }

        void DrawBanner()
        {
            var u = battle.GetUnit(playerUnitId);
            string msg;
            var bannerColor = new Color(0.2f, 0.75f, 0.85f, 0.85f); // 기본 = 탱고파이브 시안
            if ((u == null || !u.alive) && skipHint)
            {
                msg = skipActive ? "▶▶ 빨리감기 중 (6×) — SPACE: 해제" : "격파됨 — SPACE: 라운드 결과까지 빨리감기";
                if (skipActive) bannerColor = new Color(1f, 0.78f, 0.25f, 0.9f); // 호박색 — 비정상 속도 표시
            }
            else if (!string.IsNullOrEmpty(eventText) && Time.time < eventUntil)
            {
                msg = eventText;
                bannerColor = new Color(eventColor.r, eventColor.g, eventColor.b, 0.9f);
            }
            else return;

            // 프레임 사선 컷 여백만큼 텍스트를 안쪽에 — 텍스트가 프레임을 뚫지 않게
            var box = new Rect(W / 2f - 240, 118, 480, 34);
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
            GUI.Label(new Rect(hpSeg.x, hpSeg.y + 2, hpSeg.width, 18), $"{DisplayName()} — {u.unitClass}", slotNameStyle);
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
                aim == UnitMoveInput.AimMode.Skill, iconSkill,
                () => moveInput?.ToggleAim(UnitMoveInput.AimMode.Skill));

            var s2 = ClassCatalog.Get(u.unitClass).skills[1];
            float s2Cool = Mathf.Max(0f, u.skillReadyAt[1] - battle.time);
            DrawSlot(new Rect(sx + (slotW + gap) * 3, y, slotW, slotH), "D", SkillName(u.unitClass, 1),
                $"쿨 {s2.cooldownSeconds:0}s", s2Cool <= 0f,
                s2Cool, s2.cooldownSeconds > 0f ? s2Cool / s2.cooldownSeconds : 0f,
                aim == UnitMoveInput.AimMode.Skill2, iconSkill,
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
            GUI.color = hackColor;
            GUI.Label(new Rect(hackSeg.x, hackSeg.y + 2, hackSeg.width, 30), $"{charge * 100f:0}%", bigNumStyle);
            GUI.color = Color.white;
            GUI.Label(new Rect(hackSeg.x, hackSeg.y + 30, hackSeg.width, 14),
                hackReady ? "시야해킹 준비 완료 — H" : "시야해킹 게이지", subStyle);
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
                case SkillKind.Smash: return "강타";
                case SkillKind.Dash: return "돌파";
                case SkillKind.Scream: return "비명 교란";
                case SkillKind.Blink: return "그림자 도약";
                case SkillKind.Claw: return "발톱 쥐어짜기";
                case SkillKind.Burst: return "파열탄";
                case SkillKind.BombDeliver: return "폭탄 배달";
                case SkillKind.KnockShot: return "넉백샷";
                case SkillKind.Snipe: return "조준 사격";
                default: return "스킬";
            }
        }

        // ── 오버레이 ──────────────────────────────────────────────

        void DrawBriefing()
        {
            bool myWin = briefingWinner == playerTeam;
            // 높이 = 헤더(106) + 줄 실측 합 + SPACE 바 여유(100) — 줄바꿈된 긴 줄이 푸터에 낑기지 않게 (2026-09-05)
            const float LineW = 540f - 88f, LineGap = 14f;
            float linesH = 0f;
            if (briefingLines != null)
                foreach (var line in briefingLines)
                    linesH += briefLineStyle.CalcHeight(new GUIContent($"▸ {line}"), LineW) + LineGap;
            float boxH = Mathf.Max(320f, 106f + linesH + 100f);
            var box = new Rect(W / 2f - 270, H / 2f - boxH / 2f, 540, boxH);
            NeonPanel(box, myWin ? new Color(0.4f, 1f, 0.6f) : new Color(1f, 0.45f, 0.35f));
            if (panelBriefing != null)
                GUI.DrawTexture(box, panelBriefing, ScaleMode.StretchToFill); // 관제 터미널 배경

            // 패널 아트의 상·하단 장식 밴드를 피해 텍스트는 중앙부에 + 반투명 백킹으로 가독성 확보
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(box.x + 50, box.y + 44, box.width - 100, 30), Texture2D.whiteTexture);
            GUI.color = myWin ? new Color(0.4f, 1f, 0.6f) : new Color(1f, 0.45f, 0.35f);
            GUI.Label(new Rect(box.x, box.y + 46, box.width, 26),
                $"ROUND {briefingRound} — {(myWin ? "승리" : "패배")}", briefTitleStyle);
            GUI.color = Color.white;

            GUI.color = new Color(1f, 0.55f, 0.4f);
            GUI.Label(new Rect(box.x, box.y + 78, box.width, 20), "── AI 관제 로그 · 학습 브리핑 ──",
                new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter });
            GUI.color = Color.white;

            float y = box.y + 106;
            if (briefingLines != null)
                foreach (var line in briefingLines)
                {
                    float h = briefLineStyle.CalcHeight(new GUIContent($"▸ {line}"), LineW); // 줄바꿈 실측
                    GUI.Label(new Rect(box.x + 44, y, LineW, h), $"▸ {line}", briefLineStyle);
                    y += h + LineGap;
                }

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(box.x + 50, box.y + box.height - 60, box.width - 100, 24), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.85f, 0.25f);
            string footer = readyTotal > 1
                ? $"SPACE — 다음 라운드 동의  ({readyCount}/{readyTotal})"
                : "SPACE — 다음 라운드 (AI가 학습을 적용합니다)";
            GUI.Label(new Rect(box.x, box.y + box.height - 58, box.width, 22),
                footer, new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter });
            GUI.color = Color.white;
        }

        // 다음 라운드 동의 현황 (멀티) — 러너가 갱신
        int readyCount, readyTotal;

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

        void DrawSubtitle()
        {
            if (string.IsNullOrEmpty(subtitleText) || Time.time >= subtitleUntil) return;

            // 박스 = 텍스트 실측 (가로 여백 60, 줄바꿈 대응) — 긴 자막이 잘리던 문제 (2026-09-05)
            var content = new GUIContent(subtitleText);
            float boxW = Mathf.Min(W - 40f, Mathf.Max(400f, subtitleStyle.CalcSize(content).x + 60f));
            float textH = subtitleStyle.CalcHeight(content, boxW - 40f);
            float boxH = textH + 22f;
            var box = new Rect(W / 2f - boxW / 2f, H - 100f - boxH, boxW, boxH);
            NeonPanel(box, new Color(0.55f, 0.95f, 1f));
            GUI.color = new Color(0.55f, 0.95f, 1f); // 관제 AI 시안 톤
            GUI.Label(new Rect(box.x, box.y - 2, box.width, 16), "도시관리 AI",
                new GUIStyle(subStyle) { fontStyle = FontStyle.Bold });
            GUI.Label(new Rect(box.x + 20f, box.y + 14f, box.width - 40f, textH), subtitleText, subtitleStyle);
            GUI.color = Color.white;
        }

        /// <summary>빠른채팅 로그 — 좌하단. 오래된 줄부터 위로 밀려 사라진다.</summary>
        void DrawChatLog()
        {
            for (int i = chatLog.Count - 1; i >= 0; i--)
                if (Time.time >= chatLog[i].until) chatLog.RemoveAt(i);
            if (chatLog.Count == 0) return;

            const float lineH = 22f, boxW = 250f;
            float y = H - 108f - chatLog.Count * lineH;

            var logBox = new Rect(12, y - 4, boxW, chatLog.Count * lineH + 8);
            Fill(logBox, new Color(0.02f, 0.04f, 0.09f, 0.6f));
            Fill(new Rect(logBox.x, logBox.y, 2f, logBox.height), new Color(allyColor.r, allyColor.g, allyColor.b, 0.8f)); // 좌측 팀색 액센트

            for (int i = 0; i < chatLog.Count; i++)
            {
                // 마지막 1초는 서서히 옅어짐 — 사라지는 게 툭 끊기지 않게
                float remain = chatLog[i].until - Time.time;
                var c = chatLog[i].color;
                c.a = Mathf.Clamp01(remain);
                GUI.color = c;
                GUI.Label(new Rect(20, y + i * lineH, boxW - 16, lineH), chatLog[i].text, labelStyle);
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

            if (GUI.Button(new Rect(W - btnW - 12f, toggleY, btnW, btnH), chatPanelOpen ? "빠른채팅 ▾" : "빠른채팅 ▸", chipStyle))
                chatPanelOpen = !chatPanelOpen;

            if (!chatPanelOpen && !ShowChatCheatsheet) return;

            var lines = SeoYuGi.Chat.QuickChat.Lines;
            float panelH = lines.Length * rowH + 30f;
            float y0 = toggleY + btnH + 6f;

            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(new Rect(x0, y0, panelW, panelH), Texture2D.whiteTexture);
            GUI.color = new Color(0.55f, 0.95f, 1f);
            GUI.Label(new Rect(x0, y0 + 4, panelW, 20), "빠른채팅 — 클릭 or 숫자키", subStyle);
            GUI.color = Color.white;

            for (int i = 0; i < lines.Length; i++)
            {
                var r = new Rect(x0 + 8f, y0 + 26f + i * rowH, panelW - 16f, rowH - 4f);
                if (GUI.Button(r, $"[{(i + 1) % 10}] {lines[i]}", chipStyle)) // 10번째 = 0키
                    OnChatClicked?.Invoke(i);
            }
        }

        void DrawMatchEnd()
        {
            bool myWin = match.MatchWinner == playerTeam;
            ShadowLabel(new Rect(0, H / 2f - 60, W, 80),
                myWin ? "매치 승리!" : "매치 패배...", bannerStyle,
                myWin ? new Color(0.4f, 1f, 0.6f) : new Color(1f, 0.45f, 0.35f));
            ShadowLabel(new Rect(0, H / 2f + 20, W, 26),
                $"{match.GetWins(playerTeam)} : {match.GetWins(1 - playerTeam)}   ·   R — 새 매치", timerStyle, Color.white);
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
