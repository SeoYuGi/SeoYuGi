using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 히트스톱 — 타격 순간 Time.timeScale을 잠깐 죽여 무게를 준다 (다크 택티컬 타격감의 핵심).
    /// 사용: HitStop.Do(0.06f). 겹치면 더 긴 쪽까지 연장. unscaled 시계로 복원.
    /// </summary>
    public class HitStop : MonoBehaviour
    {
        const float StopScale = 0.05f;

        static HitStop instance;

        const float MaxContinuous = 0.25f; // 연쇄 상한 — 난전에서 히트스톱이 꼬리물면 게임이 멈춘 듯 보인다 (2026-09-05 프리즈 가드)

        float until;
        float activeSince;
        bool active;

        public static void Do(float seconds)
        {
            if (instance == null)
                instance = new GameObject("@HitStop").AddComponent<HitStop>();
            instance.Apply(seconds);
        }

        void Apply(float seconds)
        {
            float end = Time.unscaledTime + seconds;
            if (!active)
            {
                active = true;
                activeSince = Time.unscaledTime;
                Time.timeScale = StopScale;
            }
            // 연쇄 연장은 시작 시점 + 상한까지만 — 무한 정지 방지
            if (end > until) until = Mathf.Min(end, activeSince + MaxContinuous);
        }

        void Update()
        {
            if (active && Time.unscaledTime >= until)
            {
                Time.timeScale = 1f;
                active = false;
            }
        }

        void OnDestroy()
        {
            if (active) Time.timeScale = 1f; // 씬 전환 등으로 죽어도 시간 복원 보장
            if (instance == this) instance = null;
        }
    }
}
