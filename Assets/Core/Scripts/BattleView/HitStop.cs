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

        float until;
        bool active;

        public static void Do(float seconds)
        {
            if (instance == null)
                instance = new GameObject("@HitStop").AddComponent<HitStop>();
            instance.Apply(seconds);
        }

        void Apply(float seconds)
        {
            if (GameFreeze.Active) return; // 무전 프리즈 중 — 히트스톱이 정지를 깨면 안 된다
            float end = Time.unscaledTime + seconds;
            if (end > until) until = end;
            if (!active)
            {
                active = true;
                Time.timeScale = StopScale;
            }
        }

        void Update()
        {
            if (active && Time.unscaledTime >= until)
            {
                Time.timeScale = GameFreeze.Active ? 0f : 1f; // 프리즈 우선 복원
                active = false;
            }
        }

        void OnDestroy()
        {
            if (active) Time.timeScale = GameFreeze.Active ? 0f : 1f; // 씬 전환 등으로 죽어도 시간 복원 보장
            if (instance == this) instance = null;
        }
    }
}
