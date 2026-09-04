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
        LensDistortion lens;

        float punch;      // 0..1 — 감쇠
        float flash;      // 0..1 — 감쇠
        float glitchUntil = -1f; // unscaledTime 기준 — 해킹 지속 동안 화면 교란
        bool suddenDeath;        // 켜져 있는 동안 적색 비네트 맥동
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

            instance.lens = profile.Add<LensDistortion>();
            instance.lens.intensity.Override(0f);
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

        /// <summary>해킹 발동 — seconds 동안 화면이 지직거린다 (예측 마비의 시각 언어).</summary>
        public static void SetGlitch(float seconds)
        {
            if (instance != null)
                instance.glitchUntil = Time.unscaledTime + seconds;
        }

        /// <summary>서든데스 — 켜진 동안 화면 가장자리가 붉게 맥동. 라운드 조립 때 꺼준다.</summary>
        public static void SetSuddenDeath(bool on)
        {
            if (instance != null)
            {
                instance.suddenDeath = on;
                if (!on) instance.glitchUntil = -1f; // 라운드 리셋 — 잔여 글리치 제거
            }
        }

        void Update()
        {
            punch = Mathf.Max(0f, punch - PunchDecay * Time.unscaledDeltaTime);
            flash = Mathf.Max(0f, flash - FlashDecay * Time.unscaledDeltaTime);

            float p = punch * punch;
            float vigOut = p * 0.32f;
            float caOut = p * 0.55f;
            float lensOut = 0f;
            float t = Time.unscaledTime;

            // 해킹 지속 — 두 주기를 곱해 불규칙하게 튀는 노이즈를 만든다
            if (t < glitchUntil)
            {
                float wobble = Mathf.Abs(Mathf.Sin(t * 17f)) * Mathf.Abs(Mathf.Sin(t * 3.1f));
                caOut = Mathf.Max(caOut, 0.3f + wobble * 0.7f);
                lensOut = Mathf.Sin(t * 11f) * 0.2f;
            }

            // 서든데스 — 느린 맥동. 피격 펀치가 더 강하면 그쪽이 이긴다.
            if (suddenDeath)
                vigOut = Mathf.Max(vigOut, 0.16f + Mathf.Abs(Mathf.Sin(t * 2.2f)) * 0.12f);

            vig.intensity.Override(vigOut);
            ca.intensity.Override(caOut);
            lens.intensity.Override(lensOut);
            col.postExposure.Override(flash * flash * 1.4f);
        }
    }
}
