using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 가시성 인디케이터 — 발밑 팀 링, 내 유닛 ▼ 마커, 고스트(?) 마커.
    /// 색 규칙(G): 빨강=적 위험 / 주황=내 공격 / 파랑=아군·이동 / 노랑=자원 / 시안=시스템.
    /// 전부 코드 생성 — 프리팹·씬 배선 불필요.
    /// </summary>
    public static class UnitIndicators
    {
        static Material sharedMat;

        static Material Mat
        {
            get
            {
                if (sharedMat == null)
                    sharedMat = new Material(Shader.Find("Sprites/Default"));
                return sharedMat;
            }
        }

        /// <summary>발밑 팀 색 링. 내 유닛은 밝은 이중 링 + 펄스.</summary>
        public static void AddTeamRing(Transform unit, Color teamColor, bool isPlayer, float unitYOffset = 0.5f)
        {
            var ring = MakeRing(unit, teamColor, 0.3f, 0.4f, unitYOffset);
            if (isPlayer)
            {
                ring.AddComponent<RingPulse>(); // 내 링만 두근거림
                var outer = MakeRing(unit, Color.Lerp(teamColor, Color.white, 0.55f), 0.46f, 0.52f, unitYOffset);
                outer.AddComponent<RingPulse>();
            }
        }

        static GameObject MakeRing(Transform unit, Color color, float innerR, float outerR, float unitYOffset)
        {
            var go = new GameObject("TeamRing");
            go.transform.SetParent(unit, false);
            go.transform.localPosition = new Vector3(0f, -unitYOffset + 0.06f, 0f); // 유닛 피벗 → 바닥 살짝 위

            const int Segments = 28;
            var verts = new Vector3[Segments * 2 + 2];
            var tris = new int[Segments * 6];
            for (int i = 0; i <= Segments; i++)
            {
                float a = (float)i / Segments * Mathf.PI * 2f;
                float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                verts[i * 2] = new Vector3(cos * innerR, 0f, sin * innerR);
                verts[i * 2 + 1] = new Vector3(cos * outerR, 0f, sin * outerR);
            }
            for (int i = 0; i < Segments; i++)
            {
                int b = i * 2;
                tris[i * 6] = b; tris[i * 6 + 1] = b + 3; tris[i * 6 + 2] = b + 1;
                tris[i * 6 + 3] = b; tris[i * 6 + 4] = b + 2; tris[i * 6 + 5] = b + 3;
            }
            var mesh = new Mesh { vertices = verts, triangles = tris };
            mesh.RecalculateNormals();

            go.AddComponent<MeshFilter>().mesh = mesh;
            var rend = go.AddComponent<MeshRenderer>();
            rend.material = Mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_Color", color);
            rend.SetPropertyBlock(mpb);
            return go;
        }

        /// <summary>내 유닛 머리 위 ▼ — 난전 중 자기 위치 로스트 방지.</summary>
        public static void AddPlayerArrow(Transform unit, Color teamColor, float height = 1.7f)
        {
            var go = new GameObject("PlayerArrow");
            go.transform.SetParent(unit, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            var tm = go.AddComponent<TextMesh>();
            tm.text = "▼";
            tm.fontSize = 48;
            tm.characterSize = 0.09f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.Lerp(teamColor, Color.white, 0.4f);
            go.AddComponent<BillboardBob>();
        }

    }

    /// <summary>카메라 빌보드 + 살짝 둥실거림 — 마커류 공용.</summary>
    public class BillboardBob : MonoBehaviour
    {
        Camera cam;
        float baseY;
        float seed;

        void Start()
        {
            cam = Camera.main;
            baseY = transform.localPosition.y;
            seed = transform.position.x * 3.7f; // 개체마다 위상 어긋나게
        }

        void LateUpdate()
        {
            if (cam != null) transform.rotation = cam.transform.rotation;
            var p = transform.localPosition;
            p.y = baseY + Mathf.Sin(Time.time * 2.4f + seed) * 0.05f;
            transform.localPosition = p;
        }
    }

    /// <summary>링 스케일 펄스 — 내 유닛 식별.</summary>
    public class RingPulse : MonoBehaviour
    {
        Vector3 baseScale;

        void Start() => baseScale = transform.localScale;

        void Update() =>
            transform.localScale = baseScale * (1f + Mathf.Sin(Time.time * 3.2f) * 0.06f);
    }
}
