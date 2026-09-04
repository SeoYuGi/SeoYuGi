using System;
using System.Collections.Generic;
using SeoYuGi.Battle;
using SeoYuGi.Integration;
using SeoYuGi.Prediction;
using UnityEngine;
using UnityEngine.InputSystem;
using PredCell = SeoYuGi.Prediction.Cell; // Battle.Cell과 이름 충돌 — 반드시 alias로 구분

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 전투 진입점 + 매치 오케스트레이션 (기획서 §05: 3라운드 2선승).
    /// 맵은 BattleMaps 고정 5장 중 매치 시작 팝업에서 선택 — 시드 무작위 없음(유저·AI 모두 지형 학습).
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

        [Header("Map — BattleMaps 고정 5장 중 선택")]
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
        public int PlayerUnitId => playerUnitId;

        CoreWorldView worldView;
        BattleHud hud;
        BattleAudio battleAudio;
        VisionSystem vision;
        readonly HashSet<int> audioVisibleEnemies = new HashSet<int>(); // 발견/소실 SFX용
        Predictor predictor;
        readonly List<AiSlotDriver> aiDrivers = new List<AiSlotDriver>();

        Phase phase = Phase.Playing;
        ParsedMap map;
        GridConfig gridConfig;
        int playerTeam;
        bool gridViewBuilt;
        Coord humanPrevPos;          // Predictor 이동 관찰용 직전 위치
        Func<Coord, bool> playerVisibleFn;

        GameObject pickBg; // 맵/클래스 픽 화면 배경 (Resources/UI/BG_Title)

        // 라운드마다 파괴·재생성되는 뷰 오브젝트
        readonly List<GameObject> roundObjects = new List<GameObject>();
        readonly Dictionary<int, UnitHpBar> hpBars = new Dictionary<int, UnitHpBar>();
        readonly HashSet<int> blinkSnapIds = new HashSet<int>(); // 점멸 직후 — 슬라이드 대신 번쩍+스냅
        readonly List<ZoneCaptureDisc> zoneDiscs = new List<ZoneCaptureDisc>(); // 거점 점거 원형 게이지

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
            playerVisibleFn = c => vision.IsVisibleTo(playerTeam, c);

            // 플레이어 행동 거부 버저 — input은 라운드 넘어 유지되므로 1회만 구독
            input.OnActionDenied += () => battleAudio.PlaySfx("S24_ApBuzz", 0.5f);

            // 카메라 셰이커 — 추적/전술 캠 위에 얹는 타격감 레이어
            var mainCam = Camera.main;
            if (mainCam != null && mainCam.GetComponent<CameraShaker>() == null)
                mainCam.gameObject.AddComponent<CameraShaker>();
            ImpactFx.Ensure(); // 명중 비네트 펀치 + 격파 플래시 (글로벌 Volume)

            Match = new MatchSystem();
            ShowMapSelect();
        }

        /// <summary>맵 선택 팝업 → mapIndex 확정 + 맵 종속 상태 조립 → 클래스 선택으로.</summary>
        void ShowMapSelect()
        {
            phase = Phase.ClassSelect; // 픽 단계(맵+클래스) 동안 시뮬레이션 정지
            battleAudio.PlayBgm("B6_Title"); // 픽 화면 = 타이틀 테마
            if (UIManager.Instance == null)
                new GameObject("@UIManager").AddComponent<UIManager>(); // 씬에 없으면 자동 생성
            ShowPickBackground();

            var popup = UIManager.Instance.ShowPopupUI<UIMapSelectPopup>();
            popup.OnPicked = idx =>
            {
                mapIndex = idx;
                map = BattleMaps.Get(mapIndex);
                gridConfig = new GridConfig { width = map.Width, height = map.Height };
                predictor = NewPredictor();
                Debug.Log($"맵 [{map.Name}] ({map.Width}×{map.Height})");
                ShowClassSelect();
            };
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
            popup.OnPicked = cls =>
            {
                for (int i = 0; i < roster.Length; i++)
                    if (roster[i].id == playerUnitId)
                        roster[i].cls = cls;
                RollEnemyClasses();
                BuildRound();
                SetupCamera();
            };
        }

        /// <summary>적팀 클래스 매치당 1회 랜덤 (중복 없음). 라운드 간엔 유지 — 학습 매치 구조 보호.</summary>
        void RollEnemyClasses()
        {
            var pool = new List<UnitClass>
                { UnitClass.Tank, UnitClass.Balance, UnitClass.Assassin, UnitClass.Grenadier, UnitClass.Sniper };
            for (int i = 0; i < roster.Length; i++)
            {
                if (roster[i].team == playerTeam) continue;
                int pick = UnityEngine.Random.Range(0, pool.Count);
                roster[i].cls = pool[pick];
                pool.RemoveAt(pick);
            }
        }

        (int id, int team, UnitClass cls, string name) FindRoster(int unitId)
        {
            foreach (var r in roster)
                if (r.id == unitId) return r;
            throw new ArgumentException($"roster에 없는 unitId {unitId}");
        }

        Predictor NewPredictor()
        {
            var cfg = new PredictionConfig { MapWidth = map.Width, MapHeight = map.Height };
            foreach (var zone in map.Zones)
            foreach (var c in zone)
                cfg.ZoneCells.Add(new PredCell(c.x, c.y));
            return new Predictor(cfg);
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

            var grid = new GridModel(gridConfig);
            foreach (var c in map.Walls)
                grid.SetObstacle(c);
            foreach (var c in map.Voids)
                grid.SetVoid(c);
            foreach (var c in map.Highlands)
                grid.SetHighland(c);

            Battle = new BattleState(grid);
            foreach (var r in roster)
                Battle.AddUnit(new UnitState(r.id, r.team, map.Spawns[r.id], r.cls));

            Move = new MoveSystem(Battle, moveConfig);
            Combat = new CombatSystem(Battle, combatConfig);
            Round = new RoundSystem(Battle, roundConfig, map.Zones);
            vision = new VisionSystem(Battle);

            if (!gridViewBuilt)
            {
                gridViewBuilt = true;
                gridView.Build(grid);
                var allZoneCells = new List<Coord>();
                foreach (var zone in map.Zones)
                    allZoneCells.AddRange(zone);
                gridView.MarkZones(allZoneCells);
                CreateZoneLabels();
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
                var disc = ZoneCaptureDisc.Create(transform, gridView.CoordToWorld(z.Center));
                zoneDiscs.Add(disc);
                roundObjects.Add(disc.gameObject);
            }

            foreach (var r in roster)
            {
                var view = CreateUnitView();
                view.name = $"Unit_{r.id}_{r.cls}";
                // 클래스별 덩치 차이 — 모델 들어오기 전 임시 구분 (Bind 전에 적용해야 기준 스케일로 잡힘)
                view.transform.localScale *= ViewScale(r.cls);
                view.Bind(r.id, teamColors[r.team], gridView, map.Spawns[r.id]);
                viewRegistry.Register(view);
                roundObjects.Add(view.gameObject);

                // 가시성: 발밑 팀 링 (내 유닛 = 이중 링+펄스), 내 유닛 머리 위 ▼
                bool isPlayer = r.id == playerUnitId;
                UnitIndicators.AddTeamRing(view.transform, teamColors[r.team], isPlayer);
                if (isPlayer) UnitIndicators.AddPlayerArrow(view.transform, teamColors[r.team]);

                // HP 핍: 아군 초록/적 빨강, 풀피면 숨김(내 유닛 제외)
                var pipColor = r.team == playerTeam ? new Color(0.3f, 0.9f, 0.4f) : new Color(1f, 0.3f, 0.25f);
                var bar = UnitHpBar.Create(transform, Battle.GetUnit(r.id), view.transform, r.name, teamColors[r.team],
                    pipColor, alwaysShowPips: isPlayer);
                hpBars[r.id] = bar;
                roundObjects.Add(bar.gameObject);

            }

            hud.Init(Battle, Round, combatConfig, Match, playerUnitId, teamColors, FindRoster(playerUnitId).name);
            input.Init(Move, Combat, gridView, viewRegistry, playerUnitId, playerVisibleFn);

            Round.OnZoneCaptured += zone =>
            {
                var tint = Color.Lerp(teamColors[zone.owner], Color.white, 0.35f);
                foreach (var c in zone.cells)
                    gridView.SetBaseTint(c, tint);
                Debug.Log($"거점 {zone.Center} → 팀 {zone.owner} 탈환");

                bool ours = zone.owner == playerTeam;
                battleAudio.PlaySfx(ours ? "S12a_ZoneCaptured" : "S12b_ZoneLost", 1.5f);
                if (ours) PlayVoiceLine("Voice_ZoneCaptured", "구역 확보");
                else PlayVoiceLine("Voice_ZoneLost", "구역 상실");
            };
            Round.OnSuddenDeath += _ =>
            {
                Debug.Log("서든데스! 다음 탈환 또는 킬로 즉시 승부");
                battleAudio.PlaySfx("S15_SuddenDeath", 2f);
                PlayVoiceLine("Voice_SuddenDeath", "서든데스");
            };

            // 슬롯: 나 빼고 전부 AI. 적팀 뇌에만 Predictor 주입 — "AI군은 인간을 노린다"(기획서 §05).
            worldView = new CoreWorldView(Battle, Combat, Round, vision, playerUnitId, Match.CurrentRound);
            aiDrivers.Clear();
            foreach (var r in roster)
                if (r.id != playerUnitId)
                    aiDrivers.Add(new AiSlotDriver(r.id, r.cls, Move, Combat,
                        r.team != playerTeam ? predictor : null));

            Move.OnUnitMoved += (unitId, path, yellow) =>
            {
                // 시야 밖(비활성) 뷰는 연출 생략 — 다시 보일 때 SyncPresentation의 SnapTo가 위치를 맞춘다
                var movedView = viewRegistry.Get(unitId);
                if (movedView != null && movedView.gameObject.activeInHierarchy)
                    movedView.PlayPath(path, moveConfig.hopDuration);
                if (unitId == playerUnitId)
                {
                    ObserveHumanPath(path);
                    battleAudio.PlaySfx(yellow ? "S7_YellowMove" : "S6_Hop", yellow ? 1f : 0.4f);
                    if (yellow) CameraShaker.Shake(0.12f); // 과부하 점프 — 미세한 무게
                }
            };

            Combat.OnTelegraph += strike =>
            {
                if (strike.team == playerTeam) battleAudio.PlaySfx("S2_TelegraphAlly", 0.8f);
                else if (AnyCellVisible(strike)) battleAudio.PlaySfx("S1_TelegraphEnemy", 0.8f);
            };
            Combat.OnStrikeResolved += (strike, hit) =>
            {
                if (strike.attackerId == playerUnitId)
                {
                    battleAudio.PlaySfx(hit ? "S3_Hit" : "S4_Miss", 0.8f);
                    if (hit) battleAudio.PlaySfx("S5_ApRefund", 1f); // 적중 = 예측 성공 = AP 환급음
                }
                else if (hit) battleAudio.PlaySfx("S3_Hit", 0.8f);
            };
            Combat.OnGuard += _ => battleAudio.PlaySfx("S8_Guard", 0.8f);
            Combat.OnSkillCast += (unitId, kind) =>
            {
                battleAudio.PlaySfx(SkillSfx(kind), 1.5f);
                // 즉발 이동기(대시·점멸)는 캐스팅 순간에 무게 — 예고형은 판정 시 피해 셰이크가 담당
                if (kind == SkillKind.Dash && IsUnitVisibleToPlayer(unitId)) CameraShaker.Shake(0.18f);
            };
            Combat.OnWallCrash += unitId =>
            {
                battleAudio.PlaySfx("S21_WallCrash", 0.6f);
                if (IsUnitVisibleToPlayer(unitId)) CameraShaker.Shake(0.35f); // 벽에 처박히는 쾅
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
                    ImpactVfx.Sparks(gridView.CoordToWorld(dead.pos), machine: dead.team == 1, scale: 1.8f);
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
                    CameraShaker.Shake(0.28f); // 피격 — 짧고 절도 있게
                    HitStop.Do(0.05f);
                    ImpactFx.Punch(0.5f);
                    ImpactVfx.Sparks(gridView.CoordToWorld(victim.pos), machine: victim.team == 1);
                    battleAudio.PlayThump(big: false);
                }
            };

            // 플로팅 텍스트 — 누가 뭘 하는지 머리 위에 뜸 (시야 안일 때만)
            Combat.OnSkillCast += (unitId, kind) =>
            {
                var v = viewRegistry.Get(unitId);
                if (v != null && v.gameObject.activeInHierarchy)
                    FloatingText.Spawn(v.transform.position, SkillLabel(kind), new Color(1f, 0.9f, 0.4f));
                if (kind == SkillKind.Blink)
                    blinkSnapIds.Add(unitId); // 점멸은 슬라이드 대신 번쩍+스냅 (SyncPresentation)
            };

            // 타격 연출 — 시야 밖 칸은 예고 필터와 같은 규칙으로 숨긴다 (정보 누출 방지)
            Combat.OnStrikeResolved += (strike, hit) =>
            {
                int pillars = 0;
                foreach (var c in strike.cells)
                    if (strike.team == playerTeam || playerVisibleFn(c))
                    {
                        CellFlash.Spawn(gridView.CoordToWorld(c), Color.white);
                        if (hit && pillars < 5) // 명중 판정 — 섬광 기둥 (예고→해소)
                        {
                            ImpactVfx.Pillar(gridView.CoordToWorld(c), new Color(1f, 0.75f, 0.45f));
                            pillars++;
                        }
                    }
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

            Combat.OnGuard += unitId =>
            {
                var u = Battle.GetUnit(unitId);
                if (u.team == playerTeam || playerVisibleFn(u.pos))
                {
                    CellFlash.Spawn(gridView.CoordToWorld(u.pos), new Color(0.3f, 0.7f, 1f));
                    FloatingText.Spawn(gridView.CoordToWorld(u.pos), "방어", new Color(0.45f, 0.75f, 1f), 0.9f, 0.6f);
                }
            };

            humanPrevPos = Battle.GetUnit(playerUnitId).pos;
            audioVisibleEnemies.Clear();
            input.enabled = true;
            phase = Phase.Playing;

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
            blinkSnapIds.Clear();
            viewRegistry.Clear();
        }

        /// <summary>거점 패치 중앙에 대형 A/B/C 글자 (탱고파이브식).</summary>
        void CreateZoneLabels()
        {
            for (int i = 0; i < Round.Zones.Count && i < ZoneLetters.Length; i++)
            {
                var go = new GameObject($"ZoneLabel_{ZoneLetters[i]}");
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

        bool IsUnitVisibleToPlayer(int unitId)
        {
            var u = Battle.GetUnit(unitId);
            if (u == null) return false;
            return u.team == playerTeam || vision.IsVisibleTo(playerTeam, u.pos);
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
                case SkillKind.Smash: return "S16_Smash";
                case SkillKind.Dash: return "S17_Dash";
                case SkillKind.Blink: return "S18_Blink";
                case SkillKind.Burst: return "S19_Burst";
                case SkillKind.Snipe: return "S20_Snipe";
                default: return "S3_Hit";
            }
        }

        /// <summary>인간 이동을 홉 단위로 Predictor에 공급 — 학습 단위는 개체(슬롯) (세부기획 E).</summary>
        void ObserveHumanPath(IReadOnlyList<Coord> path)
        {
            var from = humanPrevPos;
            foreach (var to in path)
            {
                predictor.Observe(new ActionEvent
                {
                    ActorId = playerUnitId,
                    Team = (TeamId)playerTeam,
                    Type = ActionType.Move,
                    From = new PredCell(from.x, from.y),
                    To = new PredCell(to.x, to.y),
                    Time = Battle.time
                });
                from = to;
            }
            humanPrevPos = from;
        }

        void OnRoundFinished(int winnerTeam)
        {
            input.enabled = false; // 오버레이 중 조작·학습 오염 차단
            Debug.Log($"라운드 {Match.CurrentRound} 종료 — 팀 {winnerTeam} 승리");
            battleAudio.SetCaptureLoop(false);
            battleAudio.PlaySfx("S14_RoundEnd", 1.5f);

            int endedRound = Match.CurrentRound;
            bool matchOver = Match.RecordRoundResult(winnerTeam);
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
                battleAudio.PlayBgm("B3_Briefing");
                battleAudio.SetTypingLoop(true);
                PlayVoiceLine("Voice_PredictionApplied", "예측 모델 적용");
            }
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
            ShowClassSelect();          // 재시작 때도 다시 픽 + 적팀 재롤
        }

        static string SkillLabel(SkillKind kind)
        {
            switch (kind)
            {
                case SkillKind.Smash: return "강타!";
                case SkillKind.Dash: return "돌파!";
                case SkillKind.Blink: return "그림자 도약!";
                case SkillKind.Burst: return "파열탄!";
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
                if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                    StartNextRound();
                return;
            }
            if (phase == Phase.MatchOver)
            {
                if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
                    RestartMatch();
                return;
            }

            Move.Tick(Time.deltaTime);
            Combat.Tick(Time.deltaTime);
            Round.Tick(Time.deltaTime);
            vision.Tick();

            if (Round.Winner != -1)
            {
                OnRoundFinished(Round.Winner);
                return;
            }

            worldView.Refresh();
            foreach (var driver in aiDrivers)
                driver.Tick(worldView);

            SyncPresentation();

            // 밀침·대시로 위치가 바뀌어도 다음 관찰의 From이 실제 직전 위치가 되도록 보정
            humanPrevPos = Battle.GetUnit(playerUnitId).pos;
        }

        void SyncPresentation()
        {
            gridView.UpdateFog(playerVisibleFn); // 시야 밖 타일 어둡게 (세부기획 B)

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

            foreach (var unit in Battle.Units)
            {
                var view = viewRegistry.Get(unit.id);
                if (view == null) continue;

                bool isEnemy = unit.team != playerTeam;
                bool visible = unit.alive && (!isEnemy || vision.IsVisibleTo(playerTeam, unit.pos));

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

                // 쿨타임/방어 시각화: 잠긴 유닛은 어둡게
                view.SetDimmed(unit.moveCooldown > 0f || unit.guardUntil > Battle.time);

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
