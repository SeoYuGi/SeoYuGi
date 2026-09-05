using System.IO;
using SeoYuGi.BattleView; // GameFonts
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UITitlePopup 프리팹 베이커 — 런타임 코드가 만들던 UI(배경·클릭 영역·튜토리얼/훈련장 판·닉네임 입력)를
/// 프리팹 안에 실제 노드로 구워 넣는다. 메뉴 1회 실행. 이후 위치·크기는 프리팹에서 사람이 직접 수정하고,
/// UITitlePopup.Init은 노드가 이미 있으면 레이아웃을 건드리지 않고 이벤트만 묶는다 (CLAUDE.md UI 프리팹 규칙).
/// </summary>
public static class UITitlePopupBuilder
{
    const string PrefabPath = "Assets/Resources/UI/Popup/UITitlePopup.prefab";
    const string SpriteDir = "Assets/UI/BakedSprites";

    // UITitlePopup.cs의 실측값과 동일 — 그림(1670x941) 속 버튼 판 정규화 좌표
    const float BtnX0 = 145f / 1670f, BtnX1 = 478f / 1670f;
    static readonly (float top, float bottom)[] BtnRows =
    {
        (410f / 941f, 493f / 941f),
        (511f / 941f, 591f / 941f),
        (612f / 941f, 690f / 941f),
        (710f / 941f, 789f / 941f),
    };
    static readonly Color Cyan = new Color(0.35f, 0.85f, 1f);

    [MenuItem("SeoYuGi/UI/Bake Title Popup Prefab")]
    public static void Bake()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var rootRt = (RectTransform)root.transform;

            // 프리팹 원본 노드 정리 — 런타임 Init과 같은 상태로
            var dim = root.transform.Find("Dim")?.GetComponent<Image>();
            if (dim != null) { dim.color = new Color(0f, 0f, 0f, 0f); dim.raycastTarget = false; }
            var title = root.transform.Find("Title");
            if (title != null) title.gameObject.SetActive(false); // 로고는 배경 그림에 박혀 있다

            var bg = BakeBackground(rootRt);

            // 그림 속 버튼 4자리 — 투명 클릭 영역 (위에서부터 멀티/지휘관/설정/종료)
            string[] names = { "BtnSingle", "BtnHost", "BtnJoin", "BtnQuit" };
            for (int i = 0; i < names.Length; i++)
            {
                var btn = FindOrCreate(rootRt, names[i]);
                var rt = (RectTransform)btn.transform;
                rt.SetParent(bg, false);
                var row = BtnRows[i];
                rt.anchorMin = new Vector2(BtnX0, 1f - row.bottom);
                rt.anchorMax = new Vector2(BtnX1, 1f - row.top);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                rt.SetAsLastSibling();
                var img = btn.GetComponent<Image>() ?? btn.AddComponent<Image>();
                img.sprite = null;
                img.color = new Color(0f, 0f, 0f, 0f);
                img.raycastTarget = true;
                foreach (var t in btn.GetComponentsInChildren<Text>(true)) t.gameObject.SetActive(false);
                var ol = btn.GetComponent<Outline>();
                if (ol != null) ol.enabled = false;
                btn.SetActive(true);
            }

            // 우하단 작은 판 둘 — 아래가 튜토리얼, 그 위가 훈련장
            BakeSmallButton(bg, "BtnTutorial", "튜토리얼", 0.075f);
            BakeSmallButton(bg, "BtnTraining", "훈련장", 0.135f);

            // 좌하단 닉네임 — 런타임은 배경보다 먼저 만들어 루트에 붙는다. 같은 위치에 굽는다.
            BakeNickname(rootRt);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"타이틀 팝업 프리팹 베이크 완료: {PrefabPath} — 위치는 이제 프리팹에서 직접 수정");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        AssetDatabase.Refresh();
    }

    /// <summary>배경 아트 — Letterbox(검정 스트레치) + Background(BG_Main, 원본 비율 유지).</summary>
    static RectTransform BakeBackground(RectTransform root)
    {
        var letter = FindOrCreate(root, "Letterbox").transform as RectTransform;
        Stretch(letter);
        letter.SetParent(root, false);
        letter.SetAsFirstSibling();
        var limg = letter.GetComponent<Image>() ?? letter.gameObject.AddComponent<Image>();
        limg.color = Color.black;
        limg.raycastTarget = false;

        var bg = FindOrCreate(letter, "Background").transform as RectTransform;
        Stretch(bg);
        var img = bg.GetComponent<Image>() ?? bg.gameObject.AddComponent<Image>();
        img.sprite = LoadOrCreateSprite("Assets/Game/Resources/UI/BG_Main.png", SpriteDir + "/BG_Main.asset");
        img.preserveAspect = false;
        img.raycastTarget = false;
        var fit = bg.GetComponent<AspectRatioFitter>() ?? bg.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        var tex = img.sprite != null ? img.sprite.texture : null;
        fit.aspectRatio = tex != null ? (float)tex.width / tex.height : 16f / 9f;
        return bg;
    }

    static void BakeSmallButton(RectTransform parent, string name, string label, float anchorY)
    {
        var go = FindOrCreate(parent, name);
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.87f, anchorY);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(200f, 44f);
        var img = go.GetComponent<Image>() ?? go.AddComponent<Image>();
        img.color = new Color(0.02f, 0.05f, 0.1f, 0.88f);
        img.raycastTarget = true;
        var ol = go.GetComponent<Outline>() ?? go.AddComponent<Outline>();
        ol.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.7f);
        ol.effectDistance = new Vector2(1.5f, -1.5f);

        var tgo = FindOrCreate(rt, "Label");
        var trt = (RectTransform)tgo.transform;
        trt.SetParent(rt, false);
        Stretch(trt);
        var t = tgo.GetComponent<Text>() ?? tgo.AddComponent<Text>();
        t.text = label;
        t.fontSize = 20;
        t.fontStyle = FontStyle.Normal;
        t.color = Cyan;
        t.alignment = TextAnchor.MiddleCenter;
        t.font = TitleFont();
        t.raycastTarget = false;
    }

    static void BakeNickname(RectTransform root)
    {
        var font = TitleFont();

        var lgo = FindOrCreate(root, "NickLabel");
        var lrt = (RectTransform)lgo.transform;
        lrt.SetParent(root, false);
        lrt.anchorMin = lrt.anchorMax = new Vector2(0.135f, 0.115f);
        lrt.anchoredPosition = Vector2.zero;
        lrt.sizeDelta = new Vector2(220f, 22f);
        var lt = lgo.GetComponent<Text>() ?? lgo.AddComponent<Text>();
        lt.text = "닉네임";
        lt.fontSize = 15;
        lt.color = new Color(0.55f, 0.75f, 0.85f);
        lt.alignment = TextAnchor.MiddleCenter;
        lt.horizontalOverflow = HorizontalWrapMode.Overflow;
        lt.font = font;
        lt.raycastTarget = false;

        var go = FindOrCreate(root, "NickInput");
        var rt = (RectTransform)go.transform;
        rt.SetParent(root, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.135f, 0.075f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(220f, 40f);
        var img = go.GetComponent<Image>() ?? go.AddComponent<Image>();
        img.color = new Color(0.02f, 0.05f, 0.1f, 0.88f);

        Text Child(string n, string txt, Color c)
        {
            var cgo = FindOrCreate(rt, n);
            var crt = (RectTransform)cgo.transform;
            crt.SetParent(rt, false);
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
            crt.offsetMin = new Vector2(12f, 4f); crt.offsetMax = new Vector2(-12f, -4f);
            var t = cgo.GetComponent<Text>() ?? cgo.AddComponent<Text>();
            t.text = txt; t.fontSize = 18; t.color = c;
            t.alignment = TextAnchor.MiddleCenter;
            t.font = font;
            return t;
        }
        var textC = Child("Text", "", new Color(0.9f, 0.96f, 1f));
        var ph = Child("Placeholder", "닉네임 입력 (엔터)", new Color(0.45f, 0.55f, 0.65f));

        var input = go.GetComponent<InputField>() ?? go.AddComponent<InputField>();
        input.textComponent = textC;
        input.placeholder = ph;
        input.characterLimit = 6;
    }

    // ── 헬퍼 ─────────────────────────────────────────────

    static GameObject FindOrCreate(Transform parent, string name)
    {
        // 루트 전체에서 먼저 찾는다 — 이전 베이크로 다른 부모 밑에 있어도 중복 생성하지 않게
        var found = FindDeep(parent.root, name);
        if (found != null) return found.gameObject;
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            var r = FindDeep(t.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    /// <summary>텍스처를 프리팹이 참조할 수 있는 스프라이트 에셋으로 — 런타임 Sprite.Create는 저장이 안 된다.</summary>
    static Sprite LoadOrCreateSprite(string texPath, string spritePath)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        if (existing != null) return existing;
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if (tex == null) { Debug.LogWarning($"텍스처 없음: {texPath}"); return null; }
        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        sprite.name = Path.GetFileNameWithoutExtension(spritePath);
        Directory.CreateDirectory(SpriteDir);
        AssetDatabase.CreateAsset(sprite, spritePath);
        return sprite;
    }

    static Font TitleFont()
    {
        var f = GameFonts.Title;
        return f != null ? f : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
