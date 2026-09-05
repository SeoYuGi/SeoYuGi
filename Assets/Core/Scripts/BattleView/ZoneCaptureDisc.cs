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

        float shownFraction;   // 화면 표시 비율 — 깎일 땐 천천히 흘러내린다 (자르듯 뚝 떨어지지 않게)
        float penaltyFlash;    // 점령 저지 빨간 플래시 잔량 0..1

        /// <summary>점령 저지 연출 — 빨간 플래시 한 번. 게이지 하락은 SetProgress가 부드럽게 따라간다.</summary>
        public void FlashPenalty() => penaltyFlash = 1f;

        /// <summary>매 프레임 호출. fraction 0..1, color = 점거 팀 색.</summary>
        public void SetProgress(float fraction, Color color)
        {
            // 하락은 초당 0.6씩 흘러내리고, 상승은 즉시 — "깎였다"가 눈으로 읽힌다 (2026-09-05 점령 저지)
            shownFraction = fraction > shownFraction
                ? fraction
                : Mathf.MoveTowards(shownFraction, fraction, Time.deltaTime * 0.6f);
            fraction = shownFraction;

            if (penaltyFlash > 0f)
            {
                color = Color.Lerp(color, new Color(1f, 0.25f, 0.15f), penaltyFlash); // 저지 순간 빨갛게 번쩍
                penaltyFlash = Mathf.Max(0f, penaltyFlash - Time.deltaTime * 2.5f);
            }

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

            // 사각 파이 스윕 (2026-09-05): 12시(+Z)에서 시계 방향으로 fraction×360° 부채꼴을
            // 그리되, 반지름을 사각형 경계까지 늘려 패치 모양 그대로 채운다 (시계형 캡처 게이지).
            float halfW = width * 0.5f, halfD = depth * 0.5f;
            float sweep = fraction * 360f;
            const float StepDeg = 6f;
            int steps = Mathf.Max(1, Mathf.CeilToInt(sweep / StepDeg));

            var verts = new Vector3[steps + 2];
            var tris = new int[steps * 3];
            verts[0] = Vector3.zero; // 부채꼴 중심

            Vector3 OnRect(float deg)
            {
                float rad = deg * Mathf.Deg2Rad;
                float dx = Mathf.Sin(rad), dz = Mathf.Cos(rad); // 0° = +Z(12시), 시계 방향
                float k = 1f / Mathf.Max(Mathf.Abs(dx) / halfW, Mathf.Abs(dz) / halfD); // 사각 경계로 투영
                return new Vector3(dx * k, 0f, dz * k);
            }

            for (int i = 0; i <= steps; i++)
                verts[i + 1] = OnRect(Mathf.Min(sweep, i * StepDeg));
            for (int i = 0; i < steps; i++)
            {
                tris[i * 3] = 0;
                tris[i * 3 + 1] = i + 1; // 시계 방향 — 위(+Y)에서 보이는 감김
                tris[i * 3 + 2] = i + 2;
            }

            mesh.Clear();
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
        }
    }
}
