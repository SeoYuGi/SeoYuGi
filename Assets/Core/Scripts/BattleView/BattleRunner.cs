using System;
using System.Collections.Generic;
using SeoYuGi.Battle;
using SeoYuGi.Integration;
using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 전투 진입점. Core를 조립하고 매 프레임 MoveSystem.Tick(deltaTime)을 먹인다.
    /// 필요한 View 컴포넌트는 같은 GameObject에서 자동으로 찾는다(비어 있을 때).
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

        [Header("Config")]
        [SerializeField] GridConfig gridConfig = new GridConfig { width = 9, height = 9 }; // 기획서: 9×9
        [SerializeField] MoveConfig moveConfig = new MoveConfig();
        [SerializeField] CombatConfig combatConfig = new CombatConfig();

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

        // 같은 GameObject에서 자동 연결 — 인스펙터 배선 불필요
        GridView gridView;
        UnitMoveInput input;
        UnitViewRegistry viewRegistry;

        public MoveSystem Move { get; private set; }
        public CombatSystem Combat { get; private set; }
        public BattleState Battle { get; private set; }

        CoreWorldView worldView;
        readonly List<AiSlotDriver> aiDrivers = new List<AiSlotDriver>();

        void Awake()
        {
            gridView = GetComponent<GridView>();
            input = GetComponent<UnitMoveInput>();
            viewRegistry = GetComponent<UnitViewRegistry>();
            if (viewRegistry == null) viewRegistry = gameObject.AddComponent<UnitViewRegistry>();
        }

        void Start()
        {
            var grid = new GridModel(gridConfig);

            // 자동 맵: 스폰 칸 + 그 주변은 벽 금지
            var reserved = new HashSet<Coord>();
            foreach (var s in spawns)
            {
                reserved.Add(s.pos);
                foreach (var dir in Coord.Directions4)
                    reserved.Add(s.pos + dir);
            }
            foreach (var c in MapGenerator.GenerateWalls(gridConfig, wallTiles, mapSeed, reserved))
                grid.SetObstacle(c);

            Battle = new BattleState(grid);
            foreach (var s in spawns)
                Battle.AddUnit(new UnitState(s.id, s.team, s.pos, s.cls));

            Move = new MoveSystem(Battle, moveConfig);
            Combat = new CombatSystem(Battle, combatConfig);

            gridView.Build(grid);
            foreach (var s in spawns)
            {
                var view = CreateUnitView();
                view.name = $"Unit_{s.id}_{s.cls}";
                // 클래스별 덩치 차이 — 모델 들어오기 전 임시 구분 (Bind 전에 적용해야 기준 스케일로 잡힘)
                view.transform.localScale *= ViewScale(s.cls);
                view.Bind(s.id, teamColors[s.team], gridView, s.pos);
                viewRegistry.Register(view);
            }
            input.Init(Move, Combat, gridView, viewRegistry, playerUnitId);

            // 나 빼고 전부 AI 슬롯 (아군 백필 + 적팀). Predictor는 학습 연동 시 주입.
            worldView = new CoreWorldView(Battle, Combat, playerUnitId);
            foreach (var s in spawns)
                if (s.id != playerUnitId)
                    aiDrivers.Add(new AiSlotDriver(s.id, s.cls, Move, Combat));

            Move.OnUnitMoved += (unitId, path, yellow) =>
                viewRegistry.Get(unitId)?.PlayPath(path, moveConfig.hopDuration);

            Combat.OnUnitDamaged += (unitId, dmg) =>
                Debug.Log($"유닛 {unitId} 피해 {dmg} (HP {Battle.GetUnit(unitId).hp}/{Battle.GetUnit(unitId).maxHp})");
            Combat.OnUnitDied += unitId =>
                viewRegistry.Get(unitId)?.gameObject.SetActive(false);
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

        void Update()
        {
            if (Move == null) return;
            Move.Tick(Time.deltaTime);
            Combat.Tick(Time.deltaTime);

            worldView.Refresh();
            foreach (var driver in aiDrivers)
                driver.Tick(worldView);

            foreach (var unit in Battle.Units)
            {
                var view = viewRegistry.Get(unit.id);
                if (view == null || !unit.alive) continue;

                // 쿨타임/방어 시각화: 잠긴 유닛은 어둡게
                view.SetDimmed(unit.moveCooldown > 0f || unit.guardUntil > Battle.time);

                // 밀침·대시·점멸 등 연출 없는 위치 변경 동기화
                if (!view.IsMoving && !view.IsAt(unit.pos))
                    view.SnapTo(unit.pos);
            }
        }
    }
}
