using System;
using SeoYuGi.Battle;
using SeoYuGi.BattleView; // GameFonts
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

    // 캐릭터 선택 팝업과 동일한 이름·역할·네온 색 (enum 순서)
    static readonly (string name, string roleEn, Color color)[] Meta =
    {
        ("너구리",      "TANKER",    new Color(1f, 0.54f, 0.16f)),
        ("고라니",      "BRUISER",   new Color(0.64f, 0.42f, 1f)),
        ("검은 고양이", "ASSASSIN",  new Color(0.21f, 0.84f, 1f)),
        ("비둘기",      "SUPPORT",   new Color(0.29f, 0.87f, 0.37f)),
        ("까치",        "MARKSMAN",  new Color(0.23f, 0.51f, 0.96f)),
    };

    // 역할 콜 빠른채팅 — 왕자영요식 "내가 ~할게요" (탱/돌격/딜 느낌)
    static readonly string[] QuickLines =
    {
        "내가 탱커 할게요! (너구리)",
        "내가 브루저 할게요! (고라니)",
        "내가 암살자 할게요! (검은 고양이)",
        "내가 폭격수 할게요! (비둘기)",
        "내가 저격수 할게요! (까치)",
        "밸런스 맞춰 주세요!",
    };

    Text codeText, statusText, chatLogText, balanceText;
    InputField chatInput;
    readonly Text[] slotLabels = new Text[6];
    readonly Image[] slotPortraits = new Image[6];
    readonly RectTransform[] slotRoots = new RectTransform[6]; // 팀별 표시/숨김 + 상단 재배치용
    // 분대 슬롯 치수 — 프리팹 190x250은 1배 카드(윗단 ~113)와 겹쳤다 → 150x198, 줄을 살짝 올림 (2026-09-06 "슬롯 좀 더 작게").
    // 캐릭터 선택 팝업(UIClassSelectPopup)이 같은 값으로 런타임 슬롯을 만든다.
    public const float SlotW = 150f, SlotH = 198f, SlotY = 262f, SlotGap = 175f;
    public const int SlotLabelFont = 15;
    // 상단 줄 위치 3칸 — 내 팀은 항상 여기로, 상대팀 줄은 통째로 숨김 (2026-09-05)
    static readonly Vector2[] TopSlotPos = { new Vector2(-SlotGap, SlotY), new Vector2(0f, SlotY), new Vector2(SlotGap, SlotY) };
    readonly Image[] pickBackings = new Image[5];
    readonly System.Collections.Generic.List<string> chatLog = new System.Collections.Generic.List<string>();
    static readonly Sprite[] cardSprites = new Sprite[5]; // 세션 캐시

    public override void Init()
    {
        Bind<GameObject>(typeof(Buttons));

        // 캐릭터 그리드 — 클릭 = 선택. 슬롯 클릭 이동은 없음 (칸 고정).
        // 캐릭터 선택 팝업과 동일한 네온 카드 스타일(테두리·이름·역할)로 통일.
        for (int i = 0; i < 5; i++)
        {
            var cls = (UnitClass)i;
            var pick = Get<GameObject>(i);
            BindEvent(pick, _ => PickCard(cls)); // 3픽 — 편집 중인 칸(나 → 팀원1 → 팀원2)에 배정
            pickBackings[i] = pick.GetComponent<Image>();
            var portrait = pick.transform.Find("Portrait")?.GetComponent<Image>();
            var sprite = CardSprite(i);
            if (portrait != null && sprite != null)
            {
                portrait.sprite = sprite;
                portrait.color = Color.white;
                portrait.preserveAspect = false;
            }
            // 캐릭터 선택 팝업과 같은 1배 카드(280x450) — 카드가 짧아져 축소 없이 들어간다 (2026-09-06).
            // 위로는 슬롯 줄(하단 120), 아래로는 상태 문구(-420) 사이. 선택 확대(1.06)까지 안 겹침.
            var rt = (RectTransform)pick.transform;
            SeoYuGi.UI.ClassCard.Build(rt, i, 1f);
            rt.anchoredPosition = new Vector2((i - 2) * (SeoYuGi.UI.ClassCard.BaseW + 22f), -125f);
        }

        for (int i = 0; i < 6; i++)
        {
            var slot = transform.Find($"Slot{i + 1}");
            if (slot == null) continue;
            slotRoots[i] = (RectTransform)slot;
            slotRoots[i].sizeDelta = new Vector2(SlotW, SlotH);
            slotLabels[i] = slot.Find("LabelBack/Label")?.GetComponent<Text>();
            if (slotLabels[i] != null) slotLabels[i].fontSize = SlotLabelFont; // 좁아진 칸에 "너굴 / 팀원(자동)"이 들어가게
            slotPortraits[i] = slot.Find("Portrait")?.GetComponent<Image>();
            int slotIdx = i;
            BindEvent(slot.gameObject, _ => SelectSlot(slotIdx)); // 내 팀 슬롯 클릭 = 그 칸 다시 고르기
        }

        // 팀 채팅·조인 코드 폐지 (2026-09-06) — 아군은 봇이라 말할 상대가 없고, 매칭은 자동이라 코드도 없다.
        // 프리팹 요소는 살려두고 끈다 (프리팹 재빌드 없이).
        foreach (var n in new[] { "ChatBack", "ChatLog", "ChatInput", "QC1", "QC2", "QC3", "QC4", "QC5", "QC6", "CodeText" })
        {
            var t = transform.Find(n);
            if (t != null) t.gameObject.SetActive(false);
        }
        Get<GameObject>((int)Buttons.BtnCopyCode)?.SetActive(false);
        balanceText = transform.Find("BalanceText")?.GetComponent<Text>();
        if (balanceText != null)
        {
            // 프리팹 위치(우측 중앙 x650)는 1배 카드와 겹친다 — 슬롯 줄 왼쪽, 상대 칸(우측 x500)과 대칭 자리로 (2026-09-06)
            var brt = balanceText.rectTransform;
            brt.anchoredPosition = new Vector2(-620f, 245f);
            brt.sizeDelta = new Vector2(300f, 200f);
        }
        // 시작/준비 (2026-09-06 1:1 고정): 호스트 = 상대가 준비했을 때만 시작, 클라 = 준비 토글
        BindEvent(Get<GameObject>((int)Buttons.BtnStart), _ =>
        {
            if (NetBoot.IsHost)
            {
                if (!NetLobby.CanStart)
                {
                    if (statusText != null)
                        statusText.text = NetLobby.OpponentJoined ? "상대가 준비를 누르면 시작할 수 있습니다" : "상대 지휘관을 기다리는 중입니다";
                    return;
                }
                UIManager.Instance.ClosePopupUI(this);
                OnStart?.Invoke();
            }
            else NetLobby.RequestReady(!NetLobby.MyReady);
        });
        BindEvent(Get<GameObject>((int)Buttons.BtnLeave), _ => Leave());
        OnEscape = Leave; // ESC = 나가기 (뒤로)

        // 팀 변경 버튼 폐지 (2026-09-06) — 호스트 = 파랑, 합류자 = 빨강 고정
        CreateCommanderToggle(); // 지휘관 대전 — 유저1+봇2 vs 유저1+봇2, 각자 자기 봇을 무전 지휘 (2026-09-05)

        codeText = transform.Find("CodeText")?.GetComponent<Text>();
        statusText = transform.Find("StatusText")?.GetComponent<Text>();
        CreateOpponentBox(); // 우측 빨간 빈 칸 — 상대가 들어오면 "매칭됨", 준비하면 "준비 완료" (2026-09-06)

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

    static readonly Sprite[] machineSprites = new Sprite[5];

    /// <summary>기계팀 전용 회색 아트 (Card_*_M) — 없으면 원본 폴백.</summary>
    static Sprite MachineCardSprite(int cls)
    {
        if (machineSprites[cls] == null)
        {
            var tex = Resources.Load<Texture2D>("UI/" + CardArt[cls] + "_M");
            if (tex != null)
                machineSprites[cls] = Sprite.Create(tex,
                    new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }
        return machineSprites[cls] != null ? machineSprites[cls] : CardSprite(cls);
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
                img.preserveAspect = false; // 키잉 후 비율이 바뀌어 프레임이 납작해지며 글씨가 삐져나왔다 (2026-09-05)
                img.rectTransform.sizeDelta = BottomButtonSize; // 프리팹 60은 너무 얇다 (2026-09-06 "세로로 뚱뚱하게")
                var bl = Get<GameObject>((int)b).GetComponentInChildren<Text>();
                if (bl != null) { bl.fontSize = BottomButtonFont; bl.alignment = TextAnchor.MiddleCenter; } // 프레임 금속 밴드 안에 여유 있게
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

    bool nickSynced; // 슬롯 배정 전 전송 유실 대비 — Refresh에서 내 슬롯 확인 후 1회 재전송

    // 닉네임 입력은 메인화면(UITitlePopup)으로 이전 (2026-09-05) — 저장된 닉네임의
    // 자동 적용(Refresh의 nickSynced 1회 전송)은 그대로 이 팝업이 맡는다.


    /// <summary>팀 변경 버튼 — BtnLeave를 복제해 스킨·크기를 그대로 물려받는다 (프리팹 수정 없이 런타임 생성).
    /// 상대팀 첫 빈 봇 슬롯으로 이동을 요청한다. 상대팀이 인간으로 가득이면 안내만.</summary>
    Image oppBox; Text oppText; // 상대 지휘관 칸 (2026-09-06)

    /// <summary>내 팀 슬롯 줄 오른쪽에 상대 칸 하나. 상대 조합은 비공개라 존재와 준비 상태만 보인다.</summary>
    void CreateOpponentBox()
    {
        var parent = slotRoots[0] != null ? slotRoots[0].parent : transform;
        var go = new GameObject("OpponentBox", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        if (slotRoots[0] != null) { rt.anchorMin = slotRoots[0].anchorMin; rt.anchorMax = slotRoots[0].anchorMax; rt.pivot = slotRoots[0].pivot; }
        rt.sizeDelta = new Vector2(190f, 110f);
        rt.anchoredPosition = new Vector2(500f, 245f);
        oppBox = go.GetComponent<Image>();
        oppBox.color = new Color(0.35f, 0.08f, 0.1f, 0.85f);
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 0.35f, 0.35f);
        outline.effectDistance = new Vector2(2f, -2f);

        var tgo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        tgo.transform.SetParent(go.transform, false);
        var trt = (RectTransform)tgo.transform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        oppText = tgo.GetComponent<Text>();
        oppText.alignment = TextAnchor.MiddleCenter;
        oppText.fontSize = 20;
        oppText.color = Color.white;
        var font = GameFonts.Hud;
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        oppText.font = font;
        oppText.text = "?";
        RefreshOpponentBox();
    }

    /// <summary>러너가 세션 연결 직후 부른다 — 로비가 연결보다 먼저 열리므로 OnChanged를 기다리지 않고 채운다.</summary>
    public void RefreshNow() => Refresh();

    void RefreshOpponentBox()
    {
        if (oppText == null) return;
        var startLabelEarly = Get<GameObject>((int)Buttons.BtnStart)?.GetComponentInChildren<Text>();
        if (NetLobby.Slots == null) // 아직 서버 연결 중 — 방을 파는 중이거나 파진 방을 찾는 중
        {
            oppText.text = "서버 연결 중";
            oppBox.color = new Color(0.2f, 0.2f, 0.25f, 0.85f);
            if (startLabelEarly != null) startLabelEarly.text = "연결 중";
            return;
        }
        bool joined = NetLobby.OpponentJoined || !NetBoot.IsHost; // 클라 입장에선 호스트가 곧 상대
        bool ready = NetBoot.IsHost ? NetLobby.OpponentReady : true;
        if (!joined)
        {
            oppText.text = "?\n상대 지휘관 대기";
            oppBox.color = new Color(0.35f, 0.08f, 0.1f, 0.85f);
        }
        else if (NetBoot.IsHost)
        {
            oppText.text = ready ? "매칭됨\n준비 완료" : "매칭됨\n준비 중";
            oppBox.color = ready ? new Color(0.15f, 0.45f, 0.2f, 0.9f) : new Color(0.6f, 0.15f, 0.15f, 0.9f);
        }
        else
        {
            oppText.text = "매칭됨\n호스트가 시작합니다";
            oppBox.color = new Color(0.6f, 0.15f, 0.15f, 0.9f);
        }
        var startLabel = Get<GameObject>((int)Buttons.BtnStart)?.GetComponentInChildren<Text>();
        if (startLabel != null)
            startLabel.text = NetBoot.IsHost ? (NetLobby.CanStart ? "시작" : "상대 대기") : (NetLobby.MyReady ? "준비 취소" : "준비");
    }

    Text commanderLabel;

    /// <summary>분대 슬롯 선택 표시 — 슬롯 뒤에 틸 판을 6px 크게 깔아 밝은 테두리 + 1.08배.
    /// Outline은 프레임 그림을 복제하는 거라 검은 여백만 복제돼 안 보였다 (2026-09-06 "밝은 테두리 안 생기는데").
    /// 판은 슬롯의 형제(앞 순서)라 프레임 뒤에 깔린다 — 자식이면 프레임 위를 덮는다. 캐릭터 선택 팝업도 같은 함수.</summary>
    public static void SetSlotSelected(RectTransform root, bool on)
    {
        root.localScale = on ? Vector3.one * 1.08f : Vector3.one;
        string haloName = "SelHalo_" + root.GetInstanceID(); // 이름이 아니라 인스턴스 — 픽창 슬롯은 셋 다 "Img"라 하나를 공유해 마지막 칸만 남았다 (2026-09-06)
        var halo = root.parent.Find(haloName) as RectTransform;
        if (!on)
        {
            if (halo != null) halo.gameObject.SetActive(false);
            return;
        }
        if (halo == null)
        {
            var go = new GameObject(haloName, typeof(RectTransform), typeof(Image));
            halo = (RectTransform)go.transform;
            halo.SetParent(root.parent, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.45f, 1f, 0.95f, 0.95f); // 조준 슬롯·편집 라벨과 같은 틸
            img.raycastTarget = false;
            // 글로우는 2px 네 방향, 옅게 — 5px는 계단이 보여 픽셀티가 났다 (2026-09-06)
            var glow = new Color(0.45f, 1f, 0.95f, 0.3f);
            foreach (var d in new[] { new Vector2(2f, 2f), new Vector2(-2f, -2f), new Vector2(2f, -2f), new Vector2(-2f, 2f) })
            {
                var sh = go.AddComponent<Shadow>();
                sh.effectColor = glow; sh.effectDistance = d; sh.useGraphicAlpha = false;
            }
        }
        // 슬롯 바로 앞 순서 = 슬롯 뒤에 그려진다. SetSiblingIndex는 뺀 뒤 끼우는 거라 뒤에서 앞으로 올 땐 한 번 더 (2026-09-06)
        halo.SetSiblingIndex(root.GetSiblingIndex());
        if (halo.GetSiblingIndex() > root.GetSiblingIndex()) halo.SetSiblingIndex(root.GetSiblingIndex());
        halo.anchorMin = root.anchorMin; halo.anchorMax = root.anchorMax; halo.pivot = root.pivot;
        halo.anchoredPosition = root.anchoredPosition;
        halo.sizeDelta = root.sizeDelta + new Vector2(6f, 6f); // 3px 테두리 — 6px는 너무 두꺼웠다
        halo.localScale = root.localScale;
        halo.gameObject.SetActive(true);
    }

    /// <summary>하단 버튼 줄 공통 치수 — 캐릭터 선택 팝업(UIClassSelectPopup)의 출격 버튼도 같은 값 (2026-09-06).</summary>
    public static readonly Vector2 BottomButtonSize = new Vector2(220f, 78f);
    public const int BottomButtonFont = 20;

    /// <summary>지휘관 대전 토글 — 호스트만 바꾸고, 상태는 NetLobby.Commander로 전원 동기화.
    /// 하단 버튼 줄 왼쪽 칸(폐지된 코드 복사 자리). 나가기 위에 쌓으면 1배 카드 밑단과 겹친다 (2026-09-06).</summary>
    void CreateCommanderToggle()
    {
        var template = Get<GameObject>((int)Buttons.BtnLeave);
        var go = Instantiate(template, template.transform.parent);
        go.name = "BtnCommander";
        var rt = go.GetComponent<RectTransform>();
        var src = template.GetComponent<RectTransform>();
        rt.anchoredPosition = src.anchoredPosition + new Vector2(-480f, 0f);
        rt.sizeDelta = BottomButtonSize;
        commanderLabel = go.GetComponentInChildren<Text>();
        // 복제 시점이 ApplySkin보다 앞이라 민짜로 남는다 — 버튼 판 스킨·글자 크기 직접 적용 (2026-09-06)
        var plate = UISkin.ButtonPlate();
        var img2 = go.GetComponent<Image>();
        if (plate != null && img2 != null) { img2.sprite = plate; img2.color = Color.white; img2.preserveAspect = false; }
        foreach (var extra in go.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
            if (extra.gameObject != go && !(extra is Text)) extra.enabled = false;
        if (commanderLabel != null) { commanderLabel.fontSize = BottomButtonFont; commanderLabel.alignment = TextAnchor.MiddleCenter; }
        BindEvent(go, _ =>
        {
            if (!NetBoot.IsHost) { if (statusText != null) statusText.text = "모드는 호스트가 정합니다"; return; }
            NetLobby.HostSetCommander(!NetLobby.Commander);
        });
        RefreshCommanderLabel();
    }

    void RefreshCommanderLabel()
    {
        if (commanderLabel != null)
            commanderLabel.text = NetLobby.Commander ? "지휘관 대전: ON" : "지휘관 대전: OFF";
    }

    /// <summary>클라 대기 중 안내 (관전 동기화 전 단계 등).</summary>
    public void SetStatus(string text)
    {
        if (statusText != null) statusText.text = text;
    }

    // ── 3픽: 나 → 팀원1 → 팀원2. 카드 클릭이 편집 칸에 들어가고 다음 칸으로 넘어간다. 슬롯 클릭으로 되돌아가 다시 고른다. ──
    int editIdx;                 // 0 = 나, 1·2 = 내 팀 봇 (슬롯 순)
    readonly System.Collections.Generic.List<int> myOrder = new System.Collections.Generic.List<int>(); // 내 팀 슬롯 인덱스, 나 먼저

    void PickCard(UnitClass cls)
    {
        if (myOrder.Count == 0) return;
        if (editIdx == 0) NetLobby.RequestClass(cls);
        else NetLobby.RequestBotClass(NetLobby.Slots[myOrder[editIdx]].unitId, cls);
        if (editIdx < myOrder.Count - 1) editIdx++;
        Refresh();
    }

    void SelectSlot(int slotIdx)
    {
        int k = myOrder.IndexOf(slotIdx);
        if (k < 0) return; // 상대팀(숨김)·미배정
        editIdx = k;
        Refresh();
    }

    void Refresh()
    {
        RefreshCommanderLabel();
        RefreshOpponentBox();

        var slots = NetLobby.Slots;
        if (slots == null) return;

        // 1차: 내 슬롯 찾기 — 픽 하이라이트 + 상대팀 가리기 기준
        int myCls = -1, myTeam = -1;
        var localId = Unity.Netcode.NetworkManager.Singleton != null
            ? Unity.Netcode.NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
        foreach (var s in slots)
            if (s.owner != SlotOwner.Bot && s.clientId == localId)
            {
                // 저장된 닉네임 자동 적용 — 슬롯 배정이 늦는 클라도 배정 확인 후 1회 전송 (2026-09-05)
                if (!nickSynced)
                {
                    nickSynced = true;
                    string savedNick = PlayerPrefs.GetString("sy_nickname", "");
                    if (!string.IsNullOrEmpty(savedNick) && s.callsign != savedNick)
                        NetLobby.RequestName(savedNick);
                }
                myCls = (int)s.cls;
                myTeam = s.team;
                break;
            }

        // 내 팀 순서 — 나 먼저, 그다음 봇(슬롯 순). 3픽의 편집 순서이자 슬롯 클릭의 역참조.
        myOrder.Clear();
        for (int i = 0; i < slots.Length; i++)
            if (slots[i].team == myTeam && slots[i].owner != SlotOwner.Bot && slots[i].clientId == localId) myOrder.Add(i);
        for (int i = 0; i < slots.Length; i++)
            if (slots[i].team == myTeam && slots[i].owner == SlotOwner.Bot) myOrder.Add(i);
        if (editIdx >= myOrder.Count) editIdx = 0;
        int editSlot = myOrder.Count > 0 ? myOrder[editIdx] : -1;
        if (editSlot >= 0) myCls = (int)slots[editSlot].cls; // 카드 커서 = 지금 고르는 칸의 클래스
        if (statusText != null && myTeam >= 0)
            statusText.text = editIdx == 0 ? "1/3  내 캐릭터를 고르세요"
                : $"{editIdx + 1}/3  팀원 {editIdx} 캐릭터를 고르세요 (슬롯 클릭 = 다시 고르기)";

        // 상대팀 줄은 통째로 숨기고(조합 비공개 + 픽 카드 공간 확보), 내 팀은 항상 상단 줄에 (2026-09-05)
        int topIdx = 0;
        for (int i = 0; i < slotLabels.Length && i < slots.Length; i++)
        {
            var s = slots[i];
            bool me = s.owner != SlotOwner.Bot && s.clientId == localId;

            bool editing = i == editSlot;
            if (slotRoots[i] != null)
            {
                bool enemyRow = myTeam >= 0 && s.team != myTeam;
                slotRoots[i].gameObject.SetActive(!enemyRow);
                if (!enemyRow && topIdx < TopSlotPos.Length)
                    slotRoots[i].anchoredPosition = TopSlotPos[topIdx++];
                SetSlotSelected(slotRoots[i], editing); // 지금 고르는 칸 — 밝은 테두리 + 살짝 크게
            }

            if (slotLabels[i] != null)
            {
                string who = s.owner == SlotOwner.Bot ? (s.manual ? "팀원" : "팀원(자동)") : me ? "나" : "플레이어";
                slotLabels[i].text = $"{s.callsign} / {who}";
                slotLabels[i].color = editing ? new Color(0.45f, 1f, 0.95f)
                    : s.owner == SlotOwner.Bot ? new Color(0.75f, 0.75f, 0.75f)
                    : me ? new Color(0.5f, 1f, 0.6f) : Color.white;
            }

            // 선택 캐릭터 카드 — 우리 팀만 공개(봇 포함), 상대팀은 시작 전까지 비공개.
            // 봇 클래스는 호스트가 로비에서 롤식 밸런스로 배정 — 살짝 어둡게 구분.
            if (slotPortraits[i] != null)
            {
                bool hidden = myTeam >= 0 && s.team != myTeam;
                // 기계팀(팀1)은 전용 회색 아트 — 로비에서부터 "로봇 편"이 그림으로 읽힌다 (2026-09-05)
                var sprite = hidden ? null : (s.team == 1 ? MachineCardSprite((int)s.cls) : CardSprite((int)s.cls));
                slotPortraits[i].sprite = sprite;
                slotPortraits[i].color = sprite == null ? new Color(1f, 1f, 1f, 0f)
                    : s.owner == SlotOwner.Bot ? new Color(0.7f, 0.7f, 0.7f) : Color.white;
            }
        }

        // 철권식 커서 — 내 픽 카드에 선택 하이라이트 (캐릭터 선택 팝업과 동일)
        for (int i = 0; i < pickBackings.Length; i++)
        {
            if (pickBackings[i] == null) continue;
            SeoYuGi.UI.ClassCard.SetSelected((RectTransform)pickBackings[i].transform, i == myCls, Meta[i].color);
        }

        RefreshBalance(slots, myTeam);
    }

    /// <summary>내 팀 구성 경고 — 탱/돌격형 빠짐·중복 픽 표시 (왕자영요식).
    /// 봇이 빈 역할을 자동으로 메우므로 보통은 인간끼리 겹칠 때만 뜬다.</summary>
    void RefreshBalance(NetLobby.LobbySlot[] slots, int myTeam)
    {
        if (balanceText == null) return;
        if (myTeam < 0) { balanceText.text = ""; return; }

        bool hasTank = false, hasRanged = false;
        var counts = new int[5];
        foreach (var s in slots)
        {
            if (s.team != myTeam) continue;
            counts[(int)s.cls]++;
            if (s.cls == UnitClass.Tank) hasTank = true;
            if (s.cls == UnitClass.Grenadier || s.cls == UnitClass.Sniper) hasRanged = true;
        }

        var warns = new System.Collections.Generic.List<string>();
        if (!hasTank) warns.Add("탱커가 없습니다");
        if (!hasRanged) warns.Add("원거리가 없습니다");
        for (int c = 0; c < counts.Length; c++)
            if (counts[c] > 1) warns.Add($"{ClassName((UnitClass)c)} 중복 픽");

        balanceText.text = warns.Count == 0 ? "" : "팀 밸런스 부족\n" + string.Join("\n", warns);
    }

    static string ClassName(UnitClass cls)
    {
        switch (cls)
        {
            case UnitClass.Tank: return "너구리";
            case UnitClass.Balance: return "고라니";
            case UnitClass.Assassin: return "검은 고양이";
            case UnitClass.Grenadier: return "비둘기";
            case UnitClass.Sniper: return "까치";
            default: return cls.ToString();
        }
    }
}
