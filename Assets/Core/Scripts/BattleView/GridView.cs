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
        [SerializeField] Color fogColor = new Color(0.16f, 0.17f, 0.22f); // 시야 밖 타일 — 어둡게 죽여 색 정보 제거
        [SerializeField] Color fogOverlayColor = new Color(0.34f, 0.38f, 0.5f, 0.5f); // 시야 밖 안개 구름 레이어
        [SerializeField] float fogOverlayHeight = 1.0f; // 안개 레이어가 뜨는 높이 (타일·낮은 프롭 위)

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
        readonly Dictionary<Coord, GameObject> fogOverlays = new Dictionary<Coord, GameObject>(); // 시야 밖 안개 쿼드 풀
        Material fogOverlayMat; // 안개 레이어 공유 머티리얼 (반투명 언릿)
        MaterialPropertyBlock mpb;
        Material matFloorA, matFloorB, matObstacle, matZone, matHighland;

        readonly List<GameObject> tileObjects = new List<GameObject>(); // 맵 교체 재빌드 시 파괴 대상
        // GLB 고지대 단상은 렌더러가 여러 개 — 틴트/안개를 전부에 적용하기 위한 목록
        readonly Dictionary<Coord, Renderer[]> tileExtraRenderers = new Dictionary<Coord, Renderer[]>();

        public void Build(GridModel grid)
        {
            // 맵 로테이션 — 이전 맵 타일·틴트·안개 상태를 전부 버리고 새로 짓는다
            foreach (var go in tileObjects)
                if (go != null) Destroy(go);
            tileObjects.Clear();
            tileExtraRenderers.Clear();
            highlighted.Clear();
            baseTints.Clear();
            fogged.Clear();
            foreach (var kv in fogOverlays)
                if (kv.Value != null) Destroy(kv.Value);
            fogOverlays.Clear();

            this.grid = grid;
            mpb = new MaterialPropertyBlock();
            tiles = new Renderer[grid.Width, grid.Height];

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

                if (type == CellType.Highland && HighlandProp() != null)
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
        }

        GameObject CreateTile(Vector3 pos, CellType type)
        {
            if (type == CellType.Highland)
            {
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
            UpdateFogOverlays();
        }

        /// <summary>시야 밖 칸 위에 반투명 안개 레이어를 켜고, 시야 안은 끈다. 살짝 일렁여 "안개" 느낌.</summary>
        void UpdateFogOverlays()
        {
            EnsureFogMat();
            // 은은한 맥동 — 색만 갱신하면 공유 머티리얼이라 1회로 전체 반영
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 1.3f);
            var col = fogOverlayColor;
            col.a *= pulse;
            fogOverlayMat.color = col;

            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var c = new Coord(x, y);
                if (tiles[x, y] == null) continue; // Void 칸 — 맵 밖, 안개 없음
                bool on = fogged.Contains(c);
                if (!fogOverlays.TryGetValue(c, out var ov))
                {
                    if (!on) continue;       // 아직 안개도 아니면 생성 미룸
                    ov = CreateFogOverlay(c);
                    fogOverlays[c] = ov;
                }
                if (ov.activeSelf != on) ov.SetActive(on);
            }
        }

        void EnsureFogMat()
        {
            if (fogOverlayMat != null) return;
            fogOverlayMat = new Material(Shader.Find("Sprites/Default")) { color = fogOverlayColor };
            fogOverlayMat.mainTexture = SoftBlobTexture(); // 부드러운 방사형 — 칸이 겹치며 구름처럼 뭉갬
        }

        static Texture2D softBlob;
        /// <summary>중앙 불투명 → 가장자리 투명한 부드러운 원. 칸마다 얹어 겹치면 각 없는 안개가 된다.</summary>
        static Texture2D SoftBlobTexture()
        {
            if (softBlob != null) return softBlob;
            const int n = 64;
            softBlob = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c; // 0(중앙)~1(가장자리)
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a); // smoothstep — 가장자리 부드럽게
                softBlob.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            softBlob.Apply();
            return softBlob;
        }

        GameObject CreateFogOverlay(Coord c)
        {
            var ov = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ov.name = $"Fog_{c.x}_{c.y}";
            ov.transform.SetParent(transform, false);
            Destroy(ov.GetComponent<Collider>());
            ov.transform.position = transform.position + new Vector3(c.x * tileSize, fogOverlayHeight, c.y * tileSize);
            ov.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // 바닥과 평행 (위에서 내려다봄)
            ov.transform.localScale = new Vector3(tileSize * 1.7f, tileSize * 1.7f, 1f); // 이웃과 겹쳐 경계 무마
            var r = ov.GetComponent<Renderer>();
            r.sharedMaterial = fogOverlayMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ov;
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

        /// <summary>틴트/안개 프로퍼티 블록 적용 — GLB 고지대 단상은 렌더러 전부에.</summary>
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
            }
        }
    }
}
