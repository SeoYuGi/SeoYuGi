using System.Collections.Generic;
using SeoYuGi.Battle;
using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 임시 HUD (OnGUI) — 탱고파이브 리로디드 배치 파쿠리. 아트 UI 프리팹이 붙으면 통째로 교체.
    /// 상단: 생존 ● + 매치 스코어 | 남은시간 | A/B/C 거점 칩.
    /// 중앙 상단: 상황 안내 배너 ("공격할 대상을 선택하세요" 식).
    /// 하단: 통합 바 — 콜사인·HP | 이동/공격/스킬/방어 슬롯(키·AP·쿨타임) | AP 탄약식 표시.
    /// 오버레이: 라운드 간 AI 분석 브리핑(기획서 §05 — 심사 핵심 어필) · 매치 종료.
    /// hudScale로 전체 크기 조절 (GUI.matrix).
    /// </summary>
    public class BattleHud : MonoBehaviour
    {
        enum Overlay { None, Briefing, MatchEnd }

        [SerializeField] float hudScale = 1.35f;

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

        GUIStyle timerStyle, timerLabelStyle, dotStyle, chipStyle, roundStyle;
        GUIStyle bannerTextStyle, labelStyle, bannerStyle, briefTitleStyle, briefLineStyle;
        GUIStyle keyStyle, slotNameStyle, slotCostStyle, slotCoolStyle, bigNumStyle, subStyle, subtitleStyle;
        bool stylesReady;
        Texture2D iconMove, iconAttack, iconGuard, iconSkill, panelBriefing; // Resources/UI — 없으면 무시
        Texture2D texSlot, texPanel, texInfo, texChip, texBanner;           // 프레임류 — 없으면 GUI.Box 폴백
        UnitMoveInput moveInput; // 선택 상태 조회용 — 같은 GO에서 자동 연결

        // hudScale 적용 후 논리 화면 크기
        float W => Screen.width / hudScale;
        float H => Screen.height / hudScale;

        public void Init(BattleState battle, RoundSystem round, CombatConfig combatConfig, MatchSystem match,
            int playerUnitId, Color[] teamColors, string playerName = null)
        {
            this.battle = battle;
            this.round = round;
            this.combatConfig = combatConfig;
            this.match = match;
            this.playerUnitId = playerUnitId;
            this.playerName = playerName;
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
                    var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
                    tex.SetPixels32(px);
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
        }

        /// <summary>보이스 재생 동안 하단에 한글 자막 표시.</summary>
        public void ShowSubtitle(string text, float seconds)
        {
            subtitleText = text;
            subtitleUntil = Time.time + seconds;
        }

        void OnGUI()
        {
            if (battle == null) return;
            EnsureStyles();
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(hudScale, hudScale, 1f));

            DrawTopBar();
            if (overlay == Overlay.None)
            {
                DrawBanner();
                DrawBottomBar();
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
        }

        // ── 상단 바: 생존·스코어 | 남은시간 | 거점 칩 ─────────────────

        void DrawTopBar()
        {
            // 남은시간 (탱고파이브 중앙 타이머)
            var timerBox = new Rect(W / 2f - 70, 6, 140, 48);
            DrawFrame(timerBox, texInfo);
            if (round.SuddenDeath)
            {
                GUI.color = new Color(1f, 0.4f, 0.3f);
                GUI.Label(new Rect(timerBox.x, timerBox.y + 4, timerBox.width, 40), "서든데스", timerStyle);
                GUI.color = Color.white;
            }
            else
            {
                float remain = Mathf.Max(0f, round.Config.roundSeconds - battle.time);
                GUI.Label(new Rect(timerBox.x, timerBox.y + 3, timerBox.width, 14), "남은시간", timerLabelStyle);
                GUI.Label(new Rect(timerBox.x, timerBox.y + 16, timerBox.width, 30),
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
                var chipRect = new Rect(x0 + i * (chipW + gap), 60, chipW, 24);
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

            GUI.Label(new Rect(W / 2f - 100, 86, 200, 16),
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
                msg = $"◎ {SkillName(u.unitClass)} 조준 — 빨간 칸 = 발사 · 다른 칸 = 이동 · 우클릭 = 취소";
                bannerColor = new Color(1f, 0.3f, 0.2f, 0.9f);
            }
            else if (u.moveCooldown > 0f) msg = $"이동 쿨타임 {u.moveCooldown:0.0}s — 공격/방어는 가능합니다.";
            else msg = "타일 클릭 = 이동  ·  A 공격 조준 / S 스킬 조준 / D 방어";

            var box = new Rect(W / 2f - 210, 108, 420, 26);
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
            GUI.Label(box, msg, bannerTextStyle);
            GUI.color = Color.white;
        }

        string DisplayName() => string.IsNullOrEmpty(playerName)
            ? battle.GetUnit(playerUnitId).unitClass.ToString() : playerName;

        // ── 하단 통합 바: HP | 슬롯 4개 | AP ─────────────────────────

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
            DrawSlot(new Rect(sx + (slotW + gap), y, slotW, slotH), "A", "일반공격",
                $"AP {combatConfig.costAttack:0}", u.ap >= combatConfig.costAttack, 0f, 0f,
                aim == UnitMoveInput.AimMode.Attack, iconAttack);

            DrawSlot(new Rect(sx + (slotW + gap) * 2, y, slotW, slotH), "S", SkillName(u.unitClass),
                $"AP {combatConfig.costSkill:0}", u.ap >= combatConfig.costSkill, 0f, 0f,
                aim == UnitMoveInput.AimMode.Skill, iconSkill);

            float guardRemain = Mathf.Max(0f, u.guardUntil - battle.time);
            float guardFrac = combatConfig.guardDurationSeconds > 0f
                ? guardRemain / combatConfig.guardDurationSeconds : 0f;
            DrawSlot(new Rect(sx + (slotW + gap) * 3, y, slotW, slotH), "D", "방어",
                $"AP {combatConfig.costGuard:0}", u.ap >= combatConfig.costGuard, guardRemain, guardFrac, false, iconGuard);

            // AP 세그먼트 (탱고파이브 탄약 카운터 자리 — 95/최대95 식)
            var apSeg = new Rect(x0 + totalW - segW, y, segW, slotH);
            GUI.color = new Color(0.35f, 0.75f, 1f);
            GUI.Label(new Rect(apSeg.x, apSeg.y + 2, apSeg.width, 30), $"{u.ap:0.0}", bigNumStyle);
            GUI.color = Color.white;
            GUI.Label(new Rect(apSeg.x, apSeg.y + 30, apSeg.width, 14), $"AP · 최대 {combatConfig.apMax:0}", subStyle);
            Bar(new Rect(apSeg.x + 14, apSeg.y + 48, apSeg.width - 28, 8), u.ap / combatConfig.apMax, new Color(0.35f, 0.75f, 1f));
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

        static string SkillName(UnitClass cls)
        {
            switch (cls)
            {
                case UnitClass.Tank: return "강타";
                case UnitClass.Balance: return "돌파";
                case UnitClass.Assassin: return "그림자 도약";
                case UnitClass.Grenadier: return "파열탄";
                case UnitClass.Sniper: return "조준 사격";
                default: return "스킬";
            }
        }

        // ── 오버레이 ──────────────────────────────────────────────

        void DrawBriefing()
        {
            bool myWin = briefingWinner == playerTeam;
            var box = new Rect(W / 2f - 270, H / 2f - 160, 540, 320);
            GUI.Box(box, "");
            if (panelBriefing != null)
                GUI.DrawTexture(box, panelBriefing, ScaleMode.StretchToFill); // 관제 터미널 배경

            GUI.color = myWin ? new Color(0.4f, 1f, 0.6f) : new Color(1f, 0.45f, 0.35f);
            GUI.Label(new Rect(box.x, box.y + 14, box.width, 26),
                $"ROUND {briefingRound} — {(myWin ? "승리" : "패배")}", briefTitleStyle);
            GUI.color = Color.white;

            GUI.color = new Color(1f, 0.55f, 0.4f);
            GUI.Label(new Rect(box.x, box.y + 46, box.width, 20), "── AI 관제 로그 · 학습 브리핑 ──",
                new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter });
            GUI.color = Color.white;

            float y = box.y + 76;
            if (briefingLines != null)
                foreach (var line in briefingLines)
                {
                    GUI.Label(new Rect(box.x + 28, y, box.width - 56, 40), $"▸ {line}", briefLineStyle);
                    y += 44;
                }

            GUI.color = new Color(1f, 0.85f, 0.25f);
            GUI.Label(new Rect(box.x, box.y + box.height - 34, box.width, 22),
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
            GUI.Label(new Rect(box.x, box.y - 2, box.width, 16), "- AI 관제 -",
                new GUIStyle(subStyle) { fontStyle = FontStyle.Bold });
            GUI.Label(new Rect(box.x, box.y + 8, box.width, 32), subtitleText, subtitleStyle);
            GUI.color = Color.white;
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
