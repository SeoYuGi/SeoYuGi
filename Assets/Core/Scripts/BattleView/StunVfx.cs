using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 스턴 가시화 — 머리 위를 도는 노란 별(고전 만화 문법). 스턴이 풀리면 소멸.
    /// 5각 별 메시를 코드로 생성 — 글리프(★) 폰트 의존도, 텍스처 에셋도 불필요.
    /// 메시·머티리얼은 정적 캐시 1개를 전 인스턴스가 공유한다 (RingWave 방식).
    /// SyncPresentation이 매 프레임 Ensure로 수명을 연장한다.
    /// </summary>
    public class StunVfx : MonoBehaviour
    {
        const int Stars = 3;
        const float Radius = 0.38f;
        const float Height = 1.5f;
        const float SpinSpeed = 300f; // 궤도 회전 도/초
        public static readonly Color Gold = new Color(1f, 0.85f, 0.3f);

        static Mesh starMesh;
        static Material starMat;

        float until; // Time.time 기준 종료 시각
        Transform[] stars;
        Camera cam;
        float angle;

        /// <summary>unit 머리 위 별 궤도 생성 — 이미 돌고 있으면 종료 시각만 연장.</summary>
        public static void Ensure(Transform unit, float seconds)
        {
            if (seconds <= 0.05f) return;
            var v = unit.GetComponentInChildren<StunVfx>();
            if (v != null) { v.until = Mathf.Max(v.until, Time.time + seconds); return; }

            var go = new GameObject("StunVfx");
            go.transform.SetParent(unit, false);
            go.transform.localPosition = new Vector3(0f, Height, 0f);
            v = go.AddComponent<StunVfx>();
            v.until = Time.time + seconds;
        }

        /// <summary>5각 별 (외경 0.5 · 내경 0.2, XY 평면) — 부채꼴 12팬. 꼭짓점 하나가 위를 향한다.</summary>
        static Mesh StarMesh
        {
            get
            {
                if (starMesh != null) return starMesh;
                const int Points = 5;
                const float OuterR = 0.5f, InnerR = 0.2f;
                var verts = new Vector3[Points * 2 + 1];
                verts[0] = Vector3.zero;
                for (int i = 0; i < Points * 2; i++)
                {
                    float ang = (90f + i * 36f) * Mathf.Deg2Rad;
                    float r = (i % 2 == 0) ? OuterR : InnerR;
                    verts[i + 1] = new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, 0f);
                }
                var tris = new int[Points * 2 * 3];
                for (int i = 0; i < Points * 2; i++)
                {
                    tris[i * 3] = 0;
                    tris[i * 3 + 1] = 1 + (i + 1) % (Points * 2);
                    tris[i * 3 + 2] = 1 + i;
                }
                starMesh = new Mesh { name = "StunStar", vertices = verts, triangles = tris };
                return starMesh;
            }
        }

        static Material StarMat
        {
            get
            {
                if (starMat == null) // 언릿·양면 — 조명 무관하게 쨍한 노랑
                    starMat = new Material(Shader.Find("Sprites/Default")) { color = Gold };
                return starMat;
            }
        }

        void Start()
        {
            cam = Camera.main;
            stars = new Transform[Stars];
            for (int i = 0; i < Stars; i++)
            {
                var s = new GameObject("Star", typeof(MeshFilter), typeof(MeshRenderer));
                s.transform.SetParent(transform, false);
                s.transform.localScale = Vector3.one * 0.26f;
                s.GetComponent<MeshFilter>().sharedMesh = StarMesh;
                var r = s.GetComponent<MeshRenderer>();
                r.sharedMaterial = StarMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                stars[i] = s.transform;
            }
        }

        void Update()
        {
            if (Time.time >= until) { Destroy(gameObject); return; }
            angle += SpinSpeed * Time.deltaTime;
            for (int i = 0; i < Stars; i++)
            {
                float a = (angle + i * 360f / Stars) * Mathf.Deg2Rad;
                // 궤도 + 살짝 위아래 물결 — 평면 회전만 있으면 밋밋하다
                stars[i].localPosition = new Vector3(Mathf.Cos(a) * Radius, Mathf.Sin(a * 2f) * 0.07f, Mathf.Sin(a) * Radius);
                if (cam != null) // 빌보드 + 개별 자전(반대 방향 절반 속도) — 반짝이는 맛
                    stars[i].rotation = cam.transform.rotation * Quaternion.Euler(0f, 0f, -angle * 0.5f + i * 40f);
            }
        }
    }
}
