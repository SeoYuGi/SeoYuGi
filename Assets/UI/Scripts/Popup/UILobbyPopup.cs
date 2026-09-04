using System;
using SeoYuGi.Battle;
using SeoYuGi.Net;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 멀티 로비 — 슬롯 6칸(팀0 윗줄 / 팀1 아랫줄) + 중앙 캐릭터 그리드(철권식 상시 표시).
/// 슬롯은 고정(클릭 이동 없음) — 접속 순 자동 배정. 카드 클릭 = 클래스 선택.
/// 선택은 NetLobby가 브로드캐스트 — 봇 포함 모든 슬롯에 캐릭터 카드가 그려진다.
/// 상태는 NetLobby가 원본 — OnChanged 구독으로 리프레시만 한다.
/// </summary>
public class UILobbyPopup : UIPopup
{
    enum Buttons { Pick1, Pick2, Pick3, Pick4, Pick5, QC1, QC2, QC3, QC4, QC5, QC6, BtnCopyCode, BtnStart, BtnLeave }

    public Action OnStart; // 호스트 시작 — BattleRunner가 맵 픽으로 이어감
    public Action OnLeave;

    // UnitClass enum 순서와 동일 — Resources/UI의 클래스별 카드 아트
    static readonly string[] CardArt =
        { "Card_Tank", "Card_Balance", "Card_Assassin", "Card_Grenadier", "Card_Sniper" };

    // 역할 콜 빠른채팅 — 왕자영요식 "내가 ~할게요" (탱/서폿/딜 느낌)
    static readonly string[] QuickLines =
    {
        "내가 탱커 할게요! (너구리)",
        "내가 서포터 할게요! (치즈태비)",
        "내가 암살자 할게요! (검은 고양이)",
        "내가 폭격수 할게요! (비둘기)",
        "내가 저격수 할게요! (까치)",
        "밸런스 맞춰 주세요!",
    };

    Text codeText, statusText, chatLogText, balanceText;
    InputField chatInput;
    readonly Text[] slotLabels = new Text[6];
    readonly Image[] slotPortraits = new Image[6];
    readonly Image[] pickBackings = new Image[5];
    readonly System.Collections.Generic.List<string> chatLog = new System.Collections.Generic.List<string>();
    static readonly Sprite[] cardSprites = new Sprite[5]; // 세션 캐시

    public override void Init()
    {
        Bind<GameObject>(typeof(Buttons));

        // 캐릭터 그리드 — 클릭 = 선택. 슬롯 클릭 이동은 없음 (칸 고정).
        for (int i = 0; i < 5; i++)
        {
            var cls = (UnitClass)i;
            var pick = Get<GameObject>(i);
            BindEvent(pick, _ => NetLobby.RequestClass(cls));
            pickBackings[i] = pick.GetComponent<Image>();
            var portrait = pick.transform.Find("Portrait")?.GetComponent<Image>();
            var sprite = CardSprite(i);
            if (portrait != null && sprite != null)
            {
                portrait.sprite = sprite;
                portrait.color = Color.white;
            }
        }

        for (int i = 0; i < 6; i++)
        {
            var slot = transform.Find($"Slot{i + 1}");
            if (slot == null) continue;
            slotLabels[i] = slot.Find("LabelBack/Label")?.GetComponent<Text>();
            slotPortraits[i] = slot.Find("Portrait")?.GetComponent<Image>();
        }

        // 팀 채팅 — 역할 콜 버튼 + 자유 입력 (Enter 전송)
        for (int i = 0; i < QuickLines.Length; i++)
        {
            string line = QuickLines[i];
            var qc = Get<GameObject>((int)Buttons.QC1 + i);
            if (qc == null) continue;
            BindEvent(qc, _ => NetLobby.SendChat(line));
            var label = qc.GetComponentInChildren<Text>();
            if (label != null) label.text = line;
        }
        chatLogText = transform.Find("ChatLog")?.GetComponent<Text>();
        balanceText = transform.Find("BalanceText")?.GetComponent<Text>();
        chatInput = transform.Find("ChatInput")?.GetComponent<InputField>();
        NetLobby.OnChat += AddChat;

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
        BindEvent(Get<GameObject>((int)Buttons.BtnLeave), _ => Leave());
        OnEscape = Leave; // ESC = 나가기 (뒤로)

        codeText = transform.Find("CodeText")?.GetComponent<Text>();
        statusText = transform.Find("StatusText")?.GetComponent<Text>();
        Get<GameObject>((int)Buttons.BtnStart).SetActive(NetBoot.IsHost); // 시작은 호스트 전용

        ApplySkin();
        NetLobby.OnChanged += Refresh;
        Refresh();
    }

    static Sprite CardSprite(int cls)
    {
        if (cardSprites[cls] == null)
        {
            var tex = Resources.Load<Texture2D>("UI/" + CardArt[cls]);
            if (tex != null)
                cardSprites[cls] = Sprite.Create(tex,
                    new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }
        return cardSprites[cls];
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

        var slotSprite = UISkin.SlotFrame();
        if (slotSprite != null)
            for (int i = 0; i < 6; i++)
            {
                var slot = transform.Find($"Slot{i + 1}");
                var img = slot != null ? slot.GetComponent<Image>() : null;
                if (img == null) continue;
                img.sprite = slotSprite;
                // 텍스처 곱연산 틴트 — 팀 색을 밝게 끌어올려야 프레임 디테일이 살아남는다
                img.color = Color.Lerp(i < 3 ? new Color(0.45f, 0.6f, 1f) : new Color(1f, 0.5f, 0.45f), Color.white, 0.45f);
            }

        var btnSprite = UISkin.ButtonPlate();
        if (btnSprite != null)
            foreach (var b in new[] { Buttons.BtnCopyCode, Buttons.BtnStart, Buttons.BtnLeave })
            {
                var img = Get<GameObject>((int)b).GetComponent<Image>();
                img.sprite = btnSprite;
                img.color = Color.white;
            }
    }

    bool chatWasFocused; // 엔터 시 InputField가 같은 프레임에 포커스를 잃어도 전송되게 직전 상태 기억

    /// <summary>채팅 전송 — onEndEdit는 이벤트 순서에 따라 엔터 감지를 놓쳐서 폴링으로.
    /// UIPopup.Update(ESC)와 겹치지 않게 LateUpdate 사용.</summary>
    void LateUpdate()
    {
        if (chatInput == null) return;
        bool focused = chatInput.isFocused;
        var kb = UnityEngine.InputSystem.Keyboard.current;
        bool enter = kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
        if (enter && (focused || chatWasFocused) && !string.IsNullOrWhiteSpace(chatInput.text))
        {
            NetLobby.SendChat(chatInput.text);
            chatInput.text = "";
            chatInput.ActivateInputField(); // 연속 입력 — 포커스 유지
        }
        chatWasFocused = focused;
    }

    void OnDestroy()
    {
        NetLobby.OnChanged -= Refresh;
        NetLobby.OnChat -= AddChat;
    }

    void AddChat(string callsign, string text)
    {
        chatLog.Add($"{callsign}: {text}");
        if (chatLog.Count > 12) chatLog.RemoveAt(0);
        if (chatLogText != null) chatLogText.text = string.Join("\n", chatLog);
    }

    void Leave()
    {
        UIManager.Instance.ClosePopupUI(this);
        OnLeave?.Invoke();
    }

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

        // 1차: 내 슬롯 찾기 — 픽 하이라이트 + 상대팀 가리기 기준
        int myCls = -1, myTeam = -1;
        var localId = Unity.Netcode.NetworkManager.Singleton != null
            ? Unity.Netcode.NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
        foreach (var s in slots)
            if (s.owner != SlotOwner.Bot && s.clientId == localId)
            {
                myCls = (int)s.cls;
                myTeam = s.team;
                break;
            }

        for (int i = 0; i < slotLabels.Length && i < slots.Length; i++)
        {
            var s = slots[i];
            bool me = s.owner != SlotOwner.Bot && s.clientId == localId;

            if (slotLabels[i] != null)
            {
                string who = s.owner == SlotOwner.Bot ? "봇" : me ? "나" : "플레이어";
                slotLabels[i].text = $"{s.callsign} · {who}";
                slotLabels[i].color = s.owner == SlotOwner.Bot ? new Color(0.6f, 0.6f, 0.6f)
                    : me ? new Color(0.5f, 1f, 0.6f) : Color.white;
            }

            // 선택 캐릭터 카드 — 우리 팀만 공개(봇 포함), 상대팀은 시작 전까지 비공개.
            // 봇 클래스는 호스트가 로비에서 롤식 밸런스로 배정 — 살짝 어둡게 구분.
            if (slotPortraits[i] != null)
            {
                bool hidden = myTeam >= 0 && s.team != myTeam;
                var sprite = hidden ? null : CardSprite((int)s.cls);
                slotPortraits[i].sprite = sprite;
                slotPortraits[i].color = sprite == null ? new Color(1f, 1f, 1f, 0f)
                    : s.owner == SlotOwner.Bot ? new Color(0.7f, 0.7f, 0.7f) : Color.white;
            }
        }

        // 철권식 커서 — 내 픽만 밝은 테두리 + 확대
        for (int i = 0; i < pickBackings.Length; i++)
        {
            if (pickBackings[i] == null) continue;
            bool sel = i == myCls;
            pickBackings[i].color = sel ? new Color(0.35f, 1f, 0.75f) : new Color(0.12f, 0.13f, 0.17f, 0.95f);
            pickBackings[i].transform.localScale = sel ? Vector3.one * 1.08f : Vector3.one;
        }

        RefreshBalance(slots, myTeam);
    }

    /// <summary>내 팀 구성 경고 — 탱/서폿 빠짐·중복 픽 표시 (왕자영요식).
    /// 봇이 빈 역할을 자동으로 메우므로 보통은 인간끼리 겹칠 때만 뜬다.</summary>
    void RefreshBalance(NetLobby.LobbySlot[] slots, int myTeam)
    {
        if (balanceText == null) return;
        if (myTeam < 0) { balanceText.text = ""; return; }

        bool hasTank = false, hasSupport = false;
        var counts = new int[5];
        foreach (var s in slots)
        {
            if (s.team != myTeam) continue;
            counts[(int)s.cls]++;
            if (s.cls == UnitClass.Tank) hasTank = true;
            if (s.cls == UnitClass.Balance) hasSupport = true;
        }

        var warns = new System.Collections.Generic.List<string>();
        if (!hasTank) warns.Add("탱커가 없습니다");
        if (!hasSupport) warns.Add("서포터가 없습니다");
        for (int c = 0; c < counts.Length; c++)
            if (counts[c] > 1) warns.Add($"{ClassName((UnitClass)c)} 중복 픽");

        balanceText.text = warns.Count == 0 ? "" : "⚠ 팀 밸런스 부족\n" + string.Join("\n", warns);
    }

    static string ClassName(UnitClass cls)
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
