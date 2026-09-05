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

    // 시안 좌표(1336×753) → 캔버스 기준 해상도(1920×1080) 환산 = ×1.437
    const float BtnW = 388f, BtnH = 86f, BtnGap = 114f;
    const float ColumnX = 362f;   // 버튼 중심 x (좌측 정렬 열)
    const float FirstY = 24f;     // 첫 버튼 중심 y (화면 중앙 기준) — 시안 실측. 202는 로고를 덮었다

    Text searchText;
    RectTransform searchRing;   // 매칭 대기 회전 링
    readonly Image[] btnGlows = new Image[4];
    bool searching;

    public override void Init()
    {
        Bind<GameObject>(typeof(Buttons));

        var dim = transform.Find("Dim")?.GetComponent<Image>();
        if (dim != null) dim.color = new Color(0f, 0f, 0f, 0f); // 배경 아트를 가리지 않는다

        var prefabTitle = transform.Find("Title");
        if (prefabTitle != null) prefabTitle.gameObject.SetActive(false); // 로고는 배경에 박혀 있다

        BuildBackground();

        // 프리팹에 없는 네 번째. Init이 두 번 돌아도 새로 만들지 않는다 —
        // 겹쳐 만들면 같은 버튼이 두 벌 그려진다.
        var existingQuit = transform.Find("BtnQuit");
        var quit = existingQuit != null ? existingQuit.gameObject : MakeButtonObject("BtnQuit");
        StyleButton(Get<GameObject>((int)Buttons.BtnSingle), 0, "멀티 모드", () => Pick());
        StyleButton(Get<GameObject>((int)Buttons.BtnHost), 1, "지휘관 모드 (싱글)", () => OnCommander?.Invoke());
        StyleButton(Get<GameObject>((int)Buttons.BtnJoin), 2, "설정", () => OnSettings?.Invoke());
        StyleButton(quit, 3, "게임 종료", Quit);
    }

    // ── 배치 ─────────────────────────────────────────────

    /// <summary>
    /// 배경 아트 — 팝업 맨 뒤에 화면 가득. 로고·캐릭터가 이미 그려져 있다.
    /// AspectRatioFitter.EnvelopeParent = "cover" — 비율을 지키며 화면을 덮고 넘치는 쪽만 잘린다.
    /// 늘려서 채우면(preserveAspect=false) 창 비율에 따라 캐릭터가 찌그러진다.
    /// </summary>
    void BuildBackground()
    {
        var old = transform.Find("Background");
        if (old != null) Destroy(old.gameObject); // Init 재실행 시 겹쳐 깔리지 않게

        var tex = Resources.Load<Texture2D>("UI/BG_Main");
        if (tex == null) return;

        var go = new GameObject("Background", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.SetAsFirstSibling(); // 버튼보다 뒤

        var img = go.GetComponent<Image>();
        img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        img.preserveAspect = false; // 크기는 Fitter가 잡는다
        img.raycastTarget = false;

        var fit = go.GetComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fit.aspectRatio = (float)tex.width / tex.height;
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
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f); // 좌측 기준 — 해상도가 바뀌어도 왼쪽에 붙는다
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(ColumnX, FirstY - index * BtnGap);
        rt.sizeDelta = new Vector2(BtnW, BtnH);
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
        var quit = transform.Find("BtnQuit");
        if (quit != null) quit.gameObject.SetActive(false);

        if (searchText == null)
        {
            searchText = MakeText("Searching", "", 30, FontStyle.Normal, new Color(0.6f, 0.9f, 1f),
                new Vector2(0f, 0.5f), new Vector2(ColumnX, FirstY + 40f), new Vector2(700f, 60f), GameFonts.Title);

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
                searchRing.anchorMin = searchRing.anchorMax = new Vector2(0f, 0.5f);
                searchRing.anchoredPosition = new Vector2(ColumnX, FirstY - 80f);
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
