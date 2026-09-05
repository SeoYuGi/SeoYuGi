using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 휠클릭 핑 — 찍은 칸 위 팀색 링 펄스 + 통통 튀는 ▼ 마커. 2.6초 뒤 소멸.
    /// 코드 생성 (프리팹·씬 배선 불필요). 링은 RingWave를 재활용해 주기적으로 쏜다.
    /// </summary>
    public class PingMarker : MonoBehaviour
    {
        /// <summary>핑 종류 — 넷 메시지의 type 바이트와 1:1.</summary>
        public const int TypeArrow = 0;    // ▼ 여기로 (디폴트)
        public const int TypeAlert = 1;    // ! 경고
        public const int TypeQuestion = 2; // ? 모름/정찰

        const float Life = 2.6f;
        const float PulseEvery = 0.85f;

        Color color;
        int type;
        float born;
        float nextPulse;
        Transform arrow;
        Camera cam;

        public static void Spawn(Vector3 worldPos, Color color, int type = TypeArrow)
        {
            var go = new GameObject("PingMarker");
            go.transform.position = worldPos;
            var p = go.AddComponent<PingMarker>();
            p.color = color;
            p.type = type;
        }

        public static string GlyphOf(int type) =>
            type == TypeAlert ? "!" : type == TypeQuestion ? "?" : "▼";

        void Start()
        {
            cam = Camera.main;
            born = Time.time;
            RingWave.Spawn(transform.position, color, 1.6f, 0.5f);
            nextPulse = Time.time + PulseEvery;

            var a = new GameObject("Glyph");
            a.transform.SetParent(transform, false);
            var tm = a.AddComponent<TextMesh>();
            tm.text = GlyphOf(type);
            tm.fontSize = 56;
            tm.fontStyle = type == TypeArrow ? FontStyle.Normal : FontStyle.Bold;
            tm.characterSize = type == TypeArrow ? 0.12f : 0.16f; // !/?는 글자가 얇아 크게
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.Lerp(color, Color.white, 0.35f);
            arrow = a.transform;
        }

        void Update()
        {
            float t = Time.time - born;
            if (t >= Life) { Destroy(gameObject); return; }
            if (Time.time >= nextPulse)
            {
                RingWave.Spawn(transform.position, color, 1.3f, 0.45f);
                nextPulse += PulseEvery;
            }
            // 위에서 꽂혔다가 튀는 ▼ — 정지 화살표는 지형과 구분이 안 된다
            float bounce = Mathf.Abs(Mathf.Sin(t * 5f)) * 0.35f;
            arrow.position = transform.position + Vector3.up * (0.9f + bounce);
            if (cam != null) arrow.rotation = cam.transform.rotation; // 빌보드
        }
    }
}
