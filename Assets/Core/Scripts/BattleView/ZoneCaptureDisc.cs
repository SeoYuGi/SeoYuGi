using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 거점 점거 진행 게이지 — 거점 패치 모양(사각형)대로 바닥에서 차오른다.
    /// -Z(아래) 모서리에서 시작해 fraction만큼 +Z로 채워지는 직사각형. 진행 0이면 숨김.
    /// 절차 생성 쿼드 메시. 색은 점거 중인 팀 색.
    /// </summary>
    public class ZoneCaptureDisc : MonoBehaviour
    {
        const float Alpha = 0.55f;
        const float Inset = 0.06f; // 타일 경계에서 살짝 안쪽 — 그리드 라인 안 가림

        Mesh mesh;
        MeshRenderer rend;
        Material mat;
        float width, depth;   // 거점 footprint 월드 크기 (셀 범위 × tileSize)
        float lastFraction = -1f;

        /// <summary>center = 패치 중앙 월드 좌표, width/depth = 패치의 월드 크기(X/Z).</summary>
        public static ZoneCaptureDisc Create(Transform parent, Vector3 center, float width, float depth)
        {
            var go = new GameObject("ZoneCaptureDisc");
            go.transform.SetParent(parent);
            go.transform.position = center + Vector3.up * 0.08f; // 타일 위, 거점 글자 위
            var disc = go.AddComponent<ZoneCaptureDisc>();
            disc.width = Mathf.Max(0.1f, width - Inset * 2f);
            disc.depth = Mathf.Max(0.1f, depth - Inset * 2f);
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

            // Sprites/Default: URP에서도 동작하는 반투명 버텍스컬러 셰이더 (Cull Off)
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

            fraction = Mathf.Clamp01(fraction);
            if (Mathf.Approximately(fraction, lastFraction)) return;
            lastFraction = fraction;

            // -Z 모서리 고정, +Z로 fraction만큼 채운다 (XZ 평면, 법선 +Y)
            float halfW = width * 0.5f;
            float zBot = -depth * 0.5f;
            float zTop = zBot + depth * fraction;
            var verts = new[]
            {
                new Vector3(-halfW, 0f, zBot),
                new Vector3( halfW, 0f, zBot),
                new Vector3( halfW, 0f, zTop),
                new Vector3(-halfW, 0f, zTop),
            };
            var tris = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.Clear();
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
        }
    }
}
