using UnityEngine;
using UnityEngine.SceneManagement;
using SeoYuGi.BattleView;

namespace SeoYuGi.Art
{
    /// <summary>
    /// GoraniSkinTest 씬 전용 테스트 훅: 플레이어 유닛(브라보) 큐브에
    /// Resources의 고라니 GLB(gorani_runner_rigged)를 스킨으로 씌운다.
    /// 씬 배선이 필요 없도록 씬 로드 시 스스로 생성된다. 라운드마다 유닛이
    /// 재생성되므로 매 프레임 미적용 유닛을 찾아 다시 씌운다.
    /// </summary>
    public static class GoraniSkinTestBootstrap
    {
        // 팀 합의(9/4): 작업은 SampleScene에서 — 스킨·카메라 훅도 거기서 돈다
        static readonly string[] SceneNames = { "SampleScene", "GoraniSkinTest" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (System.Array.IndexOf(SceneNames, SceneManager.GetActiveScene().name) < 0) return;
            new GameObject("GoraniSkinApplier").AddComponent<GoraniSkinApplier>();

            // 롤식 카메라 장착 (씬에 QuarterViewCamera가 있으면 꺼서 충돌 방지)
            var cam = Camera.main;
            if (cam != null && cam.GetComponent<TacticalCamera>() == null)
            {
                var quarter = cam.GetComponent<QuarterViewCamera>();
                if (quarter != null) quarter.enabled = false;
                cam.gameObject.AddComponent<TacticalCamera>();
            }
        }
    }

    public class GoraniSkinApplier : MonoBehaviour
    {
        const int PlayerUnitId = 2;              // BattleRunner.playerUnitId 기본값(브라보)
        const string SkinName = "GoraniSkin";
        const string ModelResource = "gorani_runner_rigged";
        const float TargetHeight = 1.1f;         // 칸 크기 기준 목표 키(월드 단위)
        const float UnitYOffset = 0.5f;          // UnitView.yOffset — 피벗에서 바닥까지

        GameObject prefab;

        void Awake()
        {
            prefab = Resources.Load<GameObject>(ModelResource);
            if (prefab == null)
            {
                Debug.LogWarning("[GoraniSkin] Resources에서 GLB를 못 찾음 — glTFast 임포트 확인 필요");
                enabled = false;
            }
        }

        void LateUpdate()
        {
            foreach (var view in FindObjectsByType<UnitView>(FindObjectsSortMode.None))
            {
                if (view.UnitId != PlayerUnitId) continue;
                if (view.transform.Find(SkinName) != null) continue;
                Apply(view);
            }
        }

        void Apply(UnitView view)
        {
            var skin = Instantiate(prefab, view.transform);
            skin.name = SkinName;
            skin.transform.localPosition = Vector3.zero;
            skin.transform.localRotation = Quaternion.identity;

            FitToUnit(skin, view.transform);

            // 큐브 몸통은 숨기고(색·디밍은 계속 큐브 렌더러가 받지만 안 보임) 스킨만 노출
            var cube = view.GetComponent<MeshRenderer>();
            if (cube != null) cube.enabled = false;
        }

        static void FitToUnit(GameObject skin, Transform unit)
        {
            var rends = skin.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;

            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            if (b.size.y > 0.001f)
                skin.transform.localScale *= TargetHeight / b.size.y;

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
