using System;
using System.Collections.Generic;
using SeoYuGi.Ai;          // AiConfig, AiDifficulty
using SeoYuGi.Battle;
using SeoYuGi.BattleView; // GameFonts
using SeoYuGi.UI;          // ClassCard
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 캐릭터 선택 — 카드 5장(ClassCard 공용 빌더) + 제한시간 카운트다운(롤/오버워치식).
/// 팀 구성 모드: 나 + 봇 2의 클래스를 이 화면에서 짠다(적 조합 비공개). 시간 종료 시 자동 출격.
/// </summary>
public class UIClassSelectPopup : UIPopup
{
    enum Buttons { BtnTank, BtnBalance, BtnAssassin, BtnGrenadier, BtnSniper }

    public Action<UnitClass> OnPicked;              // 단일 선택 모드
    public Action<UnitClass[]> OnTeamPicked;        // 팀 구성 모드

    static readonly Color CardBg = new Color(0.02f, 0.03f, 0.07f, 0.97f);
    static readonly Color DimText = new Color(0.55f, 0.62f, 0.72f);

    readonly RectTransform[] cardRts = new RectTransform[5];

    // 팀 구성 상태
    string[] teamNames;
    UnitClass[] teamCls;
    int editIdx;
    bool TeamMode => teamCls != null;

    // 제한시간
    float deadline = -1f; // Time.unscaledTime 기준. <0 = 무제한
    Text timerText;

    const float ChipW = 250f, ChipH = 64f, ChipGap = 16f, StripY = -432f;
    readonly List<Image> chipBgs = new List<Image>();
    readonly List<Outline> chipOutlines = new List<Outline>();
    readonly List<Text> chipTexts = new List<Text>();

    public override void Init()
    {
        Bind<GameObject>(typeof(Buttons));
        for (int i = 0; i < 5; i++)
        {
            var cls = (UnitClass)i;
            var rt = (RectTransform)Get<GameObject>(i).transform;
            cardRts[i] = rt;
            ClassCard.Build(rt, i, 1f);
            rt.anchoredPosition = new Vector2((i - 2) * (ClassCard.BaseW + 22f), -10f); // 상단 난이도 바 자리 확보
            BindEvent(Get<GameObject>(i), _ => Pick(cls));
        }
    }

    void Pick(UnitClass cls)
    {
        if (TeamMode)
        {
            teamCls[editIdx] = cls; // 자동 다음 칸 이동 없음 — 칩 클릭으로만 편집 칸 변경 (2026-09-05)
            if (editIdx == 0) AutoBalanceBots(); // 내 픽이 바뀌면 봇들이 조합을 맞춘다 (2026-09-05)
            RefreshTeamStrip();
            return;
        }
        UIManager.Instance.ClosePopupUI(this);
        OnPicked?.Invoke(cls);
    }

    /// <summary>봇 클래스 자동 밸런스 — 내 픽 기준으로 탱·서폿·딜 한 축씩 채운다 (2026-09-05 솔로 모드).
    /// 역할: 너구리=탱 / 고라니=서폿 / 검은냥·비둘기·까치=딜. 내가 뭘 잡든 나머지 두 축을 봇이 맡는다.</summary>
    void AutoBalanceBots()
    {
        if (teamCls == null || teamCls.Length < 3) return;
        var dps = new[] { UnitClass.Assassin, UnitClass.Grenadier, UnitClass.Sniper };
        var need = new List<UnitClass>();
        var mine = teamCls[0];
        if (mine == UnitClass.Tank) { need.Add(UnitClass.Balance); need.Add(dps[UnityEngine.Random.Range(0, dps.Length)]); }
        else if (mine == UnitClass.Balance) { need.Add(UnitClass.Tank); need.Add(dps[UnityEngine.Random.Range(0, dps.Length)]); }
        else { need.Add(UnitClass.Tank); need.Add(UnitClass.Balance); }
        if (UnityEngine.Random.value < 0.5f) need.Reverse(); // 어느 봇이 어느 축을 맡을지도 섞는다
        teamCls[1] = need[0];
        teamCls[2] = need[1];
    }

    /// <summary>제한시간(초) 설정. 0 이하면 무제한. 종료 시 현재 선택으로 자동 확정.</summary>
    public void SetTimer(float seconds)
    {
        deadline = seconds > 0f ? Time.unscaledTime + seconds : -1f;
        if (timerText == null)
        {
            timerText = MakeText(transform, "", 40, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(360f, -90f), new Vector2(200f, 56f), GameFonts.Title); // 제목(600폭) 오른쪽 옆
        }
    }

    // ── 팀 구성 모드 ─────────────────────────────────────────

    public void SetTeam(string[] names, UnitClass[] initial)
    {
        teamNames = names;
        teamCls = (UnitClass[])initial.Clone();
        editIdx = 0;
        AutoBalanceBots(); // 시작부터 밸런스 조합 — 봇 칸을 직접 바꾸면 그 선택이 유지된다
        BuildTeamStrip();
        BuildDifficultyBar();
        RefreshTeamStrip();
    }

    // ── AI 난이도 선택 (상/중/하) ─────────────────────────────

    readonly Image[] diffBgs = new Image[3];
    readonly Text[] diffTexts = new Text[3];
    static readonly (string label, AiDifficulty diff, Color color)[] DiffOptions =
    {
        ("하 · EASY",   AiDifficulty.Easy,   new Color(0.35f, 0.8f, 0.45f)),
        ("중 · NORMAL", AiDifficulty.Normal, new Color(0.4f, 0.7f, 1f)),
        ("상 · HARD",   AiDifficulty.Hard,   new Color(1f, 0.45f, 0.35f)),
    };

    void BuildDifficultyBar()
    {
        // 선택 카드는 1.06배 확대라 상단이 361까지 올라온다 — 그 위(372~408) 띠에 배치
        const float bw = 150f, bh = 36f, gap = 10f, y = 390f;
        float total = 3 * bw + 2 * gap;
        // MiddleRight도 pos는 rect "중심" — 오른쪽 끝이 바 왼쪽에 닿도록 중심을 라벨 반폭만큼 더 왼쪽에
        MakeText(transform, "AI 난이도", 16, FontStyle.Bold, DimText, TextAnchor.MiddleRight,
            new Vector2(0.5f, 0.5f), new Vector2(-total / 2f - 14f - 60f, y), new Vector2(120f, bh), GameFonts.Hud);
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            float cx = -total / 2f + bw / 2f + i * (bw + gap);
            var bg = MakeImage(transform, null, CardBg, new Vector2(0.5f, 0.5f), new Vector2(cx, y), new Vector2(bw, bh));
            bg.GetComponent<Image>().raycastTarget = true;
            BindEvent(bg.gameObject, _ => { AiConfig.Difficulty = DiffOptions[idx].diff; RefreshDifficulty(); });
            var ol = bg.gameObject.AddComponent<Outline>();
            ol.effectDistance = new Vector2(2f, -2f);
            diffBgs[i] = bg.GetComponent<Image>();
            diffTexts[i] = MakeText(bg, DiffOptions[i].label, 15, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(bw, bh), GameFonts.Hud);
        }
        RefreshDifficulty();
    }

    void RefreshDifficulty()
    {
        for (int i = 0; i < 3; i++)
        {
            bool sel = AiConfig.Difficulty == DiffOptions[i].diff;
            var c = DiffOptions[i].color;
            diffBgs[i].color = sel ? Color.Lerp(CardBg, c, 0.4f) : CardBg;
            diffTexts[i].color = sel ? Color.white : new Color(0.6f, 0.65f, 0.72f);
            var ol = diffBgs[i].GetComponent<Outline>();
            if (ol != null) ol.effectColor = sel ? c : new Color(0.25f, 0.3f, 0.4f);
        }
    }

    void BuildTeamStrip()
    {
        int n = teamNames.Length;
        float stripW = n * ChipW + (n - 1) * ChipGap;
        float left = -stripW / 2f - 100f;

        for (int i = 0; i < n; i++)
        {
            int idx = i;
            float cx = left + ChipW / 2f + i * (ChipW + ChipGap);
            var bg = MakeImage(transform, null, CardBg, new Vector2(0.5f, 0.5f),
                new Vector2(cx, StripY), new Vector2(ChipW, ChipH));
            bg.GetComponent<Image>().raycastTarget = true;
            BindEvent(bg.gameObject, _ => { editIdx = idx; RefreshTeamStrip(); });
            var ol = bg.gameObject.AddComponent<Outline>();
            ol.effectDistance = new Vector2(2f, -2f);
            chipBgs.Add(bg.GetComponent<Image>());
            chipOutlines.Add(ol);

            var t = MakeText(bg, "", 16, FontStyle.Normal, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ChipW, ChipH), GameFonts.Hud, rich: true);
            chipTexts.Add(t);
        }

        float bx = left + stripW + 40f + 90f;
        var plate = UISkin.ButtonPlate();
        var btn = MakeImage(transform, plate, plate != null ? Color.white : new Color(0.16f, 0.7f, 0.55f),
            new Vector2(0.5f, 0.5f), new Vector2(bx, StripY), new Vector2(180f, ChipH));
        btn.GetComponent<Image>().raycastTarget = true;
        BindEvent(btn.gameObject, _ => StartTeam());
        MakeText(btn, "출격  (Enter)", 20, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(180f, ChipH), GameFonts.Hud);

        MakeText(transform, "칩을 고르고 카드를 클릭하면 그 칸에 배정됩니다 · 상대 조합은 시작 전까지 비공개", 14,
            FontStyle.Normal, DimText, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
            new Vector2(0f, StripY - 46f), new Vector2(800f, 22f), GameFonts.Hud);
    }

    void RefreshTeamStrip()
    {
        for (int i = 0; i < chipTexts.Count; i++)
        {
            var meta = ClassCard.Meta[(int)teamCls[i]];
            bool sel = i == editIdx;
            chipTexts[i].text = $"{teamNames[i]} · {(i == 0 ? "나" : "봇")}\n" +
                                $"<b><color=#{ColorUtility.ToHtmlStringRGB(meta.color)}>{meta.name}</color></b>";
            chipBgs[i].color = sel ? Color.Lerp(CardBg, meta.color, 0.3f) : CardBg;
            chipOutlines[i].effectColor = sel ? Color.Lerp(meta.color, Color.white, 0.4f) : new Color(0.25f, 0.3f, 0.4f);
        }
        // 카드 하이라이트 — 지금 편집 칸에 배정된 클래스 카드
        int editCls = (int)teamCls[editIdx];
        for (int c = 0; c < cardRts.Length; c++)
            ClassCard.SetSelected(cardRts[c], c == editCls, ClassCard.Meta[c].color);
    }

    void StartTeam()
    {
        UIManager.Instance.ClosePopupUI(this);
        OnTeamPicked?.Invoke(teamCls);
    }

    void LateUpdate()
    {
        // 제한시간 표시·자동 종료
        if (deadline > 0f)
        {
            float remain = Mathf.Max(0f, deadline - Time.unscaledTime);
            if (timerText != null)
            {
                timerText.text = Mathf.CeilToInt(remain).ToString();
                timerText.color = remain <= 5f ? new Color(1f, 0.4f, 0.3f) : Color.white; // 5초 이하 빨강
            }
            if (remain <= 0f)
            {
                deadline = -1f;
                if (TeamMode) StartTeam();
                else Pick((UnitClass)Mathf.Clamp(editIdx, 0, 4)); // 미선택이면 기본
                return;
            }
        }

        if (!TeamMode || Keyboard.current == null) return;
        if (UIManager.Instance == null || !UIManager.Instance.IsTopPopup(this)) return;
        if (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame)
            StartTeam();
    }

    // ── 로컬 uGUI 헬퍼 (팀 스트립·타이머용) ─────────────────

    static RectTransform MakeImage(Transform parent, Sprite sprite, Color color, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var go = new GameObject("Img", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.sprite = sprite; img.color = color; img.raycastTarget = false;
        return rt;
    }

    static Text MakeText(Transform parent, string text, int size, FontStyle style, Color color,
        TextAnchor align, Vector2 anchor, Vector2 pos, Vector2 sizeDelta, Font font, bool rich = false)
    {
        var go = new GameObject("Txt", typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = sizeDelta;
        var t = go.GetComponent<Text>();
        t.text = text;
        t.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = size; t.fontStyle = style; t.color = color; t.alignment = align;
        t.supportRichText = rich;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }
}
