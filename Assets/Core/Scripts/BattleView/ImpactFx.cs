using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 임팩트 포스트 프로세싱 — 명중 시 비네트+색수차 펀치, 격파 시 명도 플래시.
    /// 런타임 글로벌 Volume 자동 생성 (씬 배선 불필요). unscaled 시계 — 히트스톱 중에도 재생.
    /// 카메라의 UniversalAdditionalCameraData.renderPostProcessing이 켜져 있어야 보인다.
    /// </summary>
    public class ImpactFx : MonoBehaviour
    {
        static ImpactFx instance;

        Vignette vig;
        ChromaticAberration ca;
        ColorAdjustments col;

        float punch;      // 0..1 — 감쇠
        float flash;      // 0..1 — 감쇠
        const float PunchDecay = 6.5f;
        const float FlashDecay = 9f;

        public static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("@ImpactFx");
            instance = go.AddComponent<ImpactFx>();

            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 100f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            vol.profile = profile;

            instance.vig = profile.Add<Vignette>();
            instance.vig.intensity.Override(0f);
            instance.vig.color.Override(new Color(0.35f, 0.05f, 0.02f)); // 핏빛 가장자리

            instance.ca = profile.Add<ChromaticAberration>();
            instance.ca.intensity.Override(0f);

            instance.col = profile.Add<ColorAdjustments>();
            instance.col.postExposure.Override(0f);
        }

        /// <summary>명중 펀치. strength 0..1.</summary>
        public static void Punch(float strength)
        {
            if (instance != null)
                instance.punch = Mathf.Clamp01(instance.punch + strength);
        }

        /// <summary>격파 순간 화면 명도 스파이크 — 임팩트 프레임.</summary>
        public static void DeathFlash()
        {
            if (instance != null)
                instance.flash = 1f;
        }

        void Update()
        {
            punch = Mathf.Max(0f, punch - PunchDecay * Time.unscaledDeltaTime);
            flash = Mathf.Max(0f, flash - FlashDecay * Time.unscaledDeltaTime);

            float p = punch * punch;
            vig.intensity.Override(p * 0.32f);
            ca.intensity.Override(p * 0.55f);
            col.postExposure.Override(flash * flash * 1.4f);
        }
    }
}
