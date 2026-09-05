using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>유닛 머리 위 1회성 전투 텍스트 — 떠오르며 페이드, 카메라 빌보드.
    /// 민짜 색 글자는 IO풍이라 폐기 (2026-09-05): 어두운 외곽선 4방 + 본문, 작고 또렷하게.</summary>
    public class FloatingText : MonoBehaviour
    {
        float duration;
        float elapsed;
        float riseSpeed = 0.85f;
        float driftX;
        TextMesh tm;
        TextMesh[] outline;
        Color baseColor;

        public static void Spawn(Vector3 worldPos, string text, Color color, float scale = 1f, float duration = 0.8f)
        {
            var go = new GameObject("FloatingText");
            go.transform.position = worldPos + Vector3.up * 1.15f;
            var f = go.AddComponent<FloatingText>();
            f.duration = duration;
            f.baseColor = color;
            f.driftX = Random.Range(-0.22f, 0.22f); // 살짝 흩어져 연타가 겹쳐 안 뭉개짐

            // 외곽선 — 어두운 사본 4방. TextMesh엔 아웃라인이 없어 수동으로 두른다.
            f.outline = new TextMesh[4];
            var offs = new[] { new Vector3(0.022f, 0.022f, 0.001f), new Vector3(-0.022f, 0.022f, 0.001f),
                               new Vector3(0.022f, -0.022f, 0.001f), new Vector3(-0.022f, -0.022f, 0.001f) };
            for (int i = 0; i < 4; i++)
                f.outline[i] = MakeLabel(go.transform, text, scale, offs[i] * scale, new Color(0.02f, 0.03f, 0.05f, 0.9f));
            f.tm = MakeLabel(go.transform, text, scale, Vector3.zero, color);
        }

        static TextMesh MakeLabel(Transform parent, string text, float scale, Vector3 offset, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 60; // 해상도 높게 뽑고 characterSize로 줄인다 — 계단 방지
            tm.characterSize = 0.042f * scale;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            GameFonts.Apply(tm, GameFonts.HudHeavy); // 데미지·스킬명 = SUIT Heavy
            return tm;
        }

        void Update()
        {
            elapsed += Time.deltaTime;
            if (elapsed >= duration) { Destroy(gameObject); return; }

            float k = elapsed / duration;
            // 팝: 처음 0.1초 1.35배→1배로 탁 박힘 / 상승은 점점 감속 — "찍히고 스르륵"
            float pop = Mathf.SmoothStep(1.35f, 1f, Mathf.Clamp01(elapsed / 0.1f));
            // 카메라 거리 보정 (2026-09-05 "데미지 숫자 안 보임") — 줌아웃해도 화면상 크기 일정
            float distScale = 1f;
            if (Camera.main != null)
                distScale = Mathf.Clamp(Vector3.Distance(Camera.main.transform.position, transform.position) / 13f, 0.8f, 2.6f);
            transform.localScale = Vector3.one * (pop * distScale);
            transform.position += new Vector3(driftX * 0.3f, riseSpeed * (1f - 0.65f * k), 0f) * Time.deltaTime;
            if (Camera.main != null)
                transform.rotation = Camera.main.transform.rotation; // 빌보드

            float a = 1f - k * k; // 끝에서 급격히 페이드
            tm.color = new Color(baseColor.r, baseColor.g, baseColor.b, a);
            foreach (var o in outline)
                o.color = new Color(0.02f, 0.03f, 0.05f, 0.9f * a);
        }
    }
}
