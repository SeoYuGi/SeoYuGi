using System;
using System.Collections.Generic;
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
            rt.anchoredPosition = new Vector2((i - 2) * (ClassCard.BaseW + 22f), 20f);
            BindEvent(Get<GameObject>(i), _ => Pick(cls));
        }
    }

    void Pick(UnitClass cls)
    {
        if (TeamMode)
        {
            teamCls[editIdx] = cls;
            if (editIdx < teamCls.Length - 1) editIdx++;
            RefreshTeamStrip();
            return;
        }
        UIManager.Instance.ClosePopupUI(this);
        OnPicked?.Invoke(cls);
    }

    /// <summary>제한시간(초) 설정. 0 이하면 무제한. 종료 시 현재 선택으로 자동 확정.</summary>
    public void SetTimer(float seconds)
    {
        deadline = seconds > 0f ? Time.unscaledTime + seconds : -1f;
        if (timerText == null)
        {
            timerText = MakeText(transform, "", 40, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(200f, 56f), GameFonts.Title);
        }
    }

    // ── 팀 구성 모드 ─────────────────────────────────────────

    public void SetTeam(string[] names, UnitClass[] initial)
    {
        teamNames = names;
        teamCls = (UnitClass[])initial.Clone();
        editIdx = 0;
        BuildTeamStrip();
        RefreshTeamStrip();
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
