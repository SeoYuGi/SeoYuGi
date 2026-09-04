using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>조인 코드 입력 — 붙여넣기 버튼 우선 (코드는 어차피 채팅으로 복붙된다).</summary>
public class UIJoinCodePopup : UIPopup
{
    enum Buttons { BtnJoin, BtnPaste, BtnBack }

    public Action<string> OnJoin;
    public Action OnBack;

    InputField codeInput;

    public override void Init()
    {
        Bind<GameObject>(typeof(Buttons));
        codeInput = GetComponentInChildren<InputField>();

        BindEvent(Get<GameObject>((int)Buttons.BtnJoin), _ => Submit());
        BindEvent(Get<GameObject>((int)Buttons.BtnPaste), _ =>
        {
            if (codeInput != null) codeInput.text = GUIUtility.systemCopyBuffer?.Trim() ?? "";
        });
        BindEvent(Get<GameObject>((int)Buttons.BtnBack), _ => Back());
        OnEscape = Back; // ESC = 뒤로
    }

    void Back()
    {
        UIManager.Instance.ClosePopupUI(this);
        OnBack?.Invoke();
    }

    void Submit()
    {
        string code = codeInput != null ? codeInput.text.Trim() : "";
        if (string.IsNullOrEmpty(code)) return;
        UIManager.Instance.ClosePopupUI(this);
        OnJoin?.Invoke(code);
    }
}
