using System;
using System.Collections.Generic;
using SeoYuGi.Battle;
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
        }

        [Header("Config")]
        [SerializeField] GridConfig gridConfig = new GridConfig { width = 9, height = 9 }; // 기획서: 9×9
        [SerializeField] MoveConfig moveConfig = new MoveConfig();

        [Header("Map (자동 생성)")]
        [SerializeField] int mapSeed = 20260904;
        [Range(4, 8)]
        [SerializeField] int wallTiles = 8; // 세부기획 B: 벽 4~8개

        [Header("Setup — 3v3")]
        [SerializeField] List<UnitSpawn> spawns = new List<UnitSpawn>
        {
            new UnitSpawn { id = 1, team = 0, pos = new Coord(2, 1) },
            new UnitSpawn { id = 2, team = 0, pos = new Coord(4, 1) },
            new UnitSpawn { id = 3, team = 0, pos = new Coord(6, 1) },
            new UnitSpawn { id = 4, team = 1, pos = new Coord(2, 7) },
            new UnitSpawn { id = 5, team = 1, pos = new Coord(4, 7) },
            new UnitSpawn { id = 6, team = 1, pos = new Coord(6, 7) }
        };
        [SerializeField] Color[] teamColors = { new Color(0.25f, 0.5f, 1f), new Color(1f, 0.3f, 0.25f) };

        [Tooltip("비우면 큐브 유닛 자동 생성")]
        [SerializeField] UnitView unitPrefab;

        // 같은 GameObject에서 자동 연결 — 인스펙터 배선 불필요
        GridView gridView;
        UnitMoveInput input;
        UnitViewRegistry viewRegistry;

        public MoveSystem Move { get; private set; }
        public BattleState Battle { get; private set; }

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
                Battle.AddUnit(new UnitState(s.id, s.team, s.pos));

            Move = new MoveSystem(Battle, moveConfig);

            gridView.Build(grid);
            foreach (var s in spawns)
            {
                var view = CreateUnitView();
                view.name = $"Unit_{s.id}";
                view.Bind(s.id, teamColors[s.team], gridView, s.pos);
                viewRegistry.Register(view);
            }
            input.Init(Move, gridView, viewRegistry);

            Move.OnUnitMoved += (unitId, path, yellow) =>
                viewRegistry.Get(unitId)?.PlayPath(path, moveConfig.hopDuration);
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

            // 쿨타임 시각화: 이동 잠긴 유닛은 어둡게
            foreach (var unit in Battle.Units)
                viewRegistry.Get(unit.id)?.SetDimmed(unit.moveCooldown > 0f);
        }
    }
}
