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
        }

        void Update()
        {
            elapsed += Time.deltaTime;
            if (elapsed >= duration) { Destroy(gameObject); return; }

            transform.position += Vector3.up * (riseSpeed * Time.deltaTime);
            if (Camera.main != null)
                transform.rotation = Camera.main.transform.rotation; // 빌보드

            float k = elapsed / duration;
            tm.color = new Color(baseColor.r, baseColor.g, baseColor.b, 1f - k * k); // 끝에서 급격히 페이드
        }
    }
}
