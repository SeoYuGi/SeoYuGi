using System;
using SeoYuGi.BattleView; // GameFonts
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 시작 화면 — 매칭 시작(오버워치/롤식). 방 만들기·코드 참가는 폐기.
/// 네온 로고(Resources/UI/Logo_Title, 없으면 프리팹 텍스트 폴백) + 태그라인 + 펄스 버튼.
/// 장식은 전부 런타임 생성 — 팀원 프리팹은 건드리지 않는다.
/// </summary>
public class UITitlePopup : UIPopup
{
    enum Buttons { BtnSingle, BtnHost, BtnJoin }

    public Action OnMatch;     // 멀티 — 실사람 매칭, 부족분 봇
    public Action OnCommander; // 지휘관 모드 — 봇전 + 팀원 무전 지휘

    static readonly Color Cyan = new Color(0.35f, 0.85f, 1f);
    static readonly Color DimText = new Color(0.55f, 0.62f, 0.72f);

    Text searchText;
    RectTransform searchRing;   // 매칭 대기 회전 링
    Image btnGlow;              // 버튼 네온 펄스
    Outline btnOutline;
    Image logoImg;              // 로고 글로우 펄스용
    bool searching;

    public override void Init()
    {
        Bind<GameObject>(typeof(Buttons));
        BindEvent(Get<GameObject>((int)Buttons.BtnSingle), _ => Pick());
        Get<GameObject>((int)Buttons.BtnJoin).SetActive(false);
        StyleCommanderButton(); // 숨어 있던 BtnHost 재활용 — 프리팹은 건드리지 않는다

        var dim = transform.Find("Dim")?.GetComponent<Image>();
        if (dim != null) dim.color = new Color(0f, 0f, 0f, 0.3f);

        BuildLogo();
        StyleMatchButton();
        BuildFooter();
    }

    // ── 장식 ─────────────────────────────────────────────

    void BuildLogo()
    {
        var logoTex = Resources.Load<Texture2D>("UI/Logo_Title");
        var prefabTitle = transform.Find("Title");
        if (logoTex == null) return; // 로고 없으면 프리팹 "서유기" 텍스트 유지

        if (prefabTitle != null) prefabTitle.gameObject.SetActive(false);

        var go = new GameObject("Logo", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -215f);
        rt.sizeDelta = new Vector2(720f, 405f); // 16:9 생성 이미지
        logoImg = go.GetComponent<Image>();
        logoImg.sprite = Sprite.Create(logoTex, new Rect(0, 0, logoTex.width, logoTex.height), new Vector2(0.5f, 0.5f));
        logoImg.preserveAspect = true;
        logoImg.raycastTarget = false;

        MakeText("Tagline", "떠돌이들의 밤 — 도시관리 AI의 예측을 배신하라", 18, FontStyle.Normal, DimText,
            new Vector2(0.5f, 1f), new Vector2(0f, -418f), new Vector2(800f, 26f), GameFonts.Hud);
    }

    void StyleMatchButton()
    {
        var btn = Get<GameObject>((int)Buttons.BtnSingle);
        var rt = (RectTransform)btn.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, -130f);
        rt.sizeDelta = new Vector2(340f, 70f);

        var img = btn.GetComponent<Image>();
        img.sprite = null;
        img.color = new Color(0.03f, 0.05f, 0.1f, 0.92f); // 카드와 같은 어두운 판

        btnOutline = btn.GetComponent<Outline>();
        if (btnOutline == null) btnOutline = btn.AddComponent<Outline>();
        btnOutline.effectColor = Cyan;
        btnOutline.effectDistance = new Vector2(2f, -2f);

        // 뒤에 깔리는 발광 판 — 알파 펄스
        var glowGo = new GameObject("Glow", typeof(RectTransform), typeof(Image));
        var grt = (RectTransform)glowGo.transform;
        grt.SetParent(btn.transform, false);
        grt.anchorMin = Vector2.zero; grt.anchorMax = Vector2.one;
        grt.offsetMin = new Vector2(-10f, -10f); grt.offsetMax = new Vector2(10f, 10f);
        btnGlow = glowGo.GetComponent<Image>();
        btnGlow.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.1f);
        btnGlow.raycastTarget = false;
        grt.SetAsFirstSibling();

        var label = btn.GetComponentInChildren<Text>();
        if (label != null)
        {
            label.text = "매칭 시작";
            label.fontSize = 26;
            label.fontStyle = FontStyle.Bold;
            label.color = Color.Lerp(Color.white, Cyan, 0.25f);
            if (GameFonts.Title != null) label.font = GameFonts.Title;
        }
    }

    /// <summary>지휘관 모드 버튼 — 프리팹에 이미 있으나 꺼져 있던 BtnHost를 되살려 쓴다.</summary>
    void StyleCommanderButton()
    {
        var btn = Get<GameObject>((int)Buttons.BtnHost);
        btn.SetActive(true);
        BindEvent(btn, _ => { if (!searching) OnCommander?.Invoke(); });

        var rt = (RectTransform)btn.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, -212f); // 매칭 버튼(-130) 아래
        rt.sizeDelta = new Vector2(340f, 58f);

        var img = btn.GetComponent<Image>();
        img.sprite = null;
        img.color = new Color(0.03f, 0.05f, 0.1f, 0.92f);

        var ol = btn.GetComponent<Outline>();
        if (ol == null) ol = btn.AddComponent<Outline>();
        ol.effectColor = new Color(0.35f, 0.85f, 1f, 0.45f); // 매칭 버튼보다 약하게 — 주 버튼이 아니다
        ol.effectDistance = new Vector2(2f, -2f);

        var label = btn.GetComponentInChildren<Text>();
        if (label != null)
        {
            label.text = "지휘관 모드";
            label.fontSize = 22;
            label.fontStyle = FontStyle.Bold;
            label.color = Color.Lerp(Color.white, Cyan, 0.45f);
            if (GameFonts.Title != null) label.font = GameFonts.Title;
        }
    }

    void BuildFooter()
    {
        MakeText("Footer", "3판 2선승 · 라운드 120초 · 지휘관 모드에서는 T로 팀원에게 무전한다", 14, FontStyle.Normal,
            new Color(DimText.r, DimText.g, DimText.b, 0.8f),
            new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(900f, 22f), GameFonts.Hud);
    }

    // ── 매칭 연출 ─────────────────────────────────────────

    /// <summary>매칭 시작 — 버튼 숨기고 '상대를 찾는 중' + 회전 링. 러너가 결과에 따라 닫는다.</summary>
    public void ShowSearching()
    {
        searching = true;
        Get<GameObject>((int)Buttons.BtnSingle).SetActive(false);
        Get<GameObject>((int)Buttons.BtnHost).SetActive(false); // 매칭 중엔 모드 전환 불가
        if (searchText == null)
        {
            searchText = MakeText("Searching", "", 26, FontStyle.Normal, new Color(0.6f, 0.9f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -25f), new Vector2(700f, 60f), GameFonts.Title); // 링 위쪽 — 겹침 방지

            // 검정 배경 키잉 로드 — 링 텍스처는 plain black 위에 생성돼 그냥 쓰면 검정 사각형이 보인다
            var ringTex = BattleHud.LoadKeyed("UI/Ring_Zone");
            var ringSprite = ringTex != null
                ? Sprite.Create(ringTex, new Rect(0, 0, ringTex.width, ringTex.height), new Vector2(0.5f, 0.5f))
                : null;
            if (ringSprite != null)
            {
                var go = new GameObject("SearchRing", typeof(RectTransform), typeof(Image));
                searchRing = (RectTransform)go.transform;
                searchRing.SetParent(transform, false);
                searchRing.anchorMin = searchRing.anchorMax = new Vector2(0.5f, 0.5f);
                searchRing.anchoredPosition = new Vector2(0f, -130f);
                searchRing.sizeDelta = new Vector2(150f, 150f);
                var img = go.GetComponent<Image>();
                img.sprite = ringSprite;
                img.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.5f);
                img.raycastTarget = false;
            }
        }
        searchText.gameObject.SetActive(true);
        if (searchRing != null) searchRing.gameObject.SetActive(true);
    }

    /// <summary>매칭 대기 애니메이션 텍스트 갱신 — 러너가 매 프레임 호출.</summary>
    public void SetSearchDots(int dots)
    {
        if (searchText != null)
            searchText.text = "상대를 찾는 중" + new string('.', dots) + "\n<size=16>인원이 부족하면 AI로 채웁니다</size>";
    }

    void Pick()
    {
        OnMatch?.Invoke(); // 팝업은 러너가 매칭 연출 후 닫는다
    }

    void LateUpdate()
    {
        // 버튼·로고 네온 펄스 + Enter로 시작 + 매칭 링 회전
        float pulse = (Mathf.Sin(Time.unscaledTime * 2.4f) + 1f) * 0.5f; // 0..1
        if (btnGlow != null) btnGlow.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.06f + pulse * 0.12f);
        if (btnOutline != null) btnOutline.effectColor = Color.Lerp(Cyan * 0.7f, Cyan, pulse);
        if (logoImg != null) logoImg.color = Color.Lerp(new Color(0.92f, 0.92f, 0.92f), Color.white, pulse);

        if (!searching && Keyboard.current != null &&
            (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
            Pick(); // 신 Input System — 구 Input API는 이 프로젝트에서 예외를 던진다
        if (searching && searchRing != null) searchRing.Rotate(0f, 0f, -120f * Time.unscaledDeltaTime);
    }

    // ── 헬퍼 ─────────────────────────────────────────────

    Text MakeText(string name, string text, int size, FontStyle style, Color color,
        Vector2 anchor, Vector2 pos, Vector2 sizeDelta, Font font)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.anchoredPosition = pos; rt.sizeDelta = sizeDelta;
        var t = go.GetComponent<Text>();
        t.text = text; t.fontSize = size; t.fontStyle = style; t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.supportRichText = true;
        t.raycastTarget = false;
        t.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return t;
    }
}
