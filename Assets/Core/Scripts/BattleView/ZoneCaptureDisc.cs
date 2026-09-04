using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 거점 점거 진행 원형 게이지 — 패치 중앙 바닥에서 파이가 차오른다 (탱고파이브식).
    /// 절차 생성 팬 메시. 진행 0이면 숨김. 색은 점거 중인 팀 색.
    /// </summary>
    public class ZoneCaptureDisc : MonoBehaviour
    {
        const int Segments = 40;
        const float Radius = 1.35f;   // 3×3 패치 안에 들어오는 크기
        const float Alpha = 0.55f;

        Mesh mesh;
        MeshRenderer rend;
        Material mat;
        int lastSegments = -1;

        public static ZoneCaptureDisc Create(Transform parent, Vector3 center)
        {
            var go = new GameObject("ZoneCaptureDisc");
            go.transform.SetParent(parent);
            go.transform.position = center + Vector3.up * 0.08f; // 타일 위, 거점 글자 위
            var disc = go.AddComponent<ZoneCaptureDisc>();
            disc.Build();
            return disc;
        }

        void Build()
        {
            mesh = new Mesh();
            gameObject.AddComponent<MeshFilter>().mesh = mesh;
            rend = gameObject.AddComponent<MeshRenderer>();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;

            // Sprites/Default: URP에서도 동작하는 반투명 버텍스컬러 셰이더
            var shader = Shader.Find("Sprites/Default");
            mat = new Material(shader);
            rend.material = mat;
            rend.enabled = false;
        }

        /// <summary>매 프레임 호출. fraction 0..1, color = 점거 팀 색.</summary>
        public void SetProgress(float fraction, Color color)
        {
            if (fraction <= 0f)
            {
                if (rend.enabled) rend.enabled = false;
                return;
            }
            rend.enabled = true;
            color.a = Alpha;
            mat.color = color;

            int segs = Mathf.Clamp(Mathf.CeilToInt(fraction * Segments), 1, Segments);
            if (segs == lastSegments) return;
            lastSegments = segs;

            // 12시 방향 시작, 시계 방향 팬 (XZ 평면, 법선 +Y)
            var verts = new Vector3[segs + 2];
            var tris = new int[segs * 3];
            verts[0] = Vector3.zero;
            for (int i = 0; i <= segs; i++)
            {
                float ang = (float)i / Segments * Mathf.PI * 2f;
                verts[i + 1] = new Vector3(Mathf.Sin(ang) * Radius, 0f, Mathf.Cos(ang) * Radius);
            }
            for (int i = 0; i < segs; i++)
            {
                tris[i * 3] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = i + 2;
            }
            mesh.Clear();
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
        }
    }
}
