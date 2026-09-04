using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 바닥에 그리는 링 1개. 두 가지 용도를 한 메커니즘으로 처리한다.
    ///  - Spawn:   밖으로 퍼지며 옅어짐 — 해킹·거점 탈환·벽꿍의 충격파
    ///  - Reticle: 안으로 조여들며 회전 — 예고 경고, 예측 사격 표식 (판정 시각에 맞춰 닫힘)
    /// 코드 생성 (프리팹·씬 배선 불필요). unscaled 시계 — 히트스톱 중에도 재생.
    /// 메시·머티리얼은 정적 캐시 1개를 전 인스턴스가 공유한다.
    /// </summary>
    public class RingWave : MonoBehaviour
    {
        const int Segments = 48;
        const float InnerRatio = 0.78f; // 링 두께 — 바깥 반지름 1 기준

        static Mesh ringMesh;
        static Material ringMat;

        Color color;
        float fromRadius, toRadius;
        float duration;
        float spinDegrees;
        bool easeOut;      // true = 초반 가속(충격파), false = 등속(레티클)
        bool fadeIn;       // 레티클은 끝에서 진해져야 판정 순간이 읽힌다
        float elapsed;
        Renderer rend;
        MaterialPropertyBlock mpb;

        /// <summary>worldPos 중심에서 maxRadius까지 퍼지며 옅어지는 충격파.</summary>
        public static void Spawn(Vector3 worldPos, Color color, float maxRadius = 3f, float duration = 0.5f)
            => Create(worldPos, color, 0.2f, maxRadius, duration, 0f, easeOut: true, fadeIn: false);

        /// <summary>
        /// 조준 레티클 — fromRadius에서 toRadius로 조여들며 회전.
        /// duration을 판정까지 남은 시간으로 주면 "닫히는 순간 = 맞는 순간"이 된다.
        /// </summary>
        public static void Reticle(Vector3 worldPos, Color color, float fromRadius, float toRadius,
            float duration, float spinDegrees = 200f)
            => Create(worldPos, color, fromRadius, toRadius, duration, spinDegrees, easeOut: false, fadeIn: true);

        static void Create(Vector3 worldPos, Color color, float fromRadius, float toRadius,
            float duration, float spinDegrees, bool easeOut, bool fadeIn)
        {
            if (duration <= 0f) return;

            var go = new GameObject("RingWave");
            go.transform.position = worldPos + Vector3.up * 0.08f;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // XY로 만든 링을 바닥에 눕힘

            // 생성 홀로 링 텍스처가 있으면 그쪽 — 없으면 코드 생성 고리 메시 폴백
            var ringTex = VfxTextures.Ring;
            if (ringTex != null)
            {
                var quad = go.AddComponent<MeshFilter>();
                quad.sharedMesh = QuadMesh;
                var qr = go.AddComponent<MeshRenderer>();
                qr.sharedMaterial = ringTex;
                qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                qr.receiveShadows = false;
            }
            else
            {
                go.AddComponent<MeshFilter>().sharedMesh = SharedRing;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            var w = go.AddComponent<RingWave>();
            w.color = color;
            w.fromRadius = fromRadius;
            w.toRadius = toRadius;
            w.duration = duration;
            w.spinDegrees = spinDegrees;
            w.easeOut = easeOut;
            w.fadeIn = fadeIn;
        }

        void Awake()
        {
            rend = GetComponent<Renderer>();
            mpb = new MaterialPropertyBlock();
            transform.localScale = Vector3.zero; // 첫 프레임 깜빡임 방지
        }

        void LateUpdate()
        {
            elapsed += Time.unscaledDeltaTime;
            float k = elapsed / duration;
            if (k >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            float t = easeOut ? 1f - (1f - k) * (1f - k) : k; // 충격파는 초반 가속, 레티클은 등속
            float radius = Mathf.Lerp(fromRadius, toRadius, t);
            transform.localScale = new Vector3(radius, radius, 1f);

            if (spinDegrees != 0f)
                transform.rotation = Quaternion.Euler(90f, 0f, k * spinDegrees);

            // 가산 머티리얼은 색 자체로 페이드 (알파 무시), 폴백 메시는 알파로
            float fade = fadeIn ? Mathf.Lerp(0.35f, 1f, k) : 1f - k;
            var c = color * fade;
            c.a = color.a * fade;
            mpb.SetColor("_Color", c);
            mpb.SetColor("_BaseColor", c);
            rend.SetPropertyBlock(mpb);
        }

        // 텍스처 링용 단위 쿼드 (2×2 — 반지름 1 링 텍스처가 꽉 차게)
        static Mesh quadMesh;
        static Mesh QuadMesh
        {
            get
            {
                if (quadMesh != null) return quadMesh;
                quadMesh = new Mesh { name = "RingQuad" };
                quadMesh.vertices = new[]
                {
                    new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
                    new Vector3(-1f, 1f, 0f), new Vector3(1f, 1f, 0f)
                };
                quadMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
                quadMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                quadMesh.RecalculateBounds();
                return quadMesh;
            }
        }

        static Material Mat
        {
            get
            {
                if (ringMat == null) ringMat = new Material(Shader.Find("Sprites/Default"));
                return ringMat;
            }
        }

        /// <summary>바깥 반지름 1의 평평한 고리(annulus). XY 평면에 생성.</summary>
        static Mesh SharedRing
        {
            get
            {
                if (ringMesh != null) return ringMesh;

                var verts = new Vector3[Segments * 2];
                var tris = new int[Segments * 6];
                for (int i = 0; i < Segments; i++)
                {
                    float a = i / (float)Segments * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    verts[i * 2] = dir * InnerRatio;
                    verts[i * 2 + 1] = dir;

                    int next = (i + 1) % Segments;
                    int o = i * 6;
                    tris[o] = i * 2;
                    tris[o + 1] = i * 2 + 1;
                    tris[o + 2] = next * 2 + 1;
                    tris[o + 3] = i * 2;
                    tris[o + 4] = next * 2 + 1;
                    tris[o + 5] = next * 2;
                }

                ringMesh = new Mesh { name = "RingWaveMesh" };
                ringMesh.vertices = verts;
                ringMesh.triangles = tris;
                ringMesh.RecalculateBounds();
                return ringMesh;
            }
        }
    }
}
