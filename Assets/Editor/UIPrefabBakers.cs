using System.Collections.Generic;
using System.IO;
using SeoYuGi.BattleView; // GameFonts
using SeoYuGi.UI;          // ClassCard
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 코드로 런타임 생성하던 UI를 프리팹에 구워 넣는 베이커 모음 (CLAUDE.md UI 프리팹 규칙).
/// 배치·구조는 프리팹이 갖고, 팝업 코드는 노드가 있으면 재사용하며 값·이벤트만 채운다.
/// 밸런스 수치·문구가 바뀌면 해당 메뉴를 다시 한 번 실행해 프리팹을 갱신한다.
/// </summary>
public static class UIPrefabBakers
{
    const string PopupDir = "Assets/Resources/UI/Popup";
    const string CardDir = "Assets/Game/Resources/UI/Prefabs"; // Resources.Load("UI/Prefabs/...") 경로
    const string SpriteDir = "Assets/UI/BakedSprites";

    [MenuItem("SeoYuGi/UI/Bake All UI Prefabs")]
    public static void BakeAll()
    {
        UITitlePopupBuilder.Bake();
        BakeClassCards();
        BakeClassSelect();
        BakeLobby();
        AssetDatabase.SaveAssets();
        Debug.Log("모든 UI 프리팹 베이크 완료 — 위치는 이제 프리팹에서 직접 수정");
    }

    // ── 클래스 카드 5종 ─────────────────────────────────────

    [MenuItem("SeoYuGi/UI/Bake Class Card Prefabs")]
    public static void BakeClassCards()
    {
        spriteCache.Clear();
        Directory.CreateDirectory(CardDir);
        for (int i = 0; i < 5; i++)
        {
            var go = new GameObject("ClassCard_" + i, typeof(RectTransform), typeof(Image));
            try
            {
                ClassCard.BuildCoded((RectTransform)go.transform, i, 1f);
                PersistAllSprites(go, "Card" + i);
                SavePrefab(go, $"{CardDir}/ClassCard_{i}.prefab");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
        AssetDatabase.Refresh();
        Debug.Log($"클래스 카드 프리팹 5종 저장: {CardDir}/ClassCard_0..4.prefab");
    }

    // ── 캐릭터 선택 팝업 ────────────────────────────────────

    [MenuItem("SeoYuGi/UI/Bake ClassSelect Popup Prefab")]
    public static void BakeClassSelect()
    {
        spriteCache.Clear();
        var path = PopupDir + "/UIClassSelectPopup.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var rootRt = (RectTransform)root.transform;

            // 카드 슬롯 5칸 자리 — 내용은 런타임에 ClassCard가 채운다
            string[] cards = { "BtnTank", "BtnBalance", "BtnAssassin", "BtnGrenadier", "BtnSniper" };
            for (int i = 0; i < cards.Length; i++)
            {
                var rt = FindDeep(rootRt, cards[i]) as RectTransform;
                if (rt == null) continue;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2((i - 2) * (ClassCard.BaseW + 22f), -125f);
                rt.sizeDelta = new Vector2(ClassCard.BaseW, ClassCard.BaseH);
            }

            // 분대 슬롯 3칸 — UIClassSelectPopup.BuildTeamSlots의 코드 레이아웃 그대로
            float slotW = UILobbyPopup.SlotW, slotH = UILobbyPopup.SlotH, slotY = UILobbyPopup.SlotY, slotGap = UILobbyPopup.SlotGap;
            var frame = Persist(UISkin.SlotFrame(), "SlotFrame");
            var frameColor = frame != null ? Color.Lerp(new Color(0.45f, 0.6f, 1f), Color.white, 0.45f)
                                           : new Color(0.14f, 0.2f, 0.32f, 0.95f);
            for (int i = 0; i < 3; i++)
            {
                var slot = ImageNode(rootRt, "TeamSlot" + i, frame, frameColor,
                    new Vector2(0.5f, 0.5f), new Vector2((i - 1) * slotGap, slotY), new Vector2(slotW, slotH), raycast: true);

                var portrait = Node(slot, "Portrait");
                portrait.anchorMin = Vector2.zero; portrait.anchorMax = Vector2.one;
                portrait.offsetMin = new Vector2(16f, 20f); portrait.offsetMax = new Vector2(-16f, -16f);
                var pImg = portrait.GetComponent<Image>() ?? portrait.gameObject.AddComponent<Image>();
                pImg.color = Color.white; pImg.preserveAspect = false; pImg.raycastTarget = false;

                var lb = Node(slot, "LabelBack");
                lb.anchorMin = new Vector2(0f, 0f); lb.anchorMax = new Vector2(1f, 0f); lb.pivot = new Vector2(0.5f, 0f);
                lb.anchoredPosition = new Vector2(0f, 20f); lb.sizeDelta = new Vector2(-24f, 48f);
                var lbImg = lb.GetComponent<Image>() ?? lb.gameObject.AddComponent<Image>();
                lbImg.color = new Color(0f, 0f, 0f, 0.55f); lbImg.raycastTarget = false;

                TextNode(lb, "Label", "", 15, bold: true, Color.white,
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(slotW - 24f, 48f));
            }

            // 상태 문구·출격 버튼·타이머 (StatusY -420, ButtonY -485 — 팝업 코드 상수)
            TextNode(rootRt, "StatusText", "", 26, bold: false, Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -420f), new Vector2(700f, 44f));

            var plate = Persist(UISkin.ButtonPlate(), "ButtonPlate");
            var launch = ImageNode(rootRt, "BtnLaunch", plate, plate != null ? Color.white : new Color(0.16f, 0.7f, 0.55f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -485f), UILobbyPopup.BottomButtonSize, raycast: true);
            launch.GetComponent<Image>().preserveAspect = false;
            TextNode(launch, "Label", "출격  (Enter)", UILobbyPopup.BottomButtonFont, bold: true, Color.white,
                new Vector2(0.5f, 0.5f), Vector2.zero, UILobbyPopup.BottomButtonSize);

            TextNode(rootRt, "TimerText", "", 40, bold: true, Color.white,
                new Vector2(0.5f, 1f), new Vector2(360f, -90f), new Vector2(200f, 56f));

            // AI 난이도 바 — 컨테이너 DiffBar 밑에 라벨 + 상/중/하 (BuildDifficultyBar 코드 레이아웃)
            const float bw = 150f, bh = 36f, gap = 6f, x = -400f;
            float yTop = slotY + bh + gap;
            var bar = Node(rootRt, "DiffBar");
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0.5f);
            bar.anchoredPosition = Vector2.zero; bar.sizeDelta = Vector2.zero;
            TextNode(bar, "DiffLabel", "AI 난이도", 16, bold: true, new Color(0.55f, 0.62f, 0.72f),
                new Vector2(0.5f, 0.5f), new Vector2(x, yTop + (bh + gap)), new Vector2(bw, bh));
            string[] diffLabels = { "하 / EASY", "중 / NORMAL", "상 / HARD" };
            for (int i = 0; i < 3; i++)
            {
                var opt = ImageNode(bar, "Diff" + i, null, new Color(0.02f, 0.03f, 0.07f, 0.97f),
                    new Vector2(0.5f, 0.5f), new Vector2(x, yTop - i * (bh + gap)), new Vector2(bw, bh), raycast: true);
                var ol = opt.GetComponent<Outline>() ?? opt.gameObject.AddComponent<Outline>();
                ol.effectDistance = new Vector2(2f, -2f);
                ol.effectColor = new Color(0.25f, 0.3f, 0.4f);
                TextNode(opt, "Label", diffLabels[i], 15, bold: true, Color.white,
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(bw, bh));
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"캐릭터 선택 팝업 베이크 완료: {path}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        AssetDatabase.Refresh();
    }

    // ── 멀티 로비 팝업 ──────────────────────────────────────

    [MenuItem("SeoYuGi/UI/Bake Lobby Popup Prefab")]
    public static void BakeLobby()
    {
        spriteCache.Clear();
        var path = PopupDir + "/UILobbyPopup.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var rootRt = (RectTransform)root.transform;
            var frame = Persist(UISkin.SlotFrame(), "SlotFrame");
            var plate = Persist(UISkin.ButtonPlate(), "ButtonPlate");

            // 분대 슬롯 6칸 — 코드가 덮어쓰던 축소 치수(150x198)·라벨 크기를 프리팹 값으로
            for (int i = 0; i < 6; i++)
            {
                var slot = FindDeep(rootRt, $"Slot{i + 1}") as RectTransform;
                if (slot == null) continue;
                slot.sizeDelta = new Vector2(UILobbyPopup.SlotW, UILobbyPopup.SlotH);
                var label = slot.Find("LabelBack/Label")?.GetComponent<Text>();
                if (label != null) label.fontSize = UILobbyPopup.SlotLabelFont;
                var img = slot.GetComponent<Image>();
                if (img != null && frame != null)
                {
                    img.sprite = frame;
                    img.color = Color.Lerp(i < 3 ? new Color(0.45f, 0.6f, 1f) : new Color(1f, 0.5f, 0.45f), Color.white, 0.45f);
                }
            }

            // 카드 슬롯 5칸 자리
            for (int i = 0; i < 5; i++)
            {
                var rt = FindDeep(rootRt, $"Pick{i + 1}") as RectTransform;
                if (rt == null) continue;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2((i - 2) * (ClassCard.BaseW + 22f), -125f);
                rt.sizeDelta = new Vector2(ClassCard.BaseW, ClassCard.BaseH);
            }

            // 밸런스 경고 — 코드가 옮기던 자리(슬롯 줄 왼쪽)를 프리팹 값으로
            var balance = FindDeep(rootRt, "BalanceText") as RectTransform;
            if (balance != null)
            {
                balance.anchoredPosition = new Vector2(-620f, 245f);
                balance.sizeDelta = new Vector2(300f, 200f);
            }

            // 하단 버튼 줄 — 코드가 덮어쓰던 치수(220x78)·스킨·글자 크기
            foreach (var n in new[] { "BtnCopyCode", "BtnStart", "BtnLeave" })
            {
                var b = FindDeep(rootRt, n) as RectTransform;
                if (b == null) continue;
                b.sizeDelta = UILobbyPopup.BottomButtonSize;
                var img = b.GetComponent<Image>();
                if (img != null && plate != null) { img.sprite = plate; img.color = Color.white; img.preserveAspect = false; }
                var bl = b.GetComponentInChildren<Text>();
                if (bl != null) { bl.fontSize = UILobbyPopup.BottomButtonFont; bl.alignment = TextAnchor.MiddleCenter; }
            }

            // 상대 지휘관 칸 — CreateOpponentBox 코드 레이아웃 그대로
            var slot1 = FindDeep(rootRt, "Slot1") as RectTransform;
            var oppParent = slot1 != null ? (RectTransform)slot1.parent : rootRt;
            var opp = ImageNode(oppParent, "OpponentBox", null, new Color(0.35f, 0.08f, 0.1f, 0.85f),
                slot1 != null ? slot1.anchorMin : new Vector2(0.5f, 0.5f), new Vector2(500f, 245f), new Vector2(190f, 110f), raycast: false);
            if (slot1 != null) { opp.anchorMin = slot1.anchorMin; opp.anchorMax = slot1.anchorMax; opp.pivot = slot1.pivot; }
            var oppOl = opp.GetComponent<Outline>() ?? opp.gameObject.AddComponent<Outline>();
            oppOl.effectColor = new Color(1f, 0.35f, 0.35f);
            oppOl.effectDistance = new Vector2(2f, -2f);
            var oppText = Node(opp, "Text");
            oppText.anchorMin = Vector2.zero; oppText.anchorMax = Vector2.one;
            oppText.offsetMin = oppText.offsetMax = Vector2.zero;
            var ot = oppText.GetComponent<Text>() ?? oppText.gameObject.AddComponent<Text>();
            ot.alignment = TextAnchor.MiddleCenter; ot.fontSize = 20; ot.color = Color.white; ot.text = "?";
            ot.font = HudFont();

            // 지휘관 대전 토글 — 나가기 버튼 왼쪽 480px (CreateCommanderToggle 코드 레이아웃)
            var leave = FindDeep(rootRt, "BtnLeave") as RectTransform;
            if (leave != null && FindDeep(rootRt, "BtnCommander") == null)
            {
                var go = Object.Instantiate(leave.gameObject, leave.parent);
                go.name = "BtnCommander";
                var rt = (RectTransform)go.transform;
                rt.anchoredPosition = leave.anchoredPosition + new Vector2(-480f, 0f);
                rt.sizeDelta = UILobbyPopup.BottomButtonSize;
                var img = go.GetComponent<Image>();
                if (img != null && plate != null) { img.sprite = plate; img.color = Color.white; img.preserveAspect = false; }
                foreach (var extra in go.GetComponentsInChildren<Graphic>(true))
                    if (extra.gameObject != go && !(extra is Text)) extra.enabled = false;
                var cl = go.GetComponentInChildren<Text>();
                if (cl != null) { cl.text = "지휘관 대전: OFF"; cl.fontSize = UILobbyPopup.BottomButtonFont; cl.alignment = TextAnchor.MiddleCenter; }
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"로비 팝업 베이크 완료: {path}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        AssetDatabase.Refresh();
    }

    // ── 공용 헬퍼 ───────────────────────────────────────────

    static RectTransform Node(RectTransform parent, string name)
    {
        var found = parent.Find(name) as RectTransform;
        if (found != null) return found;
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static RectTransform ImageNode(RectTransform parent, string name, Sprite sprite, Color color,
        Vector2 anchor, Vector2 pos, Vector2 size, bool raycast)
    {
        var rt = Node(parent, name);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var img = rt.GetComponent<Image>() ?? rt.gameObject.AddComponent<Image>();
        img.sprite = sprite; img.color = color; img.raycastTarget = raycast;
        return rt;
    }

    static RectTransform TextNode(RectTransform parent, string name, string text, int size, bool bold, Color color,
        Vector2 anchor, Vector2 pos, Vector2 sizeDelta)
    {
        var rt = Node(parent, name);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = sizeDelta;
        var t = rt.GetComponent<Text>() ?? rt.gameObject.AddComponent<Text>();
        t.text = text; t.fontSize = size; t.color = color;
        t.fontStyle = FontStyle.Normal; // 무게는 폰트 파일이 — 가짜 볼드 금지 (GameFonts 규칙)
        t.font = bold ? TitleFont() : HudFont();
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return rt;
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

    static Font TitleFont()
    {
        var f = GameFonts.Title;
        return f != null ? f : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    static Font HudFont()
    {
        var f = GameFonts.Hud;
        return f != null ? f : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    // 런타임 Sprite.Create 결과는 프리팹에 저장이 안 된다 — 에셋으로 옮겨 참조를 바꾼다
    static readonly Dictionary<Sprite, Sprite> spriteCache = new Dictionary<Sprite, Sprite>();

    static Sprite Persist(Sprite s, string name)
    {
        if (s == null) return null;
        if (AssetDatabase.Contains(s)) return s;
        if (spriteCache.TryGetValue(s, out var hit)) return hit;

        // 같은 이름 에셋이 이미 있으면 재사용 — 지우고 다시 만들면 GUID가 바뀌어
        // 먼저 구운 프리팹의 참조가 끊긴다. 원본 그림이 바뀌었으면 BakedSprites 폴더를 비우고 다시 굽기.
        var spritePath = $"{SpriteDir}/{name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        if (existing != null) { spriteCache[s] = existing; return existing; }

        Directory.CreateDirectory(SpriteDir);
        var tex = s.texture;
        if (!AssetDatabase.Contains(tex))
        {
            var texPath = $"{SpriteDir}/{name}_tex.asset";
            var existingTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (existingTex != null) tex = existingTex;
            else AssetDatabase.CreateAsset(tex, texPath);
        }
        var sprite = Sprite.Create(tex, s.rect, new Vector2(0.5f, 0.5f), s.pixelsPerUnit);
        sprite.name = name;
        AssetDatabase.CreateAsset(sprite, spritePath);
        spriteCache[s] = sprite;
        return sprite;
    }

    /// <summary>하위 Image 전부 훑어 저장 안 된 스프라이트를 에셋으로 — 카드처럼 코드가 만든 트리용.</summary>
    static void PersistAllSprites(GameObject root, string prefix)
    {
        int n = 0;
        foreach (var img in root.GetComponentsInChildren<Image>(true))
        {
            if (img.sprite == null || AssetDatabase.Contains(img.sprite)) continue;
            img.sprite = Persist(img.sprite, $"{prefix}_{n++}");
        }
    }

    static void SavePrefab(GameObject go, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        PrefabUtility.SaveAsPrefabAsset(go, path);
    }
}
