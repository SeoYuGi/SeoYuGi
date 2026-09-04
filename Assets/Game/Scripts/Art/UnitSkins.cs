using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using SeoYuGi.Battle;
using SeoYuGi.BattleView;

namespace SeoYuGi.Art
{
    /// <summary>
    /// 모든 유닛 큐브에 (팀, 클래스)별 GLB 스킨을 씌운다.
    /// 씬 배선 불필요 — SampleScene(팀 합의 작업 씬)에서 자동 발동하고,
    /// 라운드마다 유닛이 재생성되므로 매 프레임 미적용 유닛을 찾아 적용한다.
    /// 롤식 TacticalCamera도 함께 장착한다.
    /// </summary>
    public static class UnitSkinBootstrap
    {
        static readonly string[] SceneNames = { "SampleScene", "GoraniSkinTest" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (System.Array.IndexOf(SceneNames, SceneManager.GetActiveScene().name) < 0) return;
            new GameObject("UnitSkinApplier").AddComponent<UnitSkinApplier>();

            var cam = Camera.main;
            if (cam != null && cam.GetComponent<TacticalCamera>() == null)
            {
                var quarter = cam.GetComponent<QuarterViewCamera>();
                if (quarter != null) quarter.enabled = false;
                cam.gameObject.AddComponent<TacticalCamera>();
            }
        }
    }

    public class UnitSkinApplier : MonoBehaviour
    {
        const string SkinName = "Skin";
        const float BaseHeight = 1.1f;       // 기본 클래스(스케일 1)의 목표 키
        const float BaseCubeScale = 0.7f;    // BattleRunner 폴백 큐브의 기준 스케일
        const float UnitYOffset = 0.5f;      // UnitView.yOffset — 피벗에서 바닥까지

        // 팀 0 = 길동물, 팀 1 = 도시관리 AI. Resources의 GLB 이름과 매핑.
        static readonly Dictionary<(int team, UnitClass cls), string> Models = new()
        {
            { (0, UnitClass.Tank),      "raccoon_tank_rigged" },
            { (0, UnitClass.Balance),   "gorani_runner_rigged" },
            { (0, UnitClass.Assassin),  "blackcat_assassin_rigged" },
            { (0, UnitClass.Grenadier), "pigeon_grenadier_rigged" },
            { (0, UnitClass.Sniper),    "magpie_sniper_rigged" },
            { (1, UnitClass.Tank),      "cleaning_bot" },
            { (1, UnitClass.Balance),   "delivery_bot" },
            { (1, UnitClass.Assassin),  "patrol_drone" },
            { (1, UnitClass.Grenadier), "sprayer_drone" },
            { (1, UnitClass.Sniper),    "surveillance_drone" },
        };

        BattleRunner runner;
        readonly Dictionary<string, GameObject> cache = new();
        readonly HashSet<UnitView> attempted = new();

        void Awake()
        {
            runner = FindFirstObjectByType<BattleRunner>();
        }

        void LateUpdate()
        {
            if (runner == null || runner.Battle == null) return;

            foreach (var view in FindObjectsByType<UnitView>(FindObjectsSortMode.None))
            {
                if (attempted.Contains(view)) continue;
                attempted.Add(view);
                Apply(view);
            }

            // 파괴된 뷰 참조 정리 (라운드 전환)
            attempted.RemoveWhere(v => v == null);
        }

        void Apply(UnitView view)
        {
            UnitState unit = null;
            foreach (var u in runner.Battle.Units)
                if (u.id == view.UnitId) { unit = u; break; }
            if (unit == null) return;

            if (!Models.TryGetValue((unit.team, unit.unitClass), out var resource)) return;
            var prefab = LoadModel(resource);
            if (prefab == null) return; // 모델 없으면 큐브 유지 (미도착분 폴백)

            var skin = Instantiate(prefab, view.transform);
            skin.name = SkinName;
            skin.transform.localPosition = Vector3.zero;
            skin.transform.localRotation = Quaternion.identity;

            // BattleRunner가 클래스별로 큐브 스케일을 키워놓음 — 그 비율만큼 스킨도 크게
            float classScale = view.transform.localScale.y / BaseCubeScale;
            FitToUnit(skin, view.transform, BaseHeight * classScale);

            var cube = view.GetComponent<MeshRenderer>();
            if (cube != null) cube.enabled = false;
        }

        GameObject LoadModel(string resource)
        {
            if (cache.TryGetValue(resource, out var p)) return p;
            p = Resources.Load<GameObject>(resource);
            if (p == null)
                Debug.LogWarning($"[UnitSkins] Resources에 {resource} 없음 — 큐브 유지");
            cache[resource] = p; // null도 캐시해서 경고 1회만
            return p;
        }

        static void FitToUnit(GameObject skin, Transform unit, float targetHeight)
        {
            var rends = skin.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;

            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            if (b.size.y > 0.001f)
                skin.transform.localScale *= targetHeight / b.size.y;

            // 스케일 반영 후 다시 재서 발바닥을 칸 바닥에, 몸통 중심을 유닛 축에 맞춤
            b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            var pos = skin.transform.position;
            pos.y += (unit.position.y - UnitYOffset) - b.min.y;
            pos.x += unit.position.x - b.center.x;
            pos.z += unit.position.z - b.center.z;
            skin.transform.position = pos;
        }
    }
}
