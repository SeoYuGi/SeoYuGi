using System;
using SeoYuGi.Battle;
using SeoYuGi.Net;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 멀티 로비 — 슬롯 6칸(팀0 좌 / 팀1 우), 조인 코드, 클래스 선택, 시작(호스트만).
/// 상태는 NetLobby가 원본 — OnChanged 구독으로 리프레시만 한다.
/// </summary>
public class UILobbyPopup : UIPopup
{
    enum Buttons { Slot1, Slot2, Slot3, Slot4, Slot5, Slot6, BtnClass, BtnCopyCode, BtnStart, BtnLeave }

    public Action OnStart; // 호스트 시작 — BattleRunner가 맵 픽으로 이어감
    public Action OnLeave;

    Text codeText, statusText;
    readonly Text[] slotLabels = new Text[6];

    public override void Init()
    {
        Bind<GameObject>(typeof(Buttons));
        for (int i = 0; i < 6; i++)
        {
            int unitId = i + 1; // Slot1~6 = unitId 1~6
            BindEvent(Get<GameObject>(i), _ => NetLobby.RequestSlot(unitId));
            slotLabels[i] = Get<GameObject>(i).GetComponentInChildren<Text>();
        }

        BindEvent(Get<GameObject>((int)Buttons.BtnClass), _ =>
        {
            var popup = UIManager.Instance.ShowPopupUI<UIClassSelectPopup>(); // 기존 팝업 재사용
            popup.OnPicked = cls => NetLobby.RequestClass(cls);
        });
        BindEvent(Get<GameObject>((int)Buttons.BtnCopyCode), _ =>
        {
            if (!string.IsNullOrEmpty(NetBoot.JoinCode))
                GUIUtility.systemCopyBuffer = NetBoot.JoinCode;
        });
        BindEvent(Get<GameObject>((int)Buttons.BtnStart), _ =>
        {
            if (!NetBoot.IsHost) return;
            UIManager.Instance.ClosePopupUI(this);
            OnStart?.Invoke();
        });
        BindEvent(Get<GameObject>((int)Buttons.BtnLeave), _ =>
        {
            UIManager.Instance.ClosePopupUI(this);
            OnLeave?.Invoke();
        });

        codeText = transform.Find("CodeText")?.GetComponent<Text>();
        statusText = transform.Find("StatusText")?.GetComponent<Text>();
        Get<GameObject>((int)Buttons.BtnStart).SetActive(NetBoot.IsHost); // 시작은 호스트 전용

        ApplySkin();
        NetLobby.OnChanged += Refresh;
        Refresh();
    }

    /// <summary>Resources/UI 아트가 있으면 입힘 — 없으면 플랫 컬러 폴백 (프리팹 재빌드 불필요).</summary>
    void ApplySkin()
    {
        var bg = Resources.Load<Texture2D>("UI/BG_Lobby");
        var dim = transform.Find("Dim")?.GetComponent<Image>();
        if (bg != null && dim != null)
        {
            dim.sprite = Sprite.Create(bg, new Rect(0, 0, bg.width, bg.height), new Vector2(0.5f, 0.5f));
            dim.color = new Color(0.75f, 0.75f, 0.75f, 1f); // 배경 위 살짝 어둡게 — 텍스트 가독
        }

        var slotTex = Resources.Load<Texture2D>("UI/Frame_LobbySlot");
        if (slotTex != null)
        {
            var slotSprite = Sprite.Create(slotTex, new Rect(0, 0, slotTex.width, slotTex.height), new Vector2(0.5f, 0.5f));
            for (int i = 0; i < 6; i++)
            {
                var img = Get<GameObject>(i).GetComponent<Image>();
                img.sprite = slotSprite;
                // 텍스처 곱연산 틴트 — 팀 색을 밝게 끌어올려야 프레임 디테일이 살아남는다
                img.color = Color.Lerp(i < 3 ? new Color(0.45f, 0.6f, 1f) : new Color(1f, 0.5f, 0.45f), Color.white, 0.45f);
            }
        }

        var btnTex = Resources.Load<Texture2D>("UI/Frame_ButtonWide");
        if (btnTex != null)
        {
            var btnSprite = Sprite.Create(btnTex, new Rect(0, 0, btnTex.width, btnTex.height), new Vector2(0.5f, 0.5f));
            foreach (var b in new[] { Buttons.BtnClass, Buttons.BtnCopyCode, Buttons.BtnStart, Buttons.BtnLeave })
            {
                var img = Get<GameObject>((int)b).GetComponent<Image>();
                img.sprite = btnSprite;
                img.color = Color.white;
            }
        }
    }

    void OnDestroy() => NetLobby.OnChanged -= Refresh;

    /// <summary>클라 대기 중 안내 (관전 동기화 전 단계 등).</summary>
    public void SetStatus(string text)
    {
        if (statusText != null) statusText.text = text;
    }

    void Refresh()
    {
        if (codeText != null)
            codeText.text = string.IsNullOrEmpty(NetBoot.JoinCode) ? "" : $"조인 코드: {NetBoot.JoinCode}";

        var slots = NetLobby.Slots;
        if (slots == null) return;
        for (int i = 0; i < slotLabels.Length && i < slots.Length; i++)
        {
            if (slotLabels[i] == null) continue;
            var s = slots[i];
            bool me = s.owner != SlotOwner.Bot &&
                      Unity.Netcode.NetworkManager.Singleton != null &&
                      s.clientId == Unity.Netcode.NetworkManager.Singleton.LocalClientId;
            string who = s.owner == SlotOwner.Bot ? "봇" : me ? "나" : "플레이어";
            slotLabels[i].text = $"{s.callsign}\n{who}\n{ClassLabel(s.cls)}";
            slotLabels[i].color = s.owner == SlotOwner.Bot ? new Color(0.6f, 0.6f, 0.6f)
                : me ? new Color(0.5f, 1f, 0.6f) : Color.white;
        }
    }

    static string ClassLabel(UnitClass cls)
    {
        switch (cls)
        {
            case UnitClass.Tank: return "너구리";
            case UnitClass.Balance: return "치즈태비";
            case UnitClass.Assassin: return "검은 고양이";
            case UnitClass.Grenadier: return "비둘기";
            case UnitClass.Sniper: return "까치";
            default: return cls.ToString();
        }
    }
}
