using System;
using System.Collections.Generic;
using SeoYuGi.Battle;
using SeoYuGi.Chat;
using SeoYuGi.Integration;
using SeoYuGi.Net;
using SeoYuGi.Prediction;
using UnityEngine;
using UnityEngine.InputSystem;
using PredCell = SeoYuGi.Prediction.Cell; // Battle.Cell과 이름 충돌 — 반드시 alias로 구분

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 전투 진입점 + 매치 오케스트레이션 (기획서 §05: 3라운드 2선승).
    /// 맵은 BattleMaps 고정 6장 중 매치 시작 시 랜덤 — 지형 자체는 수제 고정(유저·AI 모두 지형 학습).
    /// 라운드마다 Core(BattleState/시스템들)를 통째로 새로 조립하고,
    /// Predictor만 매치 내내 살아남아 라운드를 거치며 인간을 학습한다.
    /// 흐름: Playing → (라운드 종료) → Briefing(SPACE) → 다음 라운드 → ... → MatchOver(R).
    /// </summary>
    public class BattleRunner : MonoBehaviour
    {
        enum Phase { ClassSelect, Playing, Briefing, MatchOver }

        // 슬롯 로스터 — 탱고파이브식 콜사인. 내 슬롯은 선택 팝업으로, 적팀은 매치당 랜덤으로 덮어씀.
        readonly (int id, int team, UnitClass cls, string name)[] roster =
        {
            (1, 0, UnitClass.Tank,    "알파"),
            (2, 0, UnitClass.Balance, "브라보"),
            (3, 0, UnitClass.Sniper,  "찰리"),
            (4, 1, UnitClass.Tank,    "델타"),
            (5, 1, UnitClass.Balance, "에코"),
            (6, 1, UnitClass.Sniper,  "폭스"),
        };
        static readonly string[] ZoneLetters = { "A", "B", "C" };

        [Header("Config")]
        [SerializeField] MoveConfig moveConfig = new MoveConfig();
        [SerializeField] CombatConfig combatConfig = new CombatConfig();
        [SerializeField] RoundConfig roundConfig = new RoundConfig();
        [SerializeField] PickupConfig pickupConfig = new PickupConfig();

        [Header("Map — BattleMaps 고정 6장 중 랜덤")]
        [SerializeField] int mapIndex = 0;

        [Header("Camera (자동 프레이밍)")]
        [SerializeField] float cameraPitch = 55f;
        [SerializeField] float cameraDistanceScale = 0.95f;

        [Header("슬롯 — 내 조작은 1기, 나머지는 AI (기획서 §04)")]
        [SerializeField] int playerUnitId = 2; // 브라보 (밸런스)
        [SerializeField] Color[] teamColors = { new Color(0.25f, 0.5f, 1f), new Color(1f, 0.3f, 0.25f) };

        [Tooltip("비우면 큐브 유닛 자동 생성")]
        [SerializeField] UnitView unitPrefab;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // 같은 GameObject에서 자동 연결 — 인스펙터 배선 불필요
        GridView gridView;
        UnitMoveInput input;
        UnitViewRegistry viewRegistry;

        public MoveSystem Move { get; private set; }
        public CombatSystem Combat { get; private set; }
        public RoundSystem Round { get; private set; }
        public BattleState Battle { get; private set; }
        public MatchSystem Match { get; private set; }
        public PickupSystem Pickup { get; private set; }
        public int PlayerUnitId => playerUnitId;
        /// <summary>현재 매치의 파싱된 맵 — 맵 꾸미기(MapDresser) 등 외부 연출용.</summary>
        public ParsedMap CurrentMap => map;

        CoreWorldView worldView;
        BattleHud hud;
        BattleAudio battleAudio;
        VisionSystem vision;
        readonly HashSet<int> audioVisibleEnemies = new HashSet<int>(); // 발견/소실 SFX용
        Predictor predictor;
        HackSystem hackSystem; // 해킹 궁게이지 — 매치당 1개, 라운드 넘겨 유지 (기획서 '해킹', 구 디코이)
        QuickChat quickChat;   // 빠른채팅 — 숫자키 1~8. 멀티에서 팀원에게 전달될 예정
        readonly List<AiSlotDriver> aiDrivers = new List<AiSlotDriver>();

        /// <summary>빠른채팅 숫자키 매핑 — QuickChat.Lines와 순서가 1:1.</summary>
        static readonly Key[] ChatKeys =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
            Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8
        };

        Phase phase = Phase.Playing;
        float countdownUntil;   // 라운드 시작 3·2·1 — 이 시각까지 시뮬·조작 정지
        bool countdownRunning;
        ParsedMap map;
        GridConfig gridConfig;
        int playerTeam;
        bool gridViewBuilt;
        const int RoundsPerMap = 2; // 2라운드마다 맵 로테이션 — 지형 습관이 굳기 전에 판을 갈아엎는다
        readonly List<GameObject> zoneLabels = new List<GameObject>(); // 맵 교체 시 파괴 대상
        MatchSetup matchSetup;       // 슬롯 구성 — 싱글은 [LocalHuman 1 + Bot 5], 멀티 로비가 덮어씀
        HashSet<int> humanUnitIds;   // 인간 조종 슬롯 — Predictor 학습 대상 전체
        IIntentSink intentSink;      // 행동 제출 단일 통로 (싱글=즉시 실행)
        readonly Dictionary<int, Coord> humanPrevPos = new Dictionary<int, Coord>(); // 슬롯별 직전 위치
        Func<Coord, bool> playerVisibleFn;

        GameObject pickBg; // 맵/클래스 픽 화면 배경 (Resources/UI/BG_Title)

        // 라운드마다 파괴·재생성되는 뷰 오브젝트
        readonly List<GameObject> roundObjects = new List<GameObject>();
        readonly Dictionary<int, UnitHpBar> hpBars = new Dictionary<int, UnitHpBar>();
        readonly HashSet<int> blinkSnapIds = new HashSet<int>(); // 점멸 직후 — 슬라이드 대신 번쩍+스냅
        readonly Dictionary<int, GameObject> strikeTelegraphFx = new Dictionary<int, GameObject>(); // 예고 마커·레이저·투사체 — 판정 시 파괴
        readonly List<ZoneCaptureDisc> zoneDiscs = new List<ZoneCaptureDisc>(); // 거점 점거 원형 게이지
        readonly List<HealPackView> healPackViews = new List<HealPackView>();   // 힐팩 픽업 연출
        ThreatWarning threatWarning; // "내 칸에 예고 떨어짐" 경고 — 매치 내내 1개, 라운드 무관

        // 예측 사격 추적 (G) — 캐스팅 직후 예고와 매칭해 적중/실패 자막
        readonly List<(int attackerId, Coord cell, float time)> pendingPredictedShots = new List<(int, Coord, float)>();
        bool hackReadyAnnounced; // 해킹 만충 공지 — 만충 상태로 올라가는 순간에만 1회
        bool fastForward;        // 싱글에서 내가 죽은 뒤 SPACE — 라운드 결과까지 6배속 (멀뚱히 기다리지 않게)
        readonly HashSet<TelegraphStrike> predictedStrikes = new HashSet<TelegraphStrike>();
        float nextFakeCalloutTime; // 사후 귀속 자막 남발 방지

        void Awake()
        {
            gridView = GetComponent<GridView>();
            input = GetComponent<UnitMoveInput>();
            viewRegistry = GetComponent<UnitViewRegistry>();
            if (viewRegistry == null) viewRegistry = gameObject.AddComponent<UnitViewRegistry>();
            hud = GetComponent<BattleHud>();
            if (hud == null) hud = gameObject.AddComponent<BattleHud>();
            battleAudio = GetComponent<BattleAudio>();
            if (battleAudio == null) battleAudio = gameObject.AddComponent<BattleAudio>();
        }

        void Start()
        {
            playerTeam = FindRoster(playerUnitId).team;
            // 해킹 시야 강탈 중엔 전부 보임 — 이 함수가 안개·적 예고 필터·타격 VFX 필터의 공통 기준이라 여기서 걷는다
            playerVisibleFn = c => vision.IsVisibleTo(playerTeam, c)
                || (hackSystem != null && Battle != null && hackSystem.RevealActive(playerTeam, Battle.time));

            // 플레이어 행동 거부 버저 — input은 라운드 넘어 유지되므로 1회만 구독
            input.OnActionDenied += () => battleAudio.PlaySfx("S24_ApBuzz", 0.5f);

            // 빠른채팅 — 라운드를 넘어 유지되므로 여기서 1회만 구독.
            // 팀 전용: 적 무전은 안 들린다(멀티에서 정보 누출 방지 규칙을 싱글부터 지킨다).
            quickChat = new QuickChat();
            quickChat.OnMessage += (unitId, lineId) =>
            {
                ShowChatVisual(unitId, lineId);

                // 온라인 호스트 — 같은 팀 원격 인간에게만 전달 (적팀 무전 차단)
                if (NetBoot.IsOnline && NetBoot.IsHost && NetLobby.Slots != null)
                {
                    var u = Battle?.GetUnit(unitId);
                    if (u == null) return;
                    foreach (var s in NetLobby.Slots)
                        if (s.owner == SlotOwner.RemoteHuman && s.team == u.team)
                            NetSync.HostSendChatShow(s.clientId, unitId, lineId);
                }
            };

            // 무전 패널 클릭 — 숫자키와 같은 전송 경로
            hud.OnChatClicked += lineId =>
            {
                if (phase != Phase.Playing) return;
                if (IsNetClient) NetSync.ClientSendChat(playerUnitId, lineId);
                else quickChat.TrySend(playerUnitId, lineId, Time.time);
            };

            // 카메라 셰이커 — 추적/전술 캠 위에 얹는 타격감 레이어
            var mainCam = Camera.main;
            if (mainCam != null && mainCam.GetComponent<CameraShaker>() == null)
                mainCam.gameObject.AddComponent<CameraShaker>();
            ImpactFx.Ensure(); // 명중 비네트 펀치 + 격파 플래시 (글로벌 Volume)

            Match = new MatchSystem();
            NetLobby.OnMatchStart += OnNetMatchStart; // 라운드 넘어 유지 — 1회 구독
            NetSync.OnBeginRound += OnNetBeginRound;
            NetSync.OnRoundEnd += OnNetRoundEnd;
            NetSync.OnClientDamage += OnNetDamage;
            NetSync.OnClientDeath += OnNetDeath;
            NetSync.OnClientZoneOwner += OnNetZoneOwner;
            NetSync.OnIntentRequest += HostOnRemoteIntent;
            NetSync.OnChatRequest += HostOnRemoteChat;
            NetSync.OnChatShow += ShowChatVisual;
            NetSync.OnReadyRequest += HostOnReadyRequest;                  // 클라 SPACE 동의 집계
            NetSync.OnReadyState += (ready, total) => hud.SetReadyCount(ready, total);
            NetSync.OnMoved += OnNetMoved;
            NetSync.OnHacked += OnNetHacked;
            NetSync.OnClientHackCharge += OnNetHackCharge;
            NetSync.OnKilled += OnNetKilled;
            NetSync.OnTelegraph += OnNetTelegraph;
            NetSync.OnTelegraphEnd += OnNetTelegraphEnd;
            NetSync.OnSkillCast += OnNetSkillCast;
            ShowTitle();
        }

        /// <summary>클라 — 스킬 시전 릴레이. SFX + 스킬별 VFX + 라벨을 호스트와 동일하게.</summary>
        void OnNetSkillCast(int unitId, int kindInt)
        {
            if (!IsNetClient || Battle == null) return;
            var kind = (SkillKind)kindInt;
            battleAudio.PlaySfx(SkillSfx(kind), 1.5f);
            if (IsUnitVisibleToPlayer(unitId))
                SkillVfx.Cast(kind, gridView.CoordToWorld(Battle.GetUnit(unitId).pos), Battle.GetUnit(unitId).team == playerTeam);
            var v = viewRegistry.Get(unitId);
            if (v != null && v.gameObject.activeInHierarchy)
                FloatingText.Spawn(v.transform.position, SkillLabel(kind), new Color(1f, 0.9f, 0.4f));
            if (kind == SkillKind.Blink)
            {
                // 출발지 사라짐 연출 — 위치 동기는 스냅샷 도착 시 SyncPresentation이 스냅
                if (v != null && v.gameObject.activeInHierarchy)
                {
                    var from = v.transform.position;
                    GhostTrail.SpawnAt(v.gameObject, from, new Color(0.12f, 0.06f, 0.2f, 0.85f));
                    VfxLibrary.Spawn(VfxLibrary.ToonPoofDark, from, 1.5f, 0.5f); // 검은 연기 펑 — SF 텔레포트(마법진+광기둥)는 세계관·크기 안 맞아 제거
                    v.CancelMove();
                }
                blinkSnapIds.Add(unitId); // 점멸 스냅 규칙 유지
            }
        }

        /// <summary>클라 — 호스트 예고를 미러 CombatSystem에 주입.
        /// OnTelegraph가 발화돼 예고 렌더·경고 링·SFX가 기존 배선 그대로 뜬다.</summary>
        void OnNetTelegraph(TelegraphStrike strike)
        {
            if (!IsNetClient || Combat == null) return;
            Combat.InjectRemoteStrike(strike);
        }

        void OnNetTelegraphEnd(int strikeId, bool hit)
        {
            if (!IsNetClient || Combat == null) return;
            Combat.ResolveRemoteStrike(strikeId, hit);
        }

        /// <summary>클라 — 호스트 이동 릴레이. 내 유닛은 낙관 적용으로 이미 재생 — 중복 방지.</summary>
        void OnNetMoved(int unitId, Coord[] path, bool yellow)
        {
            if (!IsNetClient || Battle == null || unitId == playerUnitId) return;
            var view = viewRegistry.Get(unitId);
            if (view != null && view.gameObject.activeInHierarchy)
                view.PlayPath(path, moveConfig.hopDuration);
        }

        /// <summary>클라 — 스냅샷의 궁게이지를 로컬 미러에 덮어쓴다 (HUD 표시용, 검증은 호스트).</summary>
        void OnNetHackCharge(int unitId, float charge)
        {
            if (IsNetClient) hackSystem?.SetCharge(unitId, charge);
        }

        /// <summary>클라 — 해킹 발동 릴레이. 글리치·자막을 호스트와 동일하게.</summary>
        void OnNetHacked(int unitId)
        {
            if (!IsNetClient || Battle == null) return;
            var u = Battle.GetUnit(unitId);
            if (u != null) hackSystem?.MarkReveal(u.team, Battle.time); // 시야 강탈 창 복제 — 내 팀이면 적 표시
            var origin = u != null ? gridView.CoordToWorld(u.pos) : Vector3.zero;
            HackVfx.Play(this, origin, HackSystem.Duration);
            battleAudio.PlaySfx("S18_Blink", 1.3f);
            hud.ShowSubtitle(u != null && u.team == playerTeam
                ? "해킹 — 적 예측 마비" : "해킹 감지 — 예측 교란", 2.4f);
        }

        void OnDestroy()
        {
            NetLobby.OnMatchStart -= OnNetMatchStart;
            NetSync.OnBeginRound -= OnNetBeginRound;
            NetSync.OnRoundEnd -= OnNetRoundEnd;
            NetSync.OnClientDamage -= OnNetDamage;
            NetSync.OnClientDeath -= OnNetDeath;
            NetSync.OnClientZoneOwner -= OnNetZoneOwner;
            NetSync.OnIntentRequest -= HostOnRemoteIntent;
            NetSync.OnChatRequest -= HostOnRemoteChat;
            NetSync.OnChatShow -= ShowChatVisual;
            NetSync.OnMoved -= OnNetMoved;
            NetSync.OnHacked -= OnNetHacked;
            NetSync.OnClientHackCharge -= OnNetHackCharge;
            NetSync.OnKilled -= OnNetKilled;
            NetSync.OnTelegraph -= OnNetTelegraph;
            NetSync.OnTelegraphEnd -= OnNetTelegraphEnd;
            NetSync.OnSkillCast -= OnNetSkillCast;
        }

        /// <summary>내 팀 무전만 표시 — 말풍선 + HUD 로그 + 핑. 호스트/클라 공용 시각화.</summary>
        void ShowChatVisual(int unitId, int lineId)
        {
            var u = Battle?.GetUnit(unitId);
            if (u == null || u.team != playerTeam) return;

            string text = QuickChat.TextOf(lineId);
            var view = viewRegistry.Get(unitId);
            if (view != null && view.gameObject.activeInHierarchy)
                ChatBubble.Show(unitId, view.transform, text, Color.white);
            hud.AddChatLine(FindSlot(unitId).callsign, text, Color.Lerp(teamColors[u.team], Color.white, 0.55f));
            battleAudio.PlaySfx("S22_DetectPing", 0.4f); // 전용 무전음 나오기 전까지 핑 재사용
        }

        /// <summary>호스트 — 원격 인텐트. 소유권(보낸 클라 = 그 유닛 주인)만 검증, 나머지는 코어 TryX가 판정.</summary>
        void HostOnRemoteIntent(ulong sender, BattleIntent intent)
        {
            if (!NetBoot.IsHost || phase != Phase.Playing) return;
            if (!OwnsUnit(sender, intent.unitId)) return;
            intentSink.Submit(intent);
        }

        /// <summary>호스트 — 원격 채팅 요청. 쿨다운은 quickChat이, 팀 배달은 OnMessage 핸들러가.</summary>
        void HostOnRemoteChat(ulong sender, int unitId, int lineId)
        {
            if (!NetBoot.IsHost || phase != Phase.Playing) return;
            if (!OwnsUnit(sender, unitId)) return;
            quickChat.TrySend(unitId, lineId, Time.time);
        }

        static bool OwnsUnit(ulong clientId, int unitId)
        {
            if (NetLobby.Slots == null) return false;
            foreach (var s in NetLobby.Slots)
                if (s.unitId == unitId)
                    return s.owner == SlotOwner.RemoteHuman && s.clientId == clientId;
            return false;
        }

        /// <summary>온라인 클라이언트 = 시뮬 안 돌림, 스냅샷만 반영.</summary>
        bool IsNetClient => NetBoot.IsOnline && !NetBoot.IsHost;

        /// <summary>시작 화면 — 싱글 / 방 만들기(Relay 호스트) / 코드 참가.</summary>
        void ShowTitle()
        {
            phase = Phase.ClassSelect;
            battleAudio.PlayBgm("B6_Title");
            if (UIManager.Instance == null)
                new GameObject("@UIManager").AddComponent<UIManager>();
            ShowPickBackground();

            var popup = UIManager.Instance.ShowPopupUI<UITitlePopup>();
            popup.OnMatch = () => StartCoroutine(MatchmakeRoutine(popup));
        }

        /// <summary>매칭 — 매치메이커로 실사람을 찾고, 못 채우면 봇전으로 폴백.
        /// 세션 성사 시 SDK가 NGO를 시작 → NetLobby로 이어진다.</summary>
        System.Collections.IEnumerator MatchmakeRoutine(UITitlePopup popup)
        {
            popup.ShowSearching();
            var task = NetBoot.MatchmakeAsync("Seoyugi"); // 대시보드 큐 이름
            const float Timeout = 32f; // 매치메이커 티켓 타임아웃(30s)보다 살짝 길게
            float t = 0f;
            while (!task.IsCompleted && t < Timeout)
            {
                popup.SetSearchDots(1 + (int)(t * 2f) % 3);
                t += Time.deltaTime;
                yield return null;
            }

            bool matched = task.IsCompleted && !task.IsFaulted && task.Result;
            UIManager.Instance.ClosePopupUI(popup);

            if (matched)
            {
                NetLobby.Begin();
                ShowLobby(); // 실사람 매칭 성사 — 로비(빈 슬롯은 봇)
            }
            else
            {
                PickRandomMap(); // 상대 못 찾음 → 봇전
            }
        }

        UILobbyPopup lobbyPopup;

        void ShowLobby()
        {
            lobbyPopup = UIManager.Instance.ShowPopupUI<UILobbyPopup>();
            lobbyPopup.OnLeave = () => { NetBoot.Shutdown(); ShowTitle(); };
            lobbyPopup.OnStart = () =>
                // 호스트: 맵 랜덤 → 시드 롤 → 전원에 MatchSetup 브로드캐스트
                NetLobby.HostStart(UnityEngine.Random.Range(0, BattleMaps.Count),
                    UnityEngine.Random.Range(int.MinValue, int.MaxValue));
        }

        /// <summary>매치 시작 브로드캐스트 수신 — 호스트·클라 모두 같은 라운드를 조립한다.
        /// 차이는 하나: 클라는 시뮬을 틱하지 않고 스냅샷을 덮어쓴다.</summary>
        void OnNetMatchStart()
        {
            var setup = NetLobby.ReceivedSetup;
            if (setup == null) return;

            if (!NetBoot.IsHost) UIManager.Instance.CloseAllPopupUI(); // 클라 — 로비 닫고 매치 화면으로

            matchSetup = setup;
            humanUnitIds = setup.HumanUnitIds();
            playerUnitId = NetLobby.MyUnitId();
            playerTeam = FindSlot(playerUnitId).team;
            // 해킹 시야 강탈 중엔 전부 보임 — 이 함수가 안개·적 예고 필터·타격 VFX 필터의 공통 기준이라 여기서 걷는다
            playerVisibleFn = c => vision.IsVisibleTo(playerTeam, c)
                || (hackSystem != null && Battle != null && hackSystem.RevealActive(playerTeam, Battle.time));

            mapIndex = setup.mapIndex;
            map = BattleMaps.Get(mapIndex);
            gridConfig = new GridConfig { width = map.Width, height = map.Height };
            predictor = NewPredictor(); // 클라에선 미사용 — null 분기 대신 동일 경로 유지
            hackSystem = NewHackSystem();
            Match = new MatchSystem(); // 로비 재시작 대비 — 매치 스코어 백지
            BuildRound();
            SetupCamera();
        }

        /// <summary>클라 — 호스트가 다음 라운드 조립함. 같은 라운드 번호로 재조립.</summary>
        void OnNetBeginRound(int matchRound)
        {
            if (!IsNetClient || matchSetup == null) return;
            if (phase == Phase.Playing && Match.CurrentRound == matchRound) return; // 최초 시작 중복 방지
            BuildRound();
        }

        /// <summary>클라 — 라운드 종료 수신. 호스트와 같은 화면 전환.</summary>
        void OnNetRoundEnd(int winner, int w0, int w1, bool matchOver, string[] briefing)
        {
            if (!IsNetClient) return;
            input.enabled = false;
            battleAudio.SetCaptureLoop(false);
            battleAudio.PlaySfx("S14_RoundEnd", 1.5f);

            int endedRound = Match.CurrentRound;
            Match.RecordRoundResult(winner); // 결정론 — 호스트와 같은 스코어로 수렴
            if (matchOver)
            {
                hud.ShowMatchEnd();
                phase = Phase.MatchOver;
                bool myWin = winner == playerTeam;
                battleAudio.PlayBgm(myWin ? "B4_Victory" : "B5_Defeat", loop: false);
            }
            else
            {
                hud.ShowBriefing(endedRound, winner, briefing);
                phase = Phase.Briefing;
                ResetReadyGate(); // 클라도 SPACE 동의 상태 초기화
                battleAudio.PlayBgm("B3_Briefing");
                battleAudio.SetTypingLoop(true);
            }
        }

        // ── 클라 스냅샷 차분 연출 — 이벤트 릴레이 전 단계의 근사치 ──

        void OnNetDamage(int unitId, int dmg)
        {
            if (!IsNetClient || Battle == null) return;
            var view = viewRegistry.Get(unitId);
            view?.PlayHit(Vector3.zero);
            if (view != null && view.gameObject.activeInHierarchy)
                FloatingText.Spawn(view.transform.position, $"-{dmg}", new Color(1f, 0.25f, 0.2f), 1.1f);
            battleAudio.PlaySfx("S9_Hurt", 0.6f);
            if (unitId == playerUnitId) CameraShaker.Shake(0.12f); // 가독성 다이어트 — 내 피격만 미세 셰이크, 비네트 없음
        }

        void OnNetDeath(int unitId)
        {
            if (!IsNetClient || Battle == null) return;
            var dead = Battle.GetUnit(unitId);
            battleAudio.PlaySfx(dead.team == playerTeam ? "S10a_DeathAlly" : "S10b_DeathEnemy", 1.5f);
            if (dead.team == playerTeam || playerVisibleFn(dead.pos))
            {
                CameraShaker.Shake(0.55f);
                ImpactFx.DeathFlash();
                ImpactVfx.Sparks(gridView.CoordToWorld(dead.pos), machine: dead.team == 1, scale: 1.8f); StrikeVfx.Kill(gridView.CoordToWorld(dead.pos), dead.team == 1);
                CellFlash.Spawn(gridView.CoordToWorld(dead.pos), new Color(1f, 0.2f, 0.15f), 0.6f, 1.1f);
                FloatingText.Spawn(gridView.CoordToWorld(dead.pos), "격파!", new Color(1f, 0.3f, 0.2f), 1.4f, 1.1f);
            }
        }

        void OnNetZoneOwner(int zoneIdx, int owner)
        {
            if (!IsNetClient || Round == null || zoneIdx >= Round.Zones.Count) return;
            var zone = Round.Zones[zoneIdx];
            var tint = Color.Lerp(teamColors[owner], Color.white, 0.35f);
            foreach (var c in zone.cells)
                gridView.SetBaseTint(c, tint);
            bool ours = owner == playerTeam;
            battleAudio.PlaySfx(ours ? "S12a_ZoneCaptured" : "S12b_ZoneLost", 1.5f);

            // 중앙 큰 공지 — 호스트와 동일하게 (클라도 "누가 어느 거점 먹었는지" 봄)
            string letter = zoneIdx < ZoneLetters.Length ? ZoneLetters[zoneIdx] : "";
            hud.ShowAnnounce(ours
                ? $"아군이 {letter} 거점을 점령했습니다"
                : $"상대팀이 {letter} 거점을 점령했습니다", teamColors[owner], 2.8f);
            hud.PushEvent(ours ? $"아군이 {letter} 거점 점령!" : $"상대팀이 {letter} 거점 점령!", teamColors[owner]);

            var center = gridView.CoordToWorld(zone.Center);
            RingWave.Spawn(center, teamColors[owner], 5f, 0.7f);
            ImpactVfx.Pillar(center, Color.Lerp(teamColors[owner], Color.white, 0.4f));
        }

        /// <summary>맵 랜덤 확정 + 맵 종속 상태 조립 → 클래스 선택으로.</summary>
        void PickRandomMap()
        {
            mapIndex = UnityEngine.Random.Range(0, BattleMaps.Count);
            map = BattleMaps.Get(mapIndex);
            gridConfig = new GridConfig { width = map.Width, height = map.Height };
            predictor = NewPredictor();
            hackSystem = NewHackSystem();
            Debug.Log($"맵 랜덤 → [{map.Name}] ({map.Width}×{map.Height})");
            ShowClassSelect();
        }

        /// <summary>클래스 선택 팝업 → 픽 적용 + 적팀 랜덤 롤 → 매치 시작.</summary>
        void ShowClassSelect()
        {
            phase = Phase.ClassSelect;
            hud.Hide(); // 재시작 시 이전 매치 HUD·종료 배너 잔상 제거
            battleAudio.PlayBgm("B6_Title"); // 승/패 스팅어 → 타이틀 테마 복귀
            if (UIManager.Instance == null)
                new GameObject("@UIManager").AddComponent<UIManager>(); // 씬에 없으면 자동 생성
            ShowPickBackground(); // 재시작 픽에서도 배경 유지

            var popup = UIManager.Instance.ShowPopupUI<UIClassSelectPopup>();

            // 싱글 팀 구성 — 나 + 내 팀 봇 2의 클래스를 같은 화면에서 짠다 (멀티 로비 경험). 적팀은 랜덤 롤, 비공개.
            var mineIdx = new List<int>();
            for (int i = 0; i < roster.Length; i++)
                if (roster[i].id == playerUnitId) mineIdx.Insert(0, i); // 0번 = 나
                else if (roster[i].team == playerTeam) mineIdx.Add(i);
            var names = new string[mineIdx.Count];
            var initial = new UnitClass[mineIdx.Count];
            for (int i = 0; i < mineIdx.Count; i++)
            {
                names[i] = roster[mineIdx[i]].name;
                initial[i] = roster[mineIdx[i]].cls;
            }
            popup.SetTeam(names, initial);
            popup.SetTimer(30f); // 롤/오버워치식 캐릭터 선택 제한시간 — 종료 시 현재 선택으로 자동 출격
            popup.OnTeamPicked = classes =>
            {
                for (int i = 0; i < mineIdx.Count; i++)
                    roster[mineIdx[i]].cls = classes[i];
                RollEnemyClasses();
                BuildMatchSetup();
                BuildRound();
                SetupCamera();
            };
            popup.OnEscape = () => // ESC = 타이틀로 (맵 선택 화면은 제거됨 — 랜덤 픽)
            {
                UIManager.Instance.ClosePopupUI(popup);
                ShowTitle();
            };
        }

        /// <summary>
        /// 적팀 클래스 매치당 1회 랜덤 (중복 없음). 라운드 간엔 유지 — 학습 매치 구조 보호.
        /// 시드 기반 System.Random — 멀티에서 시드만 복제하면 클라가 같은 롤을 재현한다.
        /// </summary>
        void RollEnemyClasses()
        {
            int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            enemyRollSeed = seed;
            var rng = new System.Random(seed);
            var pool = new List<UnitClass>
                { UnitClass.Tank, UnitClass.Balance, UnitClass.Assassin, UnitClass.Grenadier, UnitClass.Sniper };
            for (int i = 0; i < roster.Length; i++)
            {
                if (roster[i].team == playerTeam) continue;
                int pick = rng.Next(pool.Count);
                roster[i].cls = pool[pick];
                pool.RemoveAt(pick);
            }
        }

        int enemyRollSeed;

        /// <summary>
        /// roster 배열 → MatchSetup. 싱글 = 내 슬롯만 LocalHuman, 나머지 Bot.
        /// 멀티 로비는 이 함수 대신 자기 MatchSetup을 주입하게 된다 (계약서 §4).
        /// </summary>
        void BuildMatchSetup()
        {
            var slots = new SlotConfig[roster.Length];
            for (int i = 0; i < roster.Length; i++)
                slots[i] = new SlotConfig
                {
                    unitId = roster[i].id,
                    team = roster[i].team,
                    cls = roster[i].cls,
                    callsign = ClassNames.For(roster[i].team, roster[i].cls),
                    owner = roster[i].id == playerUnitId ? SlotOwner.LocalHuman : SlotOwner.Bot
                };
            DisambiguateCallsigns(slots);
            matchSetup = new MatchSetup { mapIndex = mapIndex, enemyRollSeed = enemyRollSeed, slots = slots };
            humanUnitIds = matchSetup.HumanUnitIds();
        }

        /// <summary>같은 팀에 같은 클래스가 겹치면 뒤쪽에 번호 — 내 팀은 중복 픽이 가능하다(적팀은 롤이 중복 없음).</summary>
        static void DisambiguateCallsigns(SlotConfig[] slots)
        {
            var baseName = new string[slots.Length];
            for (int i = 0; i < slots.Length; i++) baseName[i] = slots[i].callsign;

            for (int i = 0; i < slots.Length; i++)
            {
                int n = 0;
                for (int j = 0; j < i; j++)
                    if (slots[j].team == slots[i].team && baseName[j] == baseName[i]) n++;
                if (n > 0) slots[i].callsign = baseName[i] + (char)('①' + n);
            }
        }

        (int id, int team, UnitClass cls, string name) FindRoster(int unitId)
        {
            foreach (var r in roster)
                if (r.id == unitId) return r;
            throw new ArgumentException($"roster에 없는 unitId {unitId}");
        }

        /// <summary>매치 구성에서 슬롯 조회 — BuildRound 이후의 정본 (네트워크 매치 대응).</summary>
        SlotConfig FindSlot(int unitId)
        {
            foreach (var s in matchSetup.slots)
                if (s.unitId == unitId) return s;
            throw new ArgumentException($"matchSetup에 없는 unitId {unitId}");
        }

        Predictor NewPredictor()
        {
            var cfg = new PredictionConfig { MapWidth = map.Width, MapHeight = map.Height };
            foreach (var zone in map.Zones)
            foreach (var c in zone)
                cfg.ZoneCells.Add(new PredCell(c.x, c.y));
            foreach (var h in map.Highlands)
                cfg.HighlandCells.Add(new PredCell(h.x, h.y)); // 스타일 분류(고지형 감지)용
            return new Predictor(cfg);
        }

        /// <summary>
        /// 매치 단위 해킹 장비 — 발동 연출까지 배선해서 생성.
        /// (기존엔 OnHacked에 구독자가 없어 필살기가 화면에 아무 흔적도 안 남겼다.)
        /// </summary>
        HackSystem NewHackSystem()
        {
            var hs = new HackSystem(predictor);
            hs.OnHacked += unitId =>
            {
                var u = Battle?.GetUnit(unitId);
                var origin = u != null ? gridView.CoordToWorld(u.pos) : Vector3.zero;
                HackVfx.Play(this, origin, HackSystem.Duration);

                // 해킹 = 적 전원 짧은 스턴 (기획 변경 2026-09-05: 예측 교란만으론 안 쓰게 됐다). 시뮬 상태라 호스트/싱글에서만.
                if (u != null && Battle != null)
                    foreach (var enemy in Battle.Units)
                        if (enemy.alive && enemy.team != u.team)
                        {
                            enemy.stunnedUntil = Mathf.Max(enemy.stunnedUntil, Battle.time + HackSystem.StunSeconds);
                            var ev = viewRegistry.Get(enemy.id);
                            if (ev != null && ev.gameObject.activeInHierarchy)
                            {
                                FloatingText.Spawn(ev.transform.position, "정지", StrikeVfx.MineNeon, 0.9f, 0.7f);
                                // 스턴 내내 전기 아크 — 0.6초짜리 "정지" 글자만으론 묶인 게 안 보였다
                                FxQuad.One(VfxTextures.Electric, ev.transform.position + Vector3.up * 0.55f,
                                    StrikeVfx.MineNeon, 1.3f, 0.2f, HackSystem.StunSeconds);
                            }
                        }
                battleAudio.PlaySfx("S18_Blink", 1.3f); // 전용 SFX 나오기 전까지 점멸음 재사용
                hud.ShowSubtitle(u != null && u.team == playerTeam
                    ? "해킹 — 적 예측 마비" : "해킹 감지 — 예측 교란", 2.4f);
                // 팀 자동 통보 — 성공 지점에서 쏴야 원격 클라·봇 해킹도 커버 (쿨다운 무시 규칙은 QuickChat이)
                quickChat.TrySend(unitId, QuickChat.HackLine, Time.time);
                if (NetBoot.IsOnline && NetBoot.IsHost)
                    NetSync.HostSendHacked(unitId); // 클라에도 글리치·자막
            };
            return hs;
        }

        /// <summary>픽 화면 전체 배경 — HUDRoot 캔버스(팝업보다 아래)에 깔림.</summary>
        void ShowPickBackground()
        {
            if (pickBg != null) return;
            var tex = Resources.Load<Texture2D>("UI/BG_Title");
            if (tex == null) return;
            pickBg = new GameObject("PickBackground", typeof(UnityEngine.UI.Image));
            pickBg.transform.SetParent(UIManager.Instance.HUDRoot.transform, false);
            var img = pickBg.GetComponent<UnityEngine.UI.Image>();
            img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            var rt = pickBg.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>라운드 1개 분량의 Core + 뷰 전체 조립. 라운드 시작마다 호출.</summary>
        void BuildRound()
        {
            ClearRoundObjects();
            if (pickBg != null) { Destroy(pickBg); pickBg = null; } // 픽 배경 제거

            // 맵 로테이션 — 픽한 맵에서 시작해 2라운드마다 다음 맵으로.
            // Match.CurrentRound는 호스트·클라 모두 RecordRoundResult로 결정론 전진 — 같은 맵이 나온다.
            var nextMap = BattleMaps.Get(mapIndex + (Match.CurrentRound - 1) / RoundsPerMap);
            if (map == null || nextMap.Name != map.Name)
            {
                map = nextMap;
                gridConfig = new GridConfig { width = map.Width, height = map.Height };
                gridViewBuilt = false; // 타일·거점 라벨 전부 재생성
                foreach (var go in zoneLabels)
                    if (go != null) Destroy(go);
                zoneLabels.Clear();
                Debug.Log($"맵 로테이션 → [{map.Name}] ({map.Width}×{map.Height})");
            }

            var grid = new GridModel(gridConfig);
            foreach (var c in map.Walls)
                grid.SetObstacle(c);
            foreach (var c in map.Voids)
                grid.SetVoid(c);
            foreach (var c in map.Highlands)
                grid.SetHighland(c);

            Battle = new BattleState(grid);
            foreach (var s in matchSetup.slots)
                Battle.AddUnit(new UnitState(s.unitId, s.team, map.Spawns[s.unitId], s.cls));

            Move = new MoveSystem(Battle, moveConfig);
            Combat = new CombatSystem(Battle, combatConfig);
            Round = new RoundSystem(Battle, roundConfig, map.Zones);

            // 해킹 궁게이지 — 슬롯 확보(충전은 라운드 넘겨 유지) + 적중 데미지 충전 배선.
            // Combat은 라운드마다 새로 나므로 매번 재구독 (이전 Combat은 통째로 버려짐).
            var hackUnitIds = new List<int>();
            foreach (var s in matchSetup.slots) hackUnitIds.Add(s.unitId);
            hackSystem.BeginRound(hackUnitIds);
            Combat.OnDamageDealt += hackSystem.NotifyDamage;
            Combat.OnDamageDealt += (attackerId, dealt) =>
            {
                // 내 공격 "적중" 타격감 — 가독성 다이어트의 예외. 내가 한 일의 결과는 몸으로 느껴야 한다 (2026-09-05 유저 요청).
                // 돌리 펀치(살짝 클로즈업) + 짧은 셰이크 + 아주 짧은 히트스톱 + 비네트 펀치 + 묵직한 썸프.
                if (attackerId != playerUnitId || IsNetClient) return;
                CameraShaker.PunchIn(1.1f);
                CameraShaker.Shake(0.4f);
                HitStop.Do(0.08f);
                ImpactFx.Punch(0.7f);
                battleAudio.PlayThump(big: true);
            };
            vision = new VisionSystem(Battle);
            Pickup = new PickupSystem(Battle, pickupConfig, map.HealPacks);
            Move.OnUnitMoved += (id, path, _) => Pickup.OnUnitPath(id, path); // 경로 통과 픽업 — 멈추지 않아도 먹는다

            foreach (var pack in Pickup.Packs)
            {
                var packView = HealPackView.Create(transform, gridView.CoordToWorld(pack.pos), gridView.TileSize);
                healPackViews.Add(packView);
                roundObjects.Add(packView.gameObject);
            }
            Pickup.OnPickup += (unitId, pos, healed) =>
            {
                var healedView = viewRegistry.Get(unitId);
                if (healedView != null && healedView.gameObject.activeInHierarchy)
                    FloatingText.Spawn(healedView.transform.position, $"+{healed}", new Color(0.35f, 1f, 0.5f), 1.1f);
                if (playerVisibleFn(pos))
                    CellFlash.Spawn(gridView.CoordToWorld(pos), new Color(0.4f, 1f, 0.55f));
                battleAudio.PlaySfx("S5_ApRefund", 0.7f); // 전용 SFX 나오기 전까지 회복음 재사용
            };

            if (!gridViewBuilt)
            {
                gridViewBuilt = true;
                gridView.Build(grid);
                var allZoneCells = new List<Coord>();
                foreach (var zone in map.Zones)
                    allZoneCells.AddRange(zone);
                gridView.MarkZones(allZoneCells);
                CreateZoneLabels();
                SetupCamera(); // 맵 크기가 라운드 중간에 바뀔 수 있어 재프레이밍
            }
            gridView.ClearBaseTints(); // 이전 라운드 거점 소유 틴트 제거

            // 거점 기본(미소유) = 흰색 — 탈환되면 OnZoneCaptured가 팀 색으로 덮는다.
            // 텍스처 위 곱연산이라 1.7배 부스트로 하얗게 띄움.
            var neutralZoneTint = Color.white * 1.7f;
            foreach (var zone in map.Zones)
                foreach (var c in zone)
                    gridView.SetBaseTint(c, neutralZoneTint);

            foreach (var z in Round.Zones)
            {
                // 거점 셀 범위 → 사각형 게이지 폭·깊이 (셀 수 × 타일 간격)
                int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
                foreach (var c in z.cells)
                {
                    if (c.x < minX) minX = c.x; if (c.x > maxX) maxX = c.x;
                    if (c.y < minY) minY = c.y; if (c.y > maxY) maxY = c.y;
                }
                float w = (maxX - minX + 1) * gridView.TileSize;
                float d = (maxY - minY + 1) * gridView.TileSize;
                var disc = ZoneCaptureDisc.Create(transform, gridView.CoordToWorld(z.Center), w, d);
                zoneDiscs.Add(disc);
                roundObjects.Add(disc.gameObject);
            }

            foreach (var s in matchSetup.slots)
            {
                var view = CreateUnitView();
                view.name = $"Unit_{s.unitId}_{s.cls}";
                // 클래스별 덩치 차이 — 모델 들어오기 전 임시 구분 (Bind 전에 적용해야 기준 스케일로 잡힘)
                view.transform.localScale *= ViewScale(s.cls);
                view.Bind(s.unitId, teamColors[s.team], gridView, map.Spawns[s.unitId]);
                viewRegistry.Register(view);
                roundObjects.Add(view.gameObject);

                // 가시성: 발밑 팀 링 (내 유닛 = 이중 링+펄스), 내 유닛 머리 위 ▼
                bool isPlayer = s.unitId == playerUnitId;
                UnitIndicators.AddTeamRing(view.transform, teamColors[s.team], isPlayer);
                if (isPlayer) UnitIndicators.AddPlayerArrow(view.transform, teamColors[s.team]);

                // HP 핍: 아군 초록/적 빨강, 풀피면 숨김(내 유닛 제외)
                var pipColor = s.team == playerTeam ? new Color(0.3f, 0.9f, 0.4f) : new Color(1f, 0.3f, 0.25f);
                var bar = UnitHpBar.Create(transform, Battle.GetUnit(s.unitId), view.transform, s.callsign,
                    teamColors[s.team], pipColor, alwaysShowPips: isPlayer);
                hpBars[s.unitId] = bar;
                roundObjects.Add(bar.gameObject);

            }

            hud.Init(Battle, Round, combatConfig, Match, playerUnitId, teamColors, FindSlot(playerUnitId).callsign,
                () => hackSystem.Charge(playerUnitId));
            hud.ShowChatCheatsheet = false; // 빠른채팅 치트시트는 접은 채로 진입 (Tab으로 토글) — 펼쳐두면 화면 우측 1/6을 상시 점유
            // input.Init은 아래에서 intentSink 생성 직후 호출

            Round.OnZoneCaptured += zone =>
            {
                var tint = Color.Lerp(teamColors[zone.owner], Color.white, 0.35f);
                foreach (var c in zone.cells)
                    gridView.SetBaseTint(c, tint);
                Debug.Log($"거점 {zone.Center} → 팀 {zone.owner} 탈환");

                bool ours = zone.owner == playerTeam;
                battleAudio.PlaySfx(ours ? "S12a_ZoneCaptured" : "S12b_ZoneLost", 1.5f);

                // 어그로 — 거점에서 맵 전체로 팀색 네온 띠가 두 겹 퍼져 나간다 (놓칠 수 없게)
                var pulseColor = StrikeVfx.TeamColor(ours);
                float mapReach = Mathf.Max(map.Width, map.Height) * 1.2f;
                RingWave.Spawn(gridView.CoordToWorld(zone.Center), pulseColor, mapReach, 0.9f);
                RingWave.Spawn(gridView.CoordToWorld(zone.Center), Color.Lerp(pulseColor, Color.white, 0.5f), mapReach * 0.6f, 0.55f);

                // 거점 글자(A/B/C) 찾기 + 중앙 멘트
                int zi = -1;
                for (int i = 0; i < Round.Zones.Count; i++)
                    if (ReferenceEquals(Round.Zones[i], zone)) { zi = i; break; }
                string letter = zi >= 0 && zi < ZoneLetters.Length ? ZoneLetters[zi] : "";
                string ment = ours
                    ? $"아군이 {letter} 거점을 점령했습니다"
                    : $"상대팀이 {letter} 거점을 점령했습니다";
                hud.ShowAnnounce(ment, teamColors[zone.owner], 2.8f); // 상단 중앙 큰 공지
                hud.PushEvent(ours ? $"아군이 {letter} 거점 점령!" : $"상대팀이 {letter} 거점 점령!", teamColors[zone.owner]);
                battleAudio.PlayVoice(ours ? "Voice_ZoneCaptured" : "Voice_ZoneLost"); // 음성만 (자막은 배너가)

                // 탈환 완료 순간 — 팀 색 충격파가 패치 밖으로 퍼진다
                var center = gridView.CoordToWorld(zone.Center);
                RingWave.Spawn(center, teamColors[zone.owner], 5f, 0.7f);
                ImpactVfx.Pillar(center, Color.Lerp(teamColors[zone.owner], Color.white, 0.4f));
                CameraShaker.Shake(0.2f);
            };
            Round.OnSuddenDeath += _ =>
            {
                Debug.Log("서든데스! 다음 탈환 또는 킬로 즉시 승부");
                battleAudio.PlaySfx("S15_SuddenDeath", 2f);
                PlayVoiceLine("Voice_SuddenDeath", "서든데스");
                ImpactFx.SetSuddenDeath(true); // 화면 가장자리 적색 맥동 시작
            };

            // 슬롯: MatchSetup 기준 — Bot 슬롯만 AI 뇌, 인간 반대팀 뇌에만 Predictor 주입 (기획서 §05).
            // 클라는 인텐트를 호스트로 쏘고(Pending), 호스트/싱글은 즉시 실행.
            intentSink = IsNetClient
                ? (IIntentSink)new NetIntentSink(Move) // 미러 주입 = 이동 낙관 적용
                : new LocalIntentSink(Battle, Move, Combat, hackSystem);
            input.Init(Move, Combat, gridView, viewRegistry, playerUnitId, intentSink, playerVisibleFn);
            worldView = new CoreWorldView(Battle, Combat, Round, vision, humanUnitIds, Match.CurrentRound,
                hackSystem, Pickup);
            aiDrivers.Clear();
            pendingPredictedShots.Clear();
            predictedStrikes.Clear();
            bool humanOnTeam0 = false, humanOnTeam1 = false;
            foreach (var s in matchSetup.slots)
                if (s.IsHuman) { if (s.team == 0) humanOnTeam0 = true; else humanOnTeam1 = true; }
            foreach (var s in matchSetup.slots)
                if (s.owner == SlotOwner.Bot && !IsNetClient) // 클라는 AI 안 돌림 — 호스트 권위
                {
                    // 상대팀에 인간이 있는 봇만 예측 뇌 — "AI는 인간을 학습해 노린다"
                    bool enemyHasHuman = s.team == 0 ? humanOnTeam1 : humanOnTeam0;
                    var driver = new AiSlotDriver(s.unitId, s.cls, intentSink,
                        enemyHasHuman ? predictor : null);
                    driver.OnPredictedShot += (attackerId, cell) =>
                    {
                        var target = new Coord(cell.X, cell.Y);
                        pendingPredictedShots.Add((attackerId, target, Battle.time));

                        // 예측 사격 — 일반 예고 링 바깥에 보라 링이 하나 더 조여든다.
                        // "읽고 쏘는 중"이 조준 단계에서 보여야 회피가 플레이어의 선택이 된다.
                        if (!playerVisibleFn(target)) return;
                        float remain = combatConfig.attackTelegraphSeconds;
                        foreach (var s in Combat.ActiveStrikes)
                            if (s.attackerId == attackerId && s.cells.Contains(target))
                            {
                                remain = s.impactTime - Battle.time;
                                break;
                            }
                        RingWave.Reticle(gridView.CoordToWorld(target),
                            new Color(0.75f, 0.4f, 1f, 0.95f), 2.4f, 0.75f, remain, 320f);
                    };
                    aiDrivers.Add(driver);
                }

            Move.OnUnitMoved += (unitId, path, yellow) =>
            {
                // 시야 밖(비활성) 뷰는 연출 생략 — 다시 보일 때 SyncPresentation의 SnapTo가 위치를 맞춘다
                var movedView = viewRegistry.Get(unitId);
                if (movedView != null && movedView.gameObject.activeInHierarchy)
                    movedView.PlayPath(path, moveConfig.hopDuration);
                if (!IsNetClient && humanUnitIds.Contains(unitId))
                    ObserveHumanPath(unitId, path); // 인간 슬롯 전원 학습 — 호스트/싱글만 (Predictor 호스트 전용)
                if (unitId == playerUnitId)
                {
                    battleAudio.PlaySfx(yellow ? "S7_YellowMove" : "S6_Hop", yellow ? 1f : 0.4f);
                    if (yellow) CameraShaker.Shake(0.12f); // 과부하 점프 — 미세한 무게
                }
                if (NetBoot.IsOnline && NetBoot.IsHost)
                {
                    // 경로 릴레이 — 같은 팀은 항상, 적팀 클라는 경로가 그 팀 시야에 걸릴 때만 (위치 누출 차단)
                    var mover = Battle.GetUnit(unitId);
                    foreach (var s in NetLobby.Slots)
                    {
                        if (s.owner != SlotOwner.RemoteHuman) continue;
                        bool canSee = s.team == mover.team;
                        if (!canSee)
                            foreach (var c in path)
                                if (vision.IsVisibleTo(s.team, c)) { canSee = true; break; }
                        if (canSee) NetSync.HostSendMoved(s.clientId, unitId, path, yellow);
                    }
                }
            };

            Combat.OnTelegraph += strike =>
            {
                // 예고 릴레이 — 시전 팀 클라는 항상, 적팀 클라는 그 팀 시야에 걸리는 예고만
                if (NetBoot.IsOnline && NetBoot.IsHost && NetLobby.Slots != null)
                    foreach (var s in NetLobby.Slots)
                    {
                        if (s.owner != SlotOwner.RemoteHuman) continue;
                        bool canSee = s.team == strike.team;
                        if (!canSee)
                            foreach (var c in strike.cells)
                                if (vision.IsVisibleTo(s.team, c)) { canSee = true; break; }
                        if (canSee) NetSync.HostSendTelegraph(s.clientId, strike);
                    }

                bool mineStrike = strike.team == playerTeam;
                if (mineStrike) battleAudio.PlaySfx("S2_TelegraphAlly", 0.8f);
                else if (AnyCellVisible(strike)) battleAudio.PlaySfx("S1_TelegraphEnemy", 0.8f);

                // 판정 칸 중심에 조여드는 경고 링 — 링이 닫히는 순간이 곧 판정 순간이다.
                // 칸마다 띄우면 난전에서 노이즈라 중심 1개만. 바닥 틴트가 범위를 담당.
                if (strike.cells.Count > 0 && (mineStrike || AnyCellVisible(strike)))
                {
                    var focus = strike.cells[strike.cells.Count / 2];
                    RingWave.Reticle(gridView.CoordToWorld(focus),
                        mineStrike ? new Color(1f, 0.6f, 0.1f, 0.8f) : new Color(0.95f, 0.25f, 0.12f, 0.9f),
                        1.6f, 0.45f, strike.impactTime - Battle.time);
                }

                // 클래스별 예고 연출 — 까치 조준경+레이저, 기계 록온, 비둘기 폭탄 투사체
                var telegraphAttacker = Battle.GetUnit(strike.attackerId);
                if (telegraphAttacker != null && strike.cells.Count > 0 && (mineStrike || AnyCellVisible(strike)))
                {
                    var visCells = new List<Vector3>();
                    foreach (var c in strike.cells)
                        if (mineStrike || playerVisibleFn(c)) visCells.Add(gridView.CoordToWorld(c));
                    // 조준 칸 — 코어가 기록한 aimCell. 네트 복제본 등 없으면 마지막 보이는 칸으로
                    bool hasAim = false;
                    foreach (var c in strike.cells) if (c == strike.aimCell) { hasAim = true; break; }
                    var aimWorld = hasAim ? gridView.CoordToWorld(strike.aimCell) : visCells[visCells.Count - 1];
                    var fx = StrikeVfx.Telegraph(strike, telegraphAttacker.unitClass, Combat.IsFlying(telegraphAttacker),
                        gridView.CoordToWorld(telegraphAttacker.pos), visCells, strike.impactTime - Battle.time,
                        mineStrike, aimWorld);
                    if (fx != null) strikeTelegraphFx[strike.id] = fx;

                    // 폭탄 배달 — 왕복 비행 (시뮬 위치는 출발 칸 그대로, 연출만 난다)
                    if (telegraphAttacker.unitClass == UnitClass.Grenadier && Combat.IsFlying(telegraphAttacker)
                        && strike.cells.Count > 1)
                    {
                        var flier = viewRegistry.Get(strike.attackerId);
                        if (flier != null && flier.gameObject.activeInHierarchy)
                            flier.PlayBombFlight(gridView.CoordToWorld(strike.cells[0]),
                                Mathf.Max(0.2f, strike.impactTime - Battle.time), 0.7f);
                    }
                }

                // 예측 사격 매칭 — 직전 제출과 같은 공격자·목표 칸이면 표식 (G)
                for (int i = pendingPredictedShots.Count - 1; i >= 0; i--)
                {
                    var shot = pendingPredictedShots[i];
                    if (Battle.time - shot.time > 0.2f) { pendingPredictedShots.RemoveAt(i); continue; }
                    if (shot.attackerId == strike.attackerId && strike.cells.Contains(shot.cell))
                    {
                        predictedStrikes.Add(strike);
                        pendingPredictedShots.RemoveAt(i);
                        break;
                    }
                }
            };
            Combat.OnStrikeResolved += (strike, hit) =>
            {
                if (NetBoot.IsOnline && NetBoot.IsHost)
                    NetSync.HostSendTelegraphEnd(strike.id, hit); // 못 받은 id는 클라가 무시

                if (strike.attackerId == playerUnitId)
                {
                    battleAudio.PlaySfx(hit ? "S3_Hit" : "S4_Miss", 0.8f);
                    if (hit) battleAudio.PlaySfx("S5_ApRefund", 1f); // 적중 = 예측 성공 = AP 환급음
                }
                else if (hit) battleAudio.PlaySfx("S3_Hit", 0.8f);

                // 예측 사격 결과 (G) — 맞으면 소름, 빗나가면 "배신 성공" 피드백
                if (predictedStrikes.Remove(strike))
                {
                    var focus = gridView.CoordToWorld(strike.cells[strike.cells.Count / 2]);
                    if (hit)
                    {
                        hud.ShowSubtitle("읽혔습니다. 당신이 갈 곳을 알고 쐈습니다.", 2.4f);
                        // 보라 = 예측. 일반 명중(주황 기둥)과 색으로 구분돼야 "읽혔다"가 읽힌다.
                        ImpactVfx.Pillar(focus, new Color(0.8f, 0.45f, 1f));
                        RingWave.Spawn(focus, new Color(0.75f, 0.4f, 1f, 0.9f), 3.2f, 0.5f);
                        ImpactFx.Punch(0.85f);
                        CameraShaker.Shake(0.4f);
                    }
                    else
                    {
                        hud.ShowSubtitle("빗나갔습니다. 평소와 다르게 움직이셨군요.", 2.2f);
                        ImpactVfx.Sparks(focus, machine: true, scale: 0.8f); // 빗나간 조준이 흩어짐
                    }
                }
                // 사후 귀속 꼼수: 진짜 예측이 아니어도 플레이어가 맞았으면 35% 확률로
                // "읽고 쏜 것처럼" 자막 — 어차피 맞은 건 사실이라 뇌가 알아서 소름 돋는다.
                // R1 제외(바보 컨셉 유지), 8초 쿨다운으로 남발 방지.
                else if (hit && strike.team != playerTeam && Match.CurrentRound >= 2 &&
                         Time.time >= nextFakeCalloutTime &&
                         StrikeCoversPlayer(strike) && UnityEngine.Random.value < 0.35f)
                {
                    nextFakeCalloutTime = Time.time + 8f;
                    hud.ShowSubtitle("읽혔습니다. 당신이 갈 곳을 알고 쐈습니다.", 2.4f);
                }
            };
            Combat.OnStunned += (_, __) => battleAudio.PlaySfx("S8_Guard", 0.8f); // 스턴 SFX (가드 사운드 재활용)
            Combat.OnSkillCast += (unitId, kind) =>
            {
                battleAudio.PlaySfx(SkillSfx(kind), 1.5f);
                // 즉발 이동기(대시·점멸)는 캐스팅 순간에 무게 — 예고형은 판정 시 피해 셰이크가 담당
                if (kind == SkillKind.Dash && IsUnitVisibleToPlayer(unitId)) CameraShaker.Shake(0.18f);

                // 스킬 특성별 시전 VFX — 시야 안일 때만 (정보 누출 방지)
                if (IsUnitVisibleToPlayer(unitId))
                    SkillVfx.Cast(kind, gridView.CoordToWorld(Battle.GetUnit(unitId).pos), Battle.GetUnit(unitId).team == playerTeam);

                // 클라 릴레이 — 시전자 팀 클라는 항상, 적팀 클라는 시전 위치가 시야 안일 때만
                if (NetBoot.IsOnline && NetBoot.IsHost && NetLobby.Slots != null)
                {
                    var caster = Battle.GetUnit(unitId);
                    foreach (var s in NetLobby.Slots)
                        if (s.owner == SlotOwner.RemoteHuman &&
                            (s.team == caster.team || vision.IsVisibleTo(s.team, caster.pos)))
                            NetSync.HostSendSkillCast(s.clientId, unitId, (int)kind);
                }
            };
            Combat.OnWallCrash += unitId =>
            {
                battleAudio.PlaySfx("S21_WallCrash", 0.6f);
                if (IsUnitVisibleToPlayer(unitId))
                {
                    CameraShaker.Shake(0.35f); // 벽에 처박히는 쾅
                    var crashed = Battle.GetUnit(unitId);
                    RingWave.Spawn(gridView.CoordToWorld(crashed.pos), new Color(1f, 0.85f, 0.6f, 0.8f), 2.2f, 0.35f);
                    ImpactVfx.Sparks(gridView.CoordToWorld(crashed.pos), machine: crashed.team == 1, scale: 1.3f);
                }
            };
            Combat.OnUnitDied += unitId =>
            {
                var dead = Battle.GetUnit(unitId);
                battleAudio.PlaySfx(dead.team == playerTeam ? "S10a_DeathAlly" : "S10b_DeathEnemy", 1.5f);
                if (IsUnitVisibleToPlayer(unitId))
                {
                    CameraShaker.Shake(0.55f); // 격파 — 가장 무거운 한 방
                    HitStop.Do(0.09f);
                    ImpactFx.DeathFlash();
                    ImpactVfx.Sparks(gridView.CoordToWorld(dead.pos), machine: dead.team == 1, scale: 1.8f); StrikeVfx.Kill(gridView.CoordToWorld(dead.pos), dead.team == 1);
                    battleAudio.PlayThump(big: true);
                }
            };

            Combat.OnUnitDamaged += (unitId, dmg, hitDir) =>
            {
                var victim = Battle.GetUnit(unitId);
                var victimView = viewRegistry.Get(unitId);
                victimView?.PlayHit(new Vector3(hitDir.x, 0f, hitDir.y)); // 리코일 틸트 + 플래시
                if (victimView != null && victimView.gameObject.activeInHierarchy)
                    FloatingText.Spawn(victimView.transform.position, $"-{dmg}", new Color(1f, 0.25f, 0.2f), 1.1f);
                Debug.Log($"유닛 {unitId} 피해 {dmg} (HP {victim.hp}/{victim.maxHp})");
                battleAudio.PlaySfx("S9_Hurt", 0.6f);
                if (IsUnitVisibleToPlayer(unitId))
                {
                    // 가독성 다이어트: 셰이크·히트스톱·비네트는 격파 전용. 일반 피격은 칸 안 연출 + 숫자 + 소리만.
                    // 내가 맞았을 때만 아주 짧은 셰이크 — "내 문제"는 몸으로 알아야 하니까.
                    if (unitId == playerUnitId) CameraShaker.Shake(0.12f);
                    var hitPos = gridView.CoordToWorld(victim.pos);
                    ImpactVfx.Sparks(hitPos, machine: victim.team == 1, scale: 1.4f);
                    StrikeVfx.HitReaction(hitPos, machine: victim.team == 1);
                    CellFlash.Spawn(hitPos, victim.team == playerTeam ? new Color(1f, 0.45f, 0.35f) : Color.white, 0.22f, 1f); // 피격 칸 번쩍 — 어디가 맞았는지
                    battleAudio.PlayThump(big: false);
                }
            };

            // 플로팅 텍스트 — 누가 뭘 하는지 머리 위에 뜸 (시야 안일 때만)
            Combat.OnSkillCast += (unitId, kind) =>
            {
                var v = viewRegistry.Get(unitId);
                bool viewActive = v != null && v.gameObject.activeInHierarchy;
                if (viewActive)
                    FloatingText.Spawn(v.transform.position, SkillLabel(kind), new Color(1f, 0.9f, 0.4f));

                if (kind == SkillKind.Blink)
                {
                    // 순간이동이 정체성 — 걷기·슬라이드 없이 그 즉시 사라졌다 나타난다.
                    // 출발지: 검은 잔상 + 연기 / 도착지: 등장 이펙트 (SkillVfx.Cast가 도착지 담당)
                    var unit = Battle.GetUnit(unitId);
                    if (viewActive)
                    {
                        var from = v.transform.position;
                        GhostTrail.SpawnAt(v.gameObject, from, new Color(0.12f, 0.06f, 0.2f, 0.85f));
                        VfxLibrary.Spawn(VfxLibrary.ToonPoofDark, from, 1.5f, 0.5f); // 검은 연기 펑 — SF 텔레포트(마법진+광기둥) 제거
                        CellFlash.Spawn(new Vector3(from.x, 0f, from.z), new Color(0.7f, 0.4f, 1f));
                        v.CancelMove();
                        v.SnapTo(unit.pos);
                        CellFlash.Spawn(gridView.CoordToWorld(unit.pos), new Color(0.7f, 0.4f, 1f));
                    }
                    else blinkSnapIds.Add(unitId); // 시야 밖 — 다시 보일 때 스냅 (SyncPresentation)
                }

                if (kind == SkillKind.Dash && viewActive)
                {
                    // 돌파 잔상 — 출발지→도착지 경로에 슈슈슉 (시뮬은 이미 도착해 있다)
                    var unit = Battle.GetUnit(unitId);
                    var to = gridView.CoordToWorld(unit.pos);
                    to.y = v.transform.position.y;
                    GhostTrail.Spawn(v.gameObject, v.transform.position, to, 3,
                        new Color(1f, 0.85f, 0.5f, 0.55f));
                }
            };

            // 타격 연출 — 시야 밖 칸은 예고 필터와 같은 규칙으로 숨긴다 (정보 누출 방지)
            Combat.OnStrikeResolved += (strike, hit) =>
            {
                // 예고 마커·레이저·투사체 정리 — 판정 순간이 곧 수명 종료
                if (strikeTelegraphFx.TryGetValue(strike.id, out var telFx))
                {
                    strikeTelegraphFx.Remove(strike.id);
                    if (telFx != null) Destroy(telFx);
                }

                int pillars = 0;
                var visCells = new List<Vector3>();
                foreach (var c in strike.cells)
                    if (strike.team == playerTeam || playerVisibleFn(c))
                    {
                        visCells.Add(gridView.CoordToWorld(c));
                        CellFlash.Spawn(gridView.CoordToWorld(c), Color.white);
                        if (hit && pillars < 5) // 명중 판정 — 섬광 기둥 (예고→해소)
                        {
                            ImpactVfx.Pillar(gridView.CoordToWorld(c), new Color(1f, 0.75f, 0.45f));
                            pillars++;
                        }
                    }

                // 클래스별 임팩트 — 베기·주먹·발톱·폭발·트레이서·광선검 (기획: 캐릭터별 이펙트)
                var striker = Battle.GetUnit(strike.attackerId);
                if (striker != null && visCells.Count > 0)
                    StrikeVfx.Resolve(strike, striker.unitClass, visCells, hit, strike.team == playerTeam);
            };

            Combat.OnUnitDied += unitId =>
            {
                var u = Battle.GetUnit(unitId);
                if (u.team == playerTeam || playerVisibleFn(u.pos))
                {
                    CellFlash.Spawn(gridView.CoordToWorld(u.pos), new Color(1f, 0.2f, 0.15f), 0.6f, 1.1f);
                    FloatingText.Spawn(gridView.CoordToWorld(u.pos), "격파!", new Color(1f, 0.3f, 0.2f), 1.4f, 1.1f);
                }
            };

            // 킬피드(우상단) + 음성 콜아웃 — 호스트/싱글에서 발생, 온라인이면 전 클라에 브로드캐스트
            Combat.OnUnitKilled += (deadId, killerId) =>
            {
                ShowKill(deadId, killerId);
                if (NetBoot.IsOnline && NetBoot.IsHost)
                    NetSync.HostSendKill(deadId, killerId); // 클라 킬피드도 뜨게
            };

            Combat.OnStunned += (unitId, seconds) =>
            {
                var u = Battle.GetUnit(unitId);
                if (u.team == playerTeam || playerVisibleFn(u.pos))
                {
                    var world = gridView.CoordToWorld(u.pos);
                    CellFlash.Spawn(world, StunVfx.Gold);
                    RingWave.Spawn(world, StunVfx.Gold, 1.6f, 0.4f); // 스턴 적중 충격파 — 준 쪽도 성공을 본다
                    FloatingText.Spawn(world, "스턴!", StunVfx.Gold, 0.9f, 0.7f);
                    var view = viewRegistry.Get(unitId);
                    if (view != null) StunVfx.Ensure(view.transform, seconds);
                }
                if (unitId == playerUnitId) { ImpactFx.Punch(0.5f); ImpactFx.SetGlitch(0.25f); } // 내가 맞음 — 화면이 휘청
            };

            humanPrevPos.Clear();
            foreach (var id in humanUnitIds)
                humanPrevPos[id] = Battle.GetUnit(id).pos;
            audioVisibleEnemies.Clear();
            ImpactFx.SetSuddenDeath(false); // 새 라운드 — 이전 라운드의 적색 맥동·잔여 글리치 제거
            input.enabled = false; // 카운트다운 종료 시 해제 — 클라도 조작(인텐트는 NetIntentSink가 호스트로 전송)
            phase = Phase.Playing;
            countdownUntil = Time.time + 3f; // 라운드 시작 3·2·1 — 그동안 시뮬·조작 정지
            countdownRunning = true;

            if (NetBoot.IsOnline && NetBoot.IsHost)
                NetSync.HostSendBeginRound(Match.CurrentRound); // 클라 — 같은 라운드 조립 신호
            if (IsNetClient)
                NetSync.ClientBind(Battle, Round, Pickup, Match.CurrentRound); // 스냅샷 수신 개시

            battleAudio.SetTypingLoop(false); // 브리핑 종료
            battleAudio.PlayBgm(Match.CurrentRound >= 3 ? "B2_Round3" : "B1_Round1");
            battleAudio.PlaySfx("S13_RoundStart", 1.5f);
            PlayVoiceLine("Voice_RoundStart", "라운드 개시");
            Debug.Log($"라운드 {Match.CurrentRound} 시작 (Predictor round={predictor.Round})");
        }

        void ClearRoundObjects()
        {
            foreach (var go in roundObjects)
                if (go != null) Destroy(go);
            roundObjects.Clear();
            hpBars.Clear();
            zoneDiscs.Clear();
            healPackViews.Clear();
            blinkSnapIds.Clear();
            foreach (var kv in strikeTelegraphFx)
                if (kv.Value != null) Destroy(kv.Value);
            strikeTelegraphFx.Clear();
            viewRegistry.Clear();
        }

        /// <summary>거점 패치 중앙에 대형 A/B/C 글자 (탱고파이브식).</summary>
        void CreateZoneLabels()
        {
            for (int i = 0; i < Round.Zones.Count && i < ZoneLetters.Length; i++)
            {
                var go = new GameObject($"ZoneLabel_{ZoneLetters[i]}");
                zoneLabels.Add(go);
                go.transform.SetParent(transform);
                go.transform.position = gridView.CoordToWorld(Round.Zones[i].Center) + Vector3.up * 0.06f;
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // 바닥에 눕힘
                var tm = go.AddComponent<TextMesh>();
                tm.text = ZoneLetters[i];
                tm.fontSize = 64;
                tm.characterSize = 0.3f; // 3×3 패치에 맞게 큼직하게
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = new Color(1f, 1f, 1f, 0.45f);
                GameFonts.Apply(tm, GameFonts.Title); // 거점 글자 = 어그로체
            }
        }

        /// <summary>맵 크기에 맞춰 카메라를 탱고파이브식 틸트 뷰로 프레이밍.</summary>
        void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (cam.GetComponent<QuarterViewCamera>() != null ||
                cam.GetComponent<SeoYuGi.Art.TacticalCamera>() != null) return; // 추적/전술 캠 우선 — 프레이밍 양보
            var center = (gridView.CoordToWorld(new Coord(0, 0)) +
                          gridView.CoordToWorld(new Coord(map.Width - 1, map.Height - 1))) * 0.5f;
            cam.transform.rotation = Quaternion.Euler(cameraPitch, 0f, 0f);
            float dist = Mathf.Max(map.Width, map.Height) * cameraDistanceScale;
            cam.transform.position = center - cam.transform.forward * dist;
        }

        /// <summary>영어 관제 보이스 + 한글 핵심 단어 자막 동시 출력.</summary>
        void PlayVoiceLine(string clip, string subtitle)
        {
            float length = battleAudio.PlayVoice(clip);
            hud.ShowSubtitle(subtitle, Mathf.Max(2.5f, length));
        }

        /// <summary>킬피드 한 줄 + 음성 콜아웃. 호스트·싱글·클라 공용 (클라는 NetSync.OnKilled 경유).</summary>
        void ShowKill(int deadId, int killerId)
        {
            var dead = Battle?.GetUnit(deadId);
            if (dead == null) return;
            string victimName = FindSlot(deadId).callsign;
            string killerName = null;
            var feedColor = new Color(0.8f, 0.8f, 0.85f); // 환경사 기본 회색
            var killer = Battle.GetUnit(killerId);
            if (killer != null)
            {
                killerName = FindSlot(killerId).callsign;
                feedColor = Color.Lerp(teamColors[killer.team], Color.white, 0.35f);
            }
            hud.AddKill(killerName, victimName, feedColor);
            hud.PushEvent(killerName != null ? $"{killerName}이(가) {victimName} 처치!" : $"{victimName} 처치됨",
                feedColor); // 상단 배너 전황 로그
            battleAudio.PlayVoice(dead.team == playerTeam ? "Voice_AllyDown" : "Voice_EnemyDown");
        }

        /// <summary>클라 — 호스트 처치 릴레이 수신. 킬피드·음성 동일하게.</summary>
        void OnNetKilled(int deadId, int killerId)
        {
            if (IsNetClient) ShowKill(deadId, killerId);
        }

        /// <summary>내 칸을 노리는 적 예고 중 가장 임박한 것 → 유닛 주위 링 + 머리 위 "!". 바닥 틴트는 내 모델에 가려 안 보인다.</summary>
        void UpdateThreatWarning()
        {
            if (threatWarning == null) threatWarning = ThreatWarning.Create(transform);

            float remain = -1f;
            var me = Battle.GetUnit(playerUnitId);
            if (me != null && me.alive)
            {
                foreach (var strike in Combat.ActiveStrikes)
                {
                    if (strike.team == playerTeam) continue;
                    foreach (var c in strike.cells)
                    {
                        if (c != me.pos) continue;
                        float r = strike.impactTime - Battle.time;
                        if (remain < 0f || r < remain) remain = r;
                    }
                }
            }

            if (remain < 0f) { threatWarning.Set(default, default, -1f); return; }
            var view = viewRegistry.Get(playerUnitId);
            var ground = gridView.CoordToWorld(me.pos);
            var head = view != null ? view.transform.position + Vector3.up * 0.6f : ground + Vector3.up * 1.1f;
            threatWarning.Set(ground, head, remain);
            // 세 겹으로 알린다: 유닛 주위(링·!) + 화면 가장자리(붉은 비네트 맥동) + HUD 배너
            hud.ShowThreat(remain);
            ImpactFx.PulseThreat(1f - Mathf.Clamp01(remain / 0.8f));
        }

        bool IsUnitVisibleToPlayer(int unitId)
        {
            var u = Battle.GetUnit(unitId);
            if (u == null) return false;
            return u.team == playerTeam || vision.IsVisibleTo(playerTeam, u.pos)
                || (hackSystem != null && hackSystem.RevealActive(playerTeam, Battle.time));
        }

        /// <summary>판정 칸에 플레이어가 있었나 — 사후 귀속 자막의 대상 확인 (근사치).</summary>
        bool StrikeCoversPlayer(TelegraphStrike strike)
        {
            var player = Battle.GetUnit(playerUnitId);
            if (player == null || !player.alive) return false;
            return strike.cells.Contains(player.pos);
        }

        bool AnyCellVisible(TelegraphStrike strike)
        {
            foreach (var c in strike.cells)
                if (vision.IsVisibleTo(playerTeam, c)) return true;
            return false;
        }

        static string SkillSfx(SkillKind kind)
        {
            switch (kind)
            {
                case SkillKind.ShieldPush: return "S16_Smash"; // 전용 SFX 나오기 전 재활용
                case SkillKind.Smash: return "S16_Smash";
                case SkillKind.Dash: return "S17_Dash";
                case SkillKind.Scream: return "S8_Guard";
                case SkillKind.Blink: return "S18_Blink";
                case SkillKind.Claw: return "S16_Smash";
                case SkillKind.Burst: return "S19_Burst";
                case SkillKind.BombDeliver: return "S19_Burst";
                case SkillKind.KnockShot: return "S20_Snipe";
                case SkillKind.Snipe: return "S20_Snipe";
                default: return "S3_Hit";
            }
        }

        /// <summary>인간 이동을 홉 단위로 Predictor에 공급 — 학습 단위는 개체(슬롯) (세부기획 E).</summary>
        void ObserveHumanPath(int unitId, IReadOnlyList<Coord> path)
        {
            var from = humanPrevPos[unitId];
            int team = Battle.GetUnit(unitId).team;
            foreach (var to in path)
            {
                predictor.Observe(new ActionEvent
                {
                    ActorId = unitId,
                    Team = (TeamId)team,
                    Type = ActionType.Move,
                    From = new PredCell(from.x, from.y),
                    To = new PredCell(to.x, to.y),
                    Time = Battle.time
                });
                from = to;
            }
            humanPrevPos[unitId] = from;
        }

        void OnRoundFinished(int winnerTeam)
        {
            input.enabled = false; // 오버레이 중 조작·학습 오염 차단
            fastForward = false;
            Time.timeScale = 1f; // 빨리감기 중 끝났으면 정상 속도로
            hud.SetSkipHint(false, false);
            Debug.Log($"라운드 {Match.CurrentRound} 종료 — 팀 {winnerTeam} 승리");
            battleAudio.SetCaptureLoop(false);
            battleAudio.PlaySfx("S14_RoundEnd", 1.5f);

            int endedRound = Match.CurrentRound;
            bool matchOver = Match.RecordRoundResult(winnerTeam);

            // 온라인 호스트 — 클라마다 자기 유닛 기준 브리핑을 담아 종료 통지
            if (NetBoot.IsOnline && NetBoot.IsHost && NetLobby.Slots != null)
                foreach (var s in NetLobby.Slots)
                    if (s.owner == SlotOwner.RemoteHuman)
                        NetSync.HostSendRoundEnd(s.clientId, winnerTeam, Match.GetWins(0), Match.GetWins(1),
                            matchOver, predictor.GetBriefing(s.unitId));

            if (matchOver)
            {
                hud.ShowMatchEnd();
                phase = Phase.MatchOver;
                bool myWin = Match.MatchWinner == playerTeam;
                battleAudio.PlayBgm(myWin ? "B4_Victory" : "B5_Defeat", loop: false);
                if (myWin) PlayVoiceLine("Voice_MatchWin", "예측 초과 — 통제 불능");
                else PlayVoiceLine("Voice_MatchLose", "구역 통제권 회수됨");
            }
            else
            {
                // 라운드 간 브리핑 — AI가 학습한 내용을 보여준다 (심사 기준 ① 어필 지점)
                hud.ShowBriefing(endedRound, winnerTeam, predictor.GetBriefing(playerUnitId));
                phase = Phase.Briefing;
                ResetReadyGate(); // SPACE 동의 집계 초기화
                battleAudio.PlayBgm("B3_Briefing");
                battleAudio.SetTypingLoop(true);
                PlayVoiceLine("Voice_PredictionApplied", "예측 모델 적용");
            }
        }

        // ── 다음 라운드 동의 게이트 (SPACE) — 호스트 집계, 전원 준비 시 진행 ──

        readonly HashSet<ulong> readyClients = new HashSet<ulong>();
        bool localReady;

        int HumanCount()
        {
            int n = 0;
            if (matchSetup?.slots != null)
                foreach (var s in matchSetup.slots)
                    if (s.owner != SlotOwner.Bot) n++;
            return Math.Max(1, n);
        }

        void HostOnReadyRequest(ulong sender) => MarkReady(sender);

        void MarkReady(ulong clientId)
        {
            if (phase != Phase.Briefing) return;
            readyClients.Add(clientId);
            int total = HumanCount();
            hud.SetReadyCount(readyClients.Count, total);
            if (NetBoot.IsOnline && NetBoot.IsHost)
                NetSync.HostSendReadyState(readyClients.Count, total);
            if (readyClients.Count >= total)
                StartNextRound();
        }

        void ResetReadyGate()
        {
            readyClients.Clear();
            localReady = false;
            hud.SetReadyCount(0, HumanCount());
        }

        void StartNextRound()
        {
            predictor.SetRound(Match.CurrentRound); // R1 관찰 → R2 적용 → R3 선점
            BuildRound();
        }

        void RestartMatch()
        {
            Match = new MatchSystem();
            predictor = NewPredictor(); // 새 매치 = 학습 백지
            hackSystem = NewHackSystem(); // 해킹 충전도 새 매치에 리셋
            if (NetBoot.IsOnline) { hud.Hide(); ShowPickBackground(); ShowLobby(); } // 온라인 — 로비로 복귀
            else ShowClassSelect(); // 싱글 — 다시 픽 + 적팀 재롤
        }

        static string SkillLabel(SkillKind kind)
        {
            switch (kind)
            {
                case SkillKind.ShieldPush: return "방패 밀어붙이기!";
                case SkillKind.Smash: return "강타!";
                case SkillKind.Dash: return "돌파!";
                case SkillKind.Scream: return "비명 교란!";
                case SkillKind.Blink: return "그림자 도약!";
                case SkillKind.Claw: return "발톱 쥐어짜기!";
                case SkillKind.Burst: return "파열탄!";
                case SkillKind.BombDeliver: return "폭탄 배달!";
                case SkillKind.KnockShot: return "넉백샷!";
                case SkillKind.Snipe: return "조준 사격!";
                default: return "스킬!";
            }
        }

        static float ViewScale(UnitClass cls)
        {
            switch (cls)
            {
                case UnitClass.Tank: return 1.3f;      // 너구리 — 큼직
                case UnitClass.Assassin: return 0.85f; // 검은 고양이 — 날렵
                case UnitClass.Sniper: return 0.8f;    // 까치 — 작음
                default: return 1f;
            }
        }

        UnitView CreateUnitView()
        {
            if (unitPrefab != null) return Instantiate(unitPrefab, transform);

            // 프리팹 없으면 큐브 유닛 자동 생성 (타일과 동일한 폴백 정책)
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(transform);
            go.transform.localScale = new Vector3(0.7f, 0.7f, 0.7f);
            return go.AddComponent<UnitView>();
        }

        /// <summary>고스트 마커 — 시야에서 사라진 적의 마지막 목격 위치 (세부기획 B).</summary>
        void Update()
        {
            if (phase == Phase.ClassSelect || Move == null) return;

            if (phase == Phase.Briefing)
            {
                // SPACE = 다음 라운드 동의. 전원(인간)이 동의하면 호스트가 진행 — 싱글은 1/1이라 즉시.
                if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame && !localReady)
                {
                    localReady = true;
                    if (IsNetClient)
                    {
                        NetSync.ClientSendReady();
                        hud.SetReadyCount(1, HumanCount()); // 낙관 표시 — 곧 호스트 브로드캐스트로 보정
                    }
                    else
                    {
                        MarkReady(0UL); // 호스트 자신 (NGO ServerClientId = 0)
                    }
                }
                return;
            }
            if (phase == Phase.MatchOver)
            {
                if (!IsNetClient && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
                    RestartMatch();
                return;
            }

            // 라운드 시작 카운트다운 — 호스트·클라 모두 시뮬·조작 정지, 중앙에 3·2·1
            if (countdownRunning)
            {
                if (Time.time < countdownUntil)
                {
                    hud.SetCountdown(Mathf.CeilToInt(countdownUntil - Time.time));
                    return;
                }
                countdownRunning = false;
                hud.SetCountdown(0);
                input.enabled = true;
            }

            // 해킹 (H) — 궁게이지 만충 시, 5초간 적 예측 AI 교란 + 적 전원 위치 표시. 클라는 Pending.
            if (Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame)
                intentSink.Submit(BattleIntent.Hack(playerUnitId));

            // 싱글 — 내가 죽으면 SPACE로 결과까지 빨리감기 (부활 대신, 2026-09-05). 온라인은 남들이 싸우는 중이라 불가.
            var meForSkip = Battle.GetUnit(playerUnitId);
            bool canSkip = !NetBoot.IsOnline && meForSkip != null && !meForSkip.alive;
            if (canSkip && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                fastForward = !fastForward;
            if (!canSkip) fastForward = false;
            if (fastForward) Time.timeScale = 6f;            // 히트스톱이 1로 되돌려도 매 프레임 다시 6
            else if (Time.timeScale > 1f) Time.timeScale = 1f; // 해제 순간 복원 (히트스톱 0.05는 건드리지 않음)
            hud.SetSkipHint(canSkip, fastForward);

            // 해킹 만충 순간 한 번 크게 — 게이지가 구석에 있어 다 차도 몰랐다 (2026-09-05 "유용한데 안 쓰게 됨")
            bool hackReadyNow = hackSystem.IsReady(playerUnitId);
            if (hackReadyNow && !hackReadyAnnounced)
            {
                hud.ShowAnnounce("해킹 준비 완료 — H 키: 적 전원 정지 + 위치 노출", StrikeVfx.MineNeon, 3.2f);
                battleAudio.PlaySfx("S22_DetectPing", 0.9f);
            }
            hackReadyAnnounced = hackReadyNow;

            // 빠른채팅 — 숫자키 1~8 즉시 전송. Tab = 치트시트 토글(기본 켜짐, 읽기 전용).
            // 쿨다운·팀 배달은 호스트 권위 — 클라는 요청만 쏜다.
            if (Keyboard.current != null)
            {
                if (Keyboard.current.tabKey.wasPressedThisFrame)
                    hud.ShowChatCheatsheet = !hud.ShowChatCheatsheet;
                for (int i = 0; i < ChatKeys.Length; i++)
                    if (Keyboard.current[ChatKeys[i]].wasPressedThisFrame)
                    {
                        if (IsNetClient) NetSync.ClientSendChat(playerUnitId, i);
                        else quickChat.TrySend(playerUnitId, i, Time.time);
                        break;
                    }
            }

            // 온라인 클라이언트 — 시뮬 없음. 스냅샷이 상태를 쓰고, 시야·연출만 로컬.
            if (IsNetClient)
            {
                vision.Tick(); // 유닛 위치는 스냅샷이 갱신 — 시야는 완전 결정론이라 로컬 재계산
                SyncPresentation();
                return;
            }

            Move.Tick(Time.deltaTime);
            Combat.Tick(Time.deltaTime); // State.time 전진 — Pickup 리스폰 타이머가 이 시계를 쓴다
            Round.Tick(Time.deltaTime);
            vision.Tick();
            Pickup.Tick();
            hackSystem.Tick(Time.deltaTime); // 궁게이지 기본 충전 (초당 1%)

            if (NetBoot.IsOnline && NetBoot.IsHost)
                NetSync.HostTick(Time.unscaledDeltaTime, Battle, Round, Pickup, Match.CurrentRound, vision, // 12Hz 팀별 스냅샷
                    id => hackSystem.Charge(id), team => hackSystem.RevealActive(team, Battle.time));

            if (Round.Winner != -1)
            {
                OnRoundFinished(Round.Winner);
                return;
            }

            worldView.Refresh();
            foreach (var driver in aiDrivers)
                driver.Tick(worldView);

            // 실시간 패턴 감지 자막 (F) — 학습이 라운드 안에서 째깍거리는 연출
            if (predictor.TryDequeueDetection(out var detection))
            {
                hud.ShowSubtitle(detection, 2.8f);
                battleAudio.PlaySfx("S22_DetectPing", 0.6f);
            }

            SyncPresentation();

            // 밀침·대시로 위치가 바뀌어도 다음 관찰의 From이 실제 직전 위치가 되도록 보정
            foreach (var id in humanUnitIds)
                humanPrevPos[id] = Battle.GetUnit(id).pos;
        }

        bool playerWasOnHighland;

        void SyncPresentation()
        {
            gridView.UpdateFog(playerVisibleFn); // 시야 밖 타일 어둡게 (세부기획 B)
            UpdateThreatWarning();

            // 고지대 진입 공지 — 로컬(내 유닛)만. 올라간 순간 1회.
            var me = Battle.GetUnit(playerUnitId);
            if (me != null && me.alive)
            {
                bool onHigh = Battle.Grid.IsHighland(me.pos);
                if (onHigh && !playerWasOnHighland)
                    hud.ShowAnnounce("고지대 확보 — 시야 +2 · 사거리 +2 · 이동 +1", teamColors[playerTeam], 2.2f);
                playerWasOnHighland = onHigh;
            }

            // 거점 점거 원형 게이지 — 점거 중인 팀 색으로 바닥에 차오름
            bool anyCapturing = false;
            for (int i = 0; i < zoneDiscs.Count; i++)
            {
                var z = Round.Zones[i];
                float frac = z.capturingTeam >= 0 ? z.progress / roundConfig.captureSeconds : 0f;
                zoneDiscs[i].SetProgress(frac, z.capturingTeam >= 0 ? teamColors[z.capturingTeam] : Color.clear);
                if (z.capturingTeam >= 0 && z.progress > 0f) anyCapturing = true;
            }
            battleAudio.SetCaptureLoop(anyCapturing);

            // 힐팩 — 팩은 소모되면 숨김, 바닥 마커는 상시 + 리스폰 카운트다운
            for (int i = 0; i < healPackViews.Count; i++)
            {
                var pack = Pickup.Packs[i];
                healPackViews[i].SetState(pack.active, pack.respawnAt - Battle.time);
            }

            // 해킹 시야 강탈 — 지속 중엔 안개 전체가 걷히고(playerVisibleFn) 적 유닛도 전부 드러난다
            bool hackReveal = hackSystem != null && hackSystem.RevealActive(playerTeam, Battle.time);

            foreach (var unit in Battle.Units)
            {
                var view = viewRegistry.Get(unit.id);
                if (view == null) continue;

                bool isEnemy = unit.team != playerTeam;
                bool visible = unit.alive && (!isEnemy || hackReveal || vision.IsVisibleTo(playerTeam, unit.pos));

                if (view.gameObject.activeSelf != visible)
                    view.gameObject.SetActive(visible);
                hpBars[unit.id].SetVisible(visible);

                // 적 발견 핑 / 소실 SFX — 가시성 전환 시 1회
                if (isEnemy && unit.alive)
                {
                    if (visible && audioVisibleEnemies.Add(unit.id))
                        battleAudio.PlaySfx("S22_DetectPing", 0.6f);
                    else if (!visible && audioVisibleEnemies.Remove(unit.id))
                        battleAudio.PlaySfx("S23_GhostFade", 0.8f);
                }

                if (!visible) continue; // 시야 밖 적 — 흔적 없이 완전 비표시 (고스트 마커 폐지)

                // 쿨타임/스턴 시각화: 잠긴 유닛은 어둡게
                view.SetDimmed(unit.moveCooldown > 0f || unit.stunnedUntil > Battle.time);

                // 스턴 지속 동안 머리 위 별 궤도 — 이벤트가 아닌 상태 기반이라 해킹 스턴·클라 동기화도 커버
                if (unit.stunnedUntil > Battle.time)
                    StunVfx.Ensure(view.transform, unit.stunnedUntil - Battle.time);

                // 밀침·대시·점멸 등 연출 없는 위치 변경 동기화
                if (!view.IsMoving && !view.IsAt(unit.pos))
                {
                    var targetWorld = gridView.CoordToWorld(unit.pos);
                    if (blinkSnapIds.Remove(unit.id))
                    {
                        // 점멸 — 순간이동이 정체성: 출발·도착 보라 번쩍 + 스냅
                        var origin = view.transform.position;
                        origin.y = 0f;
                        CellFlash.Spawn(origin, new Color(0.7f, 0.4f, 1f));
                        view.SnapTo(unit.pos);
                        CellFlash.Spawn(targetWorld, new Color(0.7f, 0.4f, 1f));
                    }
                    else if ((view.transform.position - targetWorld).sqrMagnitude < 3.2f * 3.2f)
                    {
                        view.PlaySlide(unit.pos); // 대시·밀침 — 빠른 미끄러짐
                    }
                    else
                    {
                        view.SnapTo(unit.pos); // 시야 재등장 등 먼 거리 — 화면 가로지르는 슬라이드 방지
                    }
                }
            }
        }
    }
}
