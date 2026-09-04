using System;
using System.Collections.Generic;
using SeoYuGi.Battle;
using UnityEngine;
using UnityEngine.UI;

/// <summary>매치 시작 전 맵 선택 팝업. 카드에 절차 생성 미니맵 — 클릭 → OnPicked(mapIndex) → 닫힘.</summary>
public class UIMapSelectPopup : UIPopup
{
    // BattleMaps 인덱스 순서 — 맵이 카드보다 적으면 남는 카드는 숨김
    enum Buttons { BtnMap0, BtnMap1, BtnMap2, BtnMap3, BtnMap4 }

    public Action<int> OnPicked;

    readonly List<Texture2D> minimaps = new List<Texture2D>();

    public override void Init()
    {
        Bind<GameObject>(typeof(Buttons));
        int cardCount = Enum.GetNames(typeof(Buttons)).Length;
        for (int i = 0; i < cardCount; i++)
        {
            var card = Get<GameObject>(i);
            if (i >= BattleMaps.Count) { card.SetActive(false); continue; }

            int idx = i;
            BindEvent(card, _ => Pick(idx));

            var map = BattleMaps.Get(idx);
            var tex = RenderMinimap(map);
            minimaps.Add(tex);
            // Name/Desc는 카드마다 중복되는 이름 — Bind 대신 카드 기준 직계 탐색
            card.transform.Find("Name").GetComponent<Text>().text = map.Name;
            card.transform.Find("Desc").GetComponent<Text>().text =
                $"{map.Width}×{map.Height} · 벽 {map.Walls.Count}개";

            var raw = card.transform.Find("MapView").GetComponent<RawImage>();
            raw.texture = tex;
            // 맵마다 크기가 달라 카드 안(242×150)에 비율 유지로 맞춤
            float s = Mathf.Min(242f / map.Width, 150f / map.Height);
            raw.rectTransform.sizeDelta = new Vector2(map.Width * s, map.Height * s);
        }
    }

    void Pick(int mapIndex)
    {
        UIManager.Instance.ClosePopupUI(this);
        OnPicked?.Invoke(mapIndex);
    }

    void OnDestroy()
    {
        foreach (var tex in minimaps) Destroy(tex);
    }

    /// <summary>맵 데이터를 1칸=1px 텍스처로 렌더 — 별도 아트 없이 미리보기.</summary>
    static Texture2D RenderMinimap(ParsedMap map)
    {
        var tex = new Texture2D(map.Width, map.Height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;

        var floor = new Color(0.20f, 0.22f, 0.28f);
        var wall  = new Color(0.58f, 0.62f, 0.70f);
        var high  = new Color(0.82f, 0.56f, 0.34f); // 고지대
        var zone  = new Color(0.12f, 0.48f, 0.55f);
        var team0 = new Color(0.30f, 0.70f, 0.78f);
        var team1 = new Color(1.00f, 0.41f, 0.27f);

        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                tex.SetPixel(x, y, floor);
        foreach (var c in map.Walls) tex.SetPixel(c.x, c.y, wall);
        foreach (var c in map.Highlands) tex.SetPixel(c.x, c.y, high);
        foreach (var c in map.Voids) tex.SetPixel(c.x, c.y, Color.clear); // 구덩이 — 카드 배경이 비침
        foreach (var cells in map.Zones)
            foreach (var c in cells) tex.SetPixel(c.x, c.y, zone);
        foreach (var kv in map.Spawns)
            tex.SetPixel(kv.Value.x, kv.Value.y, kv.Key <= 3 ? team0 : team1);
        tex.Apply();
        return tex;
    }
}
