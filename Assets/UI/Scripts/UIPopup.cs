using UnityEngine;
using UnityEngine.InputSystem;

public class UIPopup : UIBase
{
    /// <summary>ESC 뒤로가기 — 스택 최상단 팝업만 반응. null이면 ESC 무시 (루트 팝업용).</summary>
    public System.Action OnEscape;

    public override void Init()
    {
    }

    void Update()
    {
        if (OnEscape == null) return;
        if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;
        if (UIManager.Instance == null || !UIManager.Instance.IsTopPopup(this)) return;
        OnEscape();
    }
}
