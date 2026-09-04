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

        // 빠른채팅 로그 — 좌하단에 최근 3줄. 자막(중앙)과 자리가 겹치지 않는다.
        struct ChatEntry { public string text; public Color color; public float until; }
        readonly List<ChatEntry> chatLog = new List<ChatEntry>();
        const int ChatLogMax = 3;

        /// <summary>Tab 홀드 중 프리셋 치트시트 표시 — BattleRunner가 매 프레임 갱신.</summary>
        public bool ShowChatCheatsheet { get; set; }

        /// <summary>무전 패널의 문구 클릭 — lineId. 전송 경로는 러너가 배선.</summary>
        public event System.Action<int> OnChatClicked;
        bool chatPanelOpen; // [무전] 토글 — 마우스로도 보낼 수 있게

        GUIStyle timerStyle, timerLabelStyle, dotStyle, chipStyle, roundStyle;
        GUIStyle bannerTextStyle, labelStyle, bannerStyle, briefTitleStyle, briefLineStyle;
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
        /// Read/Write 꺼져 있으면 원본 그대로 (검정 배경 노출 폴백).
        /// </summary>
        static Texture2D LoadKeyed(string path)
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

        /// <summary>프레임 텍스처가 있으면 이미지, 없으면 GUI.Box 폴백.</summary>
        void DrawFrame(Rect r, Texture2D tex)
        {
            if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.StretchToFill);
            else GUI.Box(r, "");
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
            GUI.color = new Color(1f, 0.85f, 0.25f);
            GUI.Label(new Rect(0, H / 2f - 90, W, 100), countdownNum.ToString(), bannerStyle);
            GUI.color = Color.white;
        }

        /// <summary>보이스 재생 동안 하단에 한글 자막 표시.</summary>
        public void ShowSubtitle(string text, float seconds)
        {
            subtitleText = text;
            subtitleUntil = Time.time + seconds;
        }

        /// <summary>빠른채팅 수신 — 좌하단 로그에 한 줄 추가. 팀 필터는 호출부 담당.</summary>
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
                DrawChatPanel();
                DrawCountdown();
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
            chipStyle = new GUIStyle(GUI.skin.box) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            roundStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            bannerTextStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            bannerStyle = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            briefTitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            briefLineStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft, wordWrap = true };
            keyStyle = new GUIStyle(GUI.skin.box) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            slotNameStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            slotCostStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            slotCoolStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            bigNumStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            subStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            subtitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };

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
            else
            {
                float remain = Mathf.Max(0f, round.Config.roundSeconds - battle.time);
                GUI.Label(new Rect(timerBox.x, timerBox.y + 9, timerBox.width, 14), "남은시간", timerLabelStyle);
                GUI.Label(new Rect(timerBox.x, timerBox.y + 23, timerBox.width, 30),
                    $"{(int)remain / 60}:{(int)remain % 60:00}", timerStyle);
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

            string dots = "";
            for (int i = 0; i < total; i++) dots += i < alive ? "●" : "○";

            var style = new GUIStyle(dotStyle) { alignment = rightAlign ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft };
            GUI.color = color;
            GUI.Label(new Rect(r.x, r.y, r.width, 20), rightAlign ? $"{dots}  {(team == playerTeam ? "아군" : "적군")}" : $"{(team == playerTeam ? "아군" : "적군")}  {dots}", style);
            GUI.color = Color.white;

            string winsText = $"승리 {match.GetWins(team)}/{MatchSystem.WinsNeeded}"; // 3판 2선승
            GUI.Label(new Rect(r.x, r.y + 20, r.width, 16),
                winsText, new GUIStyle(roundStyle) { alignment = rightAlign ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft });
        }

        // ── 상황 안내 배너 (탱고파이브 "공격할 대상을 선택하세요") ────

        void DrawBanner()
        {
            var u = battle.GetUnit(playerUnitId);
            string msg;
            var bannerColor = new Color(0.2f, 0.75f, 0.85f, 0.85f); // 기본 = 탱고파이브 시안
            if (u == null || !u.alive) msg = "격파됨 — 팀원 AI가 계속 싸웁니다.";
            else if (moveInput == null || !moveInput.HasSelection) msg = $"{DisplayName()}(내 유닛)를 클릭해 선택하세요.";
            else if (moveInput.CurrentAim == UnitMoveInput.AimMode.Attack)
            {
                msg = "◎ 일반공격 조준 — 빨간 칸 = 발사 · 다른 칸 = 이동 · 우클릭 = 취소";
                bannerColor = new Color(1f, 0.6f, 0.15f, 0.9f);
            }
            else if (moveInput.CurrentAim == UnitMoveInput.AimMode.Skill)
            {
                msg = $"◎ {SkillName(u.unitClass, 0)} 조준 — 빨간 칸 = 발사 · 다른 칸 = 이동 · 우클릭 = 취소";
                bannerColor = new Color(1f, 0.3f, 0.2f, 0.9f);
            }
            else if (moveInput.CurrentAim == UnitMoveInput.AimMode.Skill2)
            {
                msg = $"◎ {SkillName(u.unitClass, 1)} 조준 — 빨간 칸 = 발사 · 다른 칸 = 이동 · 우클릭 = 취소";
                bannerColor = new Color(1f, 0.3f, 0.2f, 0.9f);
            }
            else if (u.moveCooldown > 0f) msg = $"이동 쿨타임 {u.moveCooldown:0.0}s — 공격/스킬은 가능합니다.";
            else msg = "타일 클릭 = 이동  ·  A 공격 / S 스킬1 / D 스킬2 / H 해킹";

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

            const float slotW = 88f, slotH = 64f, gap = 8f, segW = 140f;
            float totalW = segW + gap + 4 * slotW + 3 * gap + gap + segW;
            float x0 = W / 2f - totalW / 2f;
            float y = H - slotH - 14f;

            DrawFrame(new Rect(x0 - 10, y - 8, totalW + 20, slotH + 18), texPanel); // 바 배경

            // HP 세그먼트 (탱고파이브 좌측 캐릭터 정보 자리)
            var hpSeg = new Rect(x0, y, segW, slotH);
            GUI.color = Color.Lerp(allyColor, Color.white, 0.4f);
            GUI.Label(new Rect(hpSeg.x, hpSeg.y + 2, hpSeg.width, 18), $"{DisplayName()} — {u.unitClass}", slotNameStyle);
            GUI.color = Color.white;
            Bar(new Rect(hpSeg.x + 14, hpSeg.y + 26, hpSeg.width - 28, 12), u.hp / (float)u.maxHp, new Color(0.3f, 0.9f, 0.4f));
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
                aim == UnitMoveInput.AimMode.Attack, iconAttack);

            var s1 = ClassCatalog.Get(u.unitClass).skills[0];
            float s1Cool = Mathf.Max(0f, u.skillReadyAt[0] - battle.time);
            DrawSlot(new Rect(sx + (slotW + gap) * 2, y, slotW, slotH), "S", SkillName(u.unitClass, 0),
                $"쿨 {s1.cooldownSeconds:0}s", s1Cool <= 0f,
                s1Cool, s1.cooldownSeconds > 0f ? s1Cool / s1.cooldownSeconds : 0f,
                aim == UnitMoveInput.AimMode.Skill, iconSkill);

            var s2 = ClassCatalog.Get(u.unitClass).skills[1];
            float s2Cool = Mathf.Max(0f, u.skillReadyAt[1] - battle.time);
            DrawSlot(new Rect(sx + (slotW + gap) * 3, y, slotW, slotH), "D", SkillName(u.unitClass, 1),
                $"쿨 {s2.cooldownSeconds:0}s", s2Cool <= 0f,
                s2Cool, s2.cooldownSeconds > 0f ? s2Cool / s2.cooldownSeconds : 0f,
                aim == UnitMoveInput.AimMode.Skill2, iconSkill);

            // 해킹 궁게이지 세그먼트 (구 AP 탄약 카운터 자리) — 만충 시 H 발동
            var hackSeg = new Rect(x0 + totalW - segW, y, segW, slotH);
            float charge = hackCharge != null ? Mathf.Clamp01(hackCharge()) : 0f;
            bool hackReady = charge >= 1f;
            var hackColor = hackReady ? new Color(1f, 0.45f, 1f) : new Color(0.62f, 0.45f, 1f);
            GUI.color = hackColor;
            GUI.Label(new Rect(hackSeg.x, hackSeg.y + 2, hackSeg.width, 30), $"{charge * 100f:0}%", bigNumStyle);
            GUI.color = Color.white;
            GUI.Label(new Rect(hackSeg.x, hackSeg.y + 30, hackSeg.width, 14),
                hackReady ? "해킹 준비 완료 — H" : "해킹 게이지", subStyle);
            Bar(new Rect(hackSeg.x + 14, hackSeg.y + 48, hackSeg.width - 28, 8), charge, hackColor);
        }

        void DrawSlot(Rect r, string key, string name, string cost, bool enabled, float coolRemain, float coolFrac,
            bool active = false, Texture2D icon = null)
        {
            if (active)
            {
                // 조준 중인 슬롯 — 노란 프레임으로 "지금 이거 조준 중" 표시
                GUI.color = new Color(1f, 0.85f, 0.25f);
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
                GUI.color = new Color(1f, 0.6f, 0.3f);
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
            // 높이 = 헤더(106) + 줄들(40씩) + SPACE 바 여유(74) — 줄 수 늘어도 안 겹침
            int lineCount = briefingLines != null ? briefingLines.Length : 0;
            float boxH = Mathf.Max(320f, 106f + lineCount * 40f + 74f);
            var box = new Rect(W / 2f - 270, H / 2f - boxH / 2f, 540, boxH);
            GUI.Box(box, "");
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
                    GUI.Label(new Rect(box.x + 44, y, box.width - 88, 38), $"▸ {line}", briefLineStyle);
                    y += 40;
                }

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(box.x + 50, box.y + box.height - 60, box.width - 100, 24), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.85f, 0.25f);
            GUI.Label(new Rect(box.x, box.y + box.height - 58, box.width, 22),
                "SPACE — 다음 라운드 (AI가 학습을 적용합니다)",
                new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter });
            GUI.color = Color.white;
        }

        void DrawSubtitle()
        {
            if (string.IsNullOrEmpty(subtitleText) || Time.time >= subtitleUntil) return;

            var box = new Rect(W / 2f - 200, H - 140, 400, 40);
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = new Color(0.55f, 0.95f, 1f); // 관제 AI 시안 톤
            GUI.Label(new Rect(box.x, box.y - 2, box.width, 16), "도시관리 AI",
                new GUIStyle(subStyle) { fontStyle = FontStyle.Bold });
            GUI.Label(new Rect(box.x, box.y + 8, box.width, 32), subtitleText, subtitleStyle);
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

            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(12, y - 4, boxW, chatLog.Count * lineH + 8), Texture2D.whiteTexture);

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

        /// <summary>
        /// 무전 패널 — 좌하단 [무전] 토글로 펼치고, 문구 클릭 = 전송 (단축키 병기).
        /// Tab 홀드 중에도 임시로 펼쳐진다 (읽기 + 클릭 둘 다 가능).
        /// </summary>
        void DrawChatPanel()
        {
            const float btnW = 64f, btnH = 26f, rowH = 30f, panelW = 170f;
            float x0 = 12f, toggleY = H - 100f;

            if (GUI.Button(new Rect(x0, toggleY, btnW, btnH), chatPanelOpen ? "무전 ▾" : "무전 ▸", chipStyle))
                chatPanelOpen = !chatPanelOpen;

            if (!chatPanelOpen && !ShowChatCheatsheet) return;

            var lines = SeoYuGi.Chat.QuickChat.Lines;
            float panelH = lines.Length * rowH + 30f;
            float y0 = toggleY - panelH - 6f;

            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(new Rect(x0, y0, panelW, panelH), Texture2D.whiteTexture);
            GUI.color = new Color(0.55f, 0.95f, 1f);
            GUI.Label(new Rect(x0, y0 + 4, panelW, 20), "빠른채팅 — 클릭 or 숫자키", subStyle);
            GUI.color = Color.white;

            for (int i = 0; i < lines.Length; i++)
            {
                var r = new Rect(x0 + 8f, y0 + 26f + i * rowH, panelW - 16f, rowH - 4f);
                if (GUI.Button(r, $"[{i + 1}] {lines[i]}", chipStyle))
                    OnChatClicked?.Invoke(i);
            }
        }

        void DrawMatchEnd()
        {
            bool myWin = match.MatchWinner == playerTeam;
            GUI.color = myWin ? new Color(0.4f, 1f, 0.6f) : new Color(1f, 0.45f, 0.35f);
            GUI.Label(new Rect(0, H / 2f - 60, W, 80),
                myWin ? "매치 승리!" : "매치 패배...", bannerStyle);
            GUI.color = Color.white;

            GUI.Label(new Rect(0, H / 2f + 20, W, 26),
                $"{match.GetWins(playerTeam)} : {match.GetWins(1 - playerTeam)}   ·   R — 새 매치", timerStyle);
        }

        static void Bar(Rect rect, float fraction, Color color)
        {
            GUI.color = new Color(0.1f, 0.1f, 0.12f, 0.9f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fraction), rect.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
