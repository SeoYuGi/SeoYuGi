using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>유닛 머리 위 1회성 전투 텍스트 — 떠오르며 페이드, 카메라 빌보드.</summary>
    public class FloatingText : MonoBehaviour
    {
        float duration;
        float elapsed;
        float riseSpeed = 0.9f;
        TextMesh tm;
        Color baseColor;

        public static void Spawn(Vector3 worldPos, string text, Color color, float scale = 1f, float duration = 0.8f)
        {
            var go = new GameObject("FloatingText");
            go.transform.position = worldPos + Vector3.up * 1.15f;
            var f = go.AddComponent<FloatingText>();
            f.duration = duration;
            f.baseColor = color;
            f.tm = go.AddComponent<TextMesh>();
            f.tm.text = text;
            f.tm.fontSize = 56;
            f.tm.characterSize = 0.05f * scale;
            f.tm.anchor = TextAnchor.MiddleCenter;
            f.tm.alignment = TextAlignment.Center;
            f.tm.color = color;
            GameFonts.Apply(f.tm, GameFonts.HudHeavy); // 데미지·스킬명 = SUIT Heavy
        }

        void Update()
        {
            elapsed += Time.deltaTime;
            if (elapsed >= duration) { Destroy(gameObject); return; }

            float k = elapsed / duration;
            // 팝: 처음 0.12초 1.6배→1배로 탁 박힘 / 상승은 점점 감속 — "찍히고 스르륵"
            float pop = Mathf.SmoothStep(1.6f, 1f, Mathf.Clamp01(elapsed / 0.12f));
            // 카메라 거리 보정 (2026-09-05 "데미지 숫자 안 보임") — 줌아웃해도 화면상 크기 일정
            float distScale = 1f;
            if (Camera.main != null)
                distScale = Mathf.Clamp(Vector3.Distance(Camera.main.transform.position, transform.position) / 13f, 0.8f, 2.6f);
            transform.localScale = Vector3.one * (pop * distScale);
            transform.position += Vector3.up * (riseSpeed * (1f - 0.65f * k) * Time.deltaTime);
            if (Camera.main != null)
                transform.rotation = Camera.main.transform.rotation; // 빌보드

            tm.color = new Color(baseColor.r, baseColor.g, baseColor.b, 1f - k * k); // 끝에서 급격히 페이드
        }
    }
}
