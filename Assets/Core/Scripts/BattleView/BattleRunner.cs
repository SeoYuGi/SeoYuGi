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
        [SerializeField] GridConfig gridConfig = new GridConfig();
        [SerializeField] MoveConfig moveConfig = new MoveConfig();

        [Header("Setup")]
        [SerializeField] List<UnitSpawn> spawns = new List<UnitSpawn>
        {
            new UnitSpawn { id = 1, team = 0, pos = new Coord(2, 2) },
            new UnitSpawn { id = 2, team = 0, pos = new Coord(4, 2) },
            new UnitSpawn { id = 3, team = 1, pos = new Coord(7, 9) },
            new UnitSpawn { id = 4, team = 1, pos = new Coord(9, 9) }
        };
        [SerializeField] List<Coord> obstacles = new List<Coord>
        {
            new Coord(5, 5), new Coord(6, 5), new Coord(5, 6), new Coord(6, 6)
        };
        [SerializeField] Color[] teamColors = { new Color(0.25f, 0.5f, 1f), new Color(1f, 0.3f, 0.25f) };

        [Header("References")]
        [SerializeField] GridView gridView;
        [SerializeField] UnitMoveInput input;
        [SerializeField] UnitViewRegistry viewRegistry;
        [SerializeField] UnitView unitPrefab;

        public MoveSystem Move { get; private set; }
        public BattleState Battle { get; private set; }

        void Awake()
        {
            // 인스펙터 배선 빠뜨려도 동작하게 자동 탐색
            if (gridView == null) gridView = GetComponent<GridView>();
            if (input == null) input = GetComponent<UnitMoveInput>();
            if (viewRegistry == null)
            {
                viewRegistry = GetComponent<UnitViewRegistry>();
                if (viewRegistry == null) viewRegistry = gameObject.AddComponent<UnitViewRegistry>();
            }
        }

        void Start()
        {
            var grid = new GridModel(gridConfig);
            foreach (var c in obstacles)
                grid.SetObstacle(c);

            Battle = new BattleState(grid);
            foreach (var s in spawns)
                Battle.AddUnit(new UnitState(s.id, s.team, s.pos));

            Move = new MoveSystem(Battle, moveConfig);

            gridView.Build(grid);
            foreach (var s in spawns)
            {
                var view = Instantiate(unitPrefab, transform);
                view.name = $"Unit_{s.id}";
                view.Bind(s.id, teamColors[s.team], gridView, s.pos);
                viewRegistry.Register(view);
            }
            input.Init(Move, gridView, viewRegistry);

            Move.OnUnitMoved += (unitId, path, yellow) =>
                viewRegistry.Get(unitId)?.PlayPath(path, moveConfig.hopDuration);
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
