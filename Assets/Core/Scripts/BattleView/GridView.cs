using System.Collections.Generic;
using SeoYuGi.Battle;
using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 타일 렌더 + 좌표↔월드 변환 + 하이라이트(기획서 §6.1).
    /// XZ 평면, 이 오브젝트 위치가 (0,0) 타일 중심.
    /// 체커보드 틴트 + 타일 간격으로 그리드 라인이 보이게 한다.
    /// </summary>
    public class GridView : MonoBehaviour
    {
        [SerializeField] GameObject tilePrefab;   // Renderer + Collider 필수. 비우면 큐브 자동 생성
        [SerializeField] float tileSize = 1f;
        [Range(0.5f, 1f)]
        [SerializeField] float tileFill = 0.92f;  // 타일이 칸을 채우는 비율. 나머지가 틈 = 그리드 라인
        [SerializeField] Color tileColorA = new Color(0.55f, 0.58f, 0.55f);
        [SerializeField] Color tileColorB = new Color(0.45f, 0.48f, 0.45f);
        [SerializeField] Color obstacleColor = new Color(0.2f, 0.16f, 0.16f);

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP

        GridModel grid;
        Renderer[,] tiles;
        readonly List<Coord> highlighted = new List<Coord>();
        MaterialPropertyBlock mpb;

        public float TileSize => tileSize;

        public void Build(GridModel grid)
        {
            this.grid = grid;
            mpb = new MaterialPropertyBlock();
            tiles = new Renderer[grid.Width, grid.Height];

            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var coord = new Coord(x, y);
                var go = CreateTile(CoordToWorld(coord));
                go.name = $"Tile_{x}_{y}";
                tiles[x, y] = go.GetComponentInChildren<Renderer>();

                SetTileColor(coord, BaseColorOf(coord));
            }
        }

        GameObject CreateTile(Vector3 pos)
        {
            GameObject go;
            if (tilePrefab != null)
                go = Instantiate(tilePrefab, pos, tilePrefab.transform.rotation, transform);
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.SetParent(transform);
                go.transform.position = pos;
                go.transform.localScale = new Vector3(1f, 0.1f, 1f);
            }

            // 프리팹 크기와 무관하게 바닥 면적을 칸 크기에 맞춤 (Plane=10×10 같은 사고 방지)
            var renderer = go.GetComponentInChildren<Renderer>();
            var size = renderer.bounds.size;
            float footprint = Mathf.Max(size.x, size.z);
            if (footprint > 0.0001f)
            {
                float k = tileSize * tileFill / footprint;
                var s = go.transform.localScale;
                go.transform.localScale = new Vector3(s.x * k, s.y, s.z * k);
            }
            return go;
        }

        public Vector3 CoordToWorld(Coord c) =>
            transform.position + new Vector3(c.x * tileSize, 0f, c.y * tileSize);

        public Coord WorldToCoord(Vector3 world)
        {
            var local = world - transform.position;
            return new Coord(
                Mathf.RoundToInt(local.x / tileSize),
                Mathf.RoundToInt(local.z / tileSize));
        }

        /// <summary>계획 미리보기용. 셀마다 색 지정(파랑=무료, 노랑=쿨타임). 이전 하이라이트는 초기화.</summary>
        public void SetHighlights(IReadOnlyList<Coord> coords, IReadOnlyList<Color> colors)
        {
            ClearHighlights();
            for (int i = 0; i < coords.Count; i++)
            {
                var c = coords[i];
                if (!grid.InBounds(c)) continue;
                SetTileColor(c, colors[i]);
                highlighted.Add(c);
            }
        }

        public void ClearHighlights()
        {
            foreach (var c in highlighted)
                SetTileColor(c, BaseColorOf(c));
            highlighted.Clear();
        }

        Color BaseColorOf(Coord c)
        {
            if (grid.GetCell(c).type == CellType.Obstacle) return obstacleColor;
            return (c.x + c.y) % 2 == 0 ? tileColorA : tileColorB;
        }

        void SetTileColor(Coord c, Color color)
        {
            mpb.SetColor(BaseColorId, color);
            tiles[c.x, c.y].SetPropertyBlock(mpb);
        }
    }
}
