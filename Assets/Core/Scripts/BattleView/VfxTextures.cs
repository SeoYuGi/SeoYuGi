using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 생성 VFX 텍스처 로더 — Resources/VFX/*.png (검은 배경) + 가산 블렌딩.
    /// 가산이라 검은 배경이 자연 소거 — 알파 채널 불필요, 톤은 텍스처가 쥔다.
    /// 텍스처가 없으면 null — 호출부는 기존 코드 생성 연출로 폴백.
    /// 슬래시·발톱·조준경·바람은 런타임 절차 생성 — 외부 아트 불필요, 항상 존재.
    /// </summary>
    public static class VfxTextures
    {
        /// <summary>
        /// 가산 이펙트 전역 밝기 계수 (2026-09-05 매트화). 1 = 예전 화려함, 낮출수록 차분.
        /// 화면이 전부 빛나면 아무것도 도드라지지 않아 예고·화살표 같은 정보가 묻힌다.
        /// FxQuad가 모든 절차 VFX의 단일 통로라 여기 한 곳만 만지면 전부 따라온다.
        /// </summary>
        public const float Brightness = 0.42f; // 0.55도 아직 번쩍였다 (2026-09-05 "매트하게")

        /// <summary>가산 VFX에 쓸 색 — 전역 밝기를 먹인다. 알파는 보존(수명 페이드가 쓴다).</summary>
        public static Color Dim(Color c)
        {
            var d = c * Brightness;
            d.a = c.a;
            return d;
        }

        static Material glowMat, electricMat, sparkMat, ringMat;
        static Material slashMat, clawMat, crosshairMat, windMat;

        public static Material Glow => Get(ref glowMat, "VFX/Fx_Glow");         // 소프트 광구 — 기둥·플래시
        public static Material Electric => Get(ref electricMat, "VFX/Fx_Electric"); // 전기 아크 — 해킹·기계 피격
        public static Material Spark => Get(ref sparkMat, "VFX/Fx_Spark");      // 불똥 궤적 — 동물 피격·벽꿍
        public static Material Ring => Get(ref ringMat, "VFX/Fx_Ring");         // 홀로 링 — 충격파·레티클

        /// <summary>칼 베기 호(弧) — 너구리 기본공격·기계 광선검.</summary>
        public static Material Slash
        {
            get
            {
                if (slashMat == null) slashMat = MakeAdditive(GenSlash());
                return slashMat;
            }
        }

        /// <summary>발톱 3줄 긁힘 — 검은냥 기본공격·발톱찢기.</summary>
        public static Material Claw
        {
            get
            {
                if (clawMat == null) clawMat = MakeAdditive(GenClaw());
                return clawMat;
            }
        }

        /// <summary>스나이퍼 조준경 레티클 — 알파 블렌드(불투명 빨강용). 까치 전용.</summary>
        public static Material Crosshair
        {
            get
            {
                if (crosshairMat == null) crosshairMat = MakeAlpha(GenCrosshair());
                return crosshairMat;
            }
        }

        /// <summary>가로 바람 줄기 — 방패 밀침·비행 슝슝.</summary>
        public static Material Wind
        {
            get
            {
                if (windMat == null) windMat = MakeAdditive(GenWind());
                return windMat;
            }
        }

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
            var mat = MakeUnlit(tex);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            return mat;
        }

        /// <summary>URP Unlit 알파 블렌드 — 불투명 색이 필요한 마커(조준경)용.</summary>
        static Material MakeAlpha(Texture2D tex)
        {
            var mat = MakeUnlit(tex);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            return mat;
        }

        static Material MakeUnlit(Texture2D tex)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default"); // 최후 폴백
            var mat = new Material(shader);
            mat.SetTexture("_BaseMap", tex);
            mat.SetTexture("_MainTex", tex); // Sprites 폴백용
            mat.SetFloat("_Surface", 1f); // Transparent
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }

        // ── 절차 생성 (1회, 256²) — 검은 배경 + 흰 형상, 색은 머티리얼/MPB가 입힌다 ──

        const int TexSize = 256;

        static Texture2D NewTex()
        {
            var t = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        /// <summary>초승달 베기 호 — 아래가 볼록한 크레센트, 양끝이 뾰족.</summary>
        static Texture2D GenSlash()
        {
            var t = NewTex();
            var px = new Color[TexSize * TexSize];
            for (int y = 0; y < TexSize; y++)
            for (int x = 0; x < TexSize; x++)
            {
                float u = x / (float)TexSize, v = y / (float)TexSize;
                float dx = u - 0.5f, dy = v - (-0.15f); // 호 중심을 화면 아래 밖에 — 완만한 곡선
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg; // 0=오른쪽, 90=위
                float a = 0f;
                if (ang > 35f && ang < 145f)
                {
                    float taper = Mathf.Sin((ang - 35f) / 110f * Mathf.PI); // 양끝 뾰족
                    taper = Mathf.Pow(taper, 0.6f);
                    float band = Mathf.Exp(-Mathf.Pow((r - 0.72f) / 0.055f, 2f)); // 호 두께
                    a = taper * band;
                }
                px[y * TexSize + x] = new Color(1f, 1f, 1f, 1f) * a;
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>대각선 발톱 3줄 — 가운데 줄이 가장 길고 굵다.</summary>
        static Texture2D GenClaw()
        {
            var t = NewTex();
            var px = new Color[TexSize * TexSize];
            // 45° 회전 좌표계에서 세 줄 — (s = 진행 방향, q = 줄 간격 방향)
            float[] offsets = { -0.16f, 0f, 0.16f };
            float[] widths = { 0.020f, 0.028f, 0.020f };
            float[] lengths = { 0.72f, 0.9f, 0.72f };
            for (int y = 0; y < TexSize; y++)
            for (int x = 0; x < TexSize; x++)
            {
                float u = x / (float)TexSize - 0.5f, v = y / (float)TexSize - 0.5f;
                float s = (u + v) * 0.7071f;  // 대각선 진행
                float q = (v - u) * 0.7071f;  // 수직 오프셋
                float a = 0f;
                for (int i = 0; i < 3; i++)
                {
                    float half = lengths[i] * 0.5f;
                    if (Mathf.Abs(s) > half) continue;
                    float along = 1f - Mathf.Abs(s) / half;          // 끝으로 갈수록
                    float sharp = Mathf.Pow(along, 0.45f);            // 뾰족한 끝
                    float w = widths[i] * (0.35f + 0.65f * along);    // 끝은 가늘게
                    float line = Mathf.Exp(-Mathf.Pow((q - offsets[i]) / w, 2f));
                    a = Mathf.Max(a, line * sharp);
                }
                px[y * TexSize + x] = new Color(1f, 1f, 1f, 1f) * a;
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>저격 조준경 — 이중 링 + 십자선 + 중심점. 알파로 형상, 색은 MPB.</summary>
        static Texture2D GenCrosshair()
        {
            var t = NewTex();
            var px = new Color[TexSize * TexSize];
            for (int y = 0; y < TexSize; y++)
            for (int x = 0; x < TexSize; x++)
            {
                float u = x / (float)TexSize - 0.5f, v = y / (float)TexSize - 0.5f;
                float r = Mathf.Sqrt(u * u + v * v);
                float a = 0f;
                a = Mathf.Max(a, Mathf.Exp(-Mathf.Pow((r - 0.42f) / 0.018f, 2f)));  // 외곽 링
                a = Mathf.Max(a, Mathf.Exp(-Mathf.Pow((r - 0.26f) / 0.010f, 2f)));  // 내곽 링
                // 십자선 — 중심 갭(0.06)부터 링 밖(0.47)까지
                if (r > 0.06f && r < 0.47f)
                {
                    float cross = Mathf.Max(
                        Mathf.Exp(-Mathf.Pow(u / 0.011f, 2f)),
                        Mathf.Exp(-Mathf.Pow(v / 0.011f, 2f)));
                    a = Mathf.Max(a, cross);
                }
                a = Mathf.Max(a, Mathf.Exp(-Mathf.Pow(r / 0.022f, 2f))); // 중심점
                px[y * TexSize + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>가로 바람 줄기 4가닥 — 길이·위치 제각각, 끝이 뾰족.</summary>
        static Texture2D GenWind()
        {
            var t = NewTex();
            var px = new Color[TexSize * TexSize];
            float[] ys = { 0.28f, 0.45f, 0.58f, 0.74f };
            float[] starts = { 0.05f, 0.18f, 0.02f, 0.25f };
            float[] ends = { 0.75f, 0.95f, 0.6f, 0.88f };
            for (int y = 0; y < TexSize; y++)
            for (int x = 0; x < TexSize; x++)
            {
                float u = x / (float)TexSize, v = y / (float)TexSize;
                float a = 0f;
                for (int i = 0; i < 4; i++)
                {
                    if (u < starts[i] || u > ends[i]) continue;
                    float k = (u - starts[i]) / (ends[i] - starts[i]);
                    float head = Mathf.Pow(Mathf.Sin(k * Mathf.PI), 0.5f); // 양끝 페이드
                    float line = Mathf.Exp(-Mathf.Pow((v - ys[i]) / 0.014f, 2f));
                    a = Mathf.Max(a, line * head);
                }
                px[y * TexSize + x] = new Color(1f, 1f, 1f, 1f) * a;
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }
    }
}
