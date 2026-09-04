using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>UIClassSelectPopup 프리팹 생성기 — 메뉴 1회 실행으로 Resources/UI/Popup에 저장.</summary>
public static class UIClassSelectPopupBuilder
{
    const string Dir = "Assets/Resources/UI/Popup";
    const string PrefabPath = Dir + "/UIClassSelectPopup.prefab";

    // 버튼 이름 = UIClassSelectPopup.Buttons와 일치해야 바인딩됨
    static readonly (string btn, string title, string desc)[] Cards =
    {
        ("BtnTank",      "너구리",      "탱커\nHP 6 · 시야 3\n강타: 밀침 + 벽 충돌"),
        ("BtnBalance",   "고라니",    "밸런스\nHP 4 · 시야 4\n돌파: 직선 2칸 대시"),
        ("BtnAssassin",  "검은 고양이", "어쌔신\nHP 3 · 시야 4\n도약: 점멸 + 공격 버프"),
        ("BtnGrenadier", "비둘기",      "그레네이더\nHP 3 · 시야 4\n파열탄: 십자 5칸"),
        ("BtnSniper",    "까치",        "스나이퍼\nHP 2 · 시야 5\n저격: 1열 관통"),
    };

    [MenuItem("SeoYuGi/UI/Build ClassSelect Popup Prefab")]
    public static void Build()
    {
        var root = NewRect("UIClassSelectPopup", null);
        StretchFull(root);

        var dim = NewRect("Dim", root);
        StretchFull(dim);
        dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

        var title = NewText("Title", root, "캐릭터 선택", 48, FontStyle.Bold);
        title.anchorMin = title.anchorMax = new Vector2(0.5f, 1f);
        title.anchoredPosition = new Vector2(0f, -90f);
        title.sizeDelta = new Vector2(600f, 80f);

        for (int i = 0; i < Cards.Length; i++)
        {
            var card = NewRect(Cards[i].btn, root);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(280f, 380f);
            card.anchoredPosition = new Vector2((i - 2) * 310f, -20f);

            var img = card.gameObject.AddComponent<Image>();
            img.color = new Color(0.16f, 0.18f, 0.23f, 0.95f);
            card.gameObject.AddComponent<Button>().targetGraphic = img; // 호버·클릭 틴트용

            var name = NewText("Name", card, Cards[i].title, 34, FontStyle.Bold);
            name.anchorMin = name.anchorMax = new Vector2(0.5f, 1f);
            name.anchoredPosition = new Vector2(0f, -60f);
            name.sizeDelta = new Vector2(260f, 80f);

            var desc = NewText("Desc", card, Cards[i].desc, 22, FontStyle.Normal);
            StretchFull(desc);
            desc.offsetMin = new Vector2(10f, 20f);
            desc.offsetMax = new Vector2(-10f, -130f);
        }

        Directory.CreateDirectory(Dir);
        PrefabUtility.SaveAsPrefabAsset(root.gameObject, PrefabPath);
        Object.DestroyImmediate(root.gameObject);
        AssetDatabase.Refresh();
        Debug.Log($"프리팹 저장 완료: {PrefabPath}");
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
        t.color = Color.white;
        t.alignment = TextAnchor.MiddleCenter;
        t.raycastTarget = false; // 버튼 클릭 가로채기 방지
        return rt;
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
