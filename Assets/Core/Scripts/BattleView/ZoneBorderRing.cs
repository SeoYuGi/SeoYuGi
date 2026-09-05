using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 거점 둘레 테두리 띠 — 멀리서도 소유가 읽히게 (2026-09-05).
    /// 색 언어: 중립 = 흰색, 점령 = 그 팀 색 (바닥 틴트와 같은 문법이라 학습 비용 0).
    /// LineRenderer 루프 하나 — 소유가 바뀌면 SetOwnerColor로 갈아입는다.
    /// </summary>
    public class ZoneBorderRing : MonoBehaviour
    {
        LineRenderer line;

        /// <summary>center = 거점 월드 중심, w/d = 패치 폭·깊이(월드). margin만큼 바깥에 두른다.</summary>
        public static ZoneBorderRing Create(Transform parent, Vector3 center, float w, float d)
        {
            var go = new GameObject("ZoneBorderRing");
            go.transform.SetParent(parent);
            var ring = go.AddComponent<ZoneBorderRing>();

            const float margin = 0.06f, y = 0.03f; // 타일 위 살짝 — Z파이팅 방지
            float hw = w / 2f + margin, hd = d / 2f + margin;

            var l = go.AddComponent<LineRenderer>();
            l.useWorldSpace = true;
            l.loop = true;
            l.positionCount = 4;
            l.SetPositions(new[]
            {
                center + new Vector3(-hw, y, -hd),
                center + new Vector3(hw, y, -hd),
                center + new Vector3(hw, y, hd),
                center + new Vector3(-hw, y, hd)
            });
            l.startWidth = l.endWidth = 0.11f;
            l.alignment = LineAlignment.TransformZ;         // 바닥에 눕힘
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            l.material = new Material(Shader.Find("Sprites/Default")); // ZoneCaptureDisc와 동일 — URP 반투명
            l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            l.receiveShadows = false;
            ring.line = l;
            ring.SetOwnerColor(-1, null);
            return ring;
        }

        /// <summary>owner &lt; 0 = 중립(흰색), 아니면 teamColors[owner].</summary>
        public void SetOwnerColor(int owner, Color[] teamColors)
        {
            if (line == null) return;
            var c = owner >= 0 && teamColors != null ? teamColors[owner] : Color.white;
            c = Color.Lerp(c, Color.white, 0.15f);
            c.a = 0.9f;
            line.startColor = line.endColor = c;
        }
    }
}
