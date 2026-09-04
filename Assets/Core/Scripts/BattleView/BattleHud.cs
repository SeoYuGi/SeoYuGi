using SeoYuGi.Battle;
using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 임시 HUD (OnGUI) — 아트 UI 프리팹이 붙으면 이 파일 통째로 교체/삭제.
    /// 상단: 라운드·매치 스코어 + 거점 + 타이머. 좌하단: 내 유닛 HP/AP/이동 게이지.
    /// 하단 중앙: 조작 액션 바 (키·AP 비용·쿨타임 — 탱고파이브식).
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

        Overlay overlay;
        int briefingRound;   // 방금 끝난 라운드 번호
        int briefingWinner;
        string[] briefingLines;

        GUIStyle titleStyle, labelStyle, bannerStyle, briefTitleStyle, briefLineStyle;
        GUIStyle keyStyle, slotNameStyle, slotCostStyle, slotCoolStyle, hintStyle;
        bool stylesReady;
        UnitMoveInput moveInput; // 선택 상태 조회용 — 같은 GO에서 자동 연결

        // hudScale 적용 후 논리 화면 크기
        float W => Screen.width / hudScale;
        float H => Screen.height / hudScale;

        public void Init(BattleState battle, RoundSystem round, CombatConfig combatConfig, MatchSystem match, int playerUnitId)
        {
            this.battle = battle;
            this.round = round;
            this.combatConfig = combatConfig;
            this.match = match;
            this.playerUnitId = playerUnitId;
            playerTeam = battle.GetUnit(playerUnitId).team;
            overlay = Overlay.None;
            if (moveInput == null) moveInput = GetComponent<UnitMoveInput>();
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

        void OnGUI()
        {
            if (battle == null) return;
            EnsureStyles();
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(hudScale, hudScale, 1f));

            DrawTop();
            DrawPlayerPanel();
            DrawActionBar();
            if (overlay == Overlay.Briefing) DrawBriefing();
            else if (overlay == Overlay.MatchEnd) DrawMatchEnd();

            GUI.matrix = Matrix4x4.identity;
        }

        void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            bannerStyle = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            briefTitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            briefLineStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft, wordWrap = true };
            keyStyle = new GUIStyle(GUI.skin.box) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            slotNameStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            slotCostStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            slotCoolStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
        }

        void DrawTop()
        {
            int z0 = 0, z1 = 0;
            foreach (var z in round.Zones)
            {
                if (z.owner == 0) z0++;
                else if (z.owner == 1) z1++;
            }

            float remain = Mathf.Max(0f, round.Config.roundSeconds - battle.time);
            string mid = round.SuddenDeath ? "SUDDEN DEATH" : $"{(int)remain / 60}:{(int)remain % 60:00}";

            GUI.color = round.SuddenDeath ? new Color(1f, 0.4f, 0.3f) : Color.white;
            GUI.Label(new Rect(W / 2f - 200, 8, 400, 32), $"{z0}   ◆   {mid}   ◆   {z1}", titleStyle);
            GUI.color = Color.white;

            // 라운드 + 매치 스코어 (기획서 §05: 3라운드 2선승)
            GUI.Label(new Rect(W / 2f - 200, 38, 400, 20),
                $"ROUND {match.CurrentRound}/{MatchSystem.MaxRounds}   ·   매치 {match.GetWins(playerTeam)} : {match.GetWins(1 - playerTeam)}",
                new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter });
        }

        void DrawPlayerPanel()
        {
            var u = battle.GetUnit(playerUnitId);
            if (u == null || !u.alive) return;

            var panel = new Rect(12, H - 108, 250, 96);
            GUI.Box(panel, $"내 유닛 — {u.unitClass}");

            float x = panel.x + 12, w = panel.width - 60;

            GUI.Label(new Rect(x, panel.y + 24, 40, 18), $"HP", labelStyle);
            Bar(new Rect(x + 34, panel.y + 28, w - 34, 10), u.hp / (float)u.maxHp, new Color(0.3f, 0.9f, 0.4f));
            GUI.Label(new Rect(x + w + 6, panel.y + 24, 50, 18), $"{u.hp}/{u.maxHp}", labelStyle);

            GUI.Label(new Rect(x, panel.y + 44, 40, 18), $"AP", labelStyle);
            Bar(new Rect(x + 34, panel.y + 48, w - 34, 10), u.ap / combatConfig.apMax, new Color(0.35f, 0.75f, 1f));
            GUI.Label(new Rect(x + w + 6, panel.y + 44, 50, 18), $"{u.ap:0.0}", labelStyle);

            GUI.Label(new Rect(x, panel.y + 64, 40, 18), "이동", labelStyle);
            if (u.moveCooldown > 0f)
            {
                GUI.color = new Color(1f, 0.5f, 0.3f);
                GUI.Label(new Rect(x + 34, panel.y + 64, 120, 18), $"쿨타임 {u.moveCooldown:0.0}s", labelStyle);
                GUI.color = Color.white;
            }
            else
            {
                Bar(new Rect(x + 34, panel.y + 68, w - 34, 10), u.moveGauge / u.profile.freeRange, new Color(1f, 0.85f, 0.25f));
                GUI.Label(new Rect(x + w + 6, panel.y + 64, 50, 18), $"{(int)u.moveGauge}", labelStyle);
            }
        }

        // ── 액션 바 — 조작법 + AP 비용 + 쿨타임 ─────────────────────

        void DrawActionBar()
        {
            var u = battle.GetUnit(playerUnitId);
            if (u == null || !u.alive) return;

            const float slotW = 92f, slotH = 68f, gap = 8f;
            const int slots = 4;
            float totalW = slots * slotW + (slots - 1) * gap;
            float x0 = W / 2f - totalW / 2f;
            float y = H - slotH - 12f;

            // 이동: 노랑 이동 후 쿨타임 표시
            float moveCoolFrac = u.moveCooldown > 0f && u.profile.yellowCooldownSeconds > 0f
                ? u.moveCooldown / u.profile.yellowCooldownSeconds : 0f;
            DrawSlot(new Rect(x0, y, slotW, slotH), "좌클릭", "이동",
                $"AP-  ·  게이지 {(int)u.moveGauge}", u.moveCooldown <= 0f, u.moveCooldown, moveCoolFrac);

            DrawSlot(new Rect(x0 + (slotW + gap), y, slotW, slotH), "A", "일반공격",
                $"AP {combatConfig.costAttack:0}", u.ap >= combatConfig.costAttack, 0f, 0f);

            DrawSlot(new Rect(x0 + (slotW + gap) * 2, y, slotW, slotH), "S", SkillName(u.unitClass),
                $"AP {combatConfig.costSkill:0}", u.ap >= combatConfig.costSkill, 0f, 0f);

            // 방어: 지속 중이면 남은 시간 오버레이
            float guardRemain = Mathf.Max(0f, u.guardUntil - battle.time);
            float guardFrac = combatConfig.guardDurationSeconds > 0f
                ? guardRemain / combatConfig.guardDurationSeconds : 0f;
            DrawSlot(new Rect(x0 + (slotW + gap) * 3, y, slotW, slotH), "D", "방어",
                $"AP {combatConfig.costGuard:0}", u.ap >= combatConfig.costGuard, guardRemain, guardFrac);

            // 조작 힌트
            string hint = moveInput != null && moveInput.HasSelection
                ? "A/S/D — 마우스가 가리키는 칸에 사용  ·  우클릭 — 선택 해제"
                : "내 유닛(파랑)을 좌클릭해 선택하세요";
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            GUI.Label(new Rect(W / 2f - 300, y - 22, 600, 18), hint, hintStyle);
            GUI.color = Color.white;
        }

        void DrawSlot(Rect r, string key, string name, string cost, bool enabled, float coolRemain, float coolFrac)
        {
            GUI.color = enabled ? Color.white : new Color(1f, 1f, 1f, 0.4f);
            GUI.Box(r, "");
            GUI.Box(new Rect(r.x + 5, r.y + 5, key.Length > 2 ? 46 : 24, 20), key, keyStyle);
            GUI.Label(new Rect(r.x, r.y + 27, r.width, 18), name, slotNameStyle);
            GUI.Label(new Rect(r.x, r.y + 45, r.width, 16), cost, slotCostStyle);
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

        void DrawBriefing()
        {
            bool myWin = briefingWinner == playerTeam;
            var box = new Rect(W / 2f - 270, H / 2f - 160, 540, 320);
            GUI.Box(box, "");

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

        void DrawMatchEnd()
        {
            bool myWin = match.MatchWinner == playerTeam;
            GUI.color = myWin ? new Color(0.4f, 1f, 0.6f) : new Color(1f, 0.45f, 0.35f);
            GUI.Label(new Rect(0, H / 2f - 60, W, 80),
                myWin ? "매치 승리!" : "매치 패배...", bannerStyle);
            GUI.color = Color.white;

            GUI.Label(new Rect(0, H / 2f + 20, W, 26),
                $"{match.GetWins(playerTeam)} : {match.GetWins(1 - playerTeam)}   ·   R — 새 매치", titleStyle);
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
