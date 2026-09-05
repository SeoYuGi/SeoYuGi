using System.Collections.Generic;
using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 빠른채팅 말풍선 — 유닛 머리 위에 붙어 따라다니다 사라진다.
    /// FloatingText(데미지용)는 떠오르며 사라지지만 이건 머문다 — 읽히는 게 목적.
    /// 유닛당 1개만 유지: 새 메시지가 오면 기존 것을 교체.
    /// </summary>
    public class ChatBubble : MonoBehaviour
    {
        const float PopIn = 0.12f;
        static readonly Dictionary<int, ChatBubble> active = new Dictionary<int, ChatBubble>();

        int unitId;
        Transform follow;
        float duration;
        float elapsed;
        TextMesh tm;
        Color baseColor;

        /// <summary>follow = 유닛 뷰 트랜스폼. 죽거나 시야를 벗어나면 말풍선도 같이 사라진다.</summary>
        public static void Show(int unitId, Transform follow, string text, Color color, float duration = 2.4f)
        {
            if (follow == null) return;
            if (active.TryGetValue(unitId, out var old) && old != null) Destroy(old.gameObject);

            var go = new GameObject($"ChatBubble_{unitId}");
            var b = go.AddComponent<ChatBubble>();
            b.unitId = unitId;
            b.follow = follow;
            b.duration = duration;
            b.baseColor = color;
            b.tm = go.AddComponent<TextMesh>();
            b.tm.text = text;
            b.tm.fontSize = 56;
            b.tm.characterSize = 0.058f; // 0.045 → 0.058, 무전 대사 가독성 (2026-09-06)
            b.tm.anchor = TextAnchor.MiddleCenter;
            b.tm.alignment = TextAlignment.Center;
            b.tm.color = color;
            GameFonts.Apply(b.tm, GameFonts.HudHeavy);

            go.transform.position = follow.position + Vector3.up * 1.5f;
            active[unitId] = b;
        }

        void LateUpdate()
        {
            // 유닛이 사라지면(사망·시야 이탈로 뷰 비활성) 말풍선도 정리
            if (follow == null || !follow.gameObject.activeInHierarchy)
            {
                Destroy(gameObject);
                return;
            }

            elapsed += Time.deltaTime;
            if (elapsed >= duration)
            {
                Destroy(gameObject);
                return;
            }

            transform.position = follow.position + Vector3.up * 1.5f;
            if (Camera.main != null) transform.rotation = Camera.main.transform.rotation; // 빌보드

            float pop = elapsed < PopIn ? Mathf.Lerp(0.5f, 1f, elapsed / PopIn) : 1f;
            transform.localScale = Vector3.one * pop;

            float fade = elapsed > duration - 0.4f ? (duration - elapsed) / 0.4f : 1f;
            tm.color = new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Clamp01(fade));
        }

        void OnDestroy()
        {
            if (active.TryGetValue(unitId, out var cur) && cur == this) active.Remove(unitId);
        }
    }
}
