using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using SeoYuGi.Battle;

namespace SeoYuGi.Net
{
    /// <summary>
    /// 로비 — 호스트 권위 슬롯 상태를 커스텀 메시지로 동기화.
    /// NetworkObject/프리팹 등록이 전혀 필요 없는 CustomMessagingManager 기반 (씬 배선 0).
    ///
    /// 흐름: 접속 → 호스트가 빈 슬롯 자동 배정 → 클라는 슬롯 클릭(이동)·클래스 선택 요청
    ///      → 호스트만 시작 가능 → MatchSetup 브로드캐스트.
    /// 빈 슬롯 = Bot — 봇 백필은 여기서 공짜로 나온다 (1v1~3v3 자동).
    /// </summary>
    public static class NetLobby
    {
        const string MsgLobby = "sy_lobby"; // host→all : 슬롯 전체 상태
        const string MsgSlot = "sy_slot";   // client→host : 슬롯 이동 요청 (unitId)
        const string MsgClass = "sy_class"; // client→host : 클래스 변경 요청
        const string MsgStart = "sy_start"; // host→all : 매치 시작 (MatchSetup)
        const string MsgChatReq = "sy_chatq"; // client→host : 로비 채팅 요청 (텍스트)
        const string MsgChatBrd = "sy_chatb"; // host→client : 로비 채팅 배달 (콜사인+텍스트, 같은 팀만)
        const string MsgName = "sy_name";   // client→host : 닉네임 설정 요청 (2026-09-05)
        const string MsgBotClass = "sy_bcls"; // client→host : 내 팀 봇 클래스 직접 지정 (2026-09-06 3픽 로비)
        const string MsgReady = "sy_ready";   // client→host : 준비 토글 (2026-09-06 1:1 고정 — 상대가 준비해야 호스트가 시작)

        // 슬롯 템플릿 — BattleRunner.roster와 동일한 6칸 (id, team, 콜사인)
        static readonly (int id, int team, string name)[] Template =
        {
            (1, 0, "알파"), (2, 0, "브라보"), (3, 0, "찰리"),
            (4, 1, "델타"), (5, 1, "에코"), (6, 1, "폭스"),
        };

        public struct LobbySlot
        {
            public int unitId;
            public int team;
            public string callsign;
            public UnitClass cls;
            public SlotOwner owner;
            public ulong clientId; // RemoteHuman/LocalHuman(호스트 자신)일 때
            public bool manual;    // 봇 클래스를 사람이 직접 지정했다 — 자동 밸런스가 안 건드린다
            public bool ready;     // 사람 슬롯의 준비 상태 — 호스트는 시작 버튼이 곧 준비라 클라만 의미 있다 (2026-09-06)
        }

        /// <summary>그 팀에 사람이 있나.</summary>
        static bool HumanOn(int team)
        {
            if (Slots == null) return false;
            foreach (var s in Slots) if (s.team == team && s.owner != SlotOwner.Bot) return true;
            return false;
        }

        /// <summary>1:1 고정 (2026-09-06): 호스트 = 파랑(0), 합류자 = 빨강(1). 상대가 들어왔나.</summary>
        public static bool OpponentJoined => HumanOn(1);

        /// <summary>빨강 팀 사람이 준비를 눌렀나 — 호스트 시작 조건.</summary>
        public static bool OpponentReady
        {
            get
            {
                if (Slots == null) return false;
                foreach (var s in Slots) if (s.team == 1 && s.owner == SlotOwner.RemoteHuman) return s.ready;
                return false;
            }
        }

        public static bool CanStart => OpponentReady;

        /// <summary>내(로컬) 슬롯의 준비 상태 — 클라 버튼 라벨용.</summary>
        public static bool MyReady
        {
            get
            {
                var nm = NetworkManager.Singleton;
                if (Slots == null || nm == null) return false;
                foreach (var s in Slots) if (s.owner != SlotOwner.Bot && s.clientId == nm.LocalClientId) return s.ready;
                return false;
            }
        }

        public static LobbySlot[] Slots { get; private set; }
        /// <summary>지휘관 대전 토글 — 호스트가 로비에서 정하고 슬롯 상태와 함께 브로드캐스트.</summary>
        public static bool Commander { get; private set; }
        /// <summary>클라 수신 매치 구성 — 시작 메시지 도착 시 채워짐.</summary>
        public static MatchSetup ReceivedSetup { get; private set; }

        public static event Action OnChanged;    // 슬롯 상태 갱신 — UI 리프레시용
        public static event Action OnMatchStart; // 시작 브로드캐스트 수신
        public static event Action<int[]> OnHumansLeftGame; // 호스트: 이탈 인간의 unitId들 — 인게임 봇 승계용
        public static event Action OnHostDisconnected;      // 클라: 호스트가 방을 파괴/이탈 — 타이틀 복귀용
        public static event Action<string, string> OnChat; // 로비 팀 채팅 수신 — (콜사인, 텍스트)

        static bool hooked;

        /// <summary>호스트/클라 공통 — 접속 직후 1회 호출. 핸들러 등록 + (호스트) 초기 슬롯 구성.</summary>
        public static void Begin()
        {
            var nm = NetworkManager.Singleton;
            ReceivedSetup = null;

            if (nm.IsHost)
            {
                Commander = false;
                Slots = new LobbySlot[Template.Length];
                for (int i = 0; i < Template.Length; i++)
                    Slots[i] = new LobbySlot
                    {
                        unitId = Template[i].id,
                        team = Template[i].team,
                        callsign = Template[i].name,
                        cls = UnitClass.Balance,
                        owner = SlotOwner.Bot
                    };
                Occupy(FindFree(0), nm.LocalClientId, SlotOwner.LocalHuman); // 호스트 = 팀0 첫 빈칸
                AssignBotClasses();
            }

            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgLobby, OnLobbyMsg);
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgSlot, OnSlotMsg);
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgName, OnNameMsg);
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgClass, OnClassMsg);
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgBotClass, OnBotClassMsg);
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgReady, OnReadyMsg);
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgStart, OnStartMsg);
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgChatReq, OnChatReqMsg);
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgChatBrd, OnChatBrdMsg);
            NetSync.Register(); // 인게임 동기화 핸들러도 같이

            if (!hooked)
            {
                hooked = true;
                nm.OnClientConnectedCallback += OnClientConnected;
                nm.OnClientDisconnectCallback += OnClientDisconnected;
            }

            Broadcast();
            OnChanged?.Invoke();
        }

        // ── 클라 → 호스트 요청 ─────────────────────────────

        /// <summary>빈 슬롯 클릭 — 그 자리로 이동 (팀 변경 포함). 호스트는 즉시 처리.</summary>
        public static void RequestSlot(int unitId)
        {
            var nm = NetworkManager.Singleton;
            if (nm.IsHost) { MoveTo(nm.LocalClientId, unitId); return; }
            using var w = new FastBufferWriter(8, Allocator.Temp);
            w.WriteValueSafe(unitId);
            nm.CustomMessagingManager.SendNamedMessage(MsgSlot, NetworkManager.ServerClientId, w);
        }

        /// <summary>닉네임 설정 — 내 슬롯 콜사인을 바꾼다. 슬롯 브로드캐스트에 실려 전원에게 동기화되고,
        /// HostStart의 SlotConfig로도 흘러 킬피드·콜아웃에 그대로 찍힌다.</summary>
        public static void RequestName(string name)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name)) return;
            if (name.Length > 6) name = name.Substring(0, 6); // 6자 제한 (2026-09-05)
            var nm = NetworkManager.Singleton;
            if (nm.IsHost) { SetName(nm.LocalClientId, name); return; }
            using var w = new FastBufferWriter(128, Allocator.Temp);
            w.WriteValueSafe(name);
            nm.CustomMessagingManager.SendNamedMessage(MsgName, NetworkManager.ServerClientId, w);
        }

        public static void RequestClass(UnitClass cls)
        {
            var nm = NetworkManager.Singleton;
            if (nm.IsHost) { SetClass(nm.LocalClientId, cls); return; }
            using var w = new FastBufferWriter(8, Allocator.Temp);
            w.WriteValueSafe((int)cls);
            nm.CustomMessagingManager.SendNamedMessage(MsgClass, NetworkManager.ServerClientId, w);
        }

        /// <summary>준비 토글 — 클라가 누른다. 호스트는 시작 버튼이 준비를 대신한다.</summary>
        public static void RequestReady(bool on)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.CustomMessagingManager == null) return;
            if (nm.IsHost) { SetReady(nm.LocalClientId, on); return; }
            using var w = new FastBufferWriter(8, Allocator.Temp);
            w.WriteValueSafe((byte)(on ? 1 : 0));
            nm.CustomMessagingManager.SendNamedMessage(MsgReady, NetworkManager.ServerClientId, w);
        }

        static void SetReady(ulong clientId, bool on)
        {
            for (int i = 0; i < Slots.Length; i++)
                if (Slots[i].owner != SlotOwner.Bot && Slots[i].clientId == clientId) Slots[i].ready = on;
            Broadcast();
            OnChanged?.Invoke();
        }

        static void OnReadyMsg(ulong sender, FastBufferReader r)
        {
            if (!NetworkManager.Singleton.IsHost) return;
            r.ReadValueSafe(out byte on);
            SetReady(sender, on != 0);
        }

        /// <summary>내 팀 봇 클래스 직접 지정 — 3픽 로비(나 → 팀원1 → 팀원2). 호스트가 팀 검증 후 적용.</summary>
        public static void RequestBotClass(int unitId, UnitClass cls)
        {
            var nm = NetworkManager.Singleton;
            if (nm.IsHost) { SetBotClass(nm.LocalClientId, unitId, cls); return; }
            using var w = new FastBufferWriter(16, Allocator.Temp);
            w.WriteValueSafe(unitId);
            w.WriteValueSafe((int)cls);
            nm.CustomMessagingManager.SendNamedMessage(MsgBotClass, NetworkManager.ServerClientId, w);
        }

        static void SetBotClass(ulong clientId, int unitId, UnitClass cls)
        {
            int team = -1;
            foreach (var s in Slots)
                if (s.owner != SlotOwner.Bot && s.clientId == clientId) { team = s.team; break; }
            int idx = IndexOf(unitId);
            if (team < 0 || idx < 0 || Slots[idx].owner != SlotOwner.Bot || Slots[idx].team != team) return; // 내 팀 봇만
            Slots[idx].cls = cls;
            Slots[idx].manual = true;
            Slots[idx].callsign = ClassNames.Nick(team, cls);
            Broadcast();
            OnChanged?.Invoke();
        }

        /// <summary>로비 팀 채팅 — 호스트가 같은 팀에게만 배달 (왕자영요식 역할 콜).</summary>
        public static void SendChat(string text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            if (text.Length > 80) text = text.Substring(0, 80);

            var nm = NetworkManager.Singleton;
            if (nm.IsHost) { RelayChat(nm.LocalClientId, text); return; }
            using var w = new FastBufferWriter(512, Allocator.Temp);
            w.WriteValueSafe(text);
            nm.CustomMessagingManager.SendNamedMessage(MsgChatReq, NetworkManager.ServerClientId, w);
        }

        /// <summary>호스트 — 발신자 팀을 찾아 같은 팀 인간에게만 배달. 호스트 자신도 이벤트로.</summary>
        static void RelayChat(ulong senderClientId, string text)
        {
            var nm = NetworkManager.Singleton;
            int team = -1;
            string callsign = null;
            foreach (var s in Slots)
                if (s.owner != SlotOwner.Bot && s.clientId == senderClientId)
                {
                    team = s.team;
                    callsign = s.callsign;
                    break;
                }
            if (team < 0) return; // 슬롯 없는 발신자 무시

            foreach (var s in Slots)
            {
                if (s.owner == SlotOwner.Bot || s.team != team) continue;
                if (s.clientId == nm.LocalClientId) OnChat?.Invoke(callsign, text);
                else
                {
                    using var w = new FastBufferWriter(512, Allocator.Temp);
                    w.WriteValueSafe(callsign);
                    w.WriteValueSafe(text);
                    nm.CustomMessagingManager.SendNamedMessage(MsgChatBrd, s.clientId, w);
                }
            }
        }

        /// <summary>호스트 전용 — 지휘관 대전 켜기/끄기. 전원에게 슬롯 상태와 함께 전파.</summary>
        public static void HostSetCommander(bool on)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsHost) return;
            Commander = on;
            Broadcast();
            OnChanged?.Invoke();
        }

        /// <summary>호스트 전용 — 매치 시작. mapIndex·시드는 호스트가 확정.
        /// 봇 클래스는 로비에서 이미 배정·공개된 그대로 간다.</summary>
        public static void HostStart(int mapIndex, int enemyRollSeed)
        {
            var nm = NetworkManager.Singleton;
            if (!nm.IsHost) return;
            if (!CanStart) { Debug.LogWarning("NetLobby.HostStart: 상대가 준비되지 않았다 — 시작 거부"); return; } // 봇전 폴백 없음 (2026-09-06)

            var slots = new SlotConfig[Slots.Length];
            for (int i = 0; i < Slots.Length; i++)
                slots[i] = new SlotConfig
                {
                    unitId = Slots[i].unitId,
                    team = Slots[i].team,
                    cls = Slots[i].cls,
                    callsign = Slots[i].owner == SlotOwner.Bot ? ClassNames.Nick(Slots[i].team, Slots[i].cls) : Slots[i].callsign, // 봇 = 클래스 별명
                    owner = Slots[i].owner,
                    ownerClientId = Slots[i].clientId
                };
            var setup = new MatchSetup { mapIndex = mapIndex, enemyRollSeed = enemyRollSeed, commander = Commander, slots = slots };

            using var w = new FastBufferWriter(1024, Allocator.Temp);
            w.WriteValueSafe(mapIndex);
            w.WriteValueSafe(enemyRollSeed);
            w.WriteValueSafe((byte)(Commander ? 1 : 0));
            w.WriteValueSafe(slots.Length);
            foreach (var s in slots)
            {
                w.WriteValueSafe(s.unitId);
                w.WriteValueSafe(s.team);
                w.WriteValueSafe((int)s.cls);
                w.WriteValueSafe((byte)s.owner);
                w.WriteValueSafe(s.ownerClientId);
                w.WriteValueSafe(s.callsign);
            }
            nm.CustomMessagingManager.SendNamedMessageToAll(MsgStart, w);

            ReceivedSetup = setup; // 호스트 자신도 같은 경로
            OnMatchStart?.Invoke();
        }

        /// <summary>
        /// 봇 클래스 상시 배정 — 롤식 역할 채우기(탱→돌격→딜), 팀 내 중복 없음.
        /// 인간 픽이 바뀔 때마다 재계산 — 봇이 빈 역할로 갈아탄다 (로비에 사전 공개).
        /// 결정적 선택 — 같은 상황이면 같은 결과라 카드가 안 튄다.
        /// </summary>
        static void AssignBotClasses()
        {
            // 역할: 탱 = 너구리 / 돌격 = 고라니 / 딜 = 나머지 (고라니는 서포터가 아니다 — 밸런스·돌격형)
            var dealerOrder = new[] { UnitClass.Assassin, UnitClass.Grenadier, UnitClass.Sniper };

            for (int team = 0; team < 2; team++)
            {
                var pool = new System.Collections.Generic.List<UnitClass>
                    { UnitClass.Tank, UnitClass.Balance, UnitClass.Assassin, UnitClass.Grenadier, UnitClass.Sniper };
                bool hasTank = false, hasRusher = false;
                for (int i = 0; i < Slots.Length; i++)
                    if (Slots[i].team == team && (Slots[i].owner != SlotOwner.Bot || Slots[i].manual)) // 사람 픽 + 직접 지정한 봇은 고정
                    {
                        pool.Remove(Slots[i].cls);
                        if (Slots[i].cls == UnitClass.Tank) hasTank = true;
                        if (Slots[i].cls == UnitClass.Balance) hasRusher = true;
                    }

                for (int i = 0; i < Slots.Length; i++)
                {
                    if (Slots[i].team != team || Slots[i].owner != SlotOwner.Bot || Slots[i].manual) continue;

                    UnitClass want;
                    if (!hasTank && pool.Contains(UnitClass.Tank)) { want = UnitClass.Tank; hasTank = true; }
                    else if (!hasRusher && pool.Contains(UnitClass.Balance)) { want = UnitClass.Balance; hasRusher = true; }
                    else
                    {
                        want = pool[0];
                        foreach (var d in dealerOrder)
                            if (pool.Contains(d)) { want = d; break; }
                    }
                    Slots[i].cls = want;
                    Slots[i].callsign = ClassNames.Nick(team, want); // 로비 카드에도 별명으로
                    pool.Remove(want);
                }
            }
        }

        // ── 호스트 내부 처리 ─────────────────────────────

        static void OnClientConnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (!nm.IsHost || clientId == nm.LocalClientId) return;

            // 1:1 고정 (2026-09-06): 호스트 = 파랑(0), 합류자 = 빨강(1). 빨강에 이미 사람이 있으면 만석.
            if (HumanOn(1)) { nm.DisconnectClient(clientId); return; }
            int idx = FindFree(1);
            if (idx < 0) { nm.DisconnectClient(clientId); return; }

            Occupy(idx, clientId, SlotOwner.RemoteHuman);
            AssignBotClasses();
            Broadcast();
            OnChanged?.Invoke();
        }

        static void OnClientDisconnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || Slots == null) return;

            // 클라 시점: 이 콜백은 "내가 서버에서 끊겼다"는 뜻 — 호스트가 방을 파괴했거나 접속이 죽었다
            if (!nm.IsHost)
            {
                OnHostDisconnected?.Invoke();
                return;
            }

            var left = new System.Collections.Generic.List<int>();
            for (int i = 0; i < Slots.Length; i++)
                if (Slots[i].owner == SlotOwner.RemoteHuman && Slots[i].clientId == clientId)
                {
                    Slots[i].owner = SlotOwner.Bot; // 이탈 → 봇 승격, 게임 안 깨짐
                    Slots[i].clientId = 0;
                    Slots[i].ready = false;
                    left.Add(Slots[i].unitId);
                }
            AssignBotClasses();
            Broadcast();
            if (left.Count > 0) OnHumansLeftGame?.Invoke(left.ToArray()); // 인게임이면 러너가 봇 드라이버 승계
            OnChanged?.Invoke();
        }

        static void MoveTo(ulong clientId, int unitId)
        {
            int dst = IndexOf(unitId);
            if (dst < 0 || Slots[dst].owner != SlotOwner.Bot) return; // 점유된 자리 불가

            for (int i = 0; i < Slots.Length; i++)
                if (Slots[i].owner != SlotOwner.Bot && Slots[i].clientId == clientId)
                {
                    var owner = Slots[i].owner;
                    var cls = Slots[i].cls;
                    var callsign = Slots[i].callsign;
                    Slots[i].owner = SlotOwner.Bot;
                    Slots[i].clientId = 0;
                    Slots[i].callsign = ClassNames.Nick(Slots[i].team, Slots[i].cls); // 빈 슬롯 = 봇 별명 (AssignBotClasses가 곧 다시 맞춘다)
                    Slots[dst].owner = owner;
                    Slots[dst].clientId = clientId;
                    Slots[dst].cls = cls;
                    Slots[dst].callsign = callsign; // 닉네임은 사람을 따라간다 (2026-09-05)
                    break;
                }
            for (int i = 0; i < Slots.Length; i++) if (Slots[i].owner == SlotOwner.Bot) Slots[i].manual = false; // 직접 지정 해제 — 고른 사람이 팀을 옮겼다
            AssignBotClasses(); // 팀 변경 — 떠난 팀·새 팀 양쪽 봇이 역할을 다시 맞춘다 (2026-09-05 1:1 지휘 모드)
            Broadcast();
            OnChanged?.Invoke();
        }

        /// <summary>호스트 — 해당 클라 슬롯의 콜사인 교체.</summary>
        static void SetName(ulong clientId, string name)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name)) return;
            if (name.Length > 6) name = name.Substring(0, 6); // 6자 제한 (2026-09-05)
            for (int i = 0; i < Slots.Length; i++)
                if (Slots[i].owner != SlotOwner.Bot && Slots[i].clientId == clientId)
                {
                    Slots[i].callsign = name;
                    break;
                }
            Broadcast();
            OnChanged?.Invoke();
        }

        static void SetClass(ulong clientId, UnitClass cls)
        {
            for (int i = 0; i < Slots.Length; i++)
                if (Slots[i].owner != SlotOwner.Bot && Slots[i].clientId == clientId)
                    Slots[i].cls = cls;
            AssignBotClasses(); // 인간 픽 변경 → 봇이 빈 역할로 갈아탐
            Broadcast();
            OnChanged?.Invoke();
        }

        static int FindFree(int team)
        {
            for (int i = 0; i < Slots.Length; i++)
                if (Slots[i].team == team && Slots[i].owner == SlotOwner.Bot) return i;
            return -1;
        }

        static int IndexOf(int unitId)
        {
            for (int i = 0; i < Slots.Length; i++)
                if (Slots[i].unitId == unitId) return i;
            return -1;
        }

        static void Occupy(int idx, ulong clientId, SlotOwner owner)
        {
            if (idx < 0) return;
            Slots[idx].owner = owner;
            Slots[idx].clientId = clientId;
        }

        // ── 직렬화 ─────────────────────────────

        static void Broadcast()
        {
            var nm = NetworkManager.Singleton;
            // 셧다운 중 disconnect 콜백이 오면 메시징 매니저가 이미 없다 — 조용히 스킵
            if (nm == null || !nm.IsHost || nm.CustomMessagingManager == null) return;

            using var w = new FastBufferWriter(1024, Allocator.Temp);
            w.WriteValueSafe(Slots.Length);
            foreach (var s in Slots)
            {
                w.WriteValueSafe(s.unitId);
                w.WriteValueSafe(s.team);
                w.WriteValueSafe(s.callsign);
                w.WriteValueSafe((int)s.cls);
                w.WriteValueSafe((byte)s.owner);
                w.WriteValueSafe(s.clientId);
                w.WriteValueSafe((byte)(s.manual ? 1 : 0));
                w.WriteValueSafe((byte)(s.ready ? 1 : 0));
            }
            w.WriteValueSafe((byte)(Commander ? 1 : 0));
            nm.CustomMessagingManager.SendNamedMessageToAll(MsgLobby, w);
        }

        static void OnLobbyMsg(ulong sender, FastBufferReader r)
        {
            if (NetworkManager.Singleton.IsHost) return; // 호스트 상태가 원본

            r.ReadValueSafe(out int count);
            var slots = new LobbySlot[count];
            for (int i = 0; i < count; i++)
            {
                r.ReadValueSafe(out slots[i].unitId);
                r.ReadValueSafe(out slots[i].team);
                r.ReadValueSafe(out slots[i].callsign);
                r.ReadValueSafe(out int cls);
                slots[i].cls = (UnitClass)cls;
                r.ReadValueSafe(out byte owner);
                slots[i].owner = (SlotOwner)owner;
                r.ReadValueSafe(out slots[i].clientId);
                r.ReadValueSafe(out byte manual);
                slots[i].manual = manual != 0;
                r.ReadValueSafe(out byte ready);
                slots[i].ready = ready != 0;
            }
            r.ReadValueSafe(out byte commander);
            Commander = commander != 0;
            Slots = slots;
            OnChanged?.Invoke();
        }

        static void OnSlotMsg(ulong sender, FastBufferReader r)
        {
            if (!NetworkManager.Singleton.IsHost) return;
            r.ReadValueSafe(out int unitId);
            MoveTo(sender, unitId);
        }

        static void OnNameMsg(ulong sender, FastBufferReader r)
        {
            if (!NetworkManager.Singleton.IsHost) return;
            r.ReadValueSafe(out string name);
            SetName(sender, name);
        }

        static void OnClassMsg(ulong sender, FastBufferReader r)
        {
            if (!NetworkManager.Singleton.IsHost) return;
            r.ReadValueSafe(out int cls);
            SetClass(sender, (UnitClass)cls);
        }

        static void OnBotClassMsg(ulong sender, FastBufferReader r)
        {
            if (!NetworkManager.Singleton.IsHost) return;
            r.ReadValueSafe(out int unitId);
            r.ReadValueSafe(out int cls);
            SetBotClass(sender, unitId, (UnitClass)cls);
        }

        static void OnChatReqMsg(ulong sender, FastBufferReader r)
        {
            if (!NetworkManager.Singleton.IsHost) return;
            r.ReadValueSafe(out string text);
            RelayChat(sender, text);
        }

        static void OnChatBrdMsg(ulong sender, FastBufferReader r)
        {
            if (sender != NetworkManager.ServerClientId) return; // 호스트 배달만 신뢰
            r.ReadValueSafe(out string callsign);
            r.ReadValueSafe(out string text);
            OnChat?.Invoke(callsign, text);
        }

        static void OnStartMsg(ulong sender, FastBufferReader r)
        {
            if (NetworkManager.Singleton.IsHost) return;
            if (sender != NetworkManager.ServerClientId) return; // 호스트만 시작 가능

            r.ReadValueSafe(out int mapIndex);
            r.ReadValueSafe(out int seed);
            r.ReadValueSafe(out byte commander);
            r.ReadValueSafe(out int count);
            var slots = new SlotConfig[count];
            for (int i = 0; i < count; i++)
            {
                r.ReadValueSafe(out slots[i].unitId);
                r.ReadValueSafe(out slots[i].team);
                r.ReadValueSafe(out int cls);
                slots[i].cls = (UnitClass)cls;
                r.ReadValueSafe(out byte owner);
                slots[i].owner = (SlotOwner)owner;
                r.ReadValueSafe(out slots[i].ownerClientId);
                r.ReadValueSafe(out slots[i].callsign);
            }
            ReceivedSetup = new MatchSetup { mapIndex = mapIndex, enemyRollSeed = seed, commander = commander != 0, slots = slots };
            OnMatchStart?.Invoke();
        }

        /// <summary>내 클라이언트가 점유한 슬롯의 unitId. 없으면 -1.
        /// 시작 페이로드(ReceivedSetup)가 정본 — 로비 Slots 미러는 브로드캐스트가 늦으면
        /// (팀 변경·재시작 직후) 한 박자 뒤라, 미러만 보면 -1이 터졌다 (2026-09-05 예외 스택).</summary>
        public static int MyUnitId()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return -1;
            if (ReceivedSetup != null && ReceivedSetup.slots != null)
                foreach (var s in ReceivedSetup.slots)
                    if (s.owner == SlotOwner.RemoteHuman && s.ownerClientId == nm.LocalClientId) return s.unitId;
            if (Slots != null)
                foreach (var s in Slots)
                    if (s.owner != SlotOwner.Bot && s.clientId == nm.LocalClientId) return s.unitId;
            return -1;
        }
    }
}
