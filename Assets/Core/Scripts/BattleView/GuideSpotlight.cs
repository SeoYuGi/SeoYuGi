using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 튜토리얼 스포트라이트 (2026-09-05) — 화면을 어둡게 깔고 지금 봐야 할 곳만 구멍을 낸다.
    /// IMGUI는 구멍을 못 뚫으니 구멍 주위 네 장(위·아래·왼·오른)으로 어둠을 그린다. GUI.depth를 크게 둬서
    /// HUD·무전창 등 다른 OnGUI보다 뒤에(아래에) 깔린다 — 구멍 안 UI는 그대로 밝고, 밖은 어둡다.
    /// 좌표는 화면 픽셀(GUI 기준, y는 위에서 아래).
    /// </summary>
    public class GuideSpotlight : MonoBehaviour
    {
        static GuideSpotlight instance;

        Rect hole;
        string caption;
        bool active;
        float alpha;          // 부드럽게 켜지고 꺼짐
        GUIStyle captionStyle;

        public static void Set(Rect screenHole, string text)
        {
            Ensure();
            instance.hole = screenHole;
            instance.caption = text;
            instance.active = true;
        }

        public static void Clear()
        {
            if (instance != null) instance.active = false;
        }

        static void Ensure()
        {
            if (instance != null) return;
            instance = new GameObject("@GuideSpotlight").AddComponent<GuideSpotlight>();
        }

        void Update()
        {
            alpha = Mathf.MoveTowards(alpha, active ? 1f : 0f, Time.unscaledDeltaTime * 4f);
        }

        void OnGUI()
        {
            if (alpha <= 0.001f) return;
            GUI.depth = 100; // 다른 OnGUI(HUD·무전창)보다 뒤 — 구멍 안의 UI가 위에 그려진다

            float W = Screen.width, H = Screen.height;
            var h = hole;
            h.xMin = Mathf.Clamp(h.xMin, 0f, W); h.xMax = Mathf.Clamp(h.xMax, 0f, W);
            h.yMin = Mathf.Clamp(h.yMin, 0f, H); h.yMax = Mathf.Clamp(h.yMax, 0f, H);

            GUI.color = new Color(0f, 0f, 0f, 0.62f * alpha);
            var tex = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(0f, 0f, W, h.yMin), tex);                        // 위
            GUI.DrawTexture(new Rect(0f, h.yMax, W, H - h.yMax), tex);                // 아래
            GUI.DrawTexture(new Rect(0f, h.yMin, h.xMin, h.height), tex);             // 왼
            GUI.DrawTexture(new Rect(h.xMax, h.yMin, W - h.xMax, h.height), tex);     // 오른

            // 구멍 테두리 — 시안 네온 얇게
            GUI.color = new Color(0.45f, 1f, 0.95f, 0.9f * alpha);
            float t = 2f;
            GUI.DrawTexture(new Rect(h.xMin - t, h.yMin - t, h.width + t * 2, t), tex);
            GUI.DrawTexture(new Rect(h.xMin - t, h.yMax, h.width + t * 2, t), tex);
            GUI.DrawTexture(new Rect(h.xMin - t, h.yMin, t, h.height), tex);
            GUI.DrawTexture(new Rect(h.xMax, h.yMin, t, h.height), tex);

            if (!string.IsNullOrEmpty(caption))
            {
                if (captionStyle == null)
                {
                    captionStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
                    GameFonts.Apply(captionStyle, GameFonts.Title);
                }
                float s = Mathf.Max(1f, H / 1080f);
                captionStyle.fontSize = Mathf.RoundToInt(26 * s);
                // 캡션은 구멍 아래, 화면 아래쪽에 구멍이 있으면 위에
                float capH = 48f * s;
                float y = h.yMax + 16f * s + capH < H ? h.yMax + 16f * s : h.yMin - capH - 16f * s;
                var box = new Rect(Mathf.Clamp(h.center.x - 360f * s, 8f, W - 720f * s - 8f), y, 720f * s, capH);
                GUI.color = new Color(0f, 0f, 0f, 0.7f * alpha);
                GUI.DrawTexture(box, tex);
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Label(box, caption, captionStyle);
            }
            GUI.color = Color.white;
        }
    }
}
