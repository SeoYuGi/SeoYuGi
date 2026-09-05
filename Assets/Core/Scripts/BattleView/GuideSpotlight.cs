using System.Collections.Generic;
using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 튜토리얼 스포트라이트 (2026-09-05) — 화면을 어둡게 깔고 지금 봐야 할 곳만 구멍을 낸다.
    /// 2026-09-06 다중 구멍 지원 — 작전 시간 정지 화면이 무전 입력줄과 안내판 두 곳을 동시에 밝힌다.
    /// IMGUI는 구멍을 못 뚫으니 구멍들의 가로 띠 분할로 어둠을 그린다. GUI.depth를 크게 둬서
    /// HUD·무전창 등 다른 OnGUI보다 뒤에(아래에) 깔린다 — 구멍 안 UI는 그대로 밝고, 밖은 어둡다.
    /// 좌표는 화면 픽셀(GUI 기준, y는 위에서 아래).
    /// </summary>
    public class GuideSpotlight : MonoBehaviour
    {
        static GuideSpotlight instance;

        Rect[] holes = System.Array.Empty<Rect>();
        string caption;
        bool active;
        float alpha;          // 부드럽게 켜지고 꺼짐
        GUIStyle captionStyle;

        public static void Set(Rect screenHole, string text) => SetHoles(new[] { screenHole }, text);

        /// <summary>구멍 여러 개 — 캡션은 첫 구멍 밑(또는 위)에 붙는다. null이면 캡션 없음.</summary>
        public static void SetHoles(Rect[] screenHoles, string text)
        {
            Ensure();
            instance.holes = screenHoles ?? System.Array.Empty<Rect>();
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
            if (alpha <= 0.001f || holes.Length == 0) return;
            GUI.depth = 100; // 다른 OnGUI(HUD·무전창)보다 뒤 — 구멍 안의 UI가 위에 그려진다

            float W = Screen.width, H = Screen.height;
            var hs = new Rect[holes.Length];
            for (int i = 0; i < holes.Length; i++)
            {
                var h = holes[i];
                h.xMin = Mathf.Clamp(h.xMin, 0f, W); h.xMax = Mathf.Clamp(h.xMax, 0f, W);
                h.yMin = Mathf.Clamp(h.yMin, 0f, H); h.yMax = Mathf.Clamp(h.yMax, 0f, H);
                hs[i] = h;
            }

            var tex = Texture2D.whiteTexture;
            GUI.color = new Color(0f, 0f, 0f, 0.62f * alpha);

            // 가로 띠 분할 — 구멍들의 y 경계로 화면을 띠로 자르고, 띠마다 구멍 사이만 어둡게 채운다
            var ys = new List<float> { 0f, H };
            foreach (var h in hs) { ys.Add(h.yMin); ys.Add(h.yMax); }
            ys.Sort();
            for (int i = 0; i < ys.Count - 1; i++)
            {
                float y0 = ys[i], y1 = ys[i + 1];
                if (y1 - y0 <= 0.01f) continue;
                var spans = new List<Rect>();
                foreach (var h in hs)
                    if (h.yMin <= y0 + 0.01f && h.yMax >= y1 - 0.01f && h.width > 0f) spans.Add(h);
                spans.Sort((a, b) => a.xMin.CompareTo(b.xMin));
                float cur = 0f;
                foreach (var sp in spans)
                {
                    if (sp.xMin > cur) GUI.DrawTexture(new Rect(cur, y0, sp.xMin - cur, y1 - y0), tex);
                    cur = Mathf.Max(cur, sp.xMax);
                }
                if (cur < W) GUI.DrawTexture(new Rect(cur, y0, W - cur, y1 - y0), tex);
            }

            // 구멍 테두리 — 시안 네온 얇게
            GUI.color = new Color(0.45f, 1f, 0.95f, 0.9f * alpha);
            const float t = 2f;
            foreach (var h in hs)
            {
                GUI.DrawTexture(new Rect(h.xMin - t, h.yMin - t, h.width + t * 2, t), tex);
                GUI.DrawTexture(new Rect(h.xMin - t, h.yMax, h.width + t * 2, t), tex);
                GUI.DrawTexture(new Rect(h.xMin - t, h.yMin, t, h.height), tex);
                GUI.DrawTexture(new Rect(h.xMax, h.yMin, t, h.height), tex);
            }

            if (!string.IsNullOrEmpty(caption))
            {
                if (captionStyle == null)
                {
                    captionStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
                    GameFonts.Apply(captionStyle, GameFonts.Title);
                }
                var h0 = hs[0];
                float s = Mathf.Max(1f, H / 1080f);
                captionStyle.fontSize = Mathf.RoundToInt(26 * s);
                // 캡션은 구멍 아래, 화면 아래쪽에 구멍이 있으면 위에
                float capH = 48f * s;
                float y = h0.yMax + 16f * s + capH < H ? h0.yMax + 16f * s : h0.yMin - capH - 16f * s;
                var box = new Rect(Mathf.Clamp(h0.center.x - 360f * s, 8f, W - 720f * s - 8f), y, 720f * s, capH);
                GUI.color = new Color(0f, 0f, 0f, 0.7f * alpha);
                GUI.DrawTexture(box, tex);
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Label(box, caption, captionStyle);
            }
            GUI.color = Color.white;
        }
    }
}
