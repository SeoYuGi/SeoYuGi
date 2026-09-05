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

        [Header("Map. BattleMaps 고정 6장 중 랜덤")]
        [SerializeField] int mapIndex = 0;

        [Header("Camera (자동 프레이밍)")]
        [SerializeField] float cameraPitch = 55f;
        [SerializeField] float cameraDistanceScale = 0.95f;

        [Header("슬롯. 내 조작은 1기, 나머지는 AI (기획서 §04)")]
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

        /// <summary>지휘관 모드 — 내 팀 봇에게 내린 상시 명령. 멀티 모드에선 항상 비어 있다(= 완전 자율).</summary>
        // 팀별 지휘 상태 — 지휘관 대전에선 양 팀 인간이 각자 자기 봇을 지휘한다. 호스트가 둘 다 들고 AI에 물린다.
        readonly CommandState[] teamOrders = { new CommandState(), new CommandState() };
        /// <summary>내 팀 지휘 상태 — 무전창·핑·퀵챗이 쓴다.</summary>
        public CommandState Orders => teamOrders[playerTeam == 1 ? 1 : 0];

        /// <summary>이번 라운드 규칙. null이면 평범한 라운드 — HUD가 이걸 보고 배너를 띄운다.</summary>
        public RoundRule Rule { get; private set; }

        UITitlePopup titlePopup; // 열려 있는 타이틀 — 다시 열 때 겹치지 않게 닫는다
        RadioWindow radio; // 지휘관 모드 전용 무전 채팅바. 멀티 모드에선 비활성.
        PauseMenu pauseMenu; // ESC 일시정지 — 모드 무관
        VoiceRadio voice;  // 음성 무전 (V 꾹 — push-to-talk). 지휘관 모드 전용.
        bool typingPrev;   // 텍스트 입력 상태 엣지 — 유닛 조작 잠금/복원용

        /// <summary>
        /// 무전 준비 — 지휘관 모드에서만 활성. 채팅바(Enter)·음성(V 꾹)이 같은 LLM 길로 들어가고,
        /// 퀵챗(숫자키)은 LLM 없이 즉시 명령이 된다 (ApplyQuickChatOrder).
        /// </summary>
        void EnsureRadio()
        {
            if (radio == null)
            {
                radio = gameObject.AddComponent<RadioWindow>();
                radio.Init(() => Orders.LastAck);
                radio.OnFreeText += SendFreeText;

                voice = gameObject.AddComponent<VoiceRadio>();
                voice.Init(() => phase == Phase.Playing && !RadioWindow.TextInputActive);
                voice.OnTranscript += SendFreeText; // 받아쓴 문장이 내 채팅 줄로 남는다 (SendFreeText가 표시)
            }
            radio.enabled = GameModeState.IsCommander;
            radio.FreezeOnOpen = false; // 싱글도 정지 없음 (2026-09-06) — 무한 정지로 프롬프트를 수백 개 박는 구멍 + 멀티와 규칙 통일. 고민 시간 = 무전 타임
            voice.enabled = GameModeState.IsCommander;
            if (!radio.enabled) radio.Close(); // 모드가 바뀌었는데 시간이 느린 채로 남지 않게

            if (pauseMenu == null)
            {
                pauseMenu = gameObject.AddComponent<PauseMenu>();
                pauseMenu.Audio = battleAudio;
                // ESC는 조준 취소·무전 닫기가 먼저 쓴다. 둘 다 아닐 때만 메뉴가 뜬다.
                pauseMenu.CanOpen = () =>
                    (input == null || input.CurrentAim == UnitMoveInput.AimMode.None) &&
                    (radio == null || !radio.IsOpen);
            }
        }

        /// <summary>자연어 무전 발신 — 채팅바·음성 공용. 내 발신·분대 응답 모두 채팅 로그에 남는다.</summary>
        /// <summary>
        /// 관전 중 — 내 유닛이 죽었고 라운드는 아직 도는 상태.
        /// 안개를 걷어 남은 판을 볼 수 있게 하되, 지휘는 막는다 — 죽은 사람이 계속 지시하면
        /// 죽음의 대가가 사라진다. 시야 정보도 산 사람만 갖는 것이 맞다.
        /// </summary>
        public bool Spectating
        {
            get
            {
                if (phase != Phase.Playing || Battle == null) return false;
                var me = Battle.GetUnit(playerUnitId);
                return me != null && !me.alive;
            }
        }

        float nextFreeTextAt; // 자유 무전 최소 간격 — 연타로 LLM 요청을 쏟아붓지 않게 (2026-09-06)

        void SendFreeText(string text)
        {
            // 전사해도 무전은 살아 있다 — 관전하며 남은 분대를 지휘한다 (2026-09-05 "죽었을 때도 지휘")
            if (Time.unscaledTime < nextFreeTextAt)
            {
                hud.ShowSubtitle("무전 과열. 잠시 뒤 다시.", 1.2f);
                if (radio != null) radio.SetWaiting(false);
                return;
            }
            nextFreeTextAt = Time.unscaledTime + 2.5f;
            var squad = CommandableUnitIds();
            if (squad.Count == 0) { if (radio != null) radio.SetWaiting(false); return; }
            ShowRadioLine(playerUnitId, text); // 내가 보낸 무전 — 말풍선 + 채팅 로그
            battleAudio.PlaySfx("S2_TelegraphAlly", 0.7f); // 무전 발신음 — 전용 SFX 나오기 전까지 대용
            var enemies = new List<int>();
            foreach (var u in Battle.Units)
                if (u.alive && u.team != playerTeam) enemies.Add(u.id);
            LlmRadio.Request(text, SquadBrief(squad, enemies), Round != null ? Round.Zones.Count : 0, squad, enemies,
                result =>
                {
                    Orders.Apply(result); // understood=false면 기존 명령 유지 — ack만 갱신
                    if (IsNetClient && result.understood) NetSync.ClientSendOrders(result); // 원격 지휘관 — 호스트의 내 팀 봇에 적용
                    int speaker = result.orders.Count > 0 ? result.orders[0].unitId
                                : squad.Count > 0 ? squad[0] : playerUnitId;
                    ShowRadioLine(speaker, string.IsNullOrEmpty(result.ack) ? "...수신 불량." : result.ack);
                    if (result.refused) // 불복종 — 분대가 명령을 물렸다. 채팅 로그와 별개로 전황 배너에도 남긴다
                        hud.PushEvent($"[무전] {FindSlot(speaker).callsign}: 명령 거부. {result.ack}", new Color(1f, 0.78f, 0.25f));
                    ShowOrderMarkers(result.orders);
                    if (radio != null) radio.SetWaiting(false);
                });
        }

        /// <summary>무전 한 줄 시각화 — ShowChatVisual과 같은 문법(말풍선 + 채팅 로그), 문구만 자유.</summary>
        void ShowRadioLine(int unitId, string text)
        {
            var u = Battle?.GetUnit(unitId);
            if (u == null) return;
            var view = viewRegistry.Get(unitId);
            if (view != null && view.gameObject.activeInHierarchy)
                ChatBubble.Show(unitId, view.transform, text, Color.white);
            hud.AddChatLine(FindSlot(unitId).callsign, text, Color.Lerp(teamColors[u.team], Color.white, 0.55f));
        }

        /// <summary>복종 가시화 — 명령 받은 봇 머리 위에 뭘 할지 띄운다. "말이 게임을 바꿨다"가 눈에 보이게.</summary>
        void ShowOrderMarkers(List<UnitOrder> orders)
        {
            foreach (var o in orders)
            {
                var view = viewRegistry.Get(o.unitId);
                if (view == null || !view.gameObject.activeInHierarchy) continue;
                FloatingText.Spawn(view.transform.position, OrderDesc(o),
                    Color.Lerp(teamColors[playerTeam], Color.white, 0.35f), 1.1f, 1.8f);
            }
        }

        string OrderDesc(UnitOrder o)
        {
            if (o.focusEnemyId >= 0)
                return FindSlot(o.focusEnemyId).callsign + " 노려!"; // 지명 타겟이 제일 중요한 정보
            switch (o.goal)
            {
                case OrderGoal.Zone: return OrderPresets.ZoneName(o.zoneIndex) + " 거점으로!";
                case OrderGoal.Highland: return "고지대로!";
                case OrderGoal.Regroup: return "집결!";
                case OrderGoal.Fallback: return "후퇴!";
                default:
                    return o.stance == OrderStance.Aggressive ? "돌격!"
                         : o.stance == OrderStance.Evasive ? "교전 회피!" : "자율 판단!";
            }
        }

        /// <summary>퀵챗 = 무전 명령 (지휘관 모드) — 숫자키 채팅 문구대로 팀 봇이 움직인다. 8~0(사교)은 채팅만.</summary>
        void ApplyQuickChatOrder(int lineId)
        {
            if (!GameModeState.IsCommander) return;
            var squad = CommandableUnitIds();
            if (squad.Count == 0) return;
            int zoneCount = Round != null ? Round.Zones.Count : 0;

            OrderPresets.Preset p;
            switch (lineId)
            {
                case 0: p = new OrderPresets.Preset { kind = OrderPresets.Kind.Aggressive, ack = "교전에 들어갑니다." }; break;
                case 1: p = new OrderPresets.Preset { kind = OrderPresets.Kind.EachZone, ack = "거점별로 전개합니다." }; break;
                case 2:
                case 3:
                case 4:
                {
                    int z = lineId - 2;
                    if (z >= zoneCount) return; // 이 맵에 없는 거점 — 채팅만 나간다
                    p = new OrderPresets.Preset { kind = OrderPresets.Kind.GatherZone, zone = z, ack = $"{OrderPresets.ZoneName(z)} 거점으로 집결합니다." };
                    break;
                }
                case 5: p = new OrderPresets.Preset { kind = OrderPresets.Kind.RegroupOnMe, ack = "지휘관께 붙습니다." }; break;
                case 6: p = new OrderPresets.Preset { kind = OrderPresets.Kind.Fallback, ack = "물러납니다." }; break;
                default: return;
            }
            var built = OrderPresets.Build(p, squad, zoneCount);
            int speaker = Personalize(built, squad); // 성격 — 말투 + (고라니) 불복종·(비둘기) 툴툴
            Orders.Apply(built);
            if (IsNetClient) NetSync.ClientSendOrders(built); // 원격 지휘관 — 호스트의 내 팀 봇에 적용
            ShowRadioLine(speaker, Orders.LastAck); // 분대 응답도 채팅 로그에
            ShowOrderMarkers(built.orders);
        }

        /// <summary>
        /// 프리셋 명령에 분대원 성격을 입힌다 (LLM 없는 경로). 발화자는 돌려가며 하나.
        /// 고라니가 Refuse를 굴리면 그 유닛 명령만 "자율+돌격"으로 바뀌고 거부 대사가 나간다 — 나머지는 복종.
        /// 비둘기 Grumble은 툴툴대는 대사만, 명령은 그대로. 반환 = 발화자 unitId.
        /// </summary>
        int Personalize(SquadOrders built, List<int> squad)
        {
            if (squad.Count == 0) return playerUnitId;
            int speaker = squad[radioSpeakerRotation++ % squad.Count];
            string ack = built.ack;
            foreach (var id in squad)
            {
                var slot = FindSlot(id);
                var roll = Personas.Roll(slot.cls, personaRng);
                if (roll == Personas.Reply.Refuse)
                {
                    for (int i = 0; i < built.orders.Count; i++)
                        if (built.orders[i].unitId == id)
                        {
                            var o = built.orders[i];
                            o.goal = OrderGoal.Free; o.stance = OrderStance.Aggressive; o.focusEnemyId = -1;
                            built.orders[i] = o;
                        }
                    speaker = id; // 거부한 놈이 말한다 — 안 그러면 왜 혼자 딴 데 가는지 모른다
                    built.ack = Personas.RefuseLine(slot.cls, slot.team, personaRng);
                    hud.PushEvent($"[무전] {slot.callsign}: 명령 무시. 돌격", new Color(1f, 0.78f, 0.25f));
                    return speaker;
                }
                if (roll == Personas.Reply.Grumble && id == speaker)
                    ack = Personas.GrumbleLine(slot.cls, slot.team, built.ack, personaRng);
            }
            var sp = FindSlot(speaker);
            built.ack = ack == built.ack ? Personas.Speak(sp.cls, sp.team, built.ack, personaRng) : ack;
            return speaker;
        }

        /// <summary>
        /// 라운드 규칙에 분대가 스스로 반응 — "B 봉쇄 확인, A부터 갑니다" 식 무전 + 그에 맞는 초기 명령.
        /// 호스트는 인간 지휘관이 있는 모든 팀의 봇에 명령을 넣고, 대사는 각자 자기 팀만 본다 (클라는 대사만 — 명령은 호스트가).
        /// 규칙 없으면 아무 일 없음.
        /// </summary>
        void ReactToRule()
        {
            if (Rule == null || !GameModeState.IsCommander || matchSetup?.slots == null) return;
            int zoneCount = Round != null ? Round.Zones.Count : 0;

            for (int team = 0; team < 2; team++)
            {
                if (!TeamHasHuman(team)) continue;
                bool mine = team == playerTeam;
                if (!mine && IsNetClient) continue; // 상대 팀 명령은 호스트 몫

                var bots = new List<int>();
                var ranged = new List<int>();
                foreach (var s in matchSetup.slots)
                    if (s.team == team && s.owner == SlotOwner.Bot && Battle.GetUnit(s.unitId)?.alive == true)
                    {
                        bots.Add(s.unitId);
                        if (s.cls == UnitClass.Sniper || s.cls == UnitClass.Grenadier) ranged.Add(s.unitId);
                    }
                if (bots.Count == 0) continue;

                string line;
                var orders = new SquadOrders();
                switch (Rule.kind)
                {
                    case RoundRuleKind.ZoneLockdown:
                    {
                        int open = -1;
                        for (int i = 0; i < zoneCount; i++) if (Rule.ZoneEnabled(i)) { open = i; break; }
                        var locked = new List<string>();
                        for (int i = 0; i < zoneCount; i++) if (!Rule.ZoneEnabled(i)) locked.Add(OrderPresets.ZoneName(i));
                        line = $"{string.Join(", ", locked)} 봉쇄 확인. {OrderPresets.ZoneName(System.Math.Max(open, 0))} 거점부터 갑니다.";
                        orders = OrderPresets.Build(new OrderPresets.Preset { kind = OrderPresets.Kind.GatherZone, zone = System.Math.Max(open, 0) }, bots, zoneCount);
                        break;
                    }
                    case RoundRuleKind.HighlandPower:
                        line = "고지대 화력 두 배. 고지대를 먼저 잡겠습니다.";
                        orders = OrderPresets.Build(new OrderPresets.Preset { kind = OrderPresets.Kind.Highland }, ranged, zoneCount);
                        foreach (var o in OrderPresets.Build(new OrderPresets.Preset { kind = OrderPresets.Kind.EachZone },
                                     bots.FindAll(b => !ranged.Contains(b)), zoneCount).orders) orders.orders.Add(o);
                        break;
                    case RoundRuleKind.SwiftFoot:
                        line = "발이 빨라졌습니다. 거점을 빠르게 훑겠습니다.";
                        orders = OrderPresets.Build(new OrderPresets.Preset { kind = OrderPresets.Kind.EachZone }, bots, zoneCount);
                        break;
                    case RoundRuleKind.ShortFuse:
                        line = "시간이 30초 짧습니다. 초반부터 거점을 잡습니다.";
                        orders = OrderPresets.Build(new OrderPresets.Preset { kind = OrderPresets.Kind.EachZone }, bots, zoneCount);
                        break;
                    default: // NoTakeback
                        line = "탈환 불가. 첫 점령이 전부입니다. 빈 거점부터 찍습니다.";
                        orders = OrderPresets.Build(new OrderPresets.Preset { kind = OrderPresets.Kind.EachZone }, bots, zoneCount);
                        break;
                }
                orders.ack = line;
                if (!IsNetClient) teamOrders[team].Apply(orders); // 호스트(싱글 포함) — 실제 명령
                if (mine)
                {
                    var sp = FindSlot(bots[0]);
                    ShowRadioLine(bots[0], Personas.Speak(sp.cls, sp.team, line, personaRng));
                    ShowOrderMarkers(orders.orders);
                }
            }
        }

        /// <summary>
        /// LLM 무전 프롬프트용 전장 상황 — 분대원(HP·위치)·지휘관 위치·거점 소유까지.
        /// 이게 있어야 "피 없는 애는 빠져", "제일 가까운 애가 B 막아" 같은 상황 인지 명령이 성립한다.
        /// </summary>
        string SquadBrief(List<int> squad, List<int> enemies)
        {
            var sb = new System.Text.StringBuilder();
            var me = Battle.GetUnit(playerUnitId);
            var mySlot = FindSlot(playerUnitId);
            sb.Append("지휘관(나, \"").Append(mySlot.callsign).Append("\")");
            if (me != null && me.alive) sb.Append(" 위치: (").Append(me.pos.x).Append(',').Append(me.pos.y).Append(")");
            else sb.Append(". 전사, 관전 중 지휘");
            sb.Append('\n');
            // 호칭은 뭐로 불러도 대응해야 한다 (2026-09-05): 별명·동물 이름·기계 이름·클래스명·역할 전부 나열.
            sb.Append("아군 분대 (unitId: \"별명\" = 다른 호칭들):\n");
            foreach (var id in squad)
            {
                var s = FindSlot(id);
                var u = Battle.GetUnit(id);
                sb.Append(id).Append(": \"").Append(s.callsign).Append("\" = ")
                  .Append(ClassNames.For(0, s.cls)).Append('/').Append(ClassNames.For(1, s.cls)).Append('/')
                  .Append(s.cls).Append('/').Append(RoleWord(s.cls))
                  .Append(" | HP ").Append(u.hp).Append('/').Append(u.maxHp)
                  .Append(" 위치 (").Append(u.pos.x).Append(',').Append(u.pos.y).Append(")")
                  .Append(" | 성격: ").Append(Personas.PromptBlock(s.cls)).Append('\n');
            }
            // 적은 편성만 준다 — 위치·HP는 시야 밖 정보라 새면 안 된다 (실제 사격도 시야 규칙을 탄다)
            sb.Append("적군 (생존, 위치 불명. unitId: \"별명\" = 다른 호칭들):\n");
            foreach (var id in enemies)
            {
                var s = FindSlot(id);
                sb.Append(id).Append(": \"").Append(s.callsign).Append("\" = ")
                  .Append(ClassNames.For(0, s.cls)).Append('/').Append(ClassNames.For(1, s.cls)).Append('/')
                  .Append(s.cls).Append('/').Append(RoleWord(s.cls));
                if (s.IsHuman) sb.Append(". 상대 지휘관(사람이 조종)");
                sb.Append('\n');
            }
            if (Round != null)
                for (int i = 0; i < Round.Zones.Count; i++)
                {
                    var z = Round.Zones[i];
                    string owner = z.owner < 0 ? "중립" : z.owner == playerTeam ? "아군" : "적군";
                    sb.Append("거점 ").Append(OrderPresets.ZoneName(i)).Append('(').Append(i)
                      .Append("): ").Append(owner)
                      .Append(". 중심 (").Append(z.Center.x).Append(',').Append(z.Center.y).Append(")\n");
                }
            return sb.ToString();
        }

        static string RoleWord(UnitClass cls)
        {
            switch (cls)
            {
                case UnitClass.Tank: return "근접 탱커";
                case UnitClass.Balance: return "돌격형";
                case UnitClass.Assassin: return "암살자";
                case UnitClass.Grenadier: return "폭격형";
                case UnitClass.Sniper: return "저격수";
                default: return "";
            }
        }

        /// <summary>거점별 소유 팀 (-1 = 중립) — 라운드 결과 화면용.</summary>
        int[] ZoneOwners()
        {
            if (Round == null) return new int[0];
            var owners = new int[Round.Zones.Count];
            for (int i = 0; i < owners.Length; i++) owners[i] = Round.Zones[i].owner;
            return owners;
        }

        /// <summary>그 팀의 생존 수 — 라운드 결과 화면용.</summary>
        int AliveCount(int team)
        {
            int n = 0;
            if (Battle != null)
                foreach (var u in Battle.Units)
                    if (u.team == team && u.alive) n++;
            return n;
        }

        /// <summary>지휘 대상 — 내 팀에서 나를 뺀 살아있는 봇.</summary>
        List<int> CommandableUnitIds()
        {
            var list = new List<int>();
            if (matchSetup?.slots == null) return list;
            foreach (var s in matchSetup.slots)
            {
                if (s.team != playerTeam || s.unitId == playerUnitId) continue;
                if (s.owner != SlotOwner.Bot) continue;
                var u = Battle.GetUnit(s.unitId);
                if (u != null && u.alive) list.Add(s.unitId);
            }
            return list;
        }
        readonly List<bool> zoneContestedPrev = new List<bool>(); // 거점 경합 상승 엣지 — S33 1회 재생용
        int prevMyZones = -1, prevEnemyZones = -1; // 거점 우세 경보 엣지 (판세 피드백)
        readonly Dictionary<int, (int streak, float until)> killStreaks = new Dictionary<int, (int, float)>(); // 멀티킬 콜아웃 (7초 창)
        readonly Dictionary<int, int> roundKills = new Dictionary<int, int>();  // 라운드 전적 (2026-09-05)
        readonly Dictionary<int, int> matchKills = new Dictionary<int, int>();  // 매치 누적 — 매치엔드 통계·MVP
        readonly Dictionary<int, int> matchDeaths = new Dictionary<int, int>();
        readonly Dictionary<int, int> matchDamage = new Dictionary<int, int>();     // 가한 피해 — 화력 부문 (2026-09-05)
        readonly Dictionary<int, float> matchCapture = new Dictionary<int, float>(); // 점령 기여 초 — 점령 부문
        int spectateUnitId = -1; // 관전 중 따라가는 아군 — 죽으면 다음 아군으로
        int lastKillerId = -1;   // 라운드 종료 포커싱 — 마지막 처치자 (2026-09-05 "맛있게")
        int lastCapturerId = -1; // 마지막 거점 점령자
        readonly Dictionary<int, int> roundDeaths = new Dictionary<int, int>();

        /// <summary>빠른채팅 숫자키 매핑 — QuickChat.Lines와 순서가 1:1.</summary>
        static readonly Key[] ChatKeys =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
            Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0
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

        // 칸당 밀침 화살표 1개. 코어는 먼저 판정된 예고가 유닛을 밀어내고 뒤 예고는 빈 칸을 때리므로
        // (Resolve의 GetUnitAt 검사), impactTime이 가장 빠른 예고만 그리는 게 실제 결과와 일치한다.
        readonly Dictionary<Coord, (PushArrow arrow, float impactTime)> pushArrowByCell =
            new Dictionary<Coord, (PushArrow, float)>();
        readonly List<ZoneCaptureDisc> zoneDiscs = new List<ZoneCaptureDisc>(); // 거점 점거 원형 게이지
        readonly List<ZoneBorderRing> zoneBorders = new List<ZoneBorderRing>(); // 거점 테두리 띠 — 소유 표시
        float nextZoneRuleHint; // 점령 불가 안내 쿨다운 — 밟고 있는 동안 도배 방지
        readonly List<HealPackView> healPackViews = new List<HealPackView>();   // 힐팩 픽업 연출
        ThreatWarning threatWarning; // "내 칸에 예고 떨어짐" 경고 — 매치 내내 1개, 라운드 무관

        // 예측 사격 추적 (G) — 캐스팅 직후 예고와 매칭해 적중/실패 자막
        readonly List<(int attackerId, Coord cell, float time)> pendingPredictedShots = new List<(int, Coord, float)>();
        bool hackReadyAnnounced; // 해킹 만충 공지 — 만충 상태로 올라가는 순간에만 1회
        bool fastForward;        // 싱글에서 내가 죽은 뒤 SPACE — 라운드 결과까지 6배속 (멀뚱히 기다리지 않게)

        // 무전 타임 (지휘관 대전, 2026-09-05) — 호스트 시계로 전원이 동시에 8초 정지, 그 안에 양쪽 지휘관이 지시한다.
        // 개인 일시정지는 상대에게 억까, 정지 없음은 타이핑하다 죽는다 → 정해진 주기에 같이 멈추는 게 유일한 공정한 답.
        // 25초 전투 + 8초 지휘가 반복되며 '실시간 턴제'의 턴이 된다. 싱글 지휘관은 무전창 열 때 정지하므로 해당 없음.
        const float RadioTimeFirst = 20f, RadioTimeEvery = 30f, RadioTimeLen = 8f;
        float nextRadioTimeAt = -1f;  // 전투 시계(Battle.time) 기준 다음 무전 타임. -1 = 없음
        bool radioTimeActive;
        float radioTimeEndsAt;        // 실시간(unscaled) 기준 종료 시각 — 정지 중엔 전투 시계가 안 가므로
        float nextBriefingAt;         // 분대 브리핑 쿨 (실시간) — 무전 타임·무전창 열 때 한 번, 최소 12초 간격
        readonly System.Random personaRng = new System.Random(); // 분대원 말버릇·복종 주사위 (연출용 — 결정론 불필요)
        int radioSpeakerRotation;     // 프리셋 응답 발화자 돌려쓰기 — 매번 같은 놈만 말하지 않게
        bool radioOpenPrev;           // 무전창 열림 엣지 — 싱글 지휘관 브리핑 트리거
        bool spectatingPrev;          // 전사 엣지 — "지휘는 계속" 안내 1회
        bool radioTimeGuided;         // 이번 무전 타임이 첫 판 가이드용(싱글) — 끝나면 가이드 종료
        bool guideStep2Announced;     // "2/3 조작" 배너 1회

        // 훈련장 — 허수아비 (팀1, 죽지 않음). 밀려나면 3초 안 맞았을 때 제자리 복귀
        const int TrainingDummyId = 4;
        Coord trainingDummyHome;
        float trainingLastHitAt;
        float nextGuideHintAt;        // ② 조작 힌트 플로팅 텍스트 주기
        readonly HashSet<TelegraphStrike> predictedStrikes = new HashSet<TelegraphStrike>();

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
                || (hackSystem != null && Battle != null && hackSystem.RevealActive(playerTeam, Battle.time))
                || Spectating // 관전 중엔 안개를 걷는다 — 죽고 나서까지 가려두면 남은 판을 볼 수가 없다
                || GameModeState.Training; // 훈련장 — 안개 없음

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

            // 해킹 게이지 클릭 — H키와 같은 제출 경로 (2026-09-05 슬롯 클릭화)
            hud.OnHackClicked += () =>
            {
                if (phase == Phase.Playing) intentSink.Submit(BattleIntent.Hack(playerUnitId));
            };

            // 무전 패널 클릭 — 숫자키와 같은 전송 경로
            hud.OnChatClicked += lineId =>
            {
                if (phase != Phase.Playing) return;
                if (IsNetClient) { NetSync.ClientSendChat(playerUnitId, lineId); ApplyQuickChatOrder(lineId); }
                else if (quickChat.TrySend(playerUnitId, lineId, Time.time)) ApplyQuickChatOrder(lineId); // 패널 클릭도 명령 (무전 타임엔 숫자키가 타이핑에 먹힌다)
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
            NetSync.OnRestart += () => { if (IsNetClient) RestartMatch(); }; // R 전원 동의 — 클라도 로비로
            NetSync.OnClientPushed += (unitId, to, crash) =>
            {
                if (!IsNetClient) return;
                var v = viewRegistry.Get(unitId);
                if (v != null && v.gameObject.activeInHierarchy) v.PlayThrow(to, crash); // 강제 이동 포물선 (2026-09-05)
            };
            NetSync.OnClientDamage += OnNetDamage;
            NetSync.OnClientDeath += OnNetDeath;
            NetSync.OnClientZoneOwner += OnNetZoneOwner;
            NetSync.OnIntentRequest += HostOnRemoteIntent;
            NetSync.OnChatRequest += HostOnRemoteChat;
            NetSync.OnChatShow += ShowChatVisual;
            NetSync.OnPingRequest += HostOnRemotePing;
            NetSync.OnOrdersRequest += HostOnRemoteOrders; // 지휘관 대전 — 원격 지휘관의 명령
            NetSync.OnRadioTime += (on, secs) => { if (on) BeginRadioTime(secs); else EndRadioTime(); }; // 클라 — 호스트와 같은 순간 정지/재개
            NetLobby.OnHumansLeftGame += HostOnHumansLeft;
            NetLobby.OnHostDisconnected += ClientOnHostGone;
            NetSync.OnPingShow += ShowPingFromNet;
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
                SkillVfx.Cast(kind, gridView.CoordToWorld(Battle.GetUnit(unitId).pos), Battle.GetUnit(unitId).team == playerTeam, focus: unitId == playerUnitId);
            var v = viewRegistry.Get(unitId);
            // 스킬명 텍스트 제거 (가독성 패스 2026-09-05) — 예고 스킬 아이콘이 대체, 머리 위는 데미지·상태 전용
            if (kind == SkillKind.Blink)
            {
                // 출발지 사라짐 연출 — 위치 동기는 스냅샷 도착 시 SyncPresentation이 스냅
                if (v != null && v.gameObject.activeInHierarchy)
                {
                    var from = v.transform.position;
                    GhostTrail.SpawnAt(v.gameObject, from, new Color(0.12f, 0.06f, 0.2f, 0.85f));
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
            battleAudio.PlaySfx("S27_Hack", 2.2f); // 시야해킹 전용음
            hud.ShowSubtitle(u != null && u.team == playerTeam
                ? "시야해킹. 적 예측 마비" : "시야해킹 감지. 예측 교란", 2.4f);
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
            NetSync.OnPingRequest -= HostOnRemotePing;
            NetLobby.OnHumansLeftGame -= HostOnHumansLeft;
            NetLobby.OnHostDisconnected -= ClientOnHostGone;
            NetSync.OnPingShow -= ShowPingFromNet;
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
        /// <summary>호스트 — 원격 지휘관의 무전 명령. 보낸 사람 팀의 봇만, 지명 타겟은 그 팀의 적만 인정.</summary>
        void HostOnRemoteOrders(ulong sender, SquadOrders squad)
        {
            if (!NetBoot.IsHost || phase != Phase.Playing || !GameModeState.IsCommander || matchSetup?.slots == null) return;
            int team = -1;
            foreach (var s in matchSetup.slots)
                if (s.owner == SlotOwner.RemoteHuman && s.ownerClientId == sender) { team = s.team; break; }
            if (team < 0) return;

            for (int i = squad.orders.Count - 1; i >= 0; i--)
            {
                var o = squad.orders[i];
                if (!IsBotOfTeam(o.unitId, team)) { squad.orders.RemoveAt(i); continue; }
                var focus = o.focusEnemyId >= 0 ? Battle.GetUnit(o.focusEnemyId) : null;
                if (focus == null || focus.team == team) o.focusEnemyId = -1; // 남의 팀 아군을 노리라는 건 무효
                squad.orders[i] = o;
            }
            teamOrders[team].Apply(squad);
        }

        // ── 무전 타임 ──────────────────────────────────────────
        void TickRadioTime()
        {
            // 싱글도 같은 주기 (2026-09-06) — 무전창 정지를 없앤 대신 정해진 고민 시간을 준다. 싱글은 자기가 호스트.
            bool online = GameModeState.IsCommander && phase == Phase.Playing;
            bool schedules = !NetBoot.IsOnline || NetBoot.IsHost;
            if (radioTimeActive)
            {
                float remain = radioTimeEndsAt - Time.unscaledTime;
                bool guidedClosed = radioTimeGuided && radio != null && !radio.IsOpen; // 가이드 무전은 발신/ESC 하면 바로 재개
                if (remain <= 0f || (!online && !radioTimeGuided) || guidedClosed) { EndRadioTime(); return; } // 호스트 종료 통보가 보통 먼저 오고, 이건 상한
                hud.ShowAnnounce(radioTimeGuided
                        ? $"첫 무전 {Mathf.CeilToInt(remain)}. 분대에 말로 지시해 보세요"
                        : $"무전 타임 {Mathf.CeilToInt(remain)}. Enter 무전 / 숫자키/패널 프리셋",
                    StrikeVfx.MineNeon, 0.6f);
                return;
            }
            // ③ 첫 판 가이드 (싱글 지휘관) — 20초에 한 번 얼리고 무전창을 열어 예시를 보여준다
            if (Guide.Active && !Guide.RadioDone && !NetBoot.IsOnline && phase == Phase.Playing &&
                Battle != null && Battle.time >= Guide.RadioAt && !Spectating)
            {
                Guide.MarkRadio();
                radioTimeGuided = true;
                nextRadioTimeAt = Battle.time + RadioTimeEvery; // 가이드 무전이 첫 무전 타임을 대신한다 — 곧바로 두 번 얼지 않게
                BeginRadioTime(Guide.RadioLength);
                if (radio != null && radio.enabled) radio.OpenGuided();
                hud.PushEvent("튜토리얼 3/3. 지휘: 분대에 말로 지시하면 알아듣고 움직인다", StrikeVfx.MineNeon);
                return;
            }
            if (!online || !schedules || nextRadioTimeAt < 0f || Battle == null || Battle.time < nextRadioTimeAt) return;
            nextRadioTimeAt = Battle.time + RadioTimeEvery;
            BeginRadioTime(RadioTimeLen);
            if (NetBoot.IsOnline) NetSync.HostSendRadioTime(true, RadioTimeLen);
        }

        /// <summary>훈련장 — 허수아비 제자리 복귀(밀려난 뒤 3초 안 맞으면) + F1~F5 캐릭터 교체.</summary>
        void TickTraining()
        {
            var dummy = Battle.GetUnit(TrainingDummyId);
            if (dummy != null && dummy.alive && !dummy.pos.Equals(trainingDummyHome) &&
                Time.time - trainingLastHitAt > 3f && Battle.Grid.IsWalkable(trainingDummyHome))
            {
                Battle.Grid.MoveOccupant(dummy.pos, trainingDummyHome);
                dummy.pos = trainingDummyHome;
                blinkSnapIds.Add(TrainingDummyId); // 슬라이드 대신 스냅 — SyncPresentation이 뷰를 맞춘다
                var v = viewRegistry.Get(TrainingDummyId);
                if (v != null) FloatingText.Spawn(v.transform.position, "제자리 복귀", new Color(0.8f, 0.85f, 0.9f), 1f, 1f);
            }

            if (RadioWindow.TextInputActive || Keyboard.current == null) return;
            int pick = Keyboard.current.f1Key.wasPressedThisFrame ? 0 : Keyboard.current.f2Key.wasPressedThisFrame ? 1
                     : Keyboard.current.f3Key.wasPressedThisFrame ? 2 : Keyboard.current.f4Key.wasPressedThisFrame ? 3
                     : Keyboard.current.f5Key.wasPressedThisFrame ? 4 : -1;
            if (pick < 0) return;
            for (int i = 0; i < roster.Length; i++)
                if (roster[i].id == playerUnitId) roster[i].cls = (UnitClass)pick;
            BuildMatchSetup();
            BuildRound(); // 라운드 재조립 — 쿨·위치 리셋, 허수아비도 제자리
            SetupCamera();
        }

        /// <summary>
        /// 가이드 스포트라이트 — 단계마다 봐야 할 곳만 밝게 (2026-09-05 "화면 까매지고 필요한 UI만 강조").
        /// ① 카운트다운: 거점들 ② 조작 힌트: 내 유닛 ③ 가이드 무전: 무전창. 그 외엔 끔.
        /// </summary>
        void TickGuideSpotlight()
        {
            if (!Guide.Wanted || phase != Phase.Playing || Camera.main == null) { GuideSpotlight.Clear(); return; }
            var cam = Camera.main;
            float s = Mathf.Max(1f, Screen.height / 1080f);

            Rect ScreenRectAround(IEnumerable<Vector3> worldPts, float padPx)
            {
                float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
                foreach (var p in worldPts)
                {
                    var sp = cam.WorldToScreenPoint(p);
                    float gy = Screen.height - sp.y; // GUI는 위에서 아래
                    xMin = Mathf.Min(xMin, sp.x); xMax = Mathf.Max(xMax, sp.x);
                    yMin = Mathf.Min(yMin, gy); yMax = Mathf.Max(yMax, gy);
                }
                return Rect.MinMaxRect(xMin - padPx, yMin - padPx, xMax + padPx, yMax + padPx);
            }

            if (countdownRunning && Round != null) // ① 거점
            {
                var pts = new List<Vector3>();
                foreach (var z in Round.Zones) pts.Add(gridView.CoordToWorld(z.Center));
                GuideSpotlight.Set(ScreenRectAround(pts, 110f * s), "거점. 밟으면 게이지가 찬다. 더 많이 가진 팀이 이긴다");
                return;
            }
            if (radioTimeGuided && radio != null && radio.IsOpen) // ③ 무전창 (RadioWindow 배치와 같은 계산)
            {
                float rs = Mathf.Max(1f, Screen.height / 1080f) * 1.25f;
                float w = Mathf.Min(560f * rs, Screen.width * 0.5f), fieldH = 42f * rs, pad = 10f * rs;
                float x = 24f * rs, yField = Screen.height - 205f * rs;
                GuideSpotlight.Set(new Rect(x - pad - 6f, yField - 30f * rs - pad - 6f, w + pad * 2 + 12f, fieldH + 30f * rs + pad * 2 + 12f),
                    "무전. 이렇게 말하면 분대가 알아듣고 움직인다");
                return;
            }
            if (Guide.Active && !GameFreeze.Active) // ② 조작 — 내 유닛
            {
                var me = Battle?.GetUnit(playerUnitId);
                var view = me != null && me.alive ? viewRegistry.Get(playerUnitId) : null;
                if (view != null && (!Guide.MoveDone || !Guide.AttackDone))
                {
                    string cap = !Guide.MoveDone ? "내 유닛. 파란 칸을 클릭해 이동" : "A 누르고 적 칸 클릭 = 공격";
                    var p = view.transform.position;
                    GuideSpotlight.Set(ScreenRectAround(new[] { p + new Vector3(-2.2f, 0f, -2.2f), p + new Vector3(2.2f, 1.2f, 2.2f) }, 20f * s), cap);
                    return;
                }
            }
            GuideSpotlight.Clear();
        }

        /// <summary>② 조작 힌트 — 내 유닛 위 플로팅 텍스트 1.5초마다. 움직이면 이동 힌트 끝, 적이 보이면 공격 힌트, 쏘면 끝.</summary>
        void TickGuideHints()
        {
            if (!Guide.Active || phase != Phase.Playing || countdownRunning || GameFreeze.Active) return;
            if (Time.unscaledTime < nextGuideHintAt) return;
            var me = Battle?.GetUnit(playerUnitId);
            var view = me != null && me.alive ? viewRegistry.Get(playerUnitId) : null;
            if (view == null) return;

            if (!guideStep2Announced)
            {
                guideStep2Announced = true;
                hud.PushEvent("튜토리얼 2/3. 조작: 파란 칸 클릭 = 이동 / A 누르고 적 칸 클릭 = 공격", StrikeVfx.MineNeon);
            }
            string hint = null;
            if (!Guide.MoveDone) hint = "파란 칸 클릭 = 이동";
            else if (!Guide.AttackDone)
            {
                foreach (var u in Battle.Units)
                    if (u.alive && u.team != playerTeam && playerVisibleFn(u.pos)) { hint = "A 누르고 적 칸 클릭 = 공격"; break; }
            }
            if (hint == null) return;
            nextGuideHintAt = Time.unscaledTime + 1.5f;
            FloatingText.Spawn(view.transform.position + Vector3.up * 0.4f, hint, new Color(1f, 0.95f, 0.6f), 1.15f, 1.4f);
        }

        void BeginRadioTime(float seconds)
        {
            if (radioTimeActive) return;
            radioTimeActive = true;
            radioTimeEndsAt = Time.unscaledTime + seconds + 0.5f; // 클라 상한 — 호스트 종료 통보가 보통 먼저 온다
            GameFreeze.Push();
            battleAudio.PlaySfx("S22_DetectPing", 0.9f);
            hud.PushEvent("[무전 타임] 분대에 지시하라", StrikeVfx.MineNeon);
            RequestSquadBriefing(); // 분대가 먼저 상황을 보고한다 — 지시만 받는 부하가 아니라 대화 상대
            // 온라인 첫 판 — 첫 무전 타임에도 예시 문장으로 안내 (그 뒤론 가이드 종료)
            if (Guide.Wanted && NetBoot.IsOnline && radio != null && radio.enabled && !Spectating)
            {
                radio.OpenGuided();
                Guide.Finish();
            }
        }

        /// <summary>분대 브리핑 — 분대원 한 명이 한 문장 보고. 키 없음·실패는 침묵. 12초 쿨.</summary>
        void RequestSquadBriefing()
        {
            if (!GameModeState.IsCommander || phase != Phase.Playing) return; // 전사 후에도 브리핑은 온다
            if (Time.unscaledTime < nextBriefingAt) return;
            var squad = CommandableUnitIds();
            if (squad.Count == 0) return;
            nextBriefingAt = Time.unscaledTime + 12f;
            var enemies = new List<int>();
            foreach (var u in Battle.Units)
                if (u.alive && u.team != playerTeam) enemies.Add(u.id);
            LlmRadio.RequestBriefing(SquadBrief(squad, enemies), Round != null ? Round.Zones.Count : 0, squad,
                (unitId, line) =>
                {
                    if (phase != Phase.Playing || Battle?.GetUnit(unitId) == null) return;
                    ShowRadioLine(unitId, line);
                    battleAudio.PlaySfx("S2_TelegraphAlly", 0.5f); // 수신음
                });
        }

        void EndRadioTime()
        {
            if (!radioTimeActive) return;
            radioTimeActive = false;
            GameFreeze.Pop();
            if (NetBoot.IsOnline && NetBoot.IsHost) NetSync.HostSendRadioTime(false, 0f);
            hud.ShowAnnounce("교신 종료. 전투 재개", StrikeVfx.MineNeon, 1.2f);
            if (radioTimeGuided)
            {
                radioTimeGuided = false;
                if (radio != null && radio.IsOpen) radio.Close();
                Guide.Finish(); // 가이드 마지막 단계 — 다시 안 뜬다 (튜토리얼 버튼으로는 언제든)
                hud.ShowAnnounce("튜토리얼 완료. 이대로 계속 싸우거나, ESC 메뉴에서 타이틀로", StrikeVfx.MineNeon, 4.5f);
            }
        }

        bool IsBotOfTeam(int unitId, int team)
        {
            foreach (var s in matchSetup.slots)
                if (s.unitId == unitId) return s.owner == SlotOwner.Bot && s.team == team;
            return false;
        }

        bool TeamHasHuman(int team)
        {
            foreach (var s in matchSetup.slots)
                if (s.team == team && s.IsHuman) return true;
            return false;
        }

        void HostOnRemoteChat(ulong sender, int unitId, int lineId)
        {
            if (!NetBoot.IsHost || phase != Phase.Playing) return;
            if (!OwnsUnit(sender, unitId)) return;
            quickChat.TrySend(unitId, lineId, Time.time);
        }

        // ── 휠클릭 핑 — 채팅과 같은 팀 전용 배달 경로 ─────────────

        /// <summary>핑 표시 + (호스트면) 같은 팀 원격 인간에게 배달. 적 팀 핑은 로컬에 안 보인다.</summary>
        void ShowPing(int unitId, Coord cell, int type)
        {
            var u = Battle?.GetUnit(unitId);
            if (u == null) return;
            if (u.team == playerTeam)
            {
                PingMarker.Spawn(gridView.CoordToWorld(cell), teamColors[u.team], type);
                battleAudio.PlaySfx("S29_Ping", 0.9f); // 핑 전용음 — 적 감지음과 분리
            }
            // 핑 지휘 (연계 패스): 같은 팀 봇들이 6초간 복종 — ▼ 집결 / ! 집중 타겟 (근처 적 소프트 가중)
            foreach (var drv in aiDrivers)
                if (drv.Team == u.team)
                    drv.CommandPing(new SeoYuGi.Prediction.Cell(cell.x, cell.y), type, Battle.time);

            // 지휘관 모드 — 인간 지휘관이 적 유닛 "정확히 그 칸"을 핑하면 그 팀 봇에 하드 포커스 (그 적만 최우선, 6초).
            // 지휘관 대전에선 상대 인간의 핑도 여기로 들어와 상대 봇을 움직인다.
            if (GameModeState.IsCommander && humanUnitIds != null && humanUnitIds.Contains(unitId))
                foreach (var enemy in Battle.Units)
                    if (enemy.alive && enemy.team != u.team && enemy.pos.Equals(cell))
                    {
                        teamOrders[u.team].SetFocus(enemy.id, Battle.time + 6f);
                        if (u.team == playerTeam)
                            hud.PushEvent($"[무전] {FindSlot(enemy.id).callsign} 집중 사격!", teamColors[playerTeam]);
                        break;
                    }
            if (NetBoot.IsOnline && NetBoot.IsHost && NetLobby.Slots != null)
                foreach (var s in NetLobby.Slots)
                    if (s.owner == SlotOwner.RemoteHuman && s.team == u.team)
                        NetSync.HostSendPingShow(s.clientId, unitId, cell, type);
        }

        void ShowPingFromNet(int unitId, int x, int y, int type)
        {
            var u = Battle?.GetUnit(unitId);
            if (u == null || u.team != playerTeam) return; // 클라 — 내 팀 핑만
            PingMarker.Spawn(gridView.CoordToWorld(new Coord(x, y)), teamColors[u.team], type);
            battleAudio.PlaySfx("S29_Ping", 0.9f);
        }

        void HostOnRemotePing(ulong sender, int unitId, int x, int y, int type)
        {
            if (!NetBoot.IsHost || phase != Phase.Playing) return;
            if (!OwnsUnit(sender, unitId)) return;
            ShowPing(unitId, new Coord(x, y), type); // 호스트 화면 표시 + 팀 배달 (발신 클라 포함)
        }

        /// <summary>호스트 — 인간 이탈: 전투 중이면 그 유닛에 봇 드라이버를 즉시 승계 (2026-09-05).</summary>
        void HostOnHumansLeft(int[] unitIds)
        {
            if (!NetBoot.IsHost || phase != Phase.Playing || matchSetup == null) return;
            foreach (var uid in unitIds)
            {
                bool hasDriver = false;
                foreach (var d in aiDrivers) if (d.UnitId == uid) { hasDriver = true; break; }
                if (hasDriver) continue;
                foreach (var slot in matchSetup.slots)
                    if (slot.unitId == uid)
                    {
                        aiDrivers.Add(new AiSlotDriver(uid, slot.cls, slot.team, intentSink, predictor));
                        hud.PushEvent($"{slot.callsign} 이탈. 봇이 대신합니다", new Color(0.7f, 0.75f, 0.85f));
                        break;
                    }
            }
        }

        /// <summary>클라 — 호스트가 방을 파괴/이탈: 정리하고 타이틀로 (2026-09-05 "같이 나가지게").</summary>
        void ClientOnHostGone()
        {
            NetBoot.Shutdown();
            hud.Hide();
            UIManager.Instance.CloseAllPopupUI();
            ShowTitle();
        }

        // 휠 홀드 상태 — 누른 순간의 칸·스크린 좌표 고정, 끌기 방향으로 종류 선택
        bool pingHolding;
        Coord pingHoldCell;
        Vector2 pingHoldScreen;

        void UpdatePingInput()
        {
            if (Mouse.current == null) return;

            if (Mouse.current.middleButton.wasPressedThisFrame &&
                input != null && input.TryGetHoverCell(out pingHoldCell))
            {
                pingHolding = true;
                pingHoldScreen = Mouse.current.position.ReadValue();
            }
            if (!pingHolding) return;

            // 끌기 방향 → 종류. 데드존 안 = ▼ 디폴트. 오른쪽 ▼ / 위 ! / 왼쪽 ? (HUD 휠 배치와 1:1)
            var delta = Mouse.current.position.ReadValue() - pingHoldScreen;
            int sel = PingMarker.TypeArrow;
            if (delta.magnitude > 28f)
            {
                if (Mathf.Abs(delta.y) > Mathf.Abs(delta.x)) sel = delta.y > 0f ? PingMarker.TypeAlert : PingMarker.TypeArrow;
                else sel = delta.x < 0f ? PingMarker.TypeQuestion : PingMarker.TypeArrow;
            }
            hud.SetPingWheel(true, pingHoldScreen, sel);

            if (Mouse.current.middleButton.wasReleasedThisFrame)
            {
                pingHolding = false;
                hud.SetPingWheel(false, Vector2.zero, 0);
                if (IsNetClient) NetSync.ClientSendPing(playerUnitId, pingHoldCell, sel);
                else ShowPing(playerUnitId, pingHoldCell, sel); // 싱글·호스트 — 즉시 표시 + 팀 배달
            }
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

            // ShowPopupUI는 부를 때마다 새 인스턴스를 만들어 스택에 쌓는다.
            // ShowTitle이 여러 경로(시작·로비 이탈·ESC·매치 종료)에서 불리므로
            // 먼저 열려 있던 타이틀을 닫지 않으면 팝업이 겹쳐 버튼이 두 벌 그려진다.
            if (titlePopup != null) UIManager.Instance.ClosePopupUI(titlePopup);

            var popup = UIManager.Instance.ShowPopupUI<UITitlePopup>();
            titlePopup = popup;
            popup.OnMatch = () =>
            {
                GameModeState.Current = GameMode.Multi;
                StartCoroutine(MatchmakeRoutine(popup));
            };
            popup.OnSettings = () =>
            {
                // 타이틀에는 아직 전용 설정 화면이 없다 — 일시정지 메뉴의 설정을 그대로 띄운다.
                EnsureRadio(); // pauseMenu 생성이 여기 묶여 있다
                if (pauseMenu != null) pauseMenu.OpenSettings();
            };
            popup.OnCommander = () =>
            {
                // 지휘관 모드 — 매칭 없이 바로 봇전. 팀원 2기를 무전으로 지휘한다.
                GameModeState.Current = GameMode.Commander;
                UIManager.Instance.ClosePopupUI(popup);
                PickRandomMap();
            };
            popup.OnTutorial = () =>
            {
                // 튜토리얼 — 지휘관 봇전에 가이드 3단계(거점 → 조작 → 지휘)를 강제로 붙인다. 규칙 변형은 생략.
                Guide.StartTutorial();
                GameModeState.Current = GameMode.Commander;
                UIManager.Instance.ClosePopupUI(popup);
                PickRandomMap();
            };
            Guide.EndTutorial(); // 타이틀로 돌아오면 튜토리얼 세션 종료 (버튼으로 다시 시작 가능)
            GameModeState.Training = false;
            popup.OnTraining = () =>
            {
                // 훈련장 — 가장 작은 맵, 나 + 허수아비. 기술 감 잡기·캐릭터 비교용 (2026-09-05)
                GameModeState.Training = true;
                GameModeState.Current = GameMode.Multi; // 지휘 없음
                UIManager.Instance.ClosePopupUI(popup);
                PickRandomMap();
            };
        }

        /// <summary>매칭 — 매치메이커로 실사람을 찾고, 못 채우면 봇전으로 폴백.
        /// 세션 성사 시 SDK가 NGO를 시작 → NetLobby로 이어진다.</summary>
        bool matchmakeCancelled; // ESC 취소 — 루틴이 매 대기 프레임 확인

        System.Collections.IEnumerator MatchmakeRoutine(UITitlePopup popup)
        {
            matchmakeCancelled = false;
            popup.OnCancelSearch = () => matchmakeCancelled = true;
            popup.ShowSearching();
            var task = NetBoot.QuickMatchAsync(); // 빈 세션 합류 or 방 생성 — 이후 대기는 로비(n/6 표시)에서
            const float Timeout = 20f; // 퀵조인 탐색(8s)+세션 생성 여유 — 이 안에 못 끝나면 네트워크 문제
            float t = 0f;
            while (!task.IsCompleted && t < Timeout)
            {
                if (matchmakeCancelled) break;
                popup.SetSearchDots(1 + (int)(t * 2f) % 3);
                t += Time.deltaTime;
                yield return null;
            }

            if (matchmakeCancelled)
            {
                // ESC 취소 — 이미 세션이 잡혔을 수 있으니 정리하고 타이틀 버튼으로 복귀
                popup.OnCancelSearch = null;
                popup.HideSearching();
                NetBoot.Shutdown(); // 잡힌 세션·NGO 정리
                yield break;
            }

            bool matched = task.IsCompleted && !task.IsFaulted && task.Result;

            // 세션은 잡혀도 SDK가 NGO를 비동기로 시작한다 — 리스닝 전에 NetLobby.Begin()을 부르면
            // CustomMessagingManager가 null (NRE). 실제 호스트/클라가 뜰 때까지 대기. (2026-09-05)
            if (matched)
            {
                float t2 = 0f;
                while (t2 < 10f && !NetBoot.IsOnline && !matchmakeCancelled)
                {
                    popup.SetSearchDots(1 + (int)((t + t2) * 2f) % 3);
                    t2 += Time.deltaTime;
                    yield return null;
                }
                if (matchmakeCancelled)
                {
                    popup.OnCancelSearch = null;
                    popup.HideSearching();
                    NetBoot.Shutdown(); // 잡힌 세션·NGO 정리
                    yield break;
                }
                if (!NetBoot.IsOnline)
                {
                    Debug.LogWarning("세션 성사됐지만 NGO 미시작(10s). 봇전으로");
                    matched = false;
                }
            }

            popup.OnCancelSearch = null; // 이후 단계에선 취소 불가
            UIManager.Instance.ClosePopupUI(popup);

            if (matched)
            {
                NetLobby.Begin();
                ShowLobby(); // 실사람 매칭 성사 — 로비(빈 슬롯은 봇)
            }
            else
            {
                // 상대 못 찾음 → 봇전. 지휘관 모드로 — Multi 그대로 두면 무전·무전 타임·지휘가 전부 꺼진
                // "지휘관 봇전에서 지휘만 빠진" 중복 모드가 됐다 (2026-09-06). 사람 대 사람일 때만 로비 토글이 정한다.
                GameModeState.Current = GameMode.Commander;
                hud.ShowSubtitle("상대를 찾지 못했습니다. 봇전으로 시작합니다.", 3f);
                PickRandomMap();
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

            GameModeState.Current = setup.commander ? GameMode.Commander : GameMode.Multi; // 호스트 로비 토글이 정본
            matchSetup = setup;
            humanUnitIds = setup.HumanUnitIds();
            playerUnitId = NetLobby.MyUnitId();
            if (playerUnitId < 0)
            {
                // 최후 방어 — 여기서 던지면 매치 시작 자체가 무너진다. 첫 슬롯 폴백 + 원인 로그 (2026-09-05)
                Debug.LogError("매치 시작: 내 슬롯 미발견 (시작 페이로드, 로비 미러 모두 미스). 첫 슬롯 폴백");
                playerUnitId = setup.slots[0].unitId;
            }
            playerTeam = FindSlot(playerUnitId).team;
            // 해킹 시야 강탈 중엔 전부 보임 — 이 함수가 안개·적 예고 필터·타격 VFX 필터의 공통 기준이라 여기서 걷는다
            playerVisibleFn = c => vision.IsVisibleTo(playerTeam, c)
                || (hackSystem != null && Battle != null && hackSystem.RevealActive(playerTeam, Battle.time))
                || Spectating; // 관전 중엔 안개를 걷는다 — 죽고 나서까지 가려두면 남은 판을 볼 수가 없다

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
        void OnNetRoundEnd(int winner, int w0, int w1, bool matchOver, string[] briefing,
            int[] hostZoneOwners, int hostAlive0, int hostAlive1, int endReasonInt,
            (int unitId, int k, int d, int dmg, float cap)[] hostStats)
        {
            if (!IsNetClient) return;
            input.enabled = false;
            battleAudio.SetCaptureLoop(false);
            battleAudio.PlaySfx("S14_RoundEnd", 1.5f);

            int endedRound = Match.CurrentRound;
            Match.RecordRoundResult(winner); // 결정론 — 호스트와 같은 스코어로 수렴
            string reasonText = EndReasonText((RoundSystem.EndReason)endReasonInt, winner);
            bool myWinR = winner == playerTeam;
            if (matchOver)
            {
                phase = Phase.MatchOver;
                ResetReadyGate(); // R 동의 게이트 (2026-09-05)
                bool myWin = winner == playerTeam;
                hud.SetMatchEndReason(reasonText);
                StartCoroutine(RoundEndBeat(reasonText, myWinR, EndFocusUnit((RoundSystem.EndReason)endReasonInt), () =>
                {
                    // 호스트 확정 스탯 우선 — 클라 로컬 집계엔 피해·점령초가 없다 (2026-09-05)
                    if (hostStats != null && hostStats.Length > 0)
                    {
                        var list = new List<BattleHud.MatchStatEntry>();
                        foreach (var st in hostStats)
                        {
                            var slot = FindSlot(st.unitId);
                            list.Add(new BattleHud.MatchStatEntry
                            { name = slot.callsign, cls = (int)slot.cls, team = slot.team,
                              kills = st.k, deaths = st.d, damage = st.dmg, captureSec = st.cap });
                        }
                        hud.SetMatchStats(list.ToArray());
                    }
                    else hud.SetMatchStats(BuildMatchStats());
                    hud.ShowMatchEnd();
                    battleAudio.PlayBgm(myWin ? "B4_Victory" : "B5_Defeat", loop: false);
                }));
            }
            else
            {
                phase = Phase.Briefing;
                ResetReadyGate(); // 클라도 SPACE 동의 상태 초기화
                StartCoroutine(RoundEndBeat(reasonText, myWinR, EndFocusUnit((RoundSystem.EndReason)endReasonInt), () =>
                {
                    hud.SetBriefingStats(BuildRoundStats(playerTeam), BuildRoundStats(1 - playerTeam));
                    hud.SetBriefingReason(reasonText);
                    // 거점·생존은 호스트 확정치 — 클라 미러는 스냅샷 지연으로 어긋날 수 있다 (2026-09-05 "동기화가 늦나 봐")
                    var owners = hostZoneOwners != null && hostZoneOwners.Length > 0 ? hostZoneOwners : ZoneOwners();
                    int aMine = playerTeam == 0 ? hostAlive0 : hostAlive1;
                    int aFoe = playerTeam == 0 ? hostAlive1 : hostAlive0;
                    hud.ShowBriefing(endedRound, winner, briefing, owners, aMine, aFoe);
                    battleAudio.PlayBgm("B3_Briefing");
                    battleAudio.SetTypingLoop(true);
                }));
            }
        }

        // ── 클라 스냅샷 차분 연출 — 이벤트 릴레이 전 단계의 근사치 ──

        void OnNetDamage(int unitId, int dmg)
        {
            if (!IsNetClient || Battle == null) return;
            var view = viewRegistry.Get(unitId);
            view?.PlayHit(Vector3.zero);
            if (view != null && view.gameObject.activeInHierarchy)
            {
                FloatingText.Spawn(view.transform.position, $"-{dmg}",
                    dmg >= 3 ? new Color(1f, 0.45f, 0.15f) : new Color(1f, 0.25f, 0.2f),
                    Mathf.Min(0.9f + dmg * 0.22f, 1.7f)); // 데미지 비례 크기 — 호스트 경로와 동일
                HitStop.Do(0.02f + 0.012f * dmg);
            }
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
                viewRegistry.Get(unitId)?.PlayDeath(Vector3.zero);
                ImpactVfx.Sparks(gridView.CoordToWorld(dead.pos), machine: dead.team == 1, scale: 1.8f); StrikeVfx.Kill(gridView.CoordToWorld(dead.pos), dead.team == 1);
                CellFlash.Spawn(gridView.CoordToWorld(dead.pos), new Color(1f, 0.2f, 0.15f), 0.6f, 1.1f);
                FloatingText.Spawn(gridView.CoordToWorld(dead.pos), "격파!", new Color(1f, 0.3f, 0.2f), 1.4f, 1.1f);
            }
        }

        void OnNetZoneOwner(int zoneIdx, int owner)
        {
            if (!IsNetClient || Round == null || zoneIdx >= Round.Zones.Count) return;
            if (Round.Rule != null) { Round.Rule.OnZoneCaptured(zoneIdx); Round.SyncZoneActive(); } // 봉쇄 해제 — 호스트와 같은 규칙 진행 (zone.active는 스냅샷에 없다)
            var zone = Round.Zones[zoneIdx];
            lastCapturerId = FindUnitOnZone(zone.cells, owner); // 종료 포커싱 후보 (미러 기준)
            bool ours = owner == playerTeam;
            battleAudio.PlaySfx(ours ? "S12a_ZoneCaptured" : "S12b_ZoneLost", 1.5f);

            // 중앙 큰 공지 — 호스트와 동일하게 (클라도 "누가 어느 거점 먹었는지" 봄)
            string letter = zoneIdx < ZoneLetters.Length ? ZoneLetters[zoneIdx] : "";
            hud.ShowAnnounce(ours
                ? $"아군이 {letter} 거점을 점령했습니다"
                : $"상대팀이 {letter} 거점을 점령했습니다", teamColors[owner], 2.8f);
            hud.PushEvent(ours ? $"아군이 {letter} 거점 점령!" : $"상대팀이 {letter} 거점 점령!", teamColors[owner]);

            var center = ZoneWorldCenter(zone);
            ImpactVfx.Pillar(center, Color.Lerp(teamColors[owner], Color.white, 0.4f)); // 링 제거 — 기둥 전용 (가독성 패스)
        }

        /// <summary>맵 랜덤 확정 + 맵 종속 상태 조립 → 클래스 선택으로.</summary>
        void PickRandomMap()
        {
            mapIndex = UnityEngine.Random.Range(0, BattleMaps.Count);
            if (GameModeState.Training) // 훈련장 — 제일 작은 맵
            {
                int bestArea = int.MaxValue;
                for (int i = 0; i < BattleMaps.Count; i++)
                {
                    var m = BattleMaps.Get(i);
                    if (m.Width * m.Height < bestArea) { bestArea = m.Width * m.Height; mapIndex = i; }
                }
            }
            map = BattleMaps.Get(mapIndex);
            gridConfig = new GridConfig { width = map.Width, height = map.Height };
            predictor = NewPredictor();
            hackSystem = NewHackSystem();
            Debug.Log($"맵 랜덤 → [{map.Name}] ({map.Width}x{map.Height})");
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
            if (GameModeState.Training) mineIdx.RemoveAll(i => roster[i].id != playerUnitId); // 훈련장 — 팀원 없음, 내 캐릭터만
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
            if (GameModeState.Training)
            {
                var me = FindRoster(playerUnitId);
                matchSetup = new MatchSetup
                {
                    mapIndex = mapIndex, enemyRollSeed = enemyRollSeed,
                    slots = new[]
                    {
                        new SlotConfig { unitId = me.id, team = me.team, cls = me.cls, callsign = ClassNames.For(me.team, me.cls), owner = SlotOwner.LocalHuman },
                        new SlotConfig { unitId = TrainingDummyId, team = 1, cls = UnitClass.Tank, callsign = "허수아비", owner = SlotOwner.Bot },
                    }
                };
                humanUnitIds = matchSetup.HumanUnitIds();
                return;
            }
            var slots = new SlotConfig[roster.Length];
            for (int i = 0; i < roster.Length; i++)
                slots[i] = new SlotConfig
                {
                    unitId = roster[i].id,
                    team = roster[i].team,
                    cls = roster[i].cls,
                    callsign = roster[i].id == playerUnitId ? ClassNames.For(roster[i].team, roster[i].cls)
                                                             : ClassNames.Nick(roster[i].team, roster[i].cls), // 봇 = 너굴/라니/깜냥/둘기/까돌
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
                battleAudio.PlaySfx("S27_Hack", 2.2f); // 시야해킹 전용음
                hud.ShowSubtitle(u != null && u.team == playerTeam
                    ? "시야해킹. 적 예측 마비" : "시야해킹 감지. 예측 교란", 2.4f);
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
            EnsureRadio(); // 매 라운드 — StartNextRound는 2라운드부터라 1라운드에 무전창이 안 생겼다
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
                Debug.Log($"맵 로테이션 → [{map.Name}] ({map.Width}x{map.Height})");
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

            // 라운드 규칙 — 매 라운드 추첨. 시드는 매치 롤 시드 + 라운드라 호스트·클라가 같은 규칙을 뽑는다.
            // 제한시간은 규칙이 깎을 수 있으므로 사본을 만들어 쓴다(원본 설정은 그대로 둔다).
            Rule = Guide.TutorialMode || GameModeState.Training ? null // 튜토리얼·훈련장 — 규칙 변형은 소음
                : RoundRules.Roll(Match.CurrentRound, map.Zones.Count, enemyRollSeed + Match.CurrentRound * 7919);
            var roundCfg = new RoundConfig
            {
                captureSeconds = roundConfig.captureSeconds,
                captureStackBonus = roundConfig.captureStackBonus,
                decaySeconds = roundConfig.decaySeconds,
                roundSeconds = roundConfig.roundSeconds + (Rule != null ? Rule.RoundSecondsDelta : 0f)
            };
            Round = new RoundSystem(Battle, roundCfg, map.Zones) { Rule = Rule };
            Round.SyncZoneActive();
            Combat.Rule = Rule;
            Move.Rule = Rule;
            // 시전 중 이동 금지 (2026-09-05 "시전하는 동안 못 움직이게, 모두") — 평타 예고 포함 전부
            Move.IsCastingFn = unitId =>
            {
                foreach (var st in Combat.PendingStrikes)
                    if (st.attackerId == unitId) return true;
                return false;
            };

            // 해킹 궁게이지 — 슬롯 확보(충전은 라운드 넘겨 유지) + 적중 데미지 충전 배선.
            // Combat은 라운드마다 새로 나므로 매번 재구독 (이전 Combat은 통째로 버려짐).
            var hackUnitIds = new List<int>();
            foreach (var s in matchSetup.slots) hackUnitIds.Add(s.unitId);
            hackSystem.BeginRound(hackUnitIds);
            Combat.OnDamageDealt += hackSystem.NotifyDamage;
            Combat.OnDamageDealt += (attackerId, dealt) =>
            {
                matchDamage[attackerId] = matchDamage.TryGetValue(attackerId, out var mdd) ? mdd + dealt : dealt; // 화력 부문 (2026-09-05)
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
            // 탱고파이브식 명중 판정 — 공격 팀이 피격자를 못 보면(안개) 빗나갈 수 있다
            Combat.TeamVisibleFn = (team, c) => vision.IsVisibleTo(team, c);
            Pickup = new PickupSystem(Battle, pickupConfig, map.HealPacks);
            Move.OnUnitMoved += (id, path, _) => Pickup.OnUnitPath(id, path); // 경로 통과 픽업 — 멈추지 않아도 먹는다
            Move.OnUnitMoved += (id, _, __) => { if (id == playerUnitId) Guide.MarkMove(); };      // 가이드 ② — 한 번 움직이면 힌트 끝
            Combat.OnTelegraph += strike => { if (strike.attackerId == playerUnitId) Guide.MarkAttack(); };
            Combat.OnSkillCast += (id, _) => { if (id == playerUnitId) Guide.MarkAttack(); };

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
                battleAudio.PlaySfx("S32_Heal", 0.8f); // 힐 전용음
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

            // 거점 바닥 틴트 폐지 (2026-09-05) — 파랑/노랑 이동 채널과 헷갈렸다.
            // 소유는 패치 외곽 네온 테두리(ZoneBorder)가 말한다. 생성은 아래 게이지 루프에서.

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
                var disc = ZoneCaptureDisc.Create(transform, ZoneWorldCenter(z), w, d);
                zoneDiscs.Add(disc);
                roundObjects.Add(disc.gameObject);

                // 거점 테두리 띠 — 멀리서도 소유가 읽히게 (중립 흰 / 점령 팀색)
                var ring = ZoneBorderRing.Create(transform, ZoneWorldCenter(z), w, d);
                zoneBorders.Add(ring);
                roundObjects.Add(ring.gameObject);
            }

            foreach (var s in matchSetup.slots)
            {
                var view = CreateUnitView();
                view.name = $"Unit_{s.unitId}_{s.cls}";
                // 클래스별 덩치 차이 — 모델 들어오기 전 임시 구분 (Bind 전에 적용해야 기준 스케일로 잡힘)
                view.transform.localScale *= ViewScale(s.cls);
                view.Bind(s.unitId, teamColors[s.team], gridView, map.Spawns[s.unitId]);
                view.AttachClassAccent(ClassHue(s.cls)); // 발밑 클래스 링 — 캐릭터 구분 (스킬 팔레트와 동기)
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
            hud.ShowChatCheatsheet = true; // 빠른채팅은 펼친 채로 진입 (Tab으로 접기) — 2026-09-05 유저: 기본 열어둬
            // input.Init은 아래에서 intentSink 생성 직후 호출

            Round.OnZoneCaptured += zone =>
            {
                lastCapturerId = FindUnitOnZone(zone.cells, zone.owner); // 종료 포커싱 후보 (2026-09-05)
                Debug.Log($"거점 {zone.Center} → 팀 {zone.owner} 탈환"); // 소유 색은 네온 테두리가 (틴트 폐지)

                bool ours = zone.owner == playerTeam;
                battleAudio.PlaySfx(ours ? "S12a_ZoneCaptured" : "S12b_ZoneLost", 1.5f);

                // 맵 전체 링 제거 (가독성 패스 2026-09-05) — 링은 충격파 전용으로 회수.
                // 거점의 시그니처 = 빛기둥 + 상단 공지 + 보이스로 이미 3중.

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

                // 탈환 완료 순간 — 빛기둥 (링은 충격파 전용으로 회수)
                var center = ZoneWorldCenter(zone);
                ImpactVfx.Pillar(center, Color.Lerp(teamColors[zone.owner], Color.white, 0.4f));
                CameraShaker.Shake(0.2f);
            };
            Round.OnOvertime += _ =>
            {
                Debug.Log("추가시간! 다음 탈환 또는 킬로 즉시 승부");
                battleAudio.PlaySfx("S15_SuddenDeath", 2f);
                PlayVoiceLine("Voice_SuddenDeath", "추가시간");
                ImpactFx.SetSuddenDeath(true); // 화면 가장자리 맥동 시작
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
                if (s.owner == SlotOwner.Bot && !IsNetClient && !GameModeState.Training) // 클라는 AI 안 돌림 — 호스트 권위. 허수아비는 뇌 없음
                {
                    // 예측 뇌는 전 봇 공통 (2026-09-05 연계 패스) — 아군 봇도 적을 예측 사격해
                    // "읽고 쏘는" 플레이가 화면에 등장한다. 인간 학습 데이터는 여전히 적팀만 유효하게 쌓인다.
                    // 지휘는 "인간 지휘관이 있는 팀"의 봇에게. 싱글 지휘관 = 내 팀만, 지휘관 대전 = 양 팀 (각자 자기 인간).
                    bool commandable = GameModeState.IsCommander && TeamHasHuman(s.team);
                    var driver = new AiSlotDriver(s.unitId, s.cls, s.team, intentSink, predictor,
                        commandable ? teamOrders[s.team] : null);
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
                if (!IsNetClient)
                    ObserveUnitPath(unitId, path); // 전 유닛 학습 (2026-09-05) — 봇 경로도 쌓여야 아군 봇의 예측샷이 성립
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
                // 시전 런지 (타격감 2차 2026-09-05) — 근거리 예고는 공격자가 몸을 내민다. 인과가 몸짓으로 읽힌다.
                var lungeAtk = Battle.GetUnit(strike.attackerId);
                if (lungeAtk != null && lungeAtk.alive && IsUnitVisibleToPlayer(strike.attackerId) &&
                    Math.Max(Math.Abs(strike.aimCell.x - lungeAtk.pos.x), Math.Abs(strike.aimCell.y - lungeAtk.pos.y)) <= 2)
                    viewRegistry.Get(strike.attackerId)?.PlayLunge(gridView.CoordToWorld(strike.aimCell));

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

                    // 밀침 화살표 — 예고 칸에 서 있는 유닛이 어디로 밀릴지 바닥에 그린다.
                    // 연계의 전제: 밀릴 자리가 미리 보여야 폭격 유닛이 그 자리를 선점할 수 있다.
                    if (fx != null && strike.pushCells > 0 && strike.pushDir != Coord.Zero)
                    {
                        foreach (var c in strike.cells)
                        {
                            if (!mineStrike && !playerVisibleFn(c)) continue;
                            int victimId = Battle.Grid.GetUnitAt(c);
                            if (victimId == SeoYuGi.Battle.Cell.NoUnit) continue; // Prediction.Cell과 이름 충돌 — 정규화

                            // 이 칸에 이미 더 빨리 터지는 예고의 화살표가 있으면 그게 진짜다 — 덧그리지 않는다
                            if (pushArrowByCell.TryGetValue(c, out var prev) && prev.arrow != null)
                            {
                                if (prev.impactTime <= strike.impactTime) continue;
                                Destroy(prev.arrow.gameObject); // 내가 더 먼저 터진다 — 이전 것을 대체
                            }

                            var dest = Combat.PreviewPush(c, strike.pushDir, strike.pushCells, out bool crash);
                            if (dest == c && !crash) continue; // 못 밀린다 — 그릴 게 없다

                            // 초점 계층: 내가 밀리는 게 제일 급하고, 우리 팀 공격은 연계 재료, 나머지는 배경
                            bool onMe = victimId == playerUnitId;
                            var col = StrikeVfx.TeamColor(mineStrike);
                            col.a = onMe ? 0.85f : mineStrike ? 0.6f : 0.3f;
                            float w = onMe ? 1.15f : mineStrike ? 1f : 0.7f;

                            pushArrowByCell[c] = (PushArrow.Create(fx.transform, gridView.CoordToWorld(c),
                                gridView.CoordToWorld(dest), col, crash, w), strike.impactTime);
                        }
                    }

                    // 폭탄 배달·낚아채기 — 왕복 비행 (시뮬 위치는 출발 칸 그대로, 연출만 난다).
                    // 비행 여부로 판단한다 — 낚아채기는 단일 칸이라 예전 cells.Count > 1 조건에 걸리지 않았다.
                    if (telegraphAttacker.unitClass == UnitClass.Grenadier && Combat.IsFlying(telegraphAttacker))
                    {
                        var flier = viewRegistry.Get(strike.attackerId);
                        if (flier != null && flier.gameObject.activeInHierarchy)
                        {
                            // 날아가는 시간 = 예고 시간. 도착이 곧 판정 — 돌아오는 구간(0.5s)은 판정 뒤의 연출.
                            float outDur = Mathf.Max(0.2f, strike.impactTime - Battle.time);
                            if (strike.kind == SkillKind.Snatch) // 낚아채기 — 대상을 발톱에 걸고 돌아온다 (폭탄 로프트 아님)
                            {
                                int vId = Battle.Grid.GetUnitAt(strike.cells[0]);
                                var victimView = vId != SeoYuGi.Battle.Cell.NoUnit ? viewRegistry.Get(vId) : null;
                                flier.PlaySnatchFlight(gridView.CoordToWorld(strike.cells[0]),
                                    victimView != null ? victimView.transform : null, outDur, 0.5f);
                            }
                            else
                                flier.PlayBombFlight(gridView.CoordToWorld(strike.cells[0]), outDur, 0.5f);
                        }
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

                // 예측 사격 결과 (G) — AI 학습 서사 자막은 폐기(2026-09-05), 타격 연출만 남긴다.
                // 보라 = 예측. 일반 명중(주황 기둥)과 색으로 구분돼야 "읽고 쐈다"가 읽힌다.
                if (predictedStrikes.Remove(strike))
                {
                    var focus = gridView.CoordToWorld(strike.cells[strike.cells.Count / 2]);
                    bool againstMe = strike.team != playerTeam;
                    if (hit && againstMe)
                    {
                        battleAudio.PlaySfx("S26_PredictHit", 1.4f); // 예측 명중 스팅어 — 컨셉 상징음
                        ImpactVfx.Pillar(focus, new Color(0.8f, 0.45f, 1f));
                        ImpactFx.Punch(0.85f);
                        CameraShaker.Shake(0.4f);
                    }
                    else if (hit)
                    {
                        // 아군 봇의 예측 적중 — 팀이 "읽고 쏘는" 걸 보여주는 연계 연출
                        battleAudio.PlaySfx("S26_PredictHit", 1.2f);
                        ImpactVfx.Pillar(focus, new Color(0.8f, 0.45f, 1f));
                        FloatingText.Spawn(focus, "예측 적중!", new Color(0.85f, 0.6f, 1f), 1.15f, 0.9f);
                    }
                    else if (againstMe)
                    {
                        ImpactVfx.Sparks(focus, machine: true, scale: 0.8f); // 빗나간 조준이 흩어짐
                    }
                }
            };
            Combat.OnStunned += (_, __) => battleAudio.PlaySfx("S30_Stun", 1f); // 스턴 = 둔탁한 퍽 (재생성본 — 고역 없음 검증)
            Combat.OnMissed += (victimId, attackerId) =>
            {
                // 엄폐/은신 회피 성공 — "빗나감"이 피격자 위에 떠야 벽 뒤에 서는 플레이가 학습된다
                var vu = Battle.GetUnit(victimId);
                if (vu == null || !(vu.team == playerTeam || playerVisibleFn(vu.pos))) return;
                FloatingText.Spawn(gridView.CoordToWorld(vu.pos) + Vector3.up * 0.4f, "빗나감",
                    new Color(0.65f, 0.7f, 0.78f), 0.95f, 0.8f);
                battleAudio.PlaySfx("S4_Miss", 0.6f);
            };

            Combat.OnIntercepted += blockerId =>
            {
                // 탄도 요격 가시화 — 표시가 없으면 "관통 버그"처럼 읽힌다 (2026-09-05)
                var bu = Battle.GetUnit(blockerId);
                if (bu != null && (bu.team == playerTeam || playerVisibleFn(bu.pos)))
                    FloatingText.Spawn(gridView.CoordToWorld(bu.pos) + Vector3.up * 0.55f, "차단!",
                        new Color(1f, 0.78f, 0.25f), 1.05f, 0.8f);
            };
            Combat.OnStunCombo += (victimId, attackerId) =>
            {
                // 연계 성사 — "스턴 중 추가타 +1"이 화면에 보상으로 찍힌다 (연계 패스 2026-09-05)
                var vu = Battle.GetUnit(victimId);
                if (vu == null || !(vu.team == playerTeam || playerVisibleFn(vu.pos))) return;
                FloatingText.Spawn(gridView.CoordToWorld(vu.pos) + Vector3.up * 0.5f, "연계!",
                    new Color(1f, 0.8f, 0.25f), 1.25f, 0.9f);
                if (attackerId == playerUnitId) CameraShaker.Shake(0.15f); // 내 연계는 몸으로도
            };
            Combat.OnSkillCast += (unitId, kind) =>
            {
                battleAudio.PlaySfx(SkillSfx(kind), 1.5f);
                // 즉발 이동기(대시·점멸)는 캐스팅 순간에 무게 — 예고형은 판정 시 피해 셰이크가 담당
                if (kind == SkillKind.Dash && IsUnitVisibleToPlayer(unitId)) CameraShaker.Shake(0.18f);

                // 스킬 특성별 시전 VFX — 시야 안일 때만 (정보 누출 방지)
                if (IsUnitVisibleToPlayer(unitId))
                    SkillVfx.Cast(kind, gridView.CoordToWorld(Battle.GetUnit(unitId).pos), Battle.GetUnit(unitId).team == playerTeam, focus: unitId == playerUnitId);

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
            // 밀림 궤적 — 2칸 이상이거나 벽꿍이면 포물선. 1칸 밀침은 기존 미끄러짐(위치 동기화)이 자연스럽다.
            Combat.OnPushed += (unitId, from, to, crashed) =>
            {
                int cells = Mathf.Max(Mathf.Abs(to.x - from.x), Mathf.Abs(to.y - from.y));
                if (cells < 2 && !crashed) return;
                if (NetBoot.IsOnline && NetBoot.IsHost)
                    NetSync.HostSendPushed(unitId, to, crashed); // 클라도 포물선 — 없으면 스냅샷 순간이동 (2026-09-05)
                var v = viewRegistry.Get(unitId);
                if (v == null || !v.gameObject.activeInHierarchy) return; // 시야 밖 — 다시 보일 때 스냅
                v.PlayThrow(to, crashed);
            };
            Combat.OnSnatched += (unitId, from, to) =>
            {
                // 낚아채기 끌기 — 호스트 화면은 비행 연출이 대상을 직접 끌지만, 클라는 릴레이가 없으면 순간이동
                if (NetBoot.IsOnline && NetBoot.IsHost)
                    NetSync.HostSendPushed(unitId, to, false);
            };
            Combat.OnCharged += (unitId, from, to) =>
            {
                // 방패 밀어붙이기·던지기 돌진 — 빠른 미끄러짐 + 후방 바람 줄기 (고라니 돌파와 같은 문법)
                if (NetBoot.IsOnline && NetBoot.IsHost)
                    NetSync.HostSendPushed(unitId, to, false);
                var v = viewRegistry.Get(unitId);
                if (v != null && v.gameObject.activeInHierarchy)
                {
                    v.CancelMove();
                    v.PlaySlide(to, 0.13f);
                    var w0 = gridView.CoordToWorld(from);
                    var w1 = gridView.CoordToWorld(to);
                    var dir = (w1 - w0).normalized;
                    for (int i = 0; i < 3; i++)
                        FxQuad.One(VfxTextures.Wind, w0 + Vector3.up * 0.35f + dir * (i * 0.3f),
                            new Color(0.9f, 0.95f, 1f), 1.8f, 0.6f, 0.25f, velocity: -dir * 4f);
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
                    HitStop.Do(0.3f, 0.35f); // 킬 슬로모 — 완전 정지 대신 느린 0.3초 (타격감 2차)
                    ImpactFx.DeathFlash();
                    viewRegistry.Get(unitId)?.PlayDeath(Vector3.zero); // 쓰러짐 — 펑 사라지지 않게
                    ImpactVfx.Sparks(gridView.CoordToWorld(dead.pos), machine: dead.team == 1, scale: 1.8f); StrikeVfx.Kill(gridView.CoordToWorld(dead.pos), dead.team == 1);
                    battleAudio.PlayThump(big: true);
                }
            };

            Combat.OnUnitDamaged += (unitId, dmg, hitDir) =>
            {
                var victim = Battle.GetUnit(unitId);
                if (GameModeState.Training && unitId == TrainingDummyId)
                {
                    victim.hp = victim.maxHp; // 죽지 않는다 — 이 핸들러는 사망 판정보다 먼저 불린다
                    trainingLastHitAt = Time.time;
                    hud.PushEvent($"허수아비 피격 -{dmg}", teamColors[playerTeam]);
                }
                var victimView = viewRegistry.Get(unitId);
                victimView?.PlayHit(new Vector3(hitDir.x, 0f, hitDir.y)); // 리코일 틸트 + 플래시
                if (victimView != null && victimView.gameObject.activeInHierarchy)
                    FloatingText.Spawn(victimView.transform.position, $"-{dmg}",
                        dmg >= 3 ? new Color(1f, 0.45f, 0.15f) : new Color(1f, 0.25f, 0.2f), // 큰 딜은 주황빛으로 격상
                        Mathf.Min(0.9f + dmg * 0.22f, 1.7f));                                 // 데미지 비례 크기
                Debug.Log($"유닛 {unitId} 피해 {dmg} (HP {victim.hp}/{victim.maxHp})");

                // 점령 저지 (2026-09-05): 점령 진행 중인 팀원이 거점 위에서 맞으면 게이지가 깎인다.
                // 코어는 즉시, 화면 게이지는 디스크가 부드럽게 흘러내리며 빨간 플래시로 알린다.
                for (int zi = 0; zi < Round.Zones.Count; zi++)
                {
                    var z = Round.Zones[zi];
                    if (z.capturingTeam != victim.team || z.progress <= 0f) continue;
                    if (!z.cells.Contains(victim.pos)) continue;
                    z.progress = Mathf.Max(0f, z.progress - roundConfig.captureSeconds * 0.3f);
                    if (zi < zoneDiscs.Count) zoneDiscs[zi].FlashPenalty();
                    if (victim.team == playerTeam || playerVisibleFn(victim.pos))
                        FloatingText.Spawn(gridView.CoordToWorld(z.Center) + Vector3.up * 0.3f, "점령 저지!",
                            new Color(1f, 0.45f, 0.3f), 1.05f, 0.9f);
                    break;
                }

                battleAudio.PlaySfx("S9_Hurt", 0.6f);
                if (IsUnitVisibleToPlayer(unitId))
                {
                    HitStop.Do(0.02f + 0.012f * dmg); // 마이크로 히트스톱 — 격파용(0.08+)보다 훨씬 짧아 가독성 유지
                    // 가독성 다이어트: 셰이크·히트스톱·비네트는 격파 전용. 일반 피격은 칸 안 연출 + 숫자 + 소리만.
                    // 내가 맞았을 때만 아주 짧은 셰이크 — "내 문제"는 몸으로 알아야 하니까.
                    if (unitId == playerUnitId) CameraShaker.Shake(0.12f);
                    var hitPos = gridView.CoordToWorld(victim.pos);
                    // 관련도 위계 (가독성 패스 2026-09-05): 내 유닛에서 먼 전투는 작게 — 내 일만 크게 터진다.
                    // 칸플래시는 제거 — 스파크·리코일·숫자와 겹쳐 "번쩍임의 벽"만 만들었다.
                    var me = Battle.GetUnit(playerUnitId);
                    int prox = me != null && me.alive
                        ? Math.Max(Math.Abs(victim.pos.x - me.pos.x), Math.Abs(victim.pos.y - me.pos.y)) : 99;
                    float fxScale = unitId == playerUnitId || prox <= 4 ? 1.4f : 0.8f;
                    ImpactVfx.Sparks(hitPos, machine: victim.team == 1, scale: fxScale);
                    StrikeVfx.HitReaction(hitPos, machine: victim.team == 1);
                    battleAudio.PlayThump(big: false);
                }
            };

            // 플로팅 텍스트 — 누가 뭘 하는지 머리 위에 뜸 (시야 안일 때만)
            Combat.OnSkillCast += (unitId, kind) =>
            {
                var v = viewRegistry.Get(unitId);
                bool viewActive = v != null && v.gameObject.activeInHierarchy;
                // 스킬명 텍스트 제거 (가독성 패스 2026-09-05) — 예고 스킬 아이콘이 대체

                if (kind == SkillKind.Blink)
                {
                    // 순간이동이 정체성 — 걷기·슬라이드 없이 그 즉시 사라졌다 나타난다.
                    // 출발지: 검은 잔상 + 연기 / 도착지: 등장 이펙트 (SkillVfx.Cast가 도착지 담당)
                    var unit = Battle.GetUnit(unitId);
                    if (viewActive)
                    {
                        var from = v.transform.position;
                        GhostTrail.SpawnAt(v.gameObject, from, new Color(0.12f, 0.06f, 0.2f, 0.85f));
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
                        // 흰색 CellFlash 제거 (2026-09-05 "흰 박스 로우폴리") — 민짜 쿼드가 사각형으로 보였다.
                        // 판정 순간은 소프트 글로우(매트 노브 통제)로: 형태 없는 빛 번짐.
                        FxQuad.One(VfxTextures.Glow, gridView.CoordToWorld(c) + Vector3.up * 0.25f,
                            new Color(1f, 0.92f, 0.8f), 1.5f, 0.5f, 0.18f);
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

                // 인과선 (가시성 패스 2026-09-05): 공격자 → 피격 칸으로 팀색 선이 그어진다.
                // 스파크만으론 "누가 때렸는지" 방향이 없어서 사건이 안 읽혔다. 내 관련이면 조금 오래.
                if (striker != null && striker.alive && visCells.Count > 0)
                {
                    bool mineStrike = strike.team == playerTeam;
                    LaserBeam.Flash(
                        gridView.CoordToWorld(striker.pos) + Vector3.up * 0.55f,
                        gridView.CoordToWorld(strike.aimCell) + Vector3.up * 0.3f,
                        StrikeVfx.TeamColor(mineStrike),
                        strike.attackerId == playerUnitId || StrikeCoversPlayer(strike) ? 0.26f : 0.15f);
                }

                // 내 공격 판정 표기 — 입력→결과 루프 닫기: 명중/빗나감이 그 자리에 뜬다
                if (strike.attackerId == playerUnitId)
                    FloatingText.Spawn(gridView.CoordToWorld(strike.aimCell),
                        hit ? "명중!" : "빗나감",
                        hit ? new Color(0.5f, 1f, 0.95f) : new Color(0.6f, 0.65f, 0.72f),
                        hit ? 1.2f : 0.9f, 0.8f);
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
                // 처치 보너스 — 해킹 게이지 +12% (피해 1 상당). 킬이 궁극기로 이어지는 모멘텀
                var killerUnit = Battle.GetUnit(killerId);
                if (killerUnit != null && killerUnit.alive && hackSystem != null)
                    hackSystem.NotifyDamage(killerId, 1);

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
                    // 링·칸플래시 제거 (가독성 패스 2026-09-05) — 스턴의 시그니처는 머리 위 별. 링은 충격파 전용으로 회수.
                    FloatingText.Spawn(world, "스턴!", StunVfx.Gold, 0.9f, 0.7f);
                    var view = viewRegistry.Get(unitId);
                    if (view != null) StunVfx.Ensure(view.transform, seconds);
                }
                if (unitId == playerUnitId) { ImpactFx.Punch(0.5f); ImpactFx.SetGlitch(0.25f); } // 내가 맞음 — 화면이 휘청
            };

            humanPrevPos.Clear();
            foreach (var u in Battle.Units)
                humanPrevPos[u.id] = u.pos; // 전 유닛 — 봇 경로도 학습 (연계 패스)
            audioVisibleEnemies.Clear();
            ImpactFx.SetSuddenDeath(false); // 새 라운드 — 이전 라운드의 적색 맥동·잔여 글리치 제거
            input.enabled = false; // 카운트다운 종료 시 해제 — 클라도 조작(인텐트는 NetIntentSink가 호스트로 전송)
            phase = Phase.Playing;
            Time.timeScale = 1f; // 안전 복원 — 히트스톱·빨리감기 잔재가 남아 게임이 멈춘 듯 보이는 사고 방지 (2026-09-05 프리즈 보고)
            if (GameModeState.Training)
            {
                trainingDummyHome = map.Spawns[TrainingDummyId];
                trainingLastHitAt = Time.time;
                hud.ShowAnnounce("훈련장. F1~F5 캐릭터 교체 / 허수아비는 죽지 않음 / ESC 메뉴로 나가기", Color.white, 6f);
            }
            else if (Guide.Wanted)
            {
                // ① 가이드 — 거점부터. 목표 문구 대신 규칙 한 줄 + 거점 링 (싱글은 정지를 5.5초로 늘려 읽을 시간)
                hud.ShowAnnounce("거점을 밟으면 게이지가 찬다. 더 많이 가진 팀이 이긴다", Color.white, 5.5f);
                foreach (var z in Round.Zones)
                    RingWave.Spawn(gridView.CoordToWorld(z.Center), new Color(1f, 1f, 1f, 0.9f), 2.6f, 1.6f);
                if (GameModeState.IsCommander && !NetBoot.IsOnline && Match.CurrentRound <= 1)
                {
                    Guide.Begin(); // ②③은 싱글 지휘관만
                    hud.PushEvent("튜토리얼 1/3. 거점: 밟으면 게이지가 찬다, 더 많이 가진 팀이 이긴다", StrikeVfx.MineNeon);
                    guideStep2Announced = false;
                }
            }
            else hud.ShowAnnounce("목표. 거점을 모두 점령하거나, 적을 전멸시켜라", Color.white, 4f); // 판세 피드백: 승리 조건 명시
            prevMyZones = prevEnemyZones = -1; // 거점 우세 경보 리셋
            killStreaks.Clear(); // 멀티킬 스트릭 리셋
            roundKills.Clear(); roundDeaths.Clear(); // 라운드 전적 리셋
            lastKillerId = -1; lastCapturerId = -1;
            if (Match.CurrentRound <= 1) { matchKills.Clear(); matchDeaths.Clear(); matchDamage.Clear(); matchCapture.Clear(); } // 새 매치 — 누적도 백지
            spectateUnitId = -1;
            countdownUntil = Time.time + (Guide.Wanted && !NetBoot.IsOnline ? 5.5f : 3f); // 라운드 시작 3·2·1 — 첫 판 가이드는 거점 설명 읽을 시간만큼 더 (온라인은 호스트 시계라 그대로)
            countdownRunning = true;
            if (radioTimeActive) EndRadioTime(); // 라운드 재조립 — 정지 잔재 제거
            nextRadioTimeAt = RadioTimeFirst;

            if (NetBoot.IsOnline && NetBoot.IsHost)
                NetSync.HostSendBeginRound(Match.CurrentRound); // 클라 — 같은 라운드 조립 신호
            if (IsNetClient)
                NetSync.ClientBind(Battle, Round, Pickup, Match.CurrentRound); // 스냅샷 수신 개시

            battleAudio.SetTypingLoop(false); // 브리핑 종료
            battleAudio.PlayBgm(Match.CurrentRound >= 3 ? "B2_Round3" : "B1_Round1");
            battleAudio.PlaySfx("S13_RoundStart", 1.5f);
            PlayVoiceLine("Voice_RoundStart", "라운드 개시");
            hud.SetRoundRule(Rule != null ? Rule.title : null); // 상시 칩 — 자막을 놓쳐도 규칙이 보인다 (2026-09-05)
            if (Rule != null)
            {
                // 규칙은 라운드 개시 자막 뒤에 이어 붙인다 — 전술을 정하기 전에 읽혀야 한다
                hud.ShowSubtitle($"[{Rule.title}] {Rule.detail}", 5f);
                Debug.Log($"라운드 규칙: {Rule.title}. {Rule.detail}");
            }
            // 기계팀 지휘관 — 분대가 상대 동물을 스캔해 모방한다는 컨셉 대사 (성격도 같은 동물을 따른다)
            if (GameModeState.IsCommander && playerTeam == 1)
                foreach (var id in CommandableUnitIds())
                    ShowRadioLine(id, Personas.MimicLine(FindSlot(id).cls));
            ReactToRule(); // 규칙을 분대가 알아듣고 먼저 움직인다 — "B 봉쇄 확인, A부터 갑니다"
            Debug.Log($"라운드 {Match.CurrentRound} 시작 (Predictor round={predictor.Round})");
        }

        void ClearRoundObjects()
        {
            foreach (var go in roundObjects)
                if (go != null) Destroy(go);
            roundObjects.Clear();
            hpBars.Clear();
            zoneDiscs.Clear();
            zoneBorders.Clear();
            healPackViews.Clear();
            blinkSnapIds.Clear();
            foreach (var kv in strikeTelegraphFx)
                if (kv.Value != null) Destroy(kv.Value);
            strikeTelegraphFx.Clear();
            pushArrowByCell.Clear();
            viewRegistry.Clear();
        }

        /// <summary>거점 바운딩박스의 월드 중점 — 짝수 크기(4×4) 거점은 Zone.Center(정수 칸)가 반 칸 어긋난다.</summary>
        Vector3 ZoneWorldCenter(Zone z)
        {
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            foreach (var c in z.cells)
            {
                if (c.x < minX) minX = c.x; if (c.x > maxX) maxX = c.x;
                if (c.y < minY) minY = c.y; if (c.y > maxY) maxY = c.y;
            }
            return (gridView.CoordToWorld(new Coord(minX, minY))
                  + gridView.CoordToWorld(new Coord(maxX, maxY))) * 0.5f;
        }

        /// <summary>거점 패치 중앙에 대형 A/B/C 글자 (탱고파이브식).</summary>
        void CreateZoneLabels()
        {
            for (int i = 0; i < Round.Zones.Count && i < ZoneLetters.Length; i++)
            {
                var go = new GameObject($"ZoneLabel_{ZoneLetters[i]}");
                zoneLabels.Add(go);
                go.transform.SetParent(transform);
                go.transform.position = ZoneWorldCenter(Round.Zones[i]) + Vector3.up * 0.06f;
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
            var tacCam = cam.GetComponent<SeoYuGi.Art.TacticalCamera>();
            if (tacCam != null) { tacCam.Retarget(); return; } // 재빌드 후 새 유닛 즉시 추적 — 재시작 검정화면 방지 (2026-09-05)
            if (cam.GetComponent<QuarterViewCamera>() != null) return; // 추적 캠 우선 — 프레이밍 양보
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
        /// <summary>라운드 전적 줄 — 팀별 "콜사인  Kn / Dn". 브리핑 패널 주입용 (2026-09-05).</summary>
        string[] BuildRoundStats(int team)
        {
            var lines = new List<string>();
            if (matchSetup != null)
                foreach (var slot in matchSetup.slots)
                    if (slot.team == team)
                    {
                        roundKills.TryGetValue(slot.unitId, out int k);
                        roundDeaths.TryGetValue(slot.unitId, out int d);
                        lines.Add($"{slot.callsign}  {k}킬 / {d}데스");
                    }
            return lines.ToArray();
        }

        /// <summary>매치엔드 초상 통계 — 슬롯 순서대로 (콜사인·클래스·팀·매치 누적 K/D).</summary>
        BattleHud.MatchStatEntry[] BuildMatchStats()
        {
            var list = new List<BattleHud.MatchStatEntry>();
            if (matchSetup != null)
                foreach (var slot in matchSetup.slots)
                {
                    matchKills.TryGetValue(slot.unitId, out int k);
                    matchDeaths.TryGetValue(slot.unitId, out int d);
                    matchDamage.TryGetValue(slot.unitId, out int dmg);
                    matchCapture.TryGetValue(slot.unitId, out float cap);
                    list.Add(new BattleHud.MatchStatEntry
                    { name = slot.callsign, cls = (int)slot.cls, team = slot.team, kills = k, deaths = d, damage = dmg, captureSec = cap });
                }
            return list.ToArray();
        }

        /// <summary>죽은 뒤 — 살아 있는 아군 시점으로 카메라 전환 (2026-09-05 관전 개선).
        /// dir = 0 첫 아군 / +1 다음 / -1 이전 (관전 중 ←/→ 순환).</summary>
        void SpectateFollowAlly(int dir = 0)
        {
            var cam = Camera.main != null ? Camera.main.GetComponent<SeoYuGi.Art.TacticalCamera>() : null;
            if (cam == null || matchSetup == null) return;

            var alive = new List<(int unitId, string name)>();
            foreach (var slot in matchSetup.slots)
            {
                if (slot.team != playerTeam || slot.unitId == playerUnitId) continue;
                var u = Battle.GetUnit(slot.unitId);
                if (u == null || !u.alive || viewRegistry.Get(slot.unitId) == null) continue;
                alive.Add((slot.unitId, slot.callsign));
            }
            if (alive.Count == 0) return;

            int idx = alive.FindIndex(a => a.unitId == spectateUnitId);
            idx = idx < 0 ? 0 : (idx + dir + alive.Count) % alive.Count;

            spectateUnitId = alive[idx].unitId;
            cam.Spectate(viewRegistry.Get(spectateUnitId).transform);
            hud.ShowAnnounce($"{alive[idx].name} 시점 관전  (좌우 화살표로 전환)", new Color(0.7f, 0.8f, 0.9f), 1.6f);
        }

        /// <summary>매치 확정 스탯 와이어 — (unitId, 킬, 데스, 피해, 점령초). 매치오버 릴레이용 (2026-09-05).</summary>
        (int, int, int, int, float)[] BuildStatWire()
        {
            var list = new List<(int, int, int, int, float)>();
            if (matchSetup != null)
                foreach (var slot in matchSetup.slots)
                {
                    matchKills.TryGetValue(slot.unitId, out int k);
                    matchDeaths.TryGetValue(slot.unitId, out int d);
                    matchDamage.TryGetValue(slot.unitId, out int dmg);
                    matchCapture.TryGetValue(slot.unitId, out float cap);
                    list.Add((slot.unitId, k, d, dmg, cap));
                }
            return list.ToArray();
        }

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
            roundDeaths[deadId] = roundDeaths.TryGetValue(deadId, out var dcnt) ? dcnt + 1 : 1;
            matchDeaths[deadId] = matchDeaths.TryGetValue(deadId, out var mdc) ? mdc + 1 : 1;
            if (killer != null)
            {
                roundKills[killerId] = roundKills.TryGetValue(killerId, out var kcnt) ? kcnt + 1 : 1;
                matchKills[killerId] = matchKills.TryGetValue(killerId, out var mkc) ? mkc + 1 : 1;
                lastKillerId = killerId; // 종료 포커싱 후보
            }

            // 관전 시점 전환 (2026-09-05): 내가 죽거나, 따라가던 아군이 죽으면 다음 산 아군에게
            if (deadId == playerUnitId || (Spectating && deadId == spectateUnitId))
                SpectateFollowAlly();

            hud.AddKill(killerName, victimName, feedColor);
            hud.PushEvent(killerName != null ? $"{killerName}이(가) {victimName} 처치!" : $"{victimName} 처치됨",
                feedColor); // 상단 배너 전황 로그
            hud.PingEdge(gridView.CoordToWorld(dead.pos), feedColor); // 프레임 밖 킬 — 가장자리 방향 화살표 (가시성 패스 D)

            // 멀티킬 콜아웃 (2026-09-05 모멘텀) — 잘 싸운 순간을 게임이 크게 불러준다
            if (killer != null)
            {
                int st = killStreaks.TryGetValue(killerId, out var ks) && Time.time < ks.until ? ks.streak + 1 : 1;
                killStreaks[killerId] = (st, Time.time + 7f);
                if (st >= 2)
                {
                    hud.ShowAnnounce($"{killerName}. {(st == 2 ? "더블 킬!" : "트리플 킬!")}", feedColor, 2.2f);
                    battleAudio.PlayThump(big: true);
                    CameraShaker.Shake(0.25f);
                }
            }
            if (!IsNetClient) // 보이스 파일 미보유 — 격파 SFX로 대체 (클라는 OnNetDeath가 이미 재생)
                battleAudio.PlaySfx(dead.team == playerTeam ? "S10a_DeathAlly" : "S10b_DeathEnemy", 1.5f);
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
                case SkillKind.Scream: return "S31_Scream"; // 재생성 저음 비명 — 고역 스펙트럼 검사 통과분 (2026-09-05)
                case SkillKind.Blink: return "S18_Blink";
                case SkillKind.Claw: return "S16_Smash";
                case SkillKind.Burst: return "S19_Burst";
                case SkillKind.BombDeliver: return "S19_Burst";
                case SkillKind.Snatch: return "S35_Snatch"; // 휙-턱 전용 합성음 (2026-09-05)
                case SkillKind.KnockShot: return "S20_Snipe";
                case SkillKind.Snipe: return "S20_Snipe";
                default: return "S3_Hit";
            }
        }

        /// <summary>유닛 이동을 홉 단위로 Predictor에 공급 — 학습 단위는 개체(슬롯) (세부기획 E).
        /// 2026-09-05: 인간 전용 → 전 유닛. 아군 봇의 적 봇 예측 사격에 필요.</summary>
        void ObserveUnitPath(int unitId, IReadOnlyList<Coord> path)
        {
            if (!humanPrevPos.TryGetValue(unitId, out var from)) from = Battle.GetUnit(unitId).pos;
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

        /// <summary>종료 사유 → 배너·브리핑 문구 (내 관점).</summary>
        string EndReasonText(RoundSystem.EndReason reason, int winnerTeam)
        {
            bool myWin = winnerTeam == playerTeam;
            switch (reason)
            {
                case RoundSystem.EndReason.Elimination: return myWin ? "적 전멸!" : "아군 전멸";
                case RoundSystem.EndReason.AllZones: return myWin ? "거점 전체 장악!" : "거점을 모두 내줬다";
                case RoundSystem.EndReason.TimeoutZones: return myWin ? "시간 종료. 거점 우세" : "시간 종료. 거점 열세";
                case RoundSystem.EndReason.TimeoutAlive: return myWin ? "시간 종료. 생존 우세" : "시간 종료. 생존 열세";
                case RoundSystem.EndReason.OvertimeKill: return "추가시간. 결정적 킬";
                case RoundSystem.EndReason.OvertimeCapture: return "추가시간. 거점 탈환";
                default: return null;
            }
        }

        /// <summary>거점 패치 위에 서 있는 해당 팀 유닛 하나 — 점령자 추정.</summary>
        int FindUnitOnZone(IReadOnlyCollection<Coord> cells, int team)
        {
            foreach (var c in cells)
            {
                int uid = Battle.Grid.GetUnitAt(c);
                if (uid != SeoYuGi.Battle.Cell.NoUnit && Battle.GetUnit(uid)?.team == team) return uid;
            }
            return -1;
        }

        /// <summary>종료 사유별 주인공 — 킬 마감이면 마지막 처치자, 거점 마감이면 마지막 점령자.</summary>
        int EndFocusUnit(RoundSystem.EndReason reason)
        {
            switch (reason)
            {
                case RoundSystem.EndReason.AllZones:
                case RoundSystem.EndReason.OvertimeCapture:
                case RoundSystem.EndReason.TimeoutZones:
                    return lastCapturerId >= 0 ? lastCapturerId : lastKillerId;
                default:
                    return lastKillerId >= 0 ? lastKillerId : lastCapturerId;
            }
        }

        /// <summary>종료 순간 연출 — 주인공 포커싱 + 1.8초 슬로우모션 + 사유 대문짝, 그 다음 결과 (2026-09-05).</summary>
        System.Collections.IEnumerator RoundEndBeat(string reasonText, bool myWin, int focusUnitId, System.Action then)
        {
            // 라운드를 끝낸 주인공에게 카메라 — 킬캠 한 컷 (2026-09-05 "마지막 킬/점령자 포커싱")
            SeoYuGi.Art.TacticalCamera cineCam = null;
            if (focusUnitId >= 0)
            {
                var cam = Camera.main != null ? Camera.main.GetComponent<SeoYuGi.Art.TacticalCamera>() : null;
                var fv = viewRegistry.Get(focusUnitId);
                if (cam != null && fv != null && fv.gameObject.activeInHierarchy)
                {
                    cam.Spectate(fv.transform);
                    cam.CinematicZoom(7.5f); // 클로즈업 돌리 인 — 막타/점령 장면을 가까이서 (2026-09-05)
                    cineCam = cam;
                    var fu = Battle.GetUnit(focusUnitId);
                    if (fu != null)
                        FloatingText.Spawn(gridView.CoordToWorld(fu.pos) + Vector3.up * 0.8f,
                            $"★ {FindSlot(focusUnitId).callsign}", new Color(1f, 0.85f, 0.3f), 1.3f, 1.7f);
                    if (!string.IsNullOrEmpty(reasonText))
                        hud.ShowHeroCard(FindSlot(focusUnitId).callsign, reasonText, 2.1f); // 전용 히어로 카드 (2026-09-05)
                }
            }
            if (!string.IsNullOrEmpty(reasonText))
                hud.ShowAnnounce(reasonText, myWin ? new Color(0.45f, 1f, 0.7f) : new Color(1f, 0.5f, 0.4f), 1.9f);
            Time.timeScale = 0.25f; // 마지막 장면을 천천히 — 무슨 일이 있었는지 눈에 담긴다
            yield return new WaitForSecondsRealtime(2.1f); // 클로즈업 감상 시간 (줌 글라이드 포함)
            Time.timeScale = 1f;
            if (cineCam != null) cineCam.EndCinematic(); // 줌 복원 — 다음 라운드는 평소 거리
            then();
        }

        void OnRoundFinished(int winnerTeam)
        {
            input.enabled = false; // 오버레이 중 조작·학습 오염 차단
            fastForward = false;
            if (radio != null && radio.IsOpen) radio.Close(); // 채팅 치던 중 끝남 — 무전창의 정지 홀드가 남으면 브리핑이 영영 안 넘어간다 (2026-09-06)
            EndRadioTime(); // 무전 타임 중 끝났으면 정지 해제 (홀드 카운트 정리 후 timeScale 복원)
            GuideSpotlight.Clear();
            nextRadioTimeAt = -1f;
            Time.timeScale = 1f; // 빨리감기 중 끝났으면 정상 속도로
            hud.SetSkipHint(false, false);
            Debug.Log($"라운드 {Match.CurrentRound} 종료. 팀 {winnerTeam} 승리");
            battleAudio.SetCaptureLoop(false);
            battleAudio.PlaySfx("S14_RoundEnd", 1.5f);

            int endedRound = Match.CurrentRound;
            bool matchOver = Match.RecordRoundResult(winnerTeam);
            var endReason = Round.Reason;
            string reasonText = EndReasonText(endReason, winnerTeam);
            bool myWinR = winnerTeam == playerTeam;

            // 온라인 호스트 — 클라마다 자기 유닛 기준 브리핑을 담아 종료 통지 (사유 포함)
            if (NetBoot.IsOnline && NetBoot.IsHost && NetLobby.Slots != null)
                foreach (var s in NetLobby.Slots)
                    if (s.owner == SlotOwner.RemoteHuman)
                        NetSync.HostSendRoundEnd(s.clientId, winnerTeam, Match.GetWins(0), Match.GetWins(1),
                            matchOver, null, // AI 학습 브리핑 폐기 (2026-09-05) — 프로토콜은 유지, 내용만 비운다
                            ZoneOwners(), AliveCount(0), AliveCount(1), (int)endReason,
                            matchOver ? BuildStatWire() : null); // 매치오버 — MVP 다부문 확정 스탯

            if (matchOver)
            {
                phase = Phase.MatchOver;
                ResetReadyGate(); // R 동의 게이트 (2026-09-05)
                bool myWin = Match.MatchWinner == playerTeam;
                hud.SetMatchEndReason(reasonText); // 최종 종료도 왜인지 (2026-09-05)
                StartCoroutine(RoundEndBeat(reasonText, myWinR, EndFocusUnit(endReason), () =>
                {
                    hud.SetMatchStats(BuildMatchStats());
                    hud.ShowMatchEnd();
                    battleAudio.PlayBgm(myWin ? "B4_Victory" : "B5_Defeat", loop: false);
                    if (myWin) PlayVoiceLine("Voice_MatchWin", "예측 초과. 통제 불능");
                    else PlayVoiceLine("Voice_MatchLose", "구역 통제권 회수됨");
                }));
            }
            else
            {
                phase = Phase.Briefing;
                ResetReadyGate(); // SPACE 동의 집계 초기화
                StartCoroutine(RoundEndBeat(reasonText, myWinR, EndFocusUnit(endReason), () =>
                {
                    // 라운드 결과 화면 — AI 학습 브리핑(도발 문구)은 폐기 (2026-09-05, 컨셉 선회)
                    hud.SetBriefingStats(BuildRoundStats(playerTeam), BuildRoundStats(1 - playerTeam));
                    hud.SetBriefingReason(reasonText);
                    hud.ShowBriefing(endedRound, winnerTeam, null, ZoneOwners(), AliveCount(playerTeam), AliveCount(1 - playerTeam));
                    battleAudio.PlayBgm("B3_Briefing");
                    battleAudio.SetTypingLoop(true);
                    PlayVoiceLine("Voice_PredictionApplied", "예측 모델 적용");
                }));
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
            if (phase != Phase.Briefing && phase != Phase.MatchOver) return;
            readyClients.Add(clientId);
            int total = HumanCount();
            hud.SetReadyCount(readyClients.Count, total);
            if (NetBoot.IsOnline && NetBoot.IsHost)
                NetSync.HostSendReadyState(readyClients.Count, total);
            if (readyClients.Count < total) return;
            if (phase == Phase.MatchOver) // R 전원 동의 — 새 매치 (SPACE 동의와 같은 관문, 2026-09-05)
            {
                if (NetBoot.IsOnline && NetBoot.IsHost) NetSync.HostSendRestart();
                RestartMatch();
            }
            else StartNextRound();
        }

        void ResetReadyGate()
        {
            readyClients.Clear();
            localReady = false;
            hud.SetReadyCount(0, HumanCount());
        }

        void StartNextRound()
        {
            foreach (var o in teamOrders) o.Clear(); // 새 라운드 = 양 팀 명령 백지. 지난 판 지시가 넘어오지 않는다
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

        /// <summary>클래스 시그니처색 — 로비 클래스 카드와 같은 색 (ClassCard.Meta가 단일 출처).
        /// 카드에서 학습한 "주황=너구리, 시안=검은냥…"이 전장 발밑 링으로 그대로 이어진다.</summary>
        static Color ClassHue(UnitClass cls)
        {
            int i = (int)cls;
            var meta = SeoYuGi.UI.ClassCard.Meta;
            return i >= 0 && i < meta.Length ? meta[i].color : Color.white;
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
                // R = 새 매치 동의 (SPACE 동의처럼 전원 관문 — 2026-09-05). 솔로는 1/1이라 즉시.
                if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame && !localReady)
                {
                    if (!NetBoot.IsOnline) { RestartMatch(); return; }
                    localReady = true;
                    if (IsNetClient)
                    {
                        NetSync.ClientSendReady();
                        hud.SetReadyCount(1, HumanCount()); // 낙관 표시 — 곧 호스트 브로드캐스트로 보정
                    }
                    else MarkReady(0UL); // 호스트 자신
                }
                return;
            }

            // 관전 중 ←/→ = 아군 시점 순환 (2026-09-05 "화살표로 바꿔") — 라운드 진행 중(죽은 뒤)에만 유효
            if (Spectating && Keyboard.current != null)
            {
                if (Keyboard.current.rightArrowKey.wasPressedThisFrame) SpectateFollowAlly(+1);
                else if (Keyboard.current.leftArrowKey.wasPressedThisFrame) SpectateFollowAlly(-1);
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

            // ESC 일시정지 — 조준·무전이 ESC를 쓰지 않을 때만 열린다
            if (pauseMenu != null) pauseMenu.HandleHotkey();
            if (pauseMenu != null && pauseMenu.IsOpen) return; // 멈춘 동안엔 다른 입력을 받지 않는다

            // 무전 채팅바 (Enter) — 지휘관 모드에서만. 열려 있는 동안 시간이 늦춰진다.
            // 관전 중(전사)에는 아예 열리지 않는다 — 프리셋·자유서술·음성 모두 같이 막힌다.
            if (radio != null && radio.enabled) radio.HandleHotkey(); // 전사 후에도 무전 가능 — 관전 지휘
            if (voice != null) voice.enabled = GameModeState.IsCommander;

            // 타이핑 중 — 한글 물리키가 게임키와 겹친다 (ㅂ/ㅈ=카메라, ㅗ=해킹). 게임 입력 전부 잠금.
            bool spectatingNow = Spectating;
            if (spectatingNow && !spectatingPrev && GameModeState.IsCommander)
                hud.PushEvent("전사. 무전(Enter), 숫자키로 분대 지휘는 계속됩니다", StrikeVfx.MineNeon);
            spectatingPrev = spectatingNow;

            bool radioOpen = radio != null && radio.IsOpen;
            if (radioOpen && !radioOpenPrev && !NetBoot.IsOnline) RequestSquadBriefing(); // 싱글 지휘관 — Enter로 무전 열면 분대가 먼저 보고
            radioOpenPrev = radioOpen;

            bool typing = RadioWindow.TextInputActive;
            bool inputLock = typing || GameFreeze.Active; // 무전 타임 정지 중엔 이동·스킬 제출도 잠금 — 시뮬은 즉시 반영이라 정지가 곧 선공이 된다
            if (inputLock != typingPrev)
            {
                input.enabled = !inputLock; // 유닛 이동·스킬 조작 — 여기(Playing 단계)선 카운트다운 뒤라 안전
                typingPrev = inputLock;
            }

            // 해킹 (H) — 궁게이지 만충 시, 5초간 적 예측 AI 교란 + 적 전원 위치 표시. 클라는 Pending.
            if (!inputLock && Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame)
                intentSink.Submit(BattleIntent.Hack(playerUnitId));

            // 싱글 — 내가 죽으면 SPACE로 결과까지 빨리감기 (부활 대신, 2026-09-05). 온라인은 남들이 싸우는 중이라 불가.
            var meForSkip = Battle.GetUnit(playerUnitId);
            bool canSkip = !NetBoot.IsOnline && meForSkip != null && !meForSkip.alive;
            if (canSkip && !typing && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                fastForward = !fastForward;
            if (!canSkip) fastForward = false;
            if (fastForward && !GameFreeze.Active) Time.timeScale = 6f; // 히트스톱이 1로 되돌려도 매 프레임 다시 6 (프리즈는 존중)
            else if (Time.timeScale > 1f) Time.timeScale = 1f; // 해제 순간 복원 (히트스톱 0.05는 건드리지 않음)
            hud.SetSkipHint(canSkip, fastForward);

            // 해킹 만충 순간 한 번 크게 — 게이지가 구석에 있어 다 차도 몰랐다 (2026-09-05 "유용한데 안 쓰게 됨")
            bool hackReadyNow = hackSystem.IsReady(playerUnitId);
            if (hackReadyNow && !hackReadyAnnounced)
            {
                hud.ShowAnnounce("시야해킹 준비 완료. H 키: 적 전원 정지 + 위치 노출", StrikeVfx.MineNeon, 3.2f);
                battleAudio.PlaySfx("S28_HackReady", 1f);
                battleAudio.PlaySfx("S22_DetectPing", 0.9f);
            }
            hackReadyAnnounced = hackReadyNow;

            // 빠른채팅 — 숫자키 1~8 즉시 전송. Tab = 치트시트 토글(기본 켜짐, 읽기 전용).
            // 쿨다운·팀 배달은 호스트 권위 — 클라는 요청만 쏜다.
            if (!typing && Keyboard.current != null)
            {
                if (Keyboard.current.tabKey.wasPressedThisFrame)
                    hud.ShowChatCheatsheet = !hud.ShowChatCheatsheet;
                for (int i = 0; i < ChatKeys.Length; i++)
                    if (Keyboard.current[ChatKeys[i]].wasPressedThisFrame)
                    {
                        if (IsNetClient) { NetSync.ClientSendChat(playerUnitId, i); ApplyQuickChatOrder(i); } // 명령은 sy_od로 별도 릴레이
                        else if (quickChat.TrySend(playerUnitId, i, Time.time))
                            ApplyQuickChatOrder(i); // 지휘관 모드 — 퀵챗이 곧 명령
                        break;
                    }
            }

            // 휠클릭 핑 — 탭 = ▼(디폴트), 꾹 누르고 끌면 ▼/!/? 선택 휠 (롤식).
            // 칸은 누른 순간 기준 — 휠 조작으로 마우스가 옮겨가도 핑 위치는 안 흔들린다.
            if (!typing) UpdatePingInput();

            TickRadioTime(); // 지휘관 대전 — 호스트가 주기 판단, 클라는 남은 시간 표시·상한 (양쪽 공통)
            TickGuideHints(); // 첫 판 가이드 ② — 내 유닛 위에 조작 힌트
            TickGuideSpotlight(); // 가이드 — 화면을 어둡게, 봐야 할 곳만 구멍

            // 온라인 클라이언트 — 시뮬 없음. 스냅샷이 상태를 쓰고, 시야·연출만 로컬.
            if (IsNetClient)
            {
                // 시계 외삽 (2026-09-05 동기화 감사): 스냅샷 사이(50ms)에 시계가 얼면
                // 예고 카운트다운·펄스가 계단이 된다 — 프레임마다 전진, 스냅샷은 보정만.
                if (Battle != null) Battle.time += Time.deltaTime;
                vision.Tick(); // 유닛 위치는 스냅샷이 갱신 — 시야는 완전 결정론이라 로컬 재계산
                SyncPresentation();
                return;
            }

            Move.Tick(Time.deltaTime);
            Combat.Tick(Time.deltaTime); // State.time 전진 — Pickup 리스폰 타이머가 이 시계를 쓴다
            if (!GameModeState.Training) Round.Tick(Time.deltaTime); // 훈련장 — 승패·시간 없음
            else TickTraining();

            // 점령 기여 시간 — 점거 진행 중인 거점 위에 서 있는 그 팀 유닛에게 적립 (MVP 점령 부문, 2026-09-05)
            foreach (var cz in Round.Zones)
            {
                if (cz.capturingTeam < 0) continue;
                foreach (var cc in cz.cells)
                {
                    int cu = Battle.Grid.GetUnitAt(cc);
                    if (cu == SeoYuGi.Battle.Cell.NoUnit) continue;
                    if (Battle.GetUnit(cu).team != cz.capturingTeam) continue;
                    matchCapture[cu] = matchCapture.TryGetValue(cu, out var mcv) ? mcv + Time.deltaTime : Time.deltaTime;
                }
            }
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

            SyncPresentation();

            // 밀침·대시로 위치가 바뀌어도 다음 관찰의 From이 실제 직전 위치가 되도록 보정 (전 유닛 — 연계 패스)
            foreach (var u in Battle.Units)
                humanPrevPos[u.id] = u.pos;
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
                {
                    hud.ShowAnnounce("고지대 확보. 시야 +2 / 사거리 +2 / 이동 +1", teamColors[playerTeam], 2.2f);
                    battleAudio.PlaySfx("S34_Highland", 1.2f);
                }
                playerWasOnHighland = onHigh;
            }

            // 판세 피드백 (2026-09-05): 거점 소유 수 변화를 크게 알린다 — "지금 누가 이기고 있나"
            int myZones = 0, enemyZones = 0;
            foreach (var zz in Round.Zones)
            {
                if (zz.owner == playerTeam) myZones++;
                else if (zz.owner == 1 - playerTeam) enemyZones++;
            }
            if (prevMyZones >= 0 && (myZones != prevMyZones || enemyZones != prevEnemyZones))
            {
                if (myZones == 2 && prevMyZones < 2)
                    hud.ShowAnnounce("아군 거점 2개 확보. 하나 남았습니다!", teamColors[playerTeam], 2.6f);
                else if (enemyZones == 2 && prevEnemyZones < 2)
                {
                    hud.ShowAnnounce("위험. 적이 거점 2개 장악!", new Color(1f, 0.35f, 0.25f), 2.6f);
                    battleAudio.PlaySfx("S33_ZoneContest", 2f);
                }
            }
            prevMyZones = myZones; prevEnemyZones = enemyZones;

            // 거점 점거 원형 게이지 — 점거 중인 팀 색으로 바닥에 차오름
            bool anyCapturing = false;
            for (int i = 0; i < zoneDiscs.Count; i++)
            {
                var z = Round.Zones[i];
                float frac = z.capturingTeam >= 0 ? z.progress / roundConfig.captureSeconds : 0f;
                zoneDiscs[i].SetProgress(frac, z.capturingTeam >= 0 ? teamColors[z.capturingTeam] : Color.clear);
                if (i < zoneBorders.Count)
                {
                    // 봉쇄 거점은 어두운 회색 — "지금은 못 먹는 곳"이 테두리에서 읽힌다 (2026-09-05 "점령 안 되는 버그")
                    if (!z.active) zoneBorders[i].SetLocked();
                    else zoneBorders[i].SetOwnerColor(z.owner, teamColors); // 테두리 = 소유 상태 (팀원 ZoneBorderRing 채택)
                }

                // 내 유닛이 점령 불가 거점을 밟고 있으면 이유를 말해준다 — 규칙 자막을 놓치면 버그로 느낀다
                var meUnit = Battle.GetUnit(playerUnitId);
                if (meUnit != null && meUnit.alive && Time.time >= nextZoneRuleHint && z.cells.Contains(meUnit.pos))
                {
                    string hint = null;
                    if (!z.active) hint = "봉쇄된 거점. 이전 거점부터 점령하세요";
                    else if (z.owner >= 0 && z.owner != playerTeam && Rule != null && Rule.NoTakebacks) hint = "탈환 불가 라운드. 이미 굳은 거점입니다";
                    if (hint != null)
                    {
                        nextZoneRuleHint = Time.time + 4f;
                        FloatingText.Spawn(gridView.CoordToWorld(meUnit.pos) + Vector3.up * 0.4f, hint,
                            new Color(1f, 0.78f, 0.25f), 0.95f, 1.4f); // 호박색 = 시스템 안내
                    }
                }
                if (z.capturingTeam >= 0 && z.progress > 0f) anyCapturing = true;

                // 경합 감지 — 양 팀이 같은 거점을 밟는 순간 1회 긴장음 (게이지 동결의 청각 신호)
                int c0 = 0, c1 = 0;
                foreach (var cell in z.cells)
                {
                    int uid = Battle.Grid.GetUnitAt(cell);
                    if (uid == SeoYuGi.Battle.Cell.NoUnit) continue; // Prediction.Cell과 모호 — 정규화
                    if (Battle.GetUnit(uid).team == 0) c0++; else c1++;
                }
                bool contested = c0 > 0 && c1 > 0;
                while (zoneContestedPrev.Count <= i) zoneContestedPrev.Add(false);
                if (contested && !zoneContestedPrev[i])
                    battleAudio.PlaySfx("S33_ZoneContest", 2.5f);
                zoneContestedPrev[i] = contested;
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
                bool visible = (unit.alive || view.IsDying) && // 쓰러짐 연출 동안은 살려둔다 (타격감 2차)
                               (!isEnemy || hackReveal || Spectating || vision.IsVisibleTo(playerTeam, unit.pos));
                               // 관전(사망) 중엔 적 전원 공개 — 안개만 걷고 유닛은 숨기면 반쪽 관전 (2026-09-05)

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
