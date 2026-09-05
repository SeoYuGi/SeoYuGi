using System;
using SeoYuGi.BattleView; // GameFonts
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 시작 화면 — 도플갱어 메인화면 시안(Figma) 배치.
///
/// 배경(UI/BG_Main)에 로고가 이미 박혀 있으므로 로고를 따로 그리지 않는다.
/// 좌측에 버튼 네 개를 세로로 쌓는다: 멀티 모드 / 지휘관 모드 (싱글) / 설정 / 게임 종료.
/// 버튼 판은 UI/Frame_MainButton, 글씨는 프리텐다드(GameFonts).
///
/// 프리팹의 버튼 셋(BtnSingle·BtnHost·BtnJoin)을 재활용하고 모자란 하나만 런타임 생성한다 —
/// 팀원 프리팹은 건드리지 않는다는 이 파일의 기존 규칙 그대로.
/// </summary>
public class UITitlePopup : UIPopup
{
    enum Buttons { BtnSingle, BtnHost, BtnJoin }

    public Action OnMatch;     // 멀티 — 실사람 매칭, 부족분 봇
    public Action OnCommander; // 지휘관 모드 — 봇전 + 팀원 무전 지휘
    public Action OnSettings;  // 설정 (없으면 버튼이 조용히 비활성)

    static readonly Color Cyan = new Color(0.35f, 0.85f, 1f);

    // 버튼 위치는 화면이 아니라 '배경 이미지' 기준의 비율이다.
    // 배경이 레터박스로 들어가면 화면 기준 좌표는 여백만큼 어긋나므로,
    // 버튼을 배경의 자식으로 넣고 정규화 앵커로 붙인다 — 해상도·비율이 바뀌어도 그림 위 같은 자리다.
    // 값은 합성 시안(1336×753) 실측을 이미지 크기로 나눈 것.
    const float BtnCx = 0.1882f;   // 버튼 중심 x
    const float BtnW = 0.2013f;    // 버튼 폭
    const float BtnH = 0.0797f;    // 버튼 높이
    const float BtnGap = 0.1062f;  // 버튼 간격
    const float FirstCy = 0.5219f; // 첫 버튼 중심 y (아래 기준)

    Text searchText;
    RectTransform searchRing;   // 매칭 대기 회전 링
    readonly Image[] btnGlows = new Image[4];
    RectTransform bgRect;   // 배경 이미지 — 버튼의 부모
    bool searching;

    public override void Init()
    {
        Bind<GameObject>(typeof(Buttons));

        var dim = transform.Find("Dim")?.GetComponent<Image>();
        if (dim != null) dim.color = new Color(0f, 0f, 0f, 0f); // 배경 아트를 가리지 않는다

        var prefabTitle = transform.Find("Title");
        if (prefabTitle != null) prefabTitle.gameObject.SetActive(false); // 로고는 배경에 박혀 있다

        bgRect = BuildBackground();

        // 프리팹에 없는 네 번째. Init이 두 번 돌아도 새로 만들지 않는다 —
        // 겹쳐 만들면 같은 버튼이 두 벌 그려진다.
        var existingQuit = transform.Find("BtnQuit") ?? (bgRect != null ? bgRect.Find("BtnQuit") : null);
        var quit = existingQuit != null ? existingQuit.gameObject : MakeButtonObject("BtnQuit");
        StyleButton(Get<GameObject>((int)Buttons.BtnSingle), 0, "멀티 모드", () => Pick());
        StyleButton(Get<GameObject>((int)Buttons.BtnHost), 1, "지휘관 모드 (싱글)", () => OnCommander?.Invoke());
        StyleButton(Get<GameObject>((int)Buttons.BtnJoin), 2, "설정", () => OnSettings?.Invoke());
        StyleButton(quit, 3, "게임 종료", Quit);
    }

    // ── 배치 ─────────────────────────────────────────────

    /// <summary>
    /// 배경 아트 — 원본 비율을 그대로 지키고, 화면과 비율이 다르면 남는 쪽은 검게 둔다.
    /// FitInParent("contain")이라 그림이 잘리지 않는다 — cover로 덮으면 넓은 화면에서
    /// 확대돼 좌측 로고가 화면 밖으로 밀려난다.
    /// 버튼은 이 오브젝트의 자식이 되어 그림과 함께 움직인다.
    /// </summary>
    RectTransform BuildBackground()
    {
        var oldLetter = transform.Find("Letterbox");
        if (oldLetter != null) DestroyImmediate(oldLetter.gameObject); // Init 재실행 대비

        // 여백용 검은 바탕 — 배경보다 뒤, 화면 전체
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
        rt.SetParent(brt, false);           // 검은 바탕 안에서 비율을 맞춘다
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.GetComponent<Image>();
        img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        img.preserveAspect = false; // 크기는 Fitter가 잡는다
        img.raycastTarget = false;

        var fit = go.GetComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent; // 원본 비율 유지, 남는 쪽은 검정
        fit.aspectRatio = (float)tex.width / tex.height;
        return rt;
    }

    GameObject MakeButtonObject(string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(transform, false);
        var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(go.transform, false);
        var trt = (RectTransform)textGo.transform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
        return go;
    }

    /// <summary>버튼 하나를 시안 위치·모양으로. index = 위에서부터 0.</summary>
    void StyleButton(GameObject btn, int index, string label, Action onClick)
    {
        btn.SetActive(true);
        BindEvent(btn, _ => { if (!searching) onClick?.Invoke(); });

        var rt = (RectTransform)btn.transform;
        // 배경의 자식으로 두고 정규화 앵커로 붙인다 — 레터박스로 그림이 줄거나 움직여도
        // 버튼이 그림 위 같은 자리를 지킨다. 화면 기준 좌표면 여백만큼 어긋난다.
        if (bgRect != null) rt.SetParent(bgRect, false);
        float cy = FirstCy - index * BtnGap;
        rt.anchorMin = new Vector2(BtnCx - BtnW / 2f, cy - BtnH / 2f);
        rt.anchorMax = new Vector2(BtnCx + BtnW / 2f, cy + BtnH / 2f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.SetAsLastSibling(); // 배경 위로

        var plate = Resources.Load<Texture2D>("UI/Frame_MainButton");
        var img = btn.GetComponent<Image>();
        if (img == null) img = btn.AddComponent<Image>();
        if (plate != null)
        {
            img.sprite = Sprite.Create(plate, new Rect(0, 0, plate.width, plate.height), new Vector2(0.5f, 0.5f));
            img.color = Color.white;
            img.type = Image.Type.Simple;
        }
        else
        {
            img.sprite = null;
            img.color = new Color(0.03f, 0.06f, 0.12f, 0.9f); // 에셋 미도착 폴백
        }
        img.raycastTarget = true;

        // 뒤에 깔리는 발광 판 — 알파 펄스. 있으면 새로 만들지 않는다(Init 재실행 대비).
        var oldGlow = btn.transform.Find("Glow");
        if (oldGlow != null) Destroy(oldGlow.gameObject);
        var glowGo = new GameObject("Glow", typeof(RectTransform), typeof(Image));
        var grt = (RectTransform)glowGo.transform;
        grt.SetParent(btn.transform, false);
        grt.anchorMin = Vector2.zero; grt.anchorMax = Vector2.one;
        grt.offsetMin = new Vector2(-8f, -8f); grt.offsetMax = new Vector2(8f, 8f);
        grt.SetAsFirstSibling();
        var glow = glowGo.GetComponent<Image>();
        glow.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.08f);
        glow.raycastTarget = false;
        if (index < btnGlows.Length) btnGlows[index] = glow;

        var text = btn.GetComponentInChildren<Text>();
        if (text != null)
        {
            text.text = label;
            text.fontSize = 30;
            text.fontStyle = FontStyle.Normal;
            text.color = new Color(0.92f, 0.97f, 1f);
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            if (GameFonts.Hud != null) text.font = GameFonts.Hud;
        }
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

    /// <summary>매칭 시작 — 버튼 숨기고 '상대를 찾는 중' + 회전 링. 러너가 결과에 따라 닫는다.</summary>
    public void ShowSearching()
    {
        searching = true;
        Get<GameObject>((int)Buttons.BtnSingle).SetActive(false);
        Get<GameObject>((int)Buttons.BtnHost).SetActive(false);
        Get<GameObject>((int)Buttons.BtnJoin).SetActive(false);
        var quit = transform.Find("BtnQuit") ?? (bgRect != null ? bgRect.Find("BtnQuit") : null);
        if (quit != null) quit.gameObject.SetActive(false);

        if (searchText == null)
        {
            searchText = MakeText("Searching", "", 30, FontStyle.Normal, new Color(0.6f, 0.9f, 1f),
                new Vector2(BtnCx, FirstCy), new Vector2(0f, 0f), new Vector2(700f, 60f), GameFonts.Title);

            // 검정 배경 키잉 로드 — 링 텍스처는 plain black 위에 생성돼 그냥 쓰면 검정 사각형이 보인다
            var ringTex = BattleHud.LoadKeyed("UI/Ring_Zone");
            var ringSprite = ringTex != null
                ? Sprite.Create(ringTex, new Rect(0, 0, ringTex.width, ringTex.height), new Vector2(0.5f, 0.5f))
                : null;
            if (ringSprite != null)
            {
                var go = new GameObject("SearchRing", typeof(RectTransform), typeof(Image));
                searchRing = (RectTransform)go.transform;
                searchRing.SetParent(bgRect != null ? bgRect : transform, false);
                searchRing.anchorMin = searchRing.anchorMax = new Vector2(BtnCx, FirstCy - BtnGap);
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

    void LateUpdate()
    {
        float pulse = (Mathf.Sin(Time.unscaledTime * 2.4f) + 1f) * 0.5f; // 0..1
        foreach (var g in btnGlows)
            if (g != null) g.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.05f + pulse * 0.10f);

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
        rt.SetParent(bgRect != null ? bgRect : transform, false); // 그림과 함께 움직이게
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
