using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 거점 둘레 네온 테두리 — 멀리서도 소유가 읽히게 (2026-09-05).
    /// 색 언어: 중립 = 흰색 얇은 선, 점령 = 그 팀 색 글로우 띠.
    /// 3겹 구조(넓은 글로우 / 코어 / 밝은 심지) + 점령 시 맥동 —
    /// 한 겹 얇은 선은 바닥 틴트·하이라이트 사이에서 묻혔다 ("누가 먹었는지 안 보여").
    /// </summary>
    public class ZoneBorderRing : MonoBehaviour
    {
        LineRenderer core, glow, spark;
        Color baseColor = Color.white;
        bool owned;
        float pulseSeed;

        /// <summary>center = 거점 월드 중심, w/d = 패치 폭·깊이(월드). margin만큼 바깥에 두른다.</summary>
        public static ZoneBorderRing Create(Transform parent, Vector3 center, float w, float d)
        {
            var go = new GameObject("ZoneBorderRing");
            go.transform.SetParent(parent);
            // 부모는 회전 없음 — 자식 루프가 각자 90도 눕는다. 이중 회전이면 리본이 옆면으로 서서 잘려 보인다.
            var ring = go.AddComponent<ZoneBorderRing>();
            ring.pulseSeed = go.GetInstanceID() * 0.61f;

            const float margin = 0.06f;
            float hw = w / 2f + margin, hd = d / 2f + margin;

            // 3겹: 넓고 옅은 글로우(0.34) → 코어(0.12) → 밝은 심지(0.05)
            // 타일 윗면(≈0.05)과 겹치면 낮은 카메라 각도에서 Z-파이팅으로 깜빡인다 (2026-09-05) —
            // 깊이 정밀도가 갈라놓을 수 없는 간격이라, 면에서 확실히 띄우는 게 유일한 해법.
            ring.glow = MakeLoop(go.transform, center, hw, hd, 0.12f, 0.34f);
            ring.core = MakeLoop(go.transform, center, hw, hd, 0.13f, 0.12f);
            ring.spark = MakeLoop(go.transform, center, hw, hd, 0.14f, 0.05f);
            ring.SetOwnerColor(-1, null);
            return ring;
        }

        static LineRenderer MakeLoop(Transform parent, Vector3 center, float hw, float hd, float y, float width)
        {
            var go = new GameObject($"Loop{width}");
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
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
            l.startWidth = l.endWidth = width;
            l.alignment = LineAlignment.TransformZ; // 바닥에 눕힘
            l.material = new Material(Shader.Find("Sprites/Default")); // ZoneCaptureDisc와 동일 — URP 반투명
            l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            l.receiveShadows = false;
            return l;
        }

        /// <summary>owner &lt; 0 = 중립(흰색 얇은 선), 아니면 teamColors[owner] 글로우 띠.</summary>
        public void SetOwnerColor(int owner, Color[] teamColors)
        {
            if (core == null) return;
            owned = owner >= 0 && teamColors != null;
            baseColor = owned ? teamColors[owner] : Color.white;
            Apply(1f);
        }

        /// <summary>봉쇄 상태 — 어두운 회색, 맥동 없음. "지금은 못 먹는 곳" (2026-09-05).</summary>
        public void SetLocked()
        {
            if (core == null) return;
            owned = false; // 맥동 끔
            baseColor = new Color(0.3f, 0.32f, 0.36f);
            Apply(1f);
        }

        void Update()
        {
            // 점령 거점만 맥동 — "여기 누구 땅"이 살아 있는 신호로. 중립은 조용한 흰 선.
            if (owned) Apply(0.75f + 0.25f * Mathf.Sin(Time.time * 2.4f + pulseSeed));
        }

        void Apply(float k)
        {
            var c = Color.Lerp(baseColor, Color.white, 0.15f);
            var g = baseColor;
            g.a = (owned ? 0.4f : 0.12f) * k;      // 글로우 — 중립일 땐 거의 안 보이게
            c.a = (owned ? 1f : 0.75f) * k;
            var w = Color.Lerp(baseColor, Color.white, 0.65f);
            w.a = (owned ? 0.95f : 0.5f) * k;

            glow.startColor = glow.endColor = g;
            core.startColor = core.endColor = c;
            spark.startColor = spark.endColor = w;
        }
    }
}
