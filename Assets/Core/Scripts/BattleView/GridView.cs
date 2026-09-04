using System.Collections.Generic;
using SeoYuGi.Battle;
using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 타일 렌더 + 좌표↔월드 변환 + 하이라이트.
    /// XZ 평면, 이 오브젝트 위치가 (0,0) 타일 중심.
    /// 바닥: 텍스처 A 위주 + B를 드문드문 섞은 무늬(체커보드 아님).
    /// 장애물: 벽 블록으로 입체화 — 골목 구조(세부기획 B).
    /// </summary>
    public class GridView : MonoBehaviour
    {
        [SerializeField] float tileSize = 1f;
        [Range(0.5f, 1f)]
        [SerializeField] float tileFill = 0.96f;  // 타일이 칸을 채우는 비율. 나머지가 틈 = 그리드 라인
        [SerializeField] float wallHeight = 0.6f; // 장애물 벽 블록 높이

        [Header("Textures")]
        [SerializeField] Texture2D floorTextureA;
        [SerializeField] Texture2D floorTextureB;   // 바닥에 드문드문 섞이는 변형 (~20%)
        [SerializeField] Texture2D obstacleTexture;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP

        GridModel grid;
        Renderer[,] tiles;
        readonly List<Coord> highlighted = new List<Coord>();
        MaterialPropertyBlock mpb;
        Material matFloorA, matFloorB, matObstacle;

        public void Build(GridModel grid)
        {
            this.grid = grid;
            mpb = new MaterialPropertyBlock();
            tiles = new Renderer[grid.Width, grid.Height];

            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var coord = new Coord(x, y);
                bool isWall = grid.GetCell(coord).type == CellType.Obstacle;
                var go = CreateTile(CoordToWorld(coord), isWall);
                go.name = isWall ? $"Wall_{x}_{y}" : $"Tile_{x}_{y}";
                tiles[x, y] = go.GetComponentInChildren<Renderer>();

                if (floorTextureA != null)
                    tiles[x, y].sharedMaterial = MaterialOf(coord, isWall);
            }
        }

        GameObject CreateTile(Vector3 pos, bool isWall)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(transform);
            float side = tileSize * tileFill;

            if (isWall)
            {
                // 벽 블록: 바닥 윗면(y=0.05)에서 시작해 wallHeight만큼
                go.transform.localScale = new Vector3(side, wallHeight, side);
                go.transform.position = pos + Vector3.up * (wallHeight * 0.5f - 0.05f);
            }
            else
            {
                go.transform.localScale = new Vector3(side, 0.1f, side);
                go.transform.position = pos;
            }
            return go;
        }

        /// <summary>텍스처 머티리얼 3종을 타일 원본 머티리얼 기반으로 1회 생성.</summary>
        Material MaterialOf(Coord c, bool isWall)
        {
            if (matFloorA == null)
            {
                var template = tiles[c.x, c.y].sharedMaterial;
                matFloorA = new Material(template) { mainTexture = floorTextureA };
                matFloorB = new Material(template) { mainTexture = floorTextureB != null ? floorTextureB : floorTextureA };
                matObstacle = new Material(template) { mainTexture = obstacleTexture != null ? obstacleTexture : floorTextureA };
            }
            if (isWall) return matObstacle;
            return FloorVariant(c) ? matFloorB : matFloorA;
        }

        /// <summary>좌표 해시로 ~20% 칸에 변형 바닥. 결정론 — 같은 좌표는 항상 같은 무늬.</summary>
        static bool FloorVariant(Coord c) =>
            (((c.x * 73856093) ^ (c.y * 19349663)) & 0x7fffffff) % 5 == 0;

        public Vector3 CoordToWorld(Coord c) =>
            transform.position + new Vector3(c.x * tileSize, 0f, c.y * tileSize);

        public Coord WorldToCoord(Vector3 world)
        {
            var local = world - transform.position;
            return new Coord(
                Mathf.RoundToInt(local.x / tileSize),
                Mathf.RoundToInt(local.z / tileSize));
        }

        /// <summary>이동 범위 표시. 셀마다 색 지정(파랑/노랑). 이전 하이라이트는 초기화.</summary>
        public void SetHighlights(IReadOnlyList<Coord> coords, IReadOnlyList<Color> colors)
        {
            ClearHighlights();
            for (int i = 0; i < coords.Count; i++)
            {
                var c = coords[i];
                if (!grid.InBounds(c)) continue;
                mpb.SetColor(BaseColorId, colors[i]);
                tiles[c.x, c.y].SetPropertyBlock(mpb);
                highlighted.Add(c);
            }
        }

        public void ClearHighlights()
        {
            foreach (var c in highlighted)
                tiles[c.x, c.y].SetPropertyBlock(null); // 틴트 제거 → 원본 텍스처 색
            highlighted.Clear();
        }
    }
}
