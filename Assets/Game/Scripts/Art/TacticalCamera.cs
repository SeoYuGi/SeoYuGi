using UnityEngine;
using UnityEngine.InputSystem;
using SeoYuGi.BattleView;

namespace SeoYuGi.Art
{
    /// <summary>
    /// 롤(LoL)식 쿼터뷰 카메라 — Main Camera에 붙인다.
    /// - 마우스 스크롤: 줌 인/아웃
    /// - Y: 플레이어 고정 추적 ↔ 자유 시점 토글
    /// - 자유 시점일 때 마우스를 화면 모서리로 밀면 그 방향으로 시야 이동(엣지 팬)
    /// QuarterViewCamera와 같은 앵글 규약(pitch/yaw/distance)을 쓰되 둘을 동시에 켜지 말 것.
    /// </summary>
    public class TacticalCamera : MonoBehaviour
    {
        [SerializeField] BattleRunner runner;   // 비우면 자동 탐색
        [Header("앵글")]
        [SerializeField] float pitch = 60f;
        [SerializeField] float yaw = -45f;
        [SerializeField] float smoothTime = 0.12f;
        [Header("줌")]
        [SerializeField] float distance = 10f;
        [SerializeField] float zoomStep = 1.2f;   // 스크롤 한 틱당 거리 변화
        [SerializeField] float minDistance = 5f;
        [SerializeField] float maxDistance = 18f;
        [Header("엣지 팬 (자유 시점)")]
        [SerializeField] float panSpeed = 14f;      // 초당 월드 유닛 (모서리 최심부 기준)
        [SerializeField] int edgePixels = 480;      // 모서리 감지 폭 — 후하게, 깊이 비례 가속이라 넓어도 안 널뜀
        [SerializeField] float outsideTolerance = 100f; // 화면 밖 이만큼까지는 모서리로 취급

        public bool Locked { get; private set; } = true;

        Transform target;
        Vector3 focus;      // 카메라가 바라보는 지점
        Vector3 velocity;

        void Start()
        {
            if (runner == null) runner = FindFirstObjectByType<BattleRunner>();
        }

        void LateUpdate()
        {
            ReadInput();

            if (Locked)
            {
                if (target == null && !TryFindTarget()) return;
                if (target != null) focus = target.position;
            }

            var rot = Quaternion.Euler(pitch, yaw, 0f);
            var desired = focus - rot * Vector3.forward * distance;
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime);
            transform.rotation = rot;
        }

        void ReadInput()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            if (kb != null && kb.yKey.wasPressedThisFrame)
            {
                Locked = !Locked;
                if (!Locked && target != null) focus = target.position; // 풀리는 순간 현재 위치에서 시작
            }

            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    // 윈도우 스크롤은 ±120 단위로 옴 — 방향만 취함
                    distance = Mathf.Clamp(distance - Mathf.Sign(scroll) * zoomStep, minDistance, maxDistance);
                }

                if (!Locked)
                    EdgePan(mouse.position.ReadValue());
            }
        }

        void EdgePan(Vector2 mousePos)
        {
            // 화면에서 크게 벗어났으면(에디터 포커스 이탈 등) 무시, 살짝 나간 건 모서리로 인정
            if (mousePos.x < -outsideTolerance || mousePos.y < -outsideTolerance ||
                mousePos.x > Screen.width + outsideTolerance || mousePos.y > Screen.height + outsideTolerance)
                return;
            mousePos.x = Mathf.Clamp(mousePos.x, 0f, Screen.width);
            mousePos.y = Mathf.Clamp(mousePos.y, 0f, Screen.height);

            Vector2 dir = Vector2.zero;
            float depth = 0f; // 모서리에 얼마나 깊이 들어갔나 (0~1)
            if (mousePos.x <= edgePixels) { dir.x = -1f; depth = Mathf.Max(depth, 1f - mousePos.x / edgePixels); }
            else if (mousePos.x >= Screen.width - edgePixels) { dir.x = 1f; depth = Mathf.Max(depth, 1f - (Screen.width - mousePos.x) / edgePixels); }
            if (mousePos.y <= edgePixels) { dir.y = -1f; depth = Mathf.Max(depth, 1f - mousePos.y / edgePixels); }
            else if (mousePos.y >= Screen.height - edgePixels) { dir.y = 1f; depth = Mathf.Max(depth, 1f - (Screen.height - mousePos.y) / edgePixels); }
            if (dir == Vector2.zero) return;

            // 카메라 요 기준의 지면 좌표계로 이동. 깊이 비례 가속 — 살짝 걸치면 느리게, 끝까지 밀면 최고속
            var yawRot = Quaternion.Euler(0f, yaw, 0f);
            var move = yawRot * new Vector3(dir.x, 0f, dir.y);
            float speed = panSpeed * Mathf.Lerp(0.35f, 1f, depth);
            focus += move.normalized * (speed * Time.deltaTime);
        }

        bool TryFindTarget()
        {
            if (runner == null || runner.Battle == null) return false;
            var registry = runner.GetComponent<UnitViewRegistry>();
            if (registry == null) return false;
            var view = registry.Get(runner.PlayerUnitId);
            if (view == null) return false;
            target = view.transform;
            focus = target.position;
            return true;
        }
    }
}
