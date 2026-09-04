using System;
using SeoYuGi.Battle;
using UnityEngine;

/// <summary>매치 시작 전 클래스 선택 팝업. 카드 클릭 → OnPicked 콜백 → 닫힘.</summary>
public class UIClassSelectPopup : UIPopup
{
    // UnitClass enum과 같은 순서 — 인덱스 캐스팅으로 매핑
    enum Buttons { BtnTank, BtnBalance, BtnAssassin, BtnGrenadier, BtnSniper }

    public Action<UnitClass> OnPicked;

    public override void Init()
    {
        Bind<GameObject>(typeof(Buttons));
        for (int i = 0; i < 5; i++)
        {
            var cls = (UnitClass)i;
            BindEvent(Get<GameObject>(i), _ => Pick(cls));
        }
    }

    void Pick(UnitClass cls)
    {
        UIManager.Instance.ClosePopupUI(this);
        OnPicked?.Invoke(cls);
    }
}
