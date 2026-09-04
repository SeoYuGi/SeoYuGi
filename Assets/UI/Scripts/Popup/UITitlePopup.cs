using System;
using UnityEngine;

/// <summary>시작 화면 — 싱글 / 방 만들기 / 코드 참가.</summary>
public class UITitlePopup : UIPopup
{
    enum Buttons { BtnSingle, BtnHost, BtnJoin }

    public Action OnSingle;
    public Action OnHost;
    public Action OnJoin;

    public override void Init()
    {
        Bind<GameObject>(typeof(Buttons));
        BindEvent(Get<GameObject>((int)Buttons.BtnSingle), _ => Pick(OnSingle));
        BindEvent(Get<GameObject>((int)Buttons.BtnHost), _ => Pick(OnHost));
        BindEvent(Get<GameObject>((int)Buttons.BtnJoin), _ => Pick(OnJoin));

        // 타이틀 배경은 러너의 ShowPickBackground(BG_Title)가 깔아준다 — 여기선 딤만 걷어냄
        var dim = transform.Find("Dim")?.GetComponent<UnityEngine.UI.Image>();
        if (dim != null) dim.color = new Color(0f, 0f, 0f, 0.35f);

        var sprite = UISkin.ButtonPlate();
        if (sprite != null)
            for (int i = 0; i < 3; i++)
            {
                var img = Get<GameObject>(i).GetComponent<UnityEngine.UI.Image>();
                img.sprite = sprite;
                img.color = Color.white;
            }
    }

    void Pick(Action cb)
    {
        UIManager.Instance.ClosePopupUI(this);
        cb?.Invoke();
    }
}
