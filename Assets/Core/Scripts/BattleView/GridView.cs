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
        public float TileSize => tileSize; // 칸 간격 — 거점 게이지 사각형 크기 계산 등 외부 뷰용
        [Range(0.5f, 1f)]
        [SerializeField] float tileFill = 0.96f;  // 타일이 칸을 채우는 비율. 나머지가 틈 = 그리드 라인
        [SerializeField] float wallHeight = 0.6f; // 장애물 벽 블록 높이
        [SerializeField] float highlandHeight = 0.35f; // 고지대 단상 높이 (벽보다 낮아 올라선 유닛이 보임)
        [SerializeField] Color fogColor = new Color(0.22f, 0.24f, 0.3f); // 시야 밖 타일 (세부기획 B)

        [Header("Textures")]
        [SerializeField] Texture2D floorTextureA;
        [SerializeField] Texture2D floorTextureB;   // 바닥에 드문드문 섞이는 변형 (~20%)
        [SerializeField] Texture2D obstacleTexture;
        [SerializeField] Texture2D zoneTexture;     // 거점 타일
        [SerializeField] Texture2D highlandTexture; // 고지대 단상 (비우면 장애물 텍스처)

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP

        GridModel grid;
        Renderer[,] tiles;
        readonly List<Coord> highlighted = new List<Coord>();
        // 하이라이트가 걷힌 뒤에도 유지되는 기본 틴트 (거점 소유 표시 등)
        readonly Dictionary<Coord, Color> baseTints = new Dictionary<Coord, Color>();
        readonly HashSet<Coord> fogged = new HashSet<Coord>();
        MaterialPropertyBlock mpb;
        Material matFloorA, matFloorB, matObstacle, matZone, matHighland;

        readonly List<GameObject> tileObjects = new List<GameObject>(); // 맵 교체 재빌드 시 파괴 대상
        readonly HashSet<Coord> hillPlateCells = new HashSet<Coord>(); // 언덕 상판 — 틴트 있을 때만 렌더
        // GLB 고지대 단상은 렌더러가 여러 개 — 틴트/안개를 전부에 적용하기 위한 목록
        readonly Dictionary<Coord, Renderer[]> tileExtraRenderers = new Dictionary<Coord, Renderer[]>();

        public void Build(GridModel grid)
        {
            // 맵 로테이션 — 이전 맵 타일·틴트·안개 상태를 전부 버리고 새로 짓는다
            foreach (var go in tileObjects)
                if (go != null) Destroy(go);
            tileObjects.Clear();
            tileExtraRenderers.Clear();
            hillPlateCells.Clear();
            highlighted.Clear();
            baseTints.Clear();
            fogged.Clear();

            this.grid = grid;
            mpb = new MaterialPropertyBlock();
            tiles = new Renderer[grid.Width, grid.Height];

            // 언덕 모드 — 단상용 낮은 높이(씬 직렬화 0.35)로는 언덕이 안 산다.
            // 벽(0.6)의 딱 2배 — 고지대가 벽을 내려다보되 과하지 않게.
            // 언덕 메시 Y스케일·유닛 서는 높이·칸 상판 전부 이 값을 따라간다.
            if (HillProp() != null) highlandHeight = Mathf.Max(highlandHeight, 1.2f);

            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var coord = new Coord(x, y);
                var type = grid.GetCell(coord).type;
                if (type == CellType.Void) continue; // 구덩이 — 타일 없음(맵 실루엣)
                // CoordToWorld는 고지대 표면 높이를 더하므로 타일 생성은 평면 기준으로
                var flat = transform.position + new Vector3(x * tileSize, 0f, y * tileSize);
                var go = CreateTile(flat, type);
                tileObjects.Add(go);
                go.name = type switch
                {
                    CellType.Obstacle => $"Wall_{x}_{y}",
                    CellType.Highland => $"Highland_{x}_{y}",
                    _ => $"Tile_{x}_{y}"
                };
                tiles[x, y] = go.GetComponentInChildren<Renderer>();

                if (type == CellType.Highland && HillProp() != null)
                {
                    // 언덕 모드 상판 — 평소엔 렌더러 꺼짐, 하이라이트/안개 틴트가 올 때만 켜진다
                    tiles[x, y].sharedMaterial = HillPlateMaterial(tiles[x, y].sharedMaterial);
                    tiles[x, y].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    tiles[x, y].enabled = false;
                    hillPlateCells.Add(coord);
                }
                else if (type == CellType.Highland && HighlandProp() != null)
                {
                    // GLB 단상 — 원본 텍스처 유지, 틴트/안개는 모든 렌더러에 적용
                    var rends = go.GetComponentsInChildren<Renderer>();
                    if (rends.Length > 1) tileExtraRenderers[coord] = rends;
                }
                else if (floorTextureA != null)
                    tiles[x, y].sharedMaterial = MaterialOf(coord, type);
                else if (type == CellType.Highland) // 텍스처 미사용 모드에서도 고지대는 구분돼야 함
                    tiles[x, y].sharedMaterial = HighlandMaterial(tiles[x, y].sharedMaterial);
            }

            BuildHillClusters();
        }

        GameObject CreateTile(Vector3 pos, CellType type)
        {
            if (type == CellType.Highland)
            {
                // 언덕 모드(prop_hill) — 덩어리는 군집당 언덕 GLB가 맡고,
                // 칸에는 얇은 상판만 (하이라이트·안개 표시 + 클릭 콜라이더)
                if (HillProp() != null)
                {
                    var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    plate.transform.SetParent(transform);
                    plate.transform.localScale = new Vector3(tileSize * tileFill, 0.06f, tileSize * tileFill);
                    plate.transform.position = pos + Vector3.up * (highlandHeight - 0.08f); // 상판 윗면 = 기존 단상과 동일
                    return plate;
                }
                // 고지대 리마스터 — Resources GLB 단상(prop_highland)이 있으면 그걸로, 없으면 절차 줄무늬 큐브
                var prop = HighlandProp();
                if (prop != null) return CreateHighlandProp(prop, pos);
            }
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(transform);
            float side = tileSize * tileFill;

            if (type == CellType.Obstacle)
            {
                // 벽 블록: 바닥 윗면(y=0.05)에서 시작해 wallHeight만큼
                go.transform.localScale = new Vector3(side, wallHeight, side);
                go.transform.position = pos + Vector3.up * (wallHeight * 0.5f - 0.05f);
            }
            else if (type == CellType.Highland)
            {
                // 고지대 단상: 벽과 같은 기준면에서 highlandHeight만큼 — 유닛이 위에 올라선다
                go.transform.localScale = new Vector3(side, highlandHeight, side);
                go.transform.position = pos + Vector3.up * (highlandHeight * 0.5f - 0.05f);
            }
            else
            {
                go.transform.localScale = new Vector3(side, 0.1f, side);
                go.transform.position = pos;
            }
            return go;
        }

        /// <summary>텍스처 머티리얼들을 타일 원본 머티리얼 기반으로 1회 생성.</summary>
        Material MaterialOf(Coord c, CellType type)
        {
            if (matFloorA == null)
            {
                var template = tiles[c.x, c.y].sharedMaterial;
                matFloorA = new Material(template) { mainTexture = floorTextureA };
                matFloorB = new Material(template) { mainTexture = floorTextureB != null ? floorTextureB : floorTextureA };
                matObstacle = new Material(template) { mainTexture = obstacleTexture != null ? obstacleTexture : floorTextureA };
                matZone = new Material(template) { mainTexture = zoneTexture != null ? zoneTexture : floorTextureA };
                matHighland = HighlandMaterial(template);
            }
            if (type == CellType.Obstacle) return matObstacle;
            if (type == CellType.Highland) return matHighland;
            return FloorVariant(c) ? matFloorB : matFloorA;
        }

        GameObject highlandProp;
        bool highlandPropLoaded;

        GameObject HighlandProp()
        {
            if (!highlandPropLoaded)
            {
                highlandPropLoaded = true;
                highlandProp = Resources.Load<GameObject>("prop_highland"); // null이면 절차 폴백
            }
            return highlandProp;
        }

        GameObject hillProp;
        bool hillPropLoaded;

        GameObject HillProp()
        {
            if (!hillPropLoaded)
            {
                hillPropLoaded = true;
                hillProp = Resources.Load<GameObject>("prop_hill"); // null이면 칸별 단상 폴백
            }
            return hillProp;
        }

        /// <summary>
        /// 언덕 모드 — 고지대 칸들을 최대 직사각형으로 잘라 직사각형마다 언덕 GLB 1개.
        /// 바운딩 박스가 아니라 정확한 사각형이라 링 안쪽 거점·바닥을 절대 안 덮는다.
        /// 틴트/안개는 칸 상판이 담당 — 언덕 메시는 틴트 없이 원본 그대로.
        /// </summary>
        void BuildHillClusters()
        {
            var prop = HillProp();
            if (prop == null) return;

            var covered = new HashSet<Coord>();
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                if (covered.Contains(new Coord(x, y)) || !grid.IsHighland(new Coord(x, y))) continue;

                // 오른쪽으로 최대 확장
                int w = 1;
                while (x + w < grid.Width && grid.IsHighland(new Coord(x + w, y))
                       && !covered.Contains(new Coord(x + w, y))) w++;
                // 아래로 — 행 전체가 고지대일 때만 확장
                int h = 1;
                bool rowOk = true;
                while (rowOk && y + h < grid.Height)
                {
                    for (int i = 0; i < w; i++)
                        if (!grid.IsHighland(new Coord(x + i, y + h))
                            || covered.Contains(new Coord(x + i, y + h))) { rowOk = false; break; }
                    if (rowOk) h++;
                }

                for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                    covered.Add(new Coord(x + dx, y + dy));
                CreateHillProp(prop, x, y, x + w - 1, y + h - 1);
            }
        }

        void CreateHillProp(GameObject prop, int minX, int minY, int maxX, int maxY)
        {
            float w = (maxX - minX + 1) * tileSize * 1.06f; // 살짝 넘치게 — 경사가 칸 밖으로 자연스럽게
            float d = (maxY - minY + 1) * tileSize * 1.06f;
            var center = transform.position + new Vector3(
                (minX + maxX) * 0.5f * tileSize, 0f, (minY + maxY) * 0.5f * tileSize);

            var visual = Instantiate(prop, transform);
            visual.name = $"Hill_{minX}_{minY}";
            tileObjects.Add(visual);
            foreach (var c in visual.GetComponentsInChildren<Collider>(true))
                Destroy(c); // 클릭 콜라이더는 칸 상판이 담당

            var rends = visual.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            // 실루엣 과장 — 윗면(플래토)은 서는 높이에 고정, Y를 더 늘려 아랫단은 땅속으로.
            // 생성 메시가 납작해 보이는 문제를 경사를 세워서 해결 (유닛 높이 불변).
            float visualH = highlandHeight * 1.2f;
            var s = visual.transform.localScale;
            visual.transform.localScale = new Vector3(
                s.x * w / Mathf.Max(b.size.x, 0.001f),
                s.y * visualH / Mathf.Max(b.size.y, 0.001f),
                s.z * d / Mathf.Max(b.size.z, 0.001f));
            b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            visual.transform.position += new Vector3(
                center.x - b.center.x, (highlandHeight - 0.05f) - b.max.y, center.z - b.center.z);
        }

        /// <summary>
        /// GLB 고지대 단상: 클릭 레이캐스트용 박스 콜라이더는 큐브와 동일 규격으로 유지하고,
        /// 비주얼만 GLB로 교체. 발자국은 칸 크기, 높이는 highlandHeight에 바운즈 맞춤.
        /// </summary>
        GameObject CreateHighlandProp(GameObject prop, Vector3 pos)
        {
            float side = tileSize * tileFill;
            var root = new GameObject("HighlandProp");
            root.transform.SetParent(transform);
            root.transform.position = pos + Vector3.up * (highlandHeight * 0.5f - 0.05f);
            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(side, highlandHeight, side);

            var visual = Instantiate(prop, root.transform);
            foreach (var c in visual.GetComponentsInChildren<Collider>(true))
                Destroy(c); // 콜라이더는 루트 박스 하나로 충분

            var rends = visual.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                var s = visual.transform.localScale;
                visual.transform.localScale = new Vector3(
                    s.x * side / Mathf.Max(b.size.x, 0.001f),
                    s.y * highlandHeight / Mathf.Max(b.size.y, 0.001f),
                    s.z * side / Mathf.Max(b.size.z, 0.001f));
                b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                visual.transform.position += new Vector3(pos.x - b.center.x, -0.05f - b.min.y, pos.z - b.center.z);
            }
            return root;
        }

        Material matHillPlate;

        /// <summary>언덕 칸 상판 — 무텍스처 흰 판. 평소 렌더러 꺼짐, 틴트가 올 때만 켜져 색판으로 보인다.</summary>
        Material HillPlateMaterial(Material template)
        {
            if (matHillPlate == null)
                matHillPlate = new Material(template) { mainTexture = null };
            return matHillPlate;
        }

        /// <summary>고지대 전용 머티리얼 — 벽(크레이트)과 절대 헷갈리지 않게, 에셋 없으면 절차 생성 줄무늬.</summary>
        Material HighlandMaterial(Material template)
        {
            if (matHighland != null) return matHighland;
            matHighland = new Material(template)
            {
                mainTexture = highlandTexture != null ? highlandTexture : BuildHighlandStripes()
            };
            return matHighland;
        }

        /// <summary>주황 발판 + 모서리 사선 해저드 줄무늬 — "올라설 수 있는 단상"이 한눈에 읽히게.</summary>
        static Texture2D BuildHighlandStripes()
        {
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var deck   = new Color(0.93f, 0.66f, 0.32f); // 발판 면
            var deckB  = new Color(0.88f, 0.60f, 0.27f); // 미세 체크
            var hazard = new Color(0.16f, 0.14f, 0.12f); // 사선 줄무늬 (검정)
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                int edge = Math.Min(Math.Min(x, S - 1 - x), Math.Min(y, S - 1 - y));
                if (edge < 9) // 테두리 — 검/주황 사선 해저드
                    tex.SetPixel(x, y, ((x + y) / 6) % 2 == 0 ? hazard : deck);
                else
                    tex.SetPixel(x, y, ((x / 8) + (y / 8)) % 2 == 0 ? deck : deckB);
            }
            tex.Apply();
            return tex;
        }

        /// <summary>좌표 해시로 ~20% 칸에 변형 바닥. 결정론 — 같은 좌표는 항상 같은 무늬.</summary>
        static bool FloorVariant(Coord c) =>
            (((c.x * 73856093) ^ (c.y * 19349663)) & 0x7fffffff) % 5 == 0;

        /// <summary>칸의 표면 기준 월드 좌표 — 고지대 칸은 단상 윗면 높이가 더해진다.</summary>
        public Vector3 CoordToWorld(Coord c)
        {
            float y = grid != null && grid.IsHighland(c) ? highlandHeight - 0.1f : 0f;
            return transform.position + new Vector3(c.x * tileSize, y, c.y * tileSize);
        }

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
                if (!grid.InBounds(c) || tiles[c.x, c.y] == null) continue; // Void 칸은 타일 없음
                mpb.SetColor(BaseColorId, colors[i]);
                ApplyBlock(c, mpb);
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
                if (tiles[c.x, c.y] != null)
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
            if (tiles[c.x, c.y] == null) return; // Void 칸
            if (fogged.Contains(c))
            {
                mpb.SetColor(BaseColorId, fogColor);
                ApplyBlock(c, mpb);
            }
            else if (baseTints.TryGetValue(c, out var tint))
            {
                mpb.SetColor(BaseColorId, tint);
                ApplyBlock(c, mpb);
            }
            else
            {
                ApplyBlock(c, null); // 원본 텍스처 색
            }
        }

        /// <summary>틴트/안개 프로퍼티 블록 적용 — GLB 고지대 단상은 렌더러 전부에.
        /// 언덕 상판은 틴트가 있을 때만 렌더러를 켠다 (평소엔 언덕 메시만 보이게).</summary>
        void ApplyBlock(Coord c, MaterialPropertyBlock block)
        {
            if (tileExtraRenderers.TryGetValue(c, out var rends))
            {
                foreach (var r in rends)
                    if (r != null) r.SetPropertyBlock(block);
            }
            else
            {
                tiles[c.x, c.y].SetPropertyBlock(block);
                if (hillPlateCells.Contains(c))
                    tiles[c.x, c.y].enabled = block != null;
            }
        }
    }
}
