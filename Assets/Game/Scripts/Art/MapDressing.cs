using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using SeoYuGi.Battle;
using SeoYuGi.BattleView;

namespace SeoYuGi.Art
{
    /// <summary>
    /// 맵을 뒷골목 세트로 꾸민다:
    ///  ① 바닥 배경판 — 맵 아래 큰 판에 골목 아스팔트 이미지(Resources/bg_alley) — 회색 허공 제거
    ///  ② 벽(장애물) 위 잡동사니 — 쓰레기통·덤불·바리케이드를 벽 블록 위에 얹어 장애물이 곧 골목 세트
    ///  ③ 가로등 — 맵 네 모서리 바깥
    /// 플레이 칸은 건드리지 않음 — 클릭 레이캐스트 방해 금지(콜라이더 전부 제거).
    /// 배치는 맵 이름 시드 — 같은 맵은 항상 같은 골목. 소품은 Resources GLB(prop_*) 우선, 없으면 절차 폴백.
    /// </summary>
    public static class MapDressingBootstrap
    {
        static readonly string[] SceneNames = { "SampleScene", "GoraniSkinTest" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (System.Array.IndexOf(SceneNames, SceneManager.GetActiveScene().name) < 0) return;
            new GameObject("MapDresser").AddComponent<MapDresser>();
        }
    }

    public class MapDresser : MonoBehaviour
    {
        const float FloorSurfaceY = 0.05f; // 바닥 타일 윗면
        const float WallSurfaceY = 0.55f;  // 벽 블록 윗면 (GridView.wallHeight 0.6 기준)
        const float PropJitter = 0.16f;    // 칸 안 무작위 오프셋 — 자로 잰 배치 느낌 제거

        BattleRunner runner;
        GridView gridView;
        string dressedMap; // 재시작으로 맵이 바뀌면 다시 꾸민다

        readonly Dictionary<string, GameObject> cache = new();

        void LateUpdate()
        {
            if (runner == null)
            {
                runner = FindFirstObjectByType<BattleRunner>();
                if (runner == null) return;
                gridView = runner.GetComponent<GridView>();
            }
            if (runner.Battle == null || runner.CurrentMap == null || gridView == null) return;
            if (dressedMap == runner.CurrentMap.Name) return;

            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
            Dress(runner.CurrentMap);
            dressedMap = runner.CurrentMap.Name;
        }

        void Dress(ParsedMap map)
        {
            var rng = new System.Random(map.Name.GetHashCode());
            var (trash, bush, density) = ThemeOf(map.Name);

            CreateGroundPlane(map);

            // 벽 위 잡동사니 — 장애물 실루엣은 유지하고 위에만 얹는다
            foreach (var w in map.Walls)
            {
                if (rng.NextDouble() > density) continue;
                double roll = rng.NextDouble();
                string prop = roll < trash ? "prop_trash_can"
                            : roll < trash + bush ? "prop_bush"
                            : "prop_barricade";
                Place(prop, gridView.CoordToWorld(w), WallSurfaceY, 0.42f, rng);
            }

            // 가로등 — 네 모서리 바깥 (배경판 위)
            foreach (var (x, y) in new[] { (-1, -1), (map.Width, -1), (-1, map.Height), (map.Width, map.Height) })
                Place("prop_street_lamp", gridView.CoordToWorld(new Coord(x, y)), 0f, 1.7f, rng);
        }

        /// <summary>맵별 변주 — (쓰레기통, 덤불 비중, 벽 장식 밀도). 나머지 비중은 바리케이드.</summary>
        static (float trash, float bush, float density) ThemeOf(string mapName)
        {
            switch (mapName)
            {
                case "막다른 골목": return (0.55f, 0.1f, 0.7f);   // 초소형 난투장 — 쓰레기통 빽빽
                case "옥상 종주": return (0.1f, 0.65f, 0.5f);     // 옥상 정원 느낌 — 덤불 위주
                case "뒷골목 미로": return (0.5f, 0.15f, 0.35f);
                case "공사장 섬": return (0.2f, 0.1f, 0.6f);
                case "청계 물류단지": return (0.2f, 0.05f, 0.65f); // 물류장 — 바리케이드(컨테이너 대역) 위주
                default: return (0.35f, 0.3f, 0.45f);
            }
        }

        /// <summary>맵 아래 골목 아스팔트 배경판 — 이미지 없으면 어두운 무광 판.</summary>
        void CreateGroundPlane(ParsedMap map)
        {
            float tile = (gridView.CoordToWorld(new Coord(1, 0)) - gridView.CoordToWorld(new Coord(0, 0))).x;
            var center = (gridView.CoordToWorld(new Coord(0, 0)) +
                          gridView.CoordToWorld(new Coord(map.Width - 1, map.Height - 1))) * 0.5f;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "AlleyGround";
            quad.transform.SetParent(transform, false);
            quad.transform.position = new Vector3(center.x, -0.06f, center.z); // 타일 바닥(-0.05)보다 아래
            quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3((map.Width + 10) * tile, (map.Height + 10) * tile, 1f);
            Destroy(quad.GetComponent<Collider>());

            var rend = quad.GetComponent<Renderer>();
            var tex = Resources.Load<Texture2D>("bg_alley");
            var mat = new Material(rend.sharedMaterial);
            if (tex != null)
            {
                // URP Lit은 _BaseMap, 빌트인은 _MainTex — 셰이더 불문 물리게 둘 다 세팅
                mat.mainTexture = tex;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
                mat.color = Color.white;
            }
            else
            {
                var dark = new Color(0.16f, 0.15f, 0.14f); // 이미지 미도착 폴백 — 어두운 아스팔트 톤
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", dark);
                mat.color = dark;
            }
            rend.sharedMaterial = mat;
        }

        void Place(string resource, Vector3 cellPos, float surfaceY, float height, System.Random rng)
        {
            var prefab = LoadProp(resource);
            var pos = cellPos;
            pos.y = surfaceY;
            pos.x += (float)(rng.NextDouble() * 2 - 1) * PropJitter;
            pos.z += (float)(rng.NextDouble() * 2 - 1) * PropJitter;

            GameObject go = prefab != null
                ? Instantiate(prefab, transform)
                : BuildFallback(resource);
            go.transform.SetParent(transform);
            go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            FitAndGround(go, pos, height);

            foreach (var col in go.GetComponentsInChildren<Collider>(true))
                Destroy(col); // 클릭 레이캐스트 방해 금지
        }

        GameObject LoadProp(string resource)
        {
            if (cache.TryGetValue(resource, out var p)) return p;
            p = Resources.Load<GameObject>(resource);
            cache[resource] = p; // null도 캐시 — 폴백 경로로
            return p;
        }

        /// <summary>모델 미도착 시 절차 생성 폴백 — 실루엣만이라도 골목답게.</summary>
        static GameObject BuildFallback(string resource)
        {
            GameObject go;
            var mpb = new MaterialPropertyBlock();
            int baseColorId = Shader.PropertyToID("_BaseColor");
            switch (resource)
            {
                case "prop_street_lamp":
                    go = new GameObject("lamp");
                    var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    pole.transform.SetParent(go.transform, false);
                    pole.transform.localScale = new Vector3(0.08f, 0.8f, 0.08f);
                    pole.transform.localPosition = Vector3.up * 0.8f;
                    var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    head.transform.SetParent(go.transform, false);
                    head.transform.localScale = Vector3.one * 0.25f;
                    head.transform.localPosition = Vector3.up * 1.65f;
                    mpb.SetColor(baseColorId, new Color(1f, 0.8f, 0.45f));
                    head.GetComponent<Renderer>().SetPropertyBlock(mpb);
                    return go;
                case "prop_bush":
                    go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.transform.localScale = new Vector3(0.5f, 0.35f, 0.5f);
                    mpb.SetColor(baseColorId, new Color(0.25f, 0.5f, 0.25f));
                    go.GetComponent<Renderer>().SetPropertyBlock(mpb);
                    return go;
                case "prop_barricade":
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.transform.localScale = new Vector3(0.55f, 0.45f, 0.5f);
                    mpb.SetColor(baseColorId, new Color(0.55f, 0.4f, 0.25f));
                    go.GetComponent<Renderer>().SetPropertyBlock(mpb);
                    return go;
                default: // prop_trash_can
                    go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    go.transform.localScale = new Vector3(0.3f, 0.25f, 0.3f);
                    mpb.SetColor(baseColorId, new Color(0.3f, 0.45f, 0.32f));
                    go.GetComponent<Renderer>().SetPropertyBlock(mpb);
                    return go;
            }
        }

        /// <summary>바운즈로 목표 높이 정규화 + 발바닥을 지정 표면 높이에.</summary>
        static void FitAndGround(GameObject go, Vector3 surfacePos, float targetHeight)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) { go.transform.position = surfacePos; return; }

            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            if (b.size.y > 0.001f)
                go.transform.localScale *= targetHeight / b.size.y;

            b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            var pos = go.transform.position;
            pos.x += surfacePos.x - b.center.x;
            pos.z += surfacePos.z - b.center.z;
            pos.y += surfacePos.y - b.min.y;
            go.transform.position = pos;
        }
    }
}
