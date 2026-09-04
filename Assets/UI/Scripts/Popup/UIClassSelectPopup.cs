using System;
using SeoYuGi.Battle;
using UnityEngine;

/// <summary>매치 시작 전 클래스 선택 팝업. 카드 클릭 → OnPicked 콜백 → 닫힘.</summary>
public class UIClassSelectPopup : UIPopup
{
    // UnitClass enum과 같은 순서 — 인덱스 캐스팅으로 매핑
    enum Buttons { BtnTank, BtnBalance, BtnAssassin, BtnGrenadier, BtnSniper }

    public Action<UnitClass> OnPicked;

    // UnitClass enum 순서와 동일 — Resources/UI의 클래스별 초상
    static readonly string[] Portraits =
        { "Portrait_Tank", "Portrait_Balance", "Portrait_Assassin", "Portrait_Grenadier", "Portrait_Sniper" };

    public override void Init()
    {
        Bind<GameObject>(typeof(Buttons));
        for (int i = 0; i < 5; i++)
        {
            var cls = (UnitClass)i;
            BindEvent(Get<GameObject>(i), _ => Pick(cls));
            ApplyPortrait(Get<GameObject>(i), Portraits[i]);
        }
    }

    /// <summary>카드 이미지에 클래스별 초상 주입 — 리소스 없으면 프리팹 기본 유지.</summary>
    static void ApplyPortrait(GameObject card, string textureName)
    {
        var tex = Resources.Load<Texture2D>("UI/" + textureName);
        if (tex == null) return;
        var img = card.GetComponentInChildren<UnityEngine.UI.Image>();
        if (img == null) return;
        img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        img.color = Color.white;
        img.preserveAspect = false; // 카드 꽉 채움
    }

    void Pick(UnitClass cls)
    {
        UIManager.Instance.ClosePopupUI(this);
        OnPicked?.Invoke(cls);
    }
}
