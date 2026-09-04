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
    /// 라운드마다 Core(BattleState/시스템들)를 통째로 새로 조립하고,
    /// Predictor만 매치 내내 살아남아 라운드를 거치며 인간을 학습한다.
    /// 흐름: Playing → (라운드 종료) → Briefing(SPACE) → 다음 라운드 → ... → MatchOver(R).
    /// </summary>
    public class BattleRunner : MonoBehaviour
    {
        [Serializable]
        class UnitSpawn
        {
            public int id;
            public int team;
            public Coord pos;
            public UnitClass cls;
        }

        enum Phase { Playing, Briefing, MatchOver }

        [Header("Config")]
        [SerializeField] GridConfig gridConfig = new GridConfig { width = 9, height = 9 }; // 기획서: 9×9
        [SerializeField] MoveConfig moveConfig = new MoveConfig();
        [SerializeField] CombatConfig combatConfig = new CombatConfig();
        [SerializeField] RoundConfig roundConfig = new RoundConfig();

        [Header("Map (자동 생성)")]
        [SerializeField] int mapSeed = 20260904;
        [Range(4, 8)]
        [SerializeField] int wallTiles = 8; // 세부기획 B: 벽 4~8개

        [Header("Setup — 3v3 (양팀 미러 픽)")]
        [SerializeField] List<UnitSpawn> spawns = new List<UnitSpawn>
        {
            new UnitSpawn { id = 1, team = 0, pos = new Coord(2, 1), cls = UnitClass.Tank },
            new UnitSpawn { id = 2, team = 0, pos = new Coord(4, 1), cls = UnitClass.Balance },
            new UnitSpawn { id = 3, team = 0, pos = new Coord(6, 1), cls = UnitClass.Sniper },
            new UnitSpawn { id = 4, team = 1, pos = new Coord(2, 7), cls = UnitClass.Tank },
            new UnitSpawn { id = 5, team = 1, pos = new Coord(4, 7), cls = UnitClass.Balance },
            new UnitSpawn { id = 6, team = 1, pos = new Coord(6, 7), cls = UnitClass.Sniper }
        };
        [SerializeField] Color[] teamColors = { new Color(0.25f, 0.5f, 1f), new Color(1f, 0.3f, 0.25f) };

        [Header("슬롯 — 내 조작은 1기, 나머지는 AI (기획서 §04)")]
        [SerializeField] int playerUnitId = 2; // 팀0 치즈태비

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
        int playerTeam;
        List<Coord> walls;           // 매치 내내 같은 맵 — 학습이 맵 위에서 누적되도록
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
            playerTeam = spawns.Find(s => s.id == playerUnitId).team;
            playerVisibleFn = c => vision.IsVisibleTo(playerTeam, c);

            // 자동 맵: 스폰 칸 주변 + 거점 칸은 벽 금지. 매치 내내 동일 (재계산 없음).
            var reserved = new HashSet<Coord>();
            foreach (var s in spawns)
            {
                reserved.Add(s.pos);
                foreach (var dir in Coord.Directions4)
                    reserved.Add(s.pos + dir);
            }
            foreach (var c in RoundSystem.DefaultZoneCells(gridConfig.width, gridConfig.height))
            {
                reserved.Add(c);
                foreach (var dir in Coord.Directions4)
                    reserved.Add(c + dir); // 거점 입구도 확보
            }
            walls = MapGenerator.GenerateWalls(gridConfig, wallTiles, mapSeed, reserved);

            Match = new MatchSystem();
            predictor = NewPredictor();
            BuildRound();
        }

        Predictor NewPredictor()
        {
            var cfg = new PredictionConfig { MapWidth = gridConfig.width, MapHeight = gridConfig.height };
            foreach (var z in RoundSystem.DefaultZoneCells(gridConfig.width, gridConfig.height))
                cfg.ZoneCells.Add(new PredCell(z.x, z.y));
            return new Predictor(cfg);
        }

        /// <summary>라운드 1개 분량의 Core + 뷰 전체 조립. 라운드 시작마다 호출.</summary>
        void BuildRound()
        {
            ClearRoundObjects();

            var grid = new GridModel(gridConfig);
            foreach (var c in walls)
                grid.SetObstacle(c);

            Battle = new BattleState(grid);
            foreach (var s in spawns)
                Battle.AddUnit(new UnitState(s.id, s.team, s.pos, s.cls));

            Move = new MoveSystem(Battle, moveConfig);
            Combat = new CombatSystem(Battle, combatConfig);
            Round = new RoundSystem(Battle, roundConfig);
            vision = new VisionSystem(Battle);

            if (!gridViewBuilt)
            {
                gridViewBuilt = true;
                gridView.Build(grid);
                var zoneCells = new List<Coord>();
                foreach (var z in Round.Zones) zoneCells.Add(z.cell);
                gridView.MarkZones(zoneCells);
            }
            gridView.ClearBaseTints(); // 이전 라운드 거점 소유 틴트 제거

            foreach (var s in spawns)
            {
                var view = CreateUnitView();
                view.name = $"Unit_{s.id}_{s.cls}";
                // 클래스별 덩치 차이 — 모델 들어오기 전 임시 구분 (Bind 전에 적용해야 기준 스케일로 잡힘)
                view.transform.localScale *= ViewScale(s.cls);
                view.Bind(s.id, teamColors[s.team], gridView, s.pos);
                viewRegistry.Register(view);
                roundObjects.Add(view.gameObject);

                var bar = UnitHpBar.Create(transform, Battle.GetUnit(s.id), view.transform);
                hpBars[s.id] = bar;
                roundObjects.Add(bar.gameObject);

                if (s.team != playerTeam)
                {
                    var ghost = CreateGhost(teamColors[s.team]);
                    ghosts[s.id] = ghost;
                    roundObjects.Add(ghost);
                }
            }

            hud.Init(Battle, Round, combatConfig, Match, playerUnitId);
            input.Init(Move, Combat, gridView, viewRegistry, playerUnitId, playerVisibleFn);

            Round.OnZoneCaptured += zone =>
            {
                var tint = Color.Lerp(teamColors[zone.owner], Color.white, 0.35f);
                gridView.SetBaseTint(zone.cell, tint);
                Debug.Log($"거점 {zone.cell} → 팀 {zone.owner} 탈환");
            };
            Round.OnSuddenDeath += _ => Debug.Log("서든데스! 다음 탈환 또는 킬로 즉시 승부");

            // 슬롯: 나 빼고 전부 AI. 적팀 뇌에만 Predictor 주입 — "AI군은 인간을 노린다"(기획서 §05).
            worldView = new CoreWorldView(Battle, Combat, Round, vision, playerUnitId, Match.CurrentRound);
            aiDrivers.Clear();
            foreach (var s in spawns)
                if (s.id != playerUnitId)
                    aiDrivers.Add(new AiSlotDriver(s.id, s.cls, Move, Combat,
                        s.team != playerTeam ? predictor : null));

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
