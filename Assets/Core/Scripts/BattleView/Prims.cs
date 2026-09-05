using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 빌드 안전 프리미티브 (2026-09-06 "빌드 뽑으니 바닥이 마젠타"): GameObject.CreatePrimitive의 기본 머티리얼은
    /// URP에서 파이프라인 에셋의 에디터 리소스라 플레이어 빌드에선 내장 Standard(미포함 셰이더)로 떨어져 마젠타가 된다.
    /// 생성 직후 URP Lit 머티리얼로 갈아 끼운다. 에디터에선 이미 Lit이라 그대로. 콜라이더 등 나머지는 CreatePrimitive와 동일.
    /// </summary>
    public static class Prims
    {
        const string LitName = "Universal Render Pipeline/Lit";
        static Material lit;

        /// <summary>런타임 공용 URP Lit 머티리얼 — 셰이더는 유닛 스킨(UnitSkins)이 이미 같은 이름으로 쓰고 있어 빌드에 포함된다.</summary>
        public static Material Lit()
        {
            if (lit == null)
            {
                var sh = Shader.Find(LitName);
                if (sh != null) lit = new Material(sh);
            }
            return lit;
        }

        public static GameObject Create(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            var r = go.GetComponent<Renderer>();
            var m = Lit();
            if (r != null && m != null)
            {
                var cur = r.sharedMaterial;
                if (cur == null || cur.shader == null || cur.shader.name != LitName) r.sharedMaterial = m;
            }
            return go;
        }
    }
}
