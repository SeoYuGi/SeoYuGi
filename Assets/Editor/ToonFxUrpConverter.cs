using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace SeoYuGi.EditorTools
{
    /// <summary>
    /// ToonFX(레거시 빌트인 파티클 셰이더) 머티리얼을 URP Particles/Unlit로 자동 변환.
    /// 레거시 파티클 셰이더는 URP에서 핑크(에러)로 뜬다 — 에디터 로드 시 1회 검사·변환.
    /// 원본 블렌드 모드는 .mat YAML의 빌트인 fileID로 판별 (에러 셰이더는 이름을 잃기 때문).
    /// </summary>
    public static class ToonFxUrpConverter
    {
        const string Root = "Assets/Game/Art/VFX/ToonFX";

        [InitializeOnLoadMethod]
        static void AutoRun()
        {
            EditorApplication.delayCall += () => Convert(silent: true);
        }

        [MenuItem("Tools/SeoYuGi/ToonFX 머티리얼 URP 변환")]
        static void ConvertMenu() => Convert(silent: false);

        static void Convert(bool silent)
        {
            if (!AssetDatabase.IsValidFolder(Root)) return;
            var urpShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (urpShader == null)
            {
                if (!silent) Debug.LogError("URP Particles/Unlit 셰이더를 찾지 못함 — 변환 중단");
                return;
            }

            int converted = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { Root }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;

                string shaderName = mat.shader.name;
                bool broken = shaderName == "Hidden/InternalErrorShader";
                bool legacy = shaderName.StartsWith("Particles/") || shaderName.StartsWith("Legacy Shaders/Particles/");
                if (!broken && !legacy) continue;

                int blendMode = GuessBlend(path, shaderName);

                // 스왑 전에 기존 속성 확보 — 스왑하면 프로퍼티 이름이 바뀐다
                Texture tex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                Color tint = Color.white;
                if (mat.HasProperty("_TintColor")) tint = mat.GetColor("_TintColor");
                else if (mat.HasProperty("_Color")) tint = mat.GetColor("_Color");

                mat.shader = urpShader;
                mat.SetTexture("_BaseMap", tex);
                mat.SetColor("_BaseColor", tint);
                mat.SetFloat("_Surface", 1f); // Transparent
                mat.SetFloat("_Blend", blendMode == 2 ? 2f : blendMode == 1 ? 1f : blendMode == 3 ? 3f : 0f);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_AlphaClip", 0f);
                mat.SetFloat("_ColorMode", 0f); // Multiply — 파티클 정점색 × 텍스처

                switch (blendMode)
                {
                    case 2: // Additive
                        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        mat.DisableKeyword("_ALPHAMODULATE_ON");
                        break;
                    case 1: // Premultiply (ToonFX 대부분 — Alpha Blended Premultiply)
                        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                        mat.DisableKeyword("_ALPHAMODULATE_ON");
                        break;
                    case 3: // Multiply
                        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.DstColor);
                        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                        mat.EnableKeyword("_ALPHAMODULATE_ON");
                        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        break;
                    default: // Alpha
                        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        mat.DisableKeyword("_ALPHAMODULATE_ON");
                        break;
                }
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                EditorUtility.SetDirty(mat);
                converted++;
            }

            if (converted > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"ToonFX URP 변환 — 머티리얼 {converted}개 변환 완료");
            }
            else if (!silent) Debug.Log("ToonFX URP 변환 — 변환할 레거시 머티리얼 없음");
        }

        /// <summary>블렌드 판별: 0=Alpha, 1=Premultiply, 2=Additive, 3=Multiply.
        /// 셰이더 이름이 살아 있으면 이름으로, 죽었으면(에러 셰이더) YAML의 빌트인 fileID로.</summary>
        static int GuessBlend(string matPath, string shaderName)
        {
            if (shaderName.Contains("Additive")) return 2;
            if (shaderName.Contains("Premultiply")) return 1;
            if (shaderName.Contains("Multiply")) return 3;
            if (shaderName.Contains("Alpha Blended")) return 0;

            // 에러 셰이더 — 직렬화된 원본 fileID 파싱 (빌트인 엑스트라 guid 0000...f000)
            try
            {
                var yaml = File.ReadAllText(matPath);
                var m = Regex.Match(yaml, @"m_Shader:\s*\{fileID:\s*(\d+),\s*guid:\s*0{16}f0{15}");
                if (m.Success)
                {
                    switch (int.Parse(m.Groups[1].Value))
                    {
                        case 200: case 201: case 202: return 2; // Particles/Additive 계열
                        case 203: case 207: return 0;           // Alpha Blended
                        case 204: case 205: return 3;           // Multiply
                        case 206: case 210: case 211: return 1; // Premultiply 계열
                    }
                }
            }
            catch { /* 파싱 실패 — 기본 Alpha */ }
            return 0;
        }
    }
}
