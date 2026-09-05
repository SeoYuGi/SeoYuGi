using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>시작 화면 — 매칭 시작(오버워치/롤식). 방 만들기·코드 참가는 폐기.</summary>
public class UITitlePopup : UIPopup
{
    enum Buttons { BtnSingle, BtnHost, BtnJoin }

    public Action OnMatch; // 매칭 시작 (봇 채움 · 실사람은 인프라 연결 시 확장)

    Text searchText;

    public override void Init()
    {
        Bind<GameObject>(typeof(Buttons));
        // 매칭 시작 = 기존 첫 버튼 재활용, 나머지 2개(방/참가)는 폐기
        BindEvent(Get<GameObject>((int)Buttons.BtnSingle), _ => Pick());
        var startLabel = Get<GameObject>((int)Buttons.BtnSingle).GetComponentInChildren<Text>();
        if (startLabel != null) startLabel.text = "매칭 시작";
        Get<GameObject>((int)Buttons.BtnHost).SetActive(false);
        Get<GameObject>((int)Buttons.BtnJoin).SetActive(false);

        var dim = transform.Find("Dim")?.GetComponent<Image>();
        if (dim != null) dim.color = new Color(0f, 0f, 0f, 0.35f);

        var sprite = UISkin.ButtonPlate();
        if (sprite != null)
        {
            var img = Get<GameObject>((int)Buttons.BtnSingle).GetComponent<Image>();
            img.sprite = sprite; img.color = Color.white;
        }
    }

    /// <summary>매칭 연출 — 버튼 숨기고 '상대를 찾는 중' 표시. 러너가 몇 초 뒤 닫고 게임 진입.</summary>
    public void ShowSearching()
    {
        Get<GameObject>((int)Buttons.BtnSingle).SetActive(false);
        if (searchText == null)
        {
            var go = new GameObject("Searching", typeof(RectTransform), typeof(Text));
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -40f);
            rt.sizeDelta = new Vector2(700f, 60f);
            searchText = go.GetComponent<Text>();
            searchText.font = SeoYuGi.BattleView.GameFonts.Title != null
                ? SeoYuGi.BattleView.GameFonts.Title
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            searchText.fontSize = 26;
            searchText.alignment = TextAnchor.MiddleCenter;
            searchText.color = new Color(0.6f, 0.9f, 1f);
            searchText.horizontalOverflow = HorizontalWrapMode.Overflow;
        }
        searchText.gameObject.SetActive(true);
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
}
