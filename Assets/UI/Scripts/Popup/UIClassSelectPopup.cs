using System;
using System.Text;
using SeoYuGi.Battle;
using SeoYuGi.BattleView; // GameFonts
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 매치 시작 전 클래스 선택 팝업. 카드 클릭 → OnPicked 콜백 → 닫힘.
/// 카드는 목업 디자인(테두리 색·헤더·초상 슬롯·HP 바·공/방/기 도트·스킬 행)대로
/// 런타임에 전부 재구축 — 수치는 ClassCatalog 실데이터라 밸런스 패치가 자동 반영된다.
/// </summary>
public class UIClassSelectPopup : UIPopup
{
    // UnitClass enum과 같은 순서 — 인덱스 캐스팅으로 매핑
    enum Buttons { BtnTank, BtnBalance, BtnAssassin, BtnGrenadier, BtnSniper }

    public Action<UnitClass> OnPicked;

    // 카드 정적 정보 (enum 순서). 색은 목업 팔레트.
    static readonly (string name, string roleEn, Color color)[] Meta =
    {
        ("너구리",      "TANKER",    new Color(1f, 0.54f, 0.16f)),
        ("고라니",      "RUNNER",    new Color(0.64f, 0.42f, 1f)),
        ("검은 고양이", "ASSASSIN",  new Color(0.21f, 0.84f, 1f)),
        ("비둘기",      "GRENADIER", new Color(0.29f, 0.87f, 0.37f)),
        ("까치",        "MARKSMAN",  new Color(0.23f, 0.51f, 0.96f)),
    };

    static readonly string[] Portraits =
        { "Card_Tank", "Card_Balance", "Card_Assassin", "Card_Grenadier", "Card_Sniper" };

    static readonly Color CardBg = new Color(0.02f, 0.03f, 0.07f, 0.97f); // 딥네이비 — 네온 대비 강화
    static readonly Color DimText = new Color(0.55f, 0.62f, 0.72f);
    static readonly Color EmptyDot = new Color(0.16f, 0.19f, 0.26f);

    public override void Init()
    {
        Bind<GameObject>(typeof(Buttons));
        for (int i = 0; i < 5; i++)
        {
            var cls = (UnitClass)i;
            BindEvent(Get<GameObject>(i), _ => Pick(cls));
            BuildCard(Get<GameObject>(i), i);
        }
    }

    void Pick(UnitClass cls)
    {
        if (TeamMode)
        {
            // 팀 구성 모드 — 카드 클릭 = 선택된 칸에 배정 후 다음 칸으로 (세 번 클릭이면 팀 완성)
            teamCls[editIdx] = cls;
            if (editIdx < teamCls.Length - 1) editIdx++;
            RefreshTeamStrip();
            return;
        }
        UIManager.Instance.ClosePopupUI(this);
        OnPicked?.Invoke(cls);
    }

    // ── 싱글 팀 구성 모드 — 나 + 봇 2의 클래스를 이 화면에서 한 번에 짠다 (적 조합은 비공개) ──
    // 멀티 로비처럼 "팀원 조합을 내가 정하는" 경험을 팀원이 깎은 카드 화면 위에 얹는다. 카드 아래 칩 3개 + 출격 버튼.

    /// <summary>팀 확정 — classes[i] = names[i]의 클래스 (0 = 나).</summary>
    public Action<UnitClass[]> OnTeamPicked;

    string[] teamNames;
    UnitClass[] teamCls;
    int editIdx;
    readonly System.Collections.Generic.List<Image> chipBgs = new System.Collections.Generic.List<Image>();
    readonly System.Collections.Generic.List<Outline> chipOutlines = new System.Collections.Generic.List<Outline>();
    readonly System.Collections.Generic.List<Text> chipTexts = new System.Collections.Generic.List<Text>();
    bool TeamMode => teamCls != null;

    const float ChipW = 250f, ChipH = 64f, ChipGap = 16f, StripY = -432f;

    /// <summary>팀 구성 모드 켜기. names[0] = 나, 이후 봇. initial = 기본 클래스.</summary>
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
        float left = -stripW / 2f - 100f; // 출격 버튼 자리만큼 왼쪽으로

        for (int i = 0; i < n; i++)
        {
            int idx = i;
            float cx = left + ChipW / 2f + i * (ChipW + ChipGap);
            var bg = Img(transform, null, CardBg, new Vector2(cx, StripY), new Vector2(ChipW, ChipH));
            var img = bg.GetComponent<Image>();
            img.raycastTarget = true;
            BindEvent(bg.gameObject, _ => { editIdx = idx; RefreshTeamStrip(); });
            var ol = bg.gameObject.AddComponent<Outline>();
            ol.effectDistance = new Vector2(2f, -2f);
            chipBgs.Add(img);
            chipOutlines.Add(ol);

            Txt(bg, "", 16, FontStyle.Normal, Color.white, TextAnchor.MiddleCenter,
                new Vector2(-ChipW / 2f, 0f), new Vector2(ChipW, ChipH), GameFonts.Hud, rich: true);
            chipTexts.Add(bg.GetChild(bg.childCount - 1).GetComponent<Text>());
        }

        // 출격 버튼 — 스트립 오른쪽
        float bx = left + stripW + 40f + 90f;
        var plate = UISkin.ButtonPlate();
        var btn = Img(transform, plate, plate != null ? Color.white : new Color(0.16f, 0.7f, 0.55f),
            new Vector2(bx, StripY), new Vector2(180f, ChipH));
        btn.GetComponent<Image>().raycastTarget = true;
        BindEvent(btn.gameObject, _ => StartTeam());
        Txt(btn, "출격  (Enter)", 20, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter,
            new Vector2(-90f, 0f), new Vector2(180f, ChipH), GameFonts.Hud);

        Txt(transform, "칩을 고르고 카드를 클릭하면 그 칸에 배정됩니다 · 상대 조합은 시작 전까지 비공개", 14,
            FontStyle.Normal, DimText, TextAnchor.MiddleCenter,
            new Vector2(-400f, StripY - 46f), new Vector2(800f, 22f), GameFonts.Hud);
    }

    void RefreshTeamStrip()
    {
        for (int i = 0; i < chipTexts.Count; i++)
        {
            var meta = Meta[(int)teamCls[i]];
            bool sel = i == editIdx;
            chipTexts[i].text = $"{teamNames[i]} · {(i == 0 ? "나" : "봇")}\n" +
                                $"<b>{Colored(meta.name, meta.color)}</b>";
            chipBgs[i].color = sel ? Color.Lerp(CardBg, meta.color, 0.3f) : CardBg;
            chipOutlines[i].effectColor = sel ? Color.Lerp(meta.color, Color.white, 0.4f) : new Color(0.25f, 0.3f, 0.4f);
        }
    }

    void StartTeam()
    {
        UIManager.Instance.ClosePopupUI(this);
        OnTeamPicked?.Invoke(teamCls);
    }

    void LateUpdate()
    {
        if (!TeamMode || Keyboard.current == null) return;
        if (UIManager.Instance == null || !UIManager.Instance.IsTopPopup(this)) return;
        if (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame)
            StartTeam();
    }

    // ── 카드 구축 ─────────────────────────────────────────────

    const float CardW = 280f, CardH = 700f; // 3:4 슬롯(331) + HP·스탯·스킬 담게 세로 확장

    static void BuildCard(GameObject card, int i)
    {
        var meta = Meta[i];
        var def = ClassCatalog.Get((UnitClass)i);

        // 프리팹의 구 텍스트(Name/Desc) 제거 — 레이아웃 전면 교체
        foreach (var n in new[] { "Name", "Desc" })
        {
            var old = card.transform.Find(n);
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);
        }

        // 카드 크기·간격 목업대로 (pivot 중앙 기준 절대 좌표로 이하 전부 배치)
        var crt = (RectTransform)card.transform;
        crt.sizeDelta = new Vector2(CardW, CardH);
        crt.anchoredPosition = new Vector2((i - 2) * (CardW + 22f), 0f);

        // 배경 = 딥네이비 + 클래스 색 상단 열기 글로우. 테두리 = 불꽃 네온(다중 글로우).
        var rootImg = card.GetComponent<Image>();
        if (rootImg != null) { rootImg.sprite = null; rootImg.color = CardBg; }
        MakeNeonBorder(card.transform, meta.color);

        float top = CardH * 0.5f;   // +300

        // 상단 열기 글로우 — 카드 위쪽에서 클래스 색이 은은히 타오르는 느낌
        var glow = Img(card.transform, null, new Color(meta.color.r, meta.color.g, meta.color.b, 0.16f),
            new Vector2(0f, top - 90f), new Vector2(CardW - 8f, 180f));
        glow.GetComponent<Image>().raycastTarget = false;

        var portraitTex = Resources.Load<Texture2D>("UI/" + Portraits[i]);

        // 헤더: 이름(좌, 얇은 폰트 + 은은한 네온) + 역할 EN + 우상단 썸네일 + 이름 밑 발광 언더라인
        var nameColor = Color.Lerp(Color.white, meta.color, 0.25f); // 순백 대신 클래스 색 살짝 — 눈부심 완화
        NeonTxt(card.transform, meta.name, 27, nameColor, meta.color,
            new Vector2(-CardW / 2f + 18f, top - 32f), new Vector2(200f, 36f), GameFonts.Hud);
        Txt(card.transform, meta.roleEn, 13, FontStyle.Bold, meta.color, TextAnchor.MiddleLeft,
            new Vector2(-CardW / 2f + 18f, top - 58f), new Vector2(200f, 18f), GameFonts.Hud);
        var uline = Img(card.transform, null, meta.color,
            new Vector2(-CardW / 2f + 90f, top - 74f), new Vector2(150f, 2f));
        AddGlow(uline, meta.color, 3f);
        if (portraitTex != null)
            Img(card.transform, ToSprite(portraitTex), Color.white,
                new Vector2(CardW / 2f - 32f, top - 34f), new Vector2(40f, 40f), aspect: true);

        // 초상 슬롯 — 원화 3:4 비율에 맞춘 네모칸 (폭 고정 → 높이 자동). 아트가 꽉 참, 여백 없음.
        const float SlotTop = 84f;               // 헤더 아래
        float slotW = CardW - 32f;               // 248
        float slotH = slotW * 4f / 3f;           // 3:4 → 약 331
        float slotCenterY = top - SlotTop - slotH * 0.5f;
        var slot = Img(card.transform, null, new Color(0.05f, 0.07f, 0.12f, 1f),
            new Vector2(0f, slotCenterY), new Vector2(slotW, slotH));
        AddGlow(slot, meta.color, 2f);
        if (portraitTex != null)
        {
            // 원화도 3:4라 aspect 없이 슬롯을 그대로 채운다 — 레터박스·크롭 없음
            Img(card.transform, ToSprite(portraitTex), Color.white,
                new Vector2(0f, slotCenterY), new Vector2(slotW - 6f, slotH - 6f));
        }

        // HP 행 — 슬롯 하단 기준. 게이지는 채움 바(칸 글자 X — 15칸이 테두리 뚫던 문제 해결).
        float below = top - SlotTop - slotH - 22f; // 슬롯 바닥 - 여백
        float y = below;
        float barLeft = -CardW / 2f + 52f, barRight = CardW / 2f - 44f;
        float barW = barRight - barLeft;
        Txt(card.transform, "HP", 17, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft,
            new Vector2(-CardW / 2f + 18f, y), new Vector2(40f, 22f), GameFonts.Hud);
        // 바 배경 + 채움 (HP는 최대 15 기준 비율 — 탱커 만땅, 까치 1/3)
        Img(card.transform, null, new Color(0.14f, 0.17f, 0.23f, 1f),
            new Vector2((barLeft + barRight) / 2f, y), new Vector2(barW, 12f));
        float hpFrac = def.maxHp / 15f;
        var fill = Img(card.transform, null, meta.color,
            new Vector2(barLeft + barW * hpFrac / 2f, y), new Vector2(barW * hpFrac, 12f));
        AddGlow(fill, meta.color, 1.5f);
        Txt(card.transform, def.maxHp.ToString(), 17, FontStyle.Bold, Color.white, TextAnchor.MiddleRight,
            new Vector2(CardW / 2f - 16f, y), new Vector2(44f, 22f), GameFonts.Hud, pivotRight: true);

        // 스탯 3행 — 실수치: 공격=최대 스킬 피해, 방어=내구(HP/3), 기동=최대 이동 칸
        int atk = 0;
        foreach (var s in def.skills) atk = Math.Max(atk, s.damage);
        int dfn = Mathf.Clamp(Mathf.RoundToInt(def.maxHp / 3f), 1, 5);
        int mob = Mathf.Clamp(def.move.maxRange, 1, 5);

        StatRow(card.transform, "공격", atk, meta.color, below - 34f);
        StatRow(card.transform, "방어", dfn, new Color(0.35f, 0.85f, 0.45f), below - 62f);
        StatRow(card.transform, "기동", mob, meta.color, below - 90f);

        // 구분선
        Img(card.transform, null, new Color(0.2f, 0.25f, 0.33f, 1f),
            new Vector2(0f, below - 116f), new Vector2(CardW - 32f, 1.5f));

        // 스킬 2행 — 아이콘 + 이름·설명
        SkillRow(card.transform, def.skills[0], below - 142f);
        if (def.skills.Length > 1)
            SkillRow(card.transform, def.skills[1], below - 176f);
    }

    static void StatRow(Transform card, string label, int value, Color color, float y)
    {
        Txt(card.transform, label, 15, FontStyle.Bold, DimText, TextAnchor.MiddleLeft,
            new Vector2(-CardW / 2f + 18f, y), new Vector2(56f, 22f), GameFonts.Hud);

        // 도트 5개를 원형 스프라이트로 균등 배치 — 글자 오버플로로 테두리 뚫던 문제 해결
        var dotSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
        const float d0 = 84f, gap = 26f, r = 14f;
        for (int d = 0; d < 5; d++)
        {
            var dot = Img(card, dotSprite, d < value ? color : EmptyDot,
                new Vector2(-CardW / 2f + d0 + d * gap, y), new Vector2(r, r));
            if (d < value) AddGlow(dot, color, 1.2f);
        }
    }

    static void SkillRow(Transform card, SkillDef skill, float y)
    {
        var iconTex = Resources.Load<Texture2D>("UI/" + SkillIconName(skill.kind));
        if (iconTex != null)
            Img(card.transform, ToSprite(iconTex), Color.white,
                new Vector2(-CardW / 2f + 30f, y), new Vector2(26f, 26f), aspect: true);

        Txt(card.transform, $"<b>{SkillName(skill.kind)}</b>  <color=#9aa5b5><size=12>{SkillDesc(skill.kind)}</size></color>",
            16, FontStyle.Normal, Color.white, TextAnchor.MiddleLeft,
            new Vector2(-CardW / 2f + 50f, y), new Vector2(CardW - 62f, 26f), GameFonts.Hud, rich: true);
    }

    // ── 스킬 표기 테이블 ─────────────────────────────────────

    static string SkillName(SkillKind kind)
    {
        switch (kind)
        {
            case SkillKind.ShieldPush: return "방패 밀기";
            case SkillKind.Smash: return "강타";
            case SkillKind.Dash: return "돌파";
            case SkillKind.Scream: return "비명";
            case SkillKind.Blink: return "도약";
            case SkillKind.Claw: return "발톱";
            case SkillKind.Burst: return "파열탄";
            case SkillKind.BombDeliver: return "폭탄 배달";
            case SkillKind.KnockShot: return "넉백샷";
            case SkillKind.Snipe: return "저격";
            default: return kind.ToString();
        }
    }

    static string SkillDesc(SkillKind kind)
    {
        switch (kind)
        {
            case SkillKind.ShieldPush: return "전방 밀치기";
            case SkillKind.Smash: return "밀침·벽충돌";
            case SkillKind.Dash: return "직선 2칸 대시";
            case SkillKind.Scream: return "주변 1초 스턴";
            case SkillKind.Blink: return "2칸 점멸";
            case SkillKind.Claw: return "고위력 근접";
            case SkillKind.Burst: return "십자 5칸";
            case SkillKind.BombDeliver: return "원거리 투척";
            case SkillKind.KnockShot: return "밀쳐내는 사격";
            case SkillKind.Snipe: return "1열 관통";
            default: return "";
        }
    }

    static string SkillIconName(SkillKind kind)
    {
        switch (kind)
        {
            case SkillKind.Smash: return "Icon_Skill_Smash";
            case SkillKind.Dash: return "Icon_Skill_Dash";
            case SkillKind.Blink: return "Icon_Skill_Blink";
            case SkillKind.Burst: return "Icon_Skill_Burst";
            case SkillKind.Snipe: return "Icon_Skill_Snipe";
            case SkillKind.ShieldPush: return "Icon_Guard";
            case SkillKind.Claw: return "Icon_Attack";
            case SkillKind.KnockShot: return "Icon_Attack";
            case SkillKind.BombDeliver: return "Icon_Skill_BombDeliver";
            default: return "Icon_Skill_Generic";
        }
    }

    // ── uGUI 빌드 헬퍼 ───────────────────────────────────────

    static string Colored(string s, Color c) =>
        $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{s}</color>";

    static string Repeat(char c, int n) => new string(c, n);

    static Sprite ToSprite(Texture2D tex) =>
        Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));

    static void MakeNeonBorder(Transform card, Color color)
    {
        // 바깥 발광 헤일로(반투명 두꺼운 테두리) + 안쪽 밝은 라인 — 불꽃 네온 이중 테두리
        var halo = color; halo.a = 0.35f;
        Edge(card, halo, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 7f), 0f);   // top halo
        Edge(card, halo, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 7f), 0f);   // bottom halo
        Edge(card, halo, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(7f, 0f), 0f);   // left halo
        Edge(card, halo, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(7f, 0f), 0f);   // right halo

        var bright = Color.Lerp(color, Color.white, 0.45f);
        Edge(card, bright, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 3f), 0f);
        Edge(card, bright, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 3f), 0f);
        Edge(card, bright, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(3f, 0f), 0f);
        Edge(card, bright, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(3f, 0f), 0f);
    }

    static void Edge(Transform card, Color color, Vector2 aMin, Vector2 aMax, Vector2 size, float _)
    {
        var go = new GameObject("Border", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(card, false);
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
    }

    /// <summary>Shadow 다중 적층으로 네온 글로우 흉내 — 4방향 번짐.</summary>
    static void AddGlow(RectTransform rt, Color color, float spread)
    {
        var glow = color; glow.a = 0.55f;
        var s1 = rt.gameObject.AddComponent<Shadow>();
        s1.effectColor = glow; s1.effectDistance = new Vector2(spread, spread);
        var s2 = rt.gameObject.AddComponent<Shadow>();
        s2.effectColor = glow; s2.effectDistance = new Vector2(-spread, -spread);
        var s3 = rt.gameObject.AddComponent<Shadow>();
        s3.effectColor = glow; s3.effectDistance = new Vector2(spread, -spread);
        var s4 = rt.gameObject.AddComponent<Shadow>();
        s4.effectColor = glow; s4.effectDistance = new Vector2(-spread, spread);
    }

    /// <summary>네온 텍스트 — 색 글로우가 은은히 번진 글자. 얇은 폰트라 Normal 웨이트.</summary>
    static void NeonTxt(Transform parent, string text, int size, Color color, Color glow,
        Vector2 pos, Vector2 sizeDelta, Font font)
    {
        Txt(parent, text, size, FontStyle.Normal, color, TextAnchor.MiddleLeft, pos, sizeDelta, font);
        // 방금 만든 텍스트가 마지막 자식 — 글로우 적층 (약하게: 1방향만 은은히)
        var rt = (RectTransform)parent.GetChild(parent.childCount - 1);
        var g = glow; g.a = 0.4f;
        var sh = rt.gameObject.AddComponent<Shadow>();
        sh.effectColor = g;
        sh.effectDistance = new Vector2(1.5f, -1.5f);
    }

    /// <summary>카드 중앙(pivot 0.5,0.5) 기준 절대좌표 이미지.</summary>
    static RectTransform Img(Transform parent, Sprite sprite, Color color, Vector2 pos, Vector2 size, bool aspect = false)
    {
        var go = new GameObject("Img", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.preserveAspect = aspect;
        img.raycastTarget = false;
        return rt;
    }

    /// <summary>카드 중앙 기준 절대좌표 텍스트. pos = 중심(또는 pivotRight면 우측) 위치.</summary>
    static void Txt(Transform parent, string text, int size, FontStyle style, Color color,
        TextAnchor align, Vector2 pos, Vector2 sizeDelta, Font font, bool rich = false, bool pivotRight = false)
    {
        var go = new GameObject("Txt", typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = pivotRight ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        var t = go.GetComponent<Text>();
        t.text = text;
        t.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = align;
        t.supportRichText = rich;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
    }
}
