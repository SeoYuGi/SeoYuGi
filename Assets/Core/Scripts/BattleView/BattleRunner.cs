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
    /// 맵은 BattleMaps 고정 5장 중 mapIndex — 시드 무작위 없음(유저·AI 모두 지형 학습).
    /// 라운드마다 Core(BattleState/시스템들)를 통째로 새로 조립하고,
    /// Predictor만 매치 내내 살아남아 라운드를 거치며 인간을 학습한다.
    /// 흐름: Playing → (라운드 종료) → Briefing(SPACE) → 다음 라운드 → ... → MatchOver(R).
    /// </summary>
    public class BattleRunner : MonoBehaviour
    {
        enum Phase { Playing, Briefing, MatchOver }

        // 슬롯 로스터 — 양팀 미러 픽 + 탱고파이브식 콜사인. 픽 변경은 여기서.
        static readonly (int id, int team, UnitClass cls, string name)[] Roster =
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
        VisionSystem vision;
        Predictor predictor;
        readonly List<AiSlotDriver> aiDrivers = new List<AiSlotDriver>();

        Phase phase = Phase.Playing;
        ParsedMap map;
        GridConfig gridConfig;
        int playerTeam;
        bool gridViewBuilt;
        Coord humanPrevPos;          // Predictor 이동 관찰용 직전 위치
        Func<Coord, bool> playerVisibleFn;

        // 라운드마다 파괴·재생성되는 뷰 오브젝트
        readonly List<GameObject> roundObjects = new List<GameObject>();
        readonly Dictionary<int, UnitHpBar> hpBars = new Dictionary<int, UnitHpBar>();
        readonly Dictionary<int, GameObject> ghosts = new Dictionary<int, GameObject>(); // 적 잔상 마커

        void Awake()
        {
            gridView = GetComponent<GridView>();
            input = GetComponent<UnitMoveInput>();
            viewRegistry = GetComponent<UnitViewRegistry>();
            if (viewRegistry == null) viewRegistry = gameObject.AddComponent<UnitViewRegistry>();
            hud = GetComponent<BattleHud>();
            if (hud == null) hud = gameObject.AddComponent<BattleHud>();
        }

        void Start()
        {
            map = BattleMaps.Get(mapIndex);
            gridConfig = new GridConfig { width = map.Width, height = map.Height };
            playerTeam = FindRoster(playerUnitId).team;
            playerVisibleFn = c => vision.IsVisibleTo(playerTeam, c);

            Match = new MatchSystem();
            predictor = NewPredictor();
            BuildRound();
            SetupCamera();
            Debug.Log($"맵 [{map.Name}] ({map.Width}×{map.Height})");
        }

        static (int id, int team, UnitClass cls, string name) FindRoster(int unitId)
        {
            foreach (var r in Roster)
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

        /// <summary>라운드 1개 분량의 Core + 뷰 전체 조립. 라운드 시작마다 호출.</summary>
        void BuildRound()
        {
            ClearRoundObjects();

            var grid = new GridModel(gridConfig);
            foreach (var c in map.Walls)
                grid.SetObstacle(c);

            Battle = new BattleState(grid);
            foreach (var r in Roster)
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

            foreach (var r in Roster)
            {
                var view = CreateUnitView();
                view.name = $"Unit_{r.id}_{r.cls}";
                // 클래스별 덩치 차이 — 모델 들어오기 전 임시 구분 (Bind 전에 적용해야 기준 스케일로 잡힘)
                view.transform.localScale *= ViewScale(r.cls);
                view.Bind(r.id, teamColors[r.team], gridView, map.Spawns[r.id]);
                viewRegistry.Register(view);
                roundObjects.Add(view.gameObject);

                var bar = UnitHpBar.Create(transform, Battle.GetUnit(r.id), view.transform, r.name, teamColors[r.team]);
                hpBars[r.id] = bar;
                roundObjects.Add(bar.gameObject);

                if (r.team != playerTeam)
                {
                    var ghost = CreateGhost(teamColors[r.team]);
                    ghosts[r.id] = ghost;
                    roundObjects.Add(ghost);
                }
            }

            hud.Init(Battle, Round, combatConfig, Match, playerUnitId, teamColors, FindRoster(playerUnitId).name);
            input.Init(Move, Combat, gridView, viewRegistry, playerUnitId, playerVisibleFn);

            Round.OnZoneCaptured += zone =>
            {
                var tint = Color.Lerp(teamColors[zone.owner], Color.white, 0.35f);
                foreach (var c in zone.cells)
                    gridView.SetBaseTint(c, tint);
                Debug.Log($"거점 {zone.Center} → 팀 {zone.owner} 탈환");
            };
            Round.OnSuddenDeath += _ => Debug.Log("서든데스! 다음 탈환 또는 킬로 즉시 승부");

            // 슬롯: 나 빼고 전부 AI. 적팀 뇌에만 Predictor 주입 — "AI군은 인간을 노린다"(기획서 §05).
            worldView = new CoreWorldView(Battle, Combat, Round, vision, playerUnitId, Match.CurrentRound);
            aiDrivers.Clear();
            foreach (var r in Roster)
                if (r.id != playerUnitId)
                    aiDrivers.Add(new AiSlotDriver(r.id, r.cls, Move, Combat,
                        r.team != playerTeam ? predictor : null));

            Move.OnUnitMoved += (unitId, path, yellow) =>
            {
                viewRegistry.Get(unitId)?.PlayPath(path, moveConfig.hopDuration);
                if (unitId == playerUnitId) ObserveHumanPath(path);
            };

            Combat.OnUnitDamaged += (unitId, dmg) =>
                Debug.Log($"유닛 {unitId} 피해 {dmg} (HP {Battle.GetUnit(unitId).hp}/{Battle.GetUnit(unitId).maxHp})");

            humanPrevPos = Battle.GetUnit(playerUnitId).pos;
            input.enabled = true;
            phase = Phase.Playing;
            Debug.Log($"라운드 {Match.CurrentRound} 시작 (Predictor round={predictor.Round})");
        }

        void ClearRoundObjects()
        {
            foreach (var go in roundObjects)
                if (go != null) Destroy(go);
            roundObjects.Clear();
            hpBars.Clear();
            ghosts.Clear();
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
            if (cam.GetComponent<QuarterViewCamera>() != null) return; // 추적 캠 우선 — 프레이밍 양보
            var center = (gridView.CoordToWorld(new Coord(0, 0)) +
                          gridView.CoordToWorld(new Coord(map.Width - 1, map.Height - 1))) * 0.5f;
            cam.transform.rotation = Quaternion.Euler(cameraPitch, 0f, 0f);
            float dist = Mathf.Max(map.Width, map.Height) * cameraDistanceScale;
            cam.transform.position = center - cam.transform.forward * dist;
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
            int endedRound = Match.CurrentRound;
            bool matchOver = Match.RecordRoundResult(winnerTeam);
            if (matchOver)
            {
                hud.ShowMatchEnd();
                phase = Phase.MatchOver;
            }
            else
            {
                // 라운드 간 브리핑 — AI가 학습한 내용을 보여준다 (심사 기준 ① 어필 지점)
                hud.ShowBriefing(endedRound, winnerTeam, predictor.GetBriefing(playerUnitId));
                phase = Phase.Briefing;
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
            BuildRound();
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
        GameObject CreateGhost(Color teamColor)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Ghost";
            go.transform.SetParent(transform);
            go.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            Destroy(go.GetComponent<Collider>()); // 클릭 레이캐스트 방해 금지

            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(BaseColorId, Color.Lerp(teamColor, new Color(0.3f, 0.3f, 0.35f), 0.65f));
            go.GetComponent<Renderer>().SetPropertyBlock(mpb);
            go.SetActive(false);
            return go;
        }

        void Update()
        {
            if (Move == null) return;

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

            foreach (var unit in Battle.Units)
            {
                var view = viewRegistry.Get(unit.id);
                if (view == null) continue;

                bool isEnemy = unit.team != playerTeam;
                bool visible = unit.alive && (!isEnemy || vision.IsVisibleTo(playerTeam, unit.pos));

                if (view.gameObject.activeSelf != visible)
                    view.gameObject.SetActive(visible);
                hpBars[unit.id].SetVisible(visible);

                // 고스트 마커: 살아있지만 안 보이는 적 → 마지막 목격 위치에 잔상
                if (isEnemy && ghosts.TryGetValue(unit.id, out var ghost))
                {
                    bool showGhost = false;
                    if (unit.alive && !visible && vision.TryGetLastSeen(playerTeam, unit.id, out var seen))
                    {
                        showGhost = true;
                        ghost.transform.position = gridView.CoordToWorld(seen) + Vector3.up * 0.3f;
                    }
                    if (ghost.activeSelf != showGhost) ghost.SetActive(showGhost);
                }

                if (!visible) continue;

                // 쿨타임/방어 시각화: 잠긴 유닛은 어둡게
                view.SetDimmed(unit.moveCooldown > 0f || unit.guardUntil > Battle.time);

                // 밀침·대시·점멸 등 연출 없는 위치 변경 동기화
                if (!view.IsMoving && !view.IsAt(unit.pos))
                    view.SnapTo(unit.pos);
            }
        }
    }
}
