using System;
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
        [SerializeField] Color fogColor = new Color(0.22f, 0.24f, 0.3f); // 시야 밖 타일 (세부기획 B)

        [Header("Textures")]
        [SerializeField] Texture2D floorTextureA;
        [SerializeField] Texture2D floorTextureB;   // 바닥에 드문드문 섞이는 변형 (~20%)
        [SerializeField] Texture2D obstacleTexture;
        [SerializeField] Texture2D zoneTexture;     // 거점 타일

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP

        GridModel grid;
        Renderer[,] tiles;
        readonly List<Coord> highlighted = new List<Coord>();
        // 하이라이트가 걷힌 뒤에도 유지되는 기본 틴트 (거점 소유 표시 등)
        readonly Dictionary<Coord, Color> baseTints = new Dictionary<Coord, Color>();
        readonly HashSet<Coord> fogged = new HashSet<Coord>();
        MaterialPropertyBlock mpb;
        Material matFloorA, matFloorB, matObstacle, matZone;

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
                matZone = new Material(template) { mainTexture = zoneTexture != null ? zoneTexture : floorTextureA };
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
                ResetTile(c);
            highlighted.Clear();
        }

        /// <summary>거점 칸을 거점 텍스처로 표시. Build 이후 호출.</summary>
        public void MarkZones(IReadOnlyList<Coord> zoneCells)
        {
            if (matZone == null) return; // 텍스처 미사용 모드
            foreach (var c in zoneCells)
                tiles[c.x, c.y].sharedMaterial = matZone;
        }

        /// <summary>하이라이트가 없을 때 유지되는 틴트 (거점 소유 팀 색 등).</summary>
        public void SetBaseTint(Coord c, Color color)
        {
            baseTints[c] = color;
            if (!highlighted.Contains(c)) ResetTile(c);
        }

        /// <summary>거점 소유 틴트 전부 해제 — 라운드 재시작용.</summary>
        public void ClearBaseTints()
        {
            baseTints.Clear();
            RepaintAll();
        }

        /// <summary>
        /// 팀 시야로 안개 갱신 — 매 프레임 호출 (세부기획 B).
        /// 시야 밖 타일은 fogColor로 어둡게. 하이라이트(이동 범위·예고)가 안개보다 우선.
        /// </summary>
        public void UpdateFog(Func<Coord, bool> visible)
        {
            fogged.Clear();
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var c = new Coord(x, y);
                if (!visible(c)) fogged.Add(c);
            }
            RepaintAll();
        }

        void RepaintAll()
        {
            if (tiles == null) return;
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var c = new Coord(x, y);
                if (!highlighted.Contains(c)) ResetTile(c);
            }
        }

        void ResetTile(Coord c)
        {
            if (fogged.Contains(c))
            {
                mpb.SetColor(BaseColorId, fogColor);
                tiles[c.x, c.y].SetPropertyBlock(mpb);
            }
            else if (baseTints.TryGetValue(c, out var tint))
            {
                mpb.SetColor(BaseColorId, tint);
                tiles[c.x, c.y].SetPropertyBlock(mpb);
            }
            else
            {
                tiles[c.x, c.y].SetPropertyBlock(null); // 원본 텍스처 색
            }
        }
    }
}
