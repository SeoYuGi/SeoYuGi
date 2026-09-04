using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 생성 VFX 텍스처 로더 — Resources/VFX/*.png (검은 배경) + 가산 블렌딩.
    /// 가산이라 검은 배경이 자연 소거 — 알파 채널 불필요, 톤은 텍스처가 쥔다.
    /// 텍스처가 없으면 null — 호출부는 기존 코드 생성 연출로 폴백.
    /// </summary>
    public static class VfxTextures
    {
        static Material glowMat, electricMat, sparkMat, ringMat;
        static bool loaded;

        public static Material Glow => Get(ref glowMat, "VFX/Fx_Glow");         // 소프트 광구 — 기둥·플래시
        public static Material Electric => Get(ref electricMat, "VFX/Fx_Electric"); // 전기 아크 — 해킹·기계 피격
        public static Material Spark => Get(ref sparkMat, "VFX/Fx_Spark");      // 불똥 궤적 — 동물 피격·벽꿍
        public static Material Ring => Get(ref ringMat, "VFX/Fx_Ring");         // 홀로 링 — 충격파·레티클

        static Material Get(ref Material cache, string path)
        {
            if (cache != null) return cache;
            var tex = Resources.Load<Texture2D>(path);
            if (tex == null) return null;
            cache = MakeAdditive(tex);
            return cache;
        }

        /// <summary>URP Unlit 가산 머티리얼 — SrcBlend One / DstBlend One, ZWrite Off.</summary>
        static Material MakeAdditive(Texture2D tex)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default"); // 최후 폴백
            var mat = new Material(shader);
            mat.SetTexture("_BaseMap", tex);
            mat.SetTexture("_MainTex", tex); // Sprites 폴백용
            mat.SetFloat("_Surface", 1f); // Transparent
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }
    }
}
