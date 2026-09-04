using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 트라우마 기반 카메라 셰이크 — 짧고 절도 있게 (다크 택티컬 톤, 바운스 없음).
    /// 추적 카메라(TacticalCamera 등)의 LateUpdate 이후에 오프셋을 얹고,
    /// 다음 프레임 Update(모든 LateUpdate 전)에서 걷어내 SmoothDamp 오염을 최소화한다.
    /// 사용: CameraShaker.Shake(0.3f) — 값은 트라우마 가산(0..1), 체감은 제곱 커브.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class CameraShaker : MonoBehaviour
    {
        public static CameraShaker Instance { get; private set; }

        [SerializeField] float decayPerSecond = 2.4f; // 트라우마 감쇠 — 클수록 뚝 끊김
        [SerializeField] float maxOffset = 0.35f;     // 최대 위치 흔들림 (월드 유닛)
        [SerializeField] float maxRollDeg = 1.1f;     // 최대 롤 (도) — 과하면 멀미
        [SerializeField] float frequency = 28f;       // 노이즈 주파수 — 높을수록 잘게 떨림

        float trauma;
        float seed;
        Vector3 lastOffset;
        float lastRoll;

        public static void Shake(float amount)
        {
            if (Instance != null)
                Instance.trauma = Mathf.Clamp01(Instance.trauma + amount);
        }

        void Awake()
        {
            Instance = this;
            seed = 12.345f;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            // 지난 프레임 셰이크 제거 — 추적 카메라가 깨끗한 상태에서 자기 위치를 계산하게
            transform.position -= lastOffset;
            if (lastRoll != 0f) transform.Rotate(0f, 0f, -lastRoll);
            lastOffset = Vector3.zero;
            lastRoll = 0f;
        }

        void LateUpdate()
        {
            if (trauma <= 0f) return;
            trauma = Mathf.Max(0f, trauma - decayPerSecond * Time.unscaledDeltaTime);

            float shake = trauma * trauma; // 제곱 — 작은 타격은 미세하게, 큰 타격만 크게
            float t = Time.unscaledTime * frequency;
            var local = new Vector3(
                Mathf.PerlinNoise(seed, t) * 2f - 1f,
                Mathf.PerlinNoise(seed + 17f, t) * 2f - 1f,
                0f) * (maxOffset * shake);

            lastOffset = transform.rotation * local; // 카메라 화면 평면 기준
            lastRoll = (Mathf.PerlinNoise(seed + 31f, t) * 2f - 1f) * maxRollDeg * shake;

            transform.position += lastOffset;
            transform.Rotate(0f, 0f, lastRoll);
        }
    }
}
