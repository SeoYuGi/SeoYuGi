using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>플레이어 유닛 쿼터뷰 추적 카메라 — Main Camera에 붙인다.</summary>
    public class QuarterViewCamera : MonoBehaviour
    {
        [SerializeField] BattleRunner runner;  // 비우면 자동 탐색
        [SerializeField] float pitch = 60f;    // 내려다보는 각도 (클수록 탑뷰)
        [SerializeField] float yaw = -45f;     // 왼쪽 45도 다이아몬드 뷰. 0 = 격자 정렬
        [SerializeField] float distance = 10f; // 유닛과의 거리
        [SerializeField] float smoothTime = 0.2f;

        Transform target;
        Vector3 velocity;

        void Start()
        {
            if (runner == null) runner = FindFirstObjectByType<BattleRunner>();
        }

        void LateUpdate()
        {
            if (target == null && !TryFindTarget()) return;

            var rot = Quaternion.Euler(pitch, yaw, 0f);
            var desired = target.position - rot * Vector3.forward * distance;
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime);
            transform.rotation = rot;
        }

        bool TryFindTarget()
        {
            if (runner == null || runner.Battle == null) return false;
            var registry = runner.GetComponent<UnitViewRegistry>();
            if (registry == null) return false;
            var view = registry.Get(runner.PlayerUnitId);
            if (view == null) return false;
            target = view.transform;
            return true;
        }
    }
}
