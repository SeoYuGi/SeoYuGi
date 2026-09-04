using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 멀티 관련 팝업 프리팹 생성기 — 메뉴 1회 실행으로 Resources/UI/Popup에 저장.
/// "SeoYuGi/UI/Build Net Popups (All)" 하나로 타이틀·조인코드·로비 전부.
/// 버튼 이름은 각 팝업 클래스의 enum Buttons와 일치해야 바인딩된다.
/// </summary>
public static class UINetPopupBuilders
{
    const string Dir = "Assets/Resources/UI/Popup";

    [MenuItem("SeoYuGi/UI/Build Net Popups (All)")]
    public static void BuildAll()
    {
        BuildTitle();
        BuildJoinCode();
        BuildLobby();
    }

    // ── 타이틀 ─────────────────────────────

    [MenuItem("SeoYuGi/UI/Build Title Popup Prefab")]
    public static void BuildTitle()
    {
        var root = NewRect("UITitlePopup", null);
        StretchFull(root);
        Dim(root);

        var title = NewText("Title", root, "서유기", 72, FontStyle.Bold);
        title.anchorMin = title.anchorMax = new Vector2(0.5f, 1f);
        title.anchoredPosition = new Vector2(0f, -160f);
        title.sizeDelta = new Vector2(600f, 100f);

        string[] names = { "BtnSingle", "BtnHost", "BtnJoin" };
        string[] labels = { "싱글플레이", "방 만들기", "코드로 참가" };
        for (int i = 0; i < 3; i++)
        {
            var btn = MakeButton(root, names[i], labels[i], new Vector2(360f, 76f));
            btn.anchorMin = btn.anchorMax = new Vector2(0.5f, 0.5f);
            btn.anchoredPosition = new Vector2(0f, 60f - i * 100f);
        }

        Save(root, "UITitlePopup");
    }

    // ── 조인 코드 ─────────────────────────────

    [MenuItem("SeoYuGi/UI/Build JoinCode Popup Prefab")]
    public static void BuildJoinCode()
    {
        var root = NewRect("UIJoinCodePopup", null);
        StretchFull(root);
        Dim(root);

        var title = NewText("Title", root, "조인 코드 입력", 44, FontStyle.Bold);
        title.anchorMin = title.anchorMax = new Vector2(0.5f, 0.5f);
        title.anchoredPosition = new Vector2(0f, 160f);
        title.sizeDelta = new Vector2(600f, 70f);

        // InputField — 프로젝트 첫 사례. legacy Text 기반 (기존 팝업들과 일관).
        var field = NewRect("CodeInput", root);
        field.anchorMin = field.anchorMax = new Vector2(0.5f, 0.5f);
        field.sizeDelta = new Vector2(420f, 70f);
        field.anchoredPosition = new Vector2(0f, 60f);
        var fieldImg = field.gameObject.AddComponent<Image>();
        fieldImg.color = new Color(0.1f, 0.11f, 0.14f, 0.95f);
        var input = field.gameObject.AddComponent<InputField>();

        var inputText = NewText("Text", field, "", 34, FontStyle.Bold);
        StretchFull(inputText);
        inputText.offsetMin = new Vector2(16f, 8f);
        inputText.offsetMax = new Vector2(-16f, -8f);
        var textComp = inputText.GetComponent<Text>();
        textComp.alignment = TextAnchor.MiddleCenter;
        textComp.supportRichText = false;

        var placeholder = NewText("Placeholder", field, "ABC123", 34, FontStyle.Italic);
        StretchFull(placeholder);
        placeholder.offsetMin = new Vector2(16f, 8f);
        placeholder.offsetMax = new Vector2(-16f, -8f);
        var phComp = placeholder.GetComponent<Text>();
        phComp.alignment = TextAnchor.MiddleCenter;
        phComp.color = new Color(1f, 1f, 1f, 0.25f);

        input.textComponent = textComp;
        input.placeholder = phComp;
        input.characterLimit = 12;

        string[] names = { "BtnPaste", "BtnJoin", "BtnBack" };
        string[] labels = { "붙여넣기", "참가", "뒤로" };
        for (int i = 0; i < 3; i++)
        {
            var btn = MakeButton(root, names[i], labels[i], new Vector2(200f, 64f));
            btn.anchorMin = btn.anchorMax = new Vector2(0.5f, 0.5f);
            btn.anchoredPosition = new Vector2((i - 1) * 220f, -60f);
        }

        Save(root, "UIJoinCodePopup");
    }

    // ── 로비 ─────────────────────────────

    [MenuItem("SeoYuGi/UI/Build Lobby Popup Prefab")]
    public static void BuildLobby()
    {
        var root = NewRect("UILobbyPopup", null);
        StretchFull(root);
        Dim(root);

        var title = NewText("Title", root, "로비", 48, FontStyle.Bold);
        title.anchorMin = title.anchorMax = new Vector2(0.5f, 1f);
        title.anchoredPosition = new Vector2(0f, -70f);
        title.sizeDelta = new Vector2(400f, 70f);

        var code = NewText("CodeText", root, "", 30, FontStyle.Bold);
        code.anchorMin = code.anchorMax = new Vector2(0.5f, 1f);
        code.anchoredPosition = new Vector2(0f, -130f);
        code.sizeDelta = new Vector2(700f, 44f);
        code.GetComponent<Text>().color = new Color(0.55f, 0.95f, 1f);

        var status = NewText("StatusText", root, "", 26, FontStyle.Normal);
        status.anchorMin = status.anchorMax = new Vector2(0.5f, 0f);
        status.anchoredPosition = new Vector2(0f, 170f);
        status.sizeDelta = new Vector2(800f, 40f);

        // 슬롯 6칸 — 좌 3칸 팀0(파랑 틴트), 우 3칸 팀1(빨강 틴트)
        for (int i = 0; i < 6; i++)
        {
            int team = i < 3 ? 0 : 1;
            var slot = NewRect($"Slot{i + 1}", root);
            slot.anchorMin = slot.anchorMax = new Vector2(0.5f, 0.5f);
            slot.sizeDelta = new Vector2(220f, 200f);
            float x = team == 0 ? -420f + (i % 3) * 0f : 420f;
            slot.anchoredPosition = new Vector2(team == 0 ? -360f : 360f, 120f - (i % 3) * 150f);

            var img = slot.gameObject.AddComponent<Image>();
            img.color = team == 0
                ? new Color(0.14f, 0.2f, 0.32f, 0.95f)
                : new Color(0.3f, 0.15f, 0.14f, 0.95f);
            slot.gameObject.AddComponent<Button>().targetGraphic = img;

            var label = NewText("Label", slot, $"슬롯 {i + 1}", 24, FontStyle.Bold);
            StretchFull(label);
            label.GetComponent<Text>().alignment = TextAnchor.MiddleCenter;
        }

        string[] names = { "BtnClass", "BtnCopyCode", "BtnStart", "BtnLeave" };
        string[] labels = { "캐릭터 선택", "코드 복사", "시작", "나가기" };
        for (int i = 0; i < 4; i++)
        {
            var btn = MakeButton(root, names[i], labels[i], new Vector2(220f, 60f));
            btn.anchorMin = btn.anchorMax = new Vector2(0.5f, 0f);
            btn.anchoredPosition = new Vector2((i - 1.5f) * 240f, 90f);
        }

        Save(root, "UILobbyPopup");
    }

    // ── 공용 ─────────────────────────────

    static void Dim(RectTransform root)
    {
        var dim = NewRect("Dim", root);
        StretchFull(dim);
        dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.8f);
    }

    static RectTransform MakeButton(RectTransform parent, string name, string label, Vector2 size)
    {
        var btn = NewRect(name, parent);
        btn.sizeDelta = size;
        var img = btn.gameObject.AddComponent<Image>();
        img.color = new Color(0.16f, 0.18f, 0.23f, 0.95f);
        btn.gameObject.AddComponent<Button>().targetGraphic = img;

        var text = NewText("Label", btn, label, 28, FontStyle.Bold);
        StretchFull(text);
        text.GetComponent<Text>().alignment = TextAnchor.MiddleCenter;
        return btn;
    }

    static RectTransform NewRect(string name, RectTransform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        if (parent != null) rt.SetParent(parent, false);
        return rt;
    }

    static RectTransform NewText(string name, RectTransform parent, string text, int size, FontStyle style)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<Text>();
        t.text = text;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.raycastTarget = false; // 버튼 클릭 가로채기 금지
        return rt;
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void Save(RectTransform root, string name)
    {
        Directory.CreateDirectory(Dir);
        PrefabUtility.SaveAsPrefabAsset(root.gameObject, $"{Dir}/{name}.prefab");
        Object.DestroyImmediate(root.gameObject);
        AssetDatabase.Refresh();
        Debug.Log($"프리팹 저장 완료: {Dir}/{name}.prefab");
    }
}
