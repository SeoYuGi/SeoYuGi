using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 게임 폰트 로더 — Resources/Fonts. 구글 폰트 네이티브 TTF만 사용
    /// (woff 변환본은 Unity에서 글리프 메시 생성 실패 — 텍스트 중복/누락 버그의 원인이었음).
    /// Title/HudHeavy = Gothic A1 Black (타이틀·배너·자막·숫자 — 단단하고 샤프),
    /// Hud = IBM Plex Sans KR Medium (라벨·이름표·본문 — 테크니컬 정제).
    /// 없으면 null 반환 — 호출부는 기본 폰트 유지 (안전 폴백).
    /// </summary>
    public static class GameFonts
    {
        static Font title, hud;
        static bool loaded;

        public static Font Title { get { Load(); return title; } }
        public static Font Hud { get { Load(); return hud; } }
        public static Font HudHeavy => Title; // 숫자·강조 동일 — 웨이트 대비는 크기로

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            // 전반적으로 얇게 — 제목도 IBM Plex Regular, 본문은 Light
            title = Resources.Load<Font>("Fonts/IBMPlexSansKR-Regular");
            hud = Resources.Load<Font>("Fonts/IBMPlexSansKR-Light");
        }

        /// <summary>TextMesh에 폰트 적용 — 머티리얼까지 교체해야 글자가 보인다.</summary>
        public static void Apply(TextMesh tm, Font font)
        {
            if (font == null || tm == null) return;
            tm.font = font;
            var rend = tm.GetComponent<Renderer>();
            if (rend != null) rend.material = font.material;
        }

        /// <summary>GUIStyle에 폰트 적용 — null이면 기본 유지.</summary>
        public static void Apply(GUIStyle style, Font font)
        {
            if (font != null && style != null) style.font = font;
        }
    }
}
