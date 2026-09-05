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
/// 2026-09-06: 멀티 로비(UILobbyPopup)와 같은 화면 구성 — 상단 분대 슬롯 3칸(프레임 스킨) / 중앙 카드 / 하단 상태 문구·버튼.
/// 싱글 전용 요소는 AI 난이도 바(슬롯 위)와 타이머뿐.
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

    // 분대 슬롯 — 로비 Slot 프리팹과 같은 치수·스킨 (190x250, 윗줄 y245, 간격 220)
    const float SlotW = 190f, SlotH = 250f, SlotY = 245f, SlotGap = 220f;
    const float StatusY = -420f, ButtonY = -485f;
    readonly List<RectTransform> slotRoots = new List<RectTransform>();
    readonly List<Image> slotPortraits = new List<Image>();
    readonly List<Text> slotLabels = new List<Text>();
    Text statusText;

    public override void Init()
    {
        Bind<GameObject>(typeof(Buttons));
        for (int i = 0; i < 5; i++)
        {
            var cls = (UnitClass)i;
            var rt = (RectTransform)Get<GameObject>(i).transform;
            cardRts[i] = rt;
            ClassCard.Build(rt, i, 1f);
            rt.anchoredPosition = new Vector2((i - 2) * (ClassCard.BaseW + 22f), -125f); // 로비와 같은 자리 — 슬롯 줄 아래
            BindEvent(Get<GameObject>(i), _ => Pick(cls));
        }
    }

    void Pick(UnitClass cls)
    {
        if (TeamMode)
        {
            teamCls[editIdx] = cls; // 자동 다음 칸 이동 없음 — 슬롯 클릭으로만 편집 칸 변경 (2026-09-05)
            if (editIdx == 0) AutoBalanceBots(); // 내 픽이 바뀌면 봇들이 조합을 맞춘다 (2026-09-05)
            else botManual[editIdx - 1] = true;  // 봇 직접 지정 — 이후 자동 밸런스에서 제외 (2026-09-05)
            RefreshTeamSlots();
            return;
        }
        UIManager.Instance.ClosePopupUI(this);
        OnPicked?.Invoke(cls);
    }

    /// <summary>봇 클래스 자동 밸런스 — 내 픽 기준으로 탱·돌격·딜 한 축씩 채운다 (2026-09-05 솔로 모드).
    /// 역할: 너구리=탱 / 고라니=돌격형 / 검은냥·비둘기·까치=딜. 내가 뭘 잡든 나머지 두 축을 봇이 맡는다.</summary>
    readonly bool[] botManual = new bool[2]; // 봇 슬롯 직접 지정 여부 — true면 자동 밸런스가 안 건드린다

    void AutoBalanceBots()
    {
        if (teamCls == null || teamCls.Length < 3) return;
        var dps = new[] { UnitClass.Assassin, UnitClass.Grenadier, UnitClass.Sniper };

        // 고정 픽(나 + 수동 지정 봇)이 이미 맡은 축을 빼고, 부족한 축만 자동 봇이 채운다 (2026-09-05)
        bool hasTank = false, hasRusher = false;
        for (int i = 0; i < 3; i++)
        {
            if (i > 0 && !botManual[i - 1]) continue; // 자동 봇의 기존 픽은 무시 — 다시 계산 대상
            if (teamCls[i] == UnitClass.Tank) hasTank = true;
            else if (teamCls[i] == UnitClass.Balance) hasRusher = true;
        }

        var need = new List<UnitClass>();
        if (!hasTank) need.Add(UnitClass.Tank);
        if (!hasRusher) need.Add(UnitClass.Balance);
        if (need.Count == 2 && UnityEngine.Random.value < 0.5f) need.Reverse(); // 역할 배정도 섞는다

        for (int b = 0; b < 2; b++)
        {
            if (botManual[b]) continue; // 직접 고른 봇은 존중
            var want = need.Count > 0 ? need[0] : dps[UnityEngine.Random.Range(0, dps.Length)];
            if (need.Count > 0) need.RemoveAt(0);
            teamCls[b + 1] = want;
        }
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
        botManual[0] = botManual[1] = false;
        AutoBalanceBots(); // 시작부터 밸런스 조합 — 봇 슬롯을 직접 바꾸면 그 선택이 유지된다
        BuildTeamSlots();
        BuildDifficultyBar();
        RefreshTeamSlots();
    }

    // ── AI 난이도 선택 (상/중/하) ─────────────────────────────

    readonly Image[] diffBgs = new Image[3];
    readonly Text[] diffTexts = new Text[3];
    static readonly (string label, AiDifficulty diff, Color color)[] DiffOptions =
    {
        ("하 / EASY",   AiDifficulty.Easy,   new Color(0.35f, 0.8f, 0.45f)),
        ("중 / NORMAL", AiDifficulty.Normal, new Color(0.4f, 0.7f, 1f)),
        ("상 / HARD",   AiDifficulty.Hard,   new Color(1f, 0.45f, 0.35f)),
    };

    void BuildDifficultyBar()
    {
        // 제목(하단 ~410) 과 분대 슬롯(상단 370, 편집 확대 시 ~380) 사이 띠 (2026-09-06)
        const float bw = 150f, bh = 36f, gap = 10f, y = 395f;
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

    // ── 분대 슬롯 — 로비 Slot 프리팹(프레임 + 초상 + 하단 라벨)과 같은 구성을 런타임으로 ──

    void BuildTeamSlots()
    {
        int n = teamNames.Length;
        var frame = UISkin.SlotFrame();
        // 로비 ApplySkin과 같은 내 팀(파랑) 틴트 — 프레임 디테일이 살아남게 밝게 끌어올린 값
        var frameColor = frame != null ? Color.Lerp(new Color(0.45f, 0.6f, 1f), Color.white, 0.45f)
                                       : new Color(0.14f, 0.2f, 0.32f, 0.95f);

        for (int i = 0; i < n; i++)
        {
            int idx = i;
            float cx = (i - (n - 1) * 0.5f) * SlotGap; // 3칸이면 -220/0/220, 1칸(훈련장)이면 가운데
            var root = MakeImage(transform, frame, frameColor, new Vector2(0.5f, 0.5f), new Vector2(cx, SlotY), new Vector2(SlotW, SlotH));
            root.GetComponent<Image>().raycastTarget = true;
            BindEvent(root.gameObject, _ => { editIdx = idx; RefreshTeamSlots(); }); // 슬롯 클릭 = 그 칸 다시 고르기
            slotRoots.Add(root);

            var portrait = MakeStretch(root, "Portrait", new Vector2(16f, 20f), new Vector2(-16f, -16f));
            var pImg = portrait.gameObject.AddComponent<Image>();
            pImg.color = Color.white; pImg.preserveAspect = false; pImg.raycastTarget = false;
            slotPortraits.Add(pImg);

            var labelBack = new GameObject("LabelBack", typeof(RectTransform), typeof(Image));
            var lbRt = (RectTransform)labelBack.transform;
            lbRt.SetParent(root, false);
            lbRt.anchorMin = new Vector2(0f, 0f); lbRt.anchorMax = new Vector2(1f, 0f); lbRt.pivot = new Vector2(0.5f, 0f);
            lbRt.anchoredPosition = new Vector2(0f, 20f); lbRt.sizeDelta = new Vector2(-24f, 48f);
            var lbImg = labelBack.GetComponent<Image>();
            lbImg.color = new Color(0f, 0f, 0f, 0.55f); lbImg.raycastTarget = false;

            var label = MakeText(lbRt, "", 18, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(SlotW - 24f, 48f), GameFonts.Hud);
            slotLabels.Add(label);
        }

        statusText = MakeText(transform, "", 26, FontStyle.Normal, Color.white, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, StatusY), new Vector2(700f, 44f), GameFonts.Hud);

        // 출격 — 로비 하단 버튼과 같은 판·치수·글자 (UILobbyPopup.BottomButtonSize)
        var plate = UISkin.ButtonPlate();
        var btn = MakeImage(transform, plate, plate != null ? Color.white : new Color(0.16f, 0.7f, 0.55f),
            new Vector2(0.5f, 0.5f), new Vector2(0f, ButtonY), UILobbyPopup.BottomButtonSize);
        btn.GetComponent<Image>().raycastTarget = true;
        btn.GetComponent<Image>().preserveAspect = false; // 키잉 플레이트 비율 변화 대응 (2026-09-05)
        BindEvent(btn.gameObject, _ => StartTeam());
        MakeText(btn, "출격  (Enter)", UILobbyPopup.BottomButtonFont, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), Vector2.zero, UILobbyPopup.BottomButtonSize, GameFonts.Hud);
    }

    void RefreshTeamSlots()
    {
        for (int i = 0; i < slotRoots.Count; i++)
        {
            bool editing = i == editIdx;
            slotRoots[i].localScale = editing ? Vector3.one * 1.08f : Vector3.one; // 지금 고르는 칸 살짝 크게
            slotPortraits[i].sprite = ClassCard.CardSprite((int)teamCls[i]);
            slotPortraits[i].color = i == 0 ? Color.white : new Color(0.7f, 0.7f, 0.7f); // 봇은 살짝 어둡게 (로비와 동일)
            slotLabels[i].text = $"{teamNames[i]} / {(i == 0 ? "나" : "팀원")}";
            slotLabels[i].color = editing ? new Color(0.45f, 1f, 0.95f)
                : i == 0 ? new Color(0.5f, 1f, 0.6f) : new Color(0.75f, 0.75f, 0.75f);
        }
        if (statusText != null)
            statusText.text = slotRoots.Count <= 1 ? "내 캐릭터를 고르세요"
                : editIdx == 0 ? $"1/{slotRoots.Count}  내 캐릭터를 고르세요"
                : $"{editIdx + 1}/{slotRoots.Count}  팀원 {editIdx} 캐릭터를 고르세요 (슬롯 클릭 = 다시 고르기)";

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

    // ── 로컬 uGUI 헬퍼 (분대 슬롯·타이머용) ─────────────────

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

    /// <summary>부모에 꽉 채우는 빈 RectTransform — offsetMin/Max로 안쪽 여백.</summary>
    static RectTransform MakeStretch(Transform parent, string name, Vector2 offsetMin, Vector2 offsetMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
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
        GameFonts.Resolve(ref font, ref style); // Bold 요청 → 볼드 파일, 가짜 볼드 없음
        t.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = size; t.fontStyle = style; t.color = color; t.alignment = align;
        t.supportRichText = rich;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }
}
