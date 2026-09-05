using System;
using SeoYuGi.BattleView; // GameFonts
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 시작 화면 — 도플갱어 메인화면 시안(Figma) 그대로.
///
/// 배경(UI/BG_Main)에 로고·캐릭터·버튼 판·글씨까지 전부 그려져 있다. 그래서 여기서는
/// 아무것도 그리지 않고, 그림 속 버튼 자리에 **투명한 클릭 영역**만 올린다.
/// 판을 코드로 다시 그리면 시안과 어긋나고(모서리·글씨), 그림을 그대로 쓰면 어긋날 것이 없다.
///
/// 배경은 원본 비율을 지키고(FitInParent) 남는 쪽은 검게 둔다. 클릭 영역은 배경의
/// 자식이라 해상도·비율이 바뀌어도 그림 위 같은 자리를 지킨다.
///
/// 프리팹의 버튼 셋(BtnSingle·BtnHost·BtnJoin)을 재활용하고 모자란 하나만 런타임 생성한다 —
/// 팀원 프리팹은 건드리지 않는다는 이 파일의 기존 규칙 그대로.
/// </summary>
public class UITitlePopup : UIPopup
{
    enum Buttons { BtnSingle, BtnHost, BtnJoin }

    public Action OnMatch;     // 멀티 — 실사람 매칭, 부족분 봇
    public Action OnCommander; // 지휘관 모드 — 봇전 + 팀원 무전 지휘
    public Action OnTutorial;  // 튜토리얼 — 가이드 붙은 지휘관 봇전 (거점 → 조작 → 지휘 3단계)
    public Action OnTraining;  // 훈련장 — 허수아비 상대로 기술 연습, 캐릭터 즉시 교체
    public Action OnSettings;  // 설정 (없으면 버튼이 조용히 비활성)

    static readonly Color Cyan = new Color(0.35f, 0.85f, 1f);

    // 그림(1670×941) 속 버튼 판 실측 → 정규화 (x: 좌측 기준, y: 아래 기준).
    // 네 판의 x는 같다. 위에서부터 멀티 / 지휘관 / 설정 / 종료.
    const float BtnX0 = 145f / 1670f, BtnX1 = 478f / 1670f;
    static readonly (float top, float bottom)[] BtnRows =
    {
        (410f / 941f, 493f / 941f),
        (511f / 941f, 591f / 941f),
        (612f / 941f, 690f / 941f),
        (710f / 941f, 789f / 941f),
    };

    Text searchText;
    RectTransform searchRing;   // 매칭 대기 회전 링
    Image searchShade;          // 매칭 중 버튼 열을 눌러 두는 판 — 그림 속 버튼이 눌리는 것처럼 안 보이게
    RectTransform bgRect;       // 배경 이미지 — 클릭 영역의 부모
    readonly GameObject[] hits = new GameObject[4];
    bool searching;

    public override void Init()
    {
        CreateNicknameInput(); // 메인화면 콜사인 설정 (2026-09-05)
        Bind<GameObject>(typeof(Buttons));

        var dim = transform.Find("Dim")?.GetComponent<Image>();
        if (dim != null)
        {
            dim.color = new Color(0f, 0f, 0f, 0f);
            dim.raycastTarget = false; // 투명해도 레이캐스트를 먹으면 아래 버튼이 눌리지 않는다
        }

        var prefabTitle = transform.Find("Title");
        if (prefabTitle != null) prefabTitle.gameObject.SetActive(false); // 로고는 배경에 박혀 있다

        bgRect = BuildBackground();

        // 프리팹에 없는 네 번째. Init이 두 번 돌아도 새로 만들지 않는다.
        var existingQuit = transform.Find("BtnQuit") ?? (bgRect != null ? bgRect.Find("BtnQuit") : null);
        var quit = existingQuit != null ? existingQuit.gameObject : new GameObject("BtnQuit", typeof(RectTransform));

        hits[0] = HitArea(Get<GameObject>((int)Buttons.BtnSingle), 0, () => Pick());
        hits[1] = HitArea(Get<GameObject>((int)Buttons.BtnHost), 1, () => OnCommander?.Invoke());
        hits[2] = HitArea(Get<GameObject>((int)Buttons.BtnJoin), 2, () => OnSettings?.Invoke());
        hits[3] = HitArea(quit, 3, Quit);
        // 그림엔 없는 작은 판 둘 — 우하단 (2026-09-05). 아래가 튜토리얼, 그 위가 훈련장.
        CreateSmallButton("BtnTutorial", "튜토리얼", 0.075f, () => OnTutorial?.Invoke());
        CreateSmallButton("BtnTraining", "훈련장", 0.135f, () => OnTraining?.Invoke());
    }

    // ── 배치 ─────────────────────────────────────────────

    /// <summary>배경 아트 — 원본 비율 유지, 화면과 비율이 다르면 남는 쪽은 검정.</summary>
    RectTransform BuildBackground()
    {
        var oldLetter = transform.Find("Letterbox");
        if (oldLetter != null) DestroyImmediate(oldLetter.gameObject); // Init 재실행 대비

        var back = new GameObject("Letterbox", typeof(RectTransform), typeof(Image));
        var brt = (RectTransform)back.transform;
        brt.SetParent(transform, false);
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
        brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
        brt.SetAsFirstSibling();
        var bimg = back.GetComponent<Image>();
        bimg.color = Color.black;
        bimg.raycastTarget = false;

        var tex = Resources.Load<Texture2D>("UI/BG_Main");
        if (tex == null) return null;

        var go = new GameObject("Background", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
        var rt = (RectTransform)go.transform;
        rt.SetParent(brt, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        var img = go.GetComponent<Image>();
        img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        img.preserveAspect = false; // 크기는 Fitter가 잡는다
        img.raycastTarget = false;

        var fit = go.GetComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fit.aspectRatio = (float)tex.width / tex.height;
        return rt;
    }

    /// <summary>
    /// 그림 속 버튼 자리에 투명 클릭 영역. 판·글씨는 그리지 않는다 — 그림에 있다.
    /// 프리팹 버튼에 딸린 Text·Image는 꺼서 그림과 겹치지 않게 한다.
    /// </summary>
    GameObject HitArea(GameObject btn, int index, Action onClick)
    {
        btn.SetActive(true);
        BindEvent(btn, _ => { if (!searching) onClick?.Invoke(); });

        var rt = (RectTransform)btn.transform;
        if (bgRect != null) rt.SetParent(bgRect, false);
        var row = BtnRows[index];
        rt.anchorMin = new Vector2(BtnX0, 1f - row.bottom);
        rt.anchorMax = new Vector2(BtnX1, 1f - row.top);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        rt.SetAsLastSibling();

        // 레이캐스트만 받는 투명 Image. 프리팹의 Image가 있으면 그것을 쓴다.
        var img = btn.GetComponent<Image>();
        if (img == null) img = btn.AddComponent<Image>();
        img.sprite = null;
        img.color = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = true;

        // 프리팹 버튼에 딸린 라벨·장식은 그림과 겹치므로 끈다
        foreach (var t in btn.GetComponentsInChildren<Text>(true)) t.gameObject.SetActive(false);
        var old = btn.GetComponent<Outline>();
        if (old != null) old.enabled = false;
        return btn;
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ── 매칭 연출 ─────────────────────────────────────────

    /// <summary>매칭 시작 — 클릭 영역을 끄고 버튼 열 위에 어두운 판 + '상대를 찾는 중' + 회전 링.
    /// 판·글씨가 그림에 박혀 있어 지울 수 없으므로, 대신 눌러서 "지금은 안 눌린다"를 보인다.</summary>
    public void ShowSearching()
    {
        searching = true;
        foreach (var h in hits) if (h != null) h.SetActive(false);

        var parent = bgRect != null ? bgRect : (RectTransform)transform;
        float cx = (BtnX0 + BtnX1) * 0.5f;
        float top = 1f - BtnRows[0].top, bottom = 1f - BtnRows[3].bottom;

        if (searchShade == null)
        {
            var go = new GameObject("SearchShade", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(BtnX0 - 0.01f, bottom - 0.02f);
            rt.anchorMax = new Vector2(BtnX1 + 0.01f, top + 0.02f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            searchShade = go.GetComponent<Image>();
            searchShade.color = new Color(0f, 0f, 0f, 0.82f);
            searchShade.raycastTarget = false;
        }
        searchShade.gameObject.SetActive(true);

        if (searchText == null)
        {
            searchText = MakeText("Searching", "", 30, FontStyle.Normal, new Color(0.6f, 0.9f, 1f),
                new Vector2(cx, (top + bottom) * 0.5f + 0.09f), Vector2.zero, new Vector2(700f, 60f), GameFonts.Title);

            // 검정 배경 키잉 로드 — 링 텍스처는 plain black 위에 생성돼 그냥 쓰면 검정 사각형이 보인다
            var ringTex = BattleHud.LoadKeyed("UI/Ring_Zone");
            var ringSprite = ringTex != null
                ? Sprite.Create(ringTex, new Rect(0, 0, ringTex.width, ringTex.height), new Vector2(0.5f, 0.5f))
                : null;
            if (ringSprite != null)
            {
                var go = new GameObject("SearchRing", typeof(RectTransform), typeof(Image));
                searchRing = (RectTransform)go.transform;
                searchRing.SetParent(parent, false);
                searchRing.anchorMin = searchRing.anchorMax = new Vector2(cx, (top + bottom) * 0.5f - 0.08f);
                searchRing.anchoredPosition = Vector2.zero;
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

    /// <summary>매칭 취소 — 검색 연출을 걷고 버튼 열을 되살린다 (ESC, 2026-09-05).</summary>
    public void HideSearching()
    {
        searching = false;
        foreach (var h in hits) if (h != null) h.SetActive(true);
        if (searchShade != null) searchShade.gameObject.SetActive(false);
        if (searchText != null) searchText.gameObject.SetActive(false);
        if (searchRing != null) searchRing.gameObject.SetActive(false);
    }

    /// <summary>검색 중 ESC — 러너가 구독해 매치메이킹을 중단한다.</summary>
    public Action OnCancelSearch;

    /// <summary>매칭 대기 애니메이션 텍스트 갱신 — 러너가 매 프레임 호출.</summary>
    public void SetSearchDots(int dots)
    {
        if (searchText != null)
            searchText.text = "상대를 찾는 중" + new string('.', dots) + "\n<size=18>인원이 부족하면 AI로 채웁니다</size>";
    }

    void Pick()
    {
        OnMatch?.Invoke(); // 팝업은 러너가 매칭 연출 후 닫는다
    }

    InputField nickInput;

    /// <summary>우하단 작은 판 버튼 — 배경 그림에 자리가 없는 보조 입구(튜토리얼·훈련장)용 런타임 생성.</summary>
    void CreateSmallButton(string name, string label, float anchorY, Action onClick)
    {
        var parent = bgRect != null ? bgRect : (RectTransform)transform;
        var existing = parent.Find(name);
        if (existing != null) DestroyImmediate(existing.gameObject); // Init 재실행 대비

        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.87f, anchorY);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(200f, 44f);
        var img = go.GetComponent<Image>();
        img.color = new Color(0.02f, 0.05f, 0.1f, 0.88f);
        img.raycastTarget = true;
        var ol = go.AddComponent<Outline>();
        ol.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.7f);
        ol.effectDistance = new Vector2(1.5f, -1.5f);

        var tgo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        var trt = (RectTransform)tgo.transform;
        trt.SetParent(rt, false);
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
        var t = tgo.GetComponent<Text>();
        t.text = label;
        t.fontSize = 20;
        t.fontStyle = FontStyle.Normal; // Title = A2Z Bold 파일 - 가짜 볼드 없음
        t.color = Cyan;
        t.alignment = TextAnchor.MiddleCenter;
        t.font = GameFonts.Title != null ? GameFonts.Title : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.raycastTarget = false;

        BindEvent(go, _ => { if (!searching) onClick?.Invoke(); });
    }

    /// <summary>메인화면 닉네임 입력 (2026-09-05 "메인에서 하게") — 좌하단 작은 칸.
    /// PlayerPrefs에 저장만 하고, 적용은 로비가 열릴 때 자동 전송(UILobbyPopup.Refresh)이 맡는다.</summary>
    void CreateNicknameInput()
    {
        var parent = bgRect != null ? bgRect : (RectTransform)transform;
        var font = GameFonts.Title != null ? GameFonts.Title : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        MakeText("NickLabel", "닉네임", 15, FontStyle.Normal, new Color(0.55f, 0.75f, 0.85f),
            new Vector2(0.135f, 0.115f), Vector2.zero, new Vector2(220f, 22f), font);

        var go = new GameObject("NickInput", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.135f, 0.075f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(220f, 40f);
        go.GetComponent<Image>().color = new Color(0.02f, 0.05f, 0.1f, 0.88f);

        Text MakeChild(string n, string txt, Color c)
        {
            var cgo = new GameObject(n, typeof(RectTransform), typeof(Text));
            var crt = (RectTransform)cgo.transform;
            crt.SetParent(rt, false);
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
            crt.offsetMin = new Vector2(12f, 4f); crt.offsetMax = new Vector2(-12f, -4f);
            var t = cgo.GetComponent<Text>();
            t.text = txt; t.fontSize = 18; t.color = c;
            t.alignment = TextAnchor.MiddleCenter; // 가운데 정렬 (2026-09-05)
            t.font = font;
            return t;
        }
        var textC = MakeChild("Text", "", new Color(0.9f, 0.96f, 1f));
        var ph = MakeChild("Placeholder", "닉네임 입력 (엔터)", new Color(0.45f, 0.55f, 0.65f));

        nickInput = go.AddComponent<InputField>();
        nickInput.textComponent = textC;
        nickInput.placeholder = ph;
        nickInput.characterLimit = 6; // 6자 제한 (2026-09-05)
        nickInput.text = PlayerPrefs.GetString("sy_nickname", "");
        nickInput.onEndEdit.AddListener(v =>
        {
            v = v?.Trim();
            if (!string.IsNullOrEmpty(v)) PlayerPrefs.SetString("sy_nickname", v);
        });
    }

    void LateUpdate()
    {
        if (!searching && (nickInput == null || !nickInput.isFocused) && Keyboard.current != null &&
            (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
            Pick(); // 신 Input System — 구 Input API는 이 프로젝트에서 예외를 던진다 (닉네임 입력 중엔 무시)
        if (searching && searchRing != null) searchRing.Rotate(0f, 0f, -120f * Time.unscaledDeltaTime);
        if (searching && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            OnCancelSearch?.Invoke(); // ESC = 매칭 취소 → 타이틀 버튼 복귀 (2026-09-05)
    }

    // ── 헬퍼 ─────────────────────────────────────────────

    Text MakeText(string name, string text, int size, FontStyle style, Color color,
        Vector2 anchor, Vector2 pos, Vector2 sizeDelta, Font font)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform;
        rt.SetParent(bgRect != null ? bgRect : transform, false); // 그림과 함께 움직이게
        rt.anchorMin = rt.anchorMax = anchor;
        rt.anchoredPosition = pos; rt.sizeDelta = sizeDelta;
        var t = go.GetComponent<Text>();
        GameFonts.Resolve(ref font, ref style); // Bold 요청 → 볼드 파일, 가짜 볼드 없음
        t.text = text; t.fontSize = size; t.fontStyle = style; t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.supportRichText = true;
        t.raycastTarget = false;
        t.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return t;
    }
}
