using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 게임 폰트 로더 — Resources/Fonts. 구글 폰트 네이티브 TTF만 사용
    /// (woff 변환본은 Unity에서 글리프 메시 생성 실패 — 텍스트 중복/누락 버그의 원인이었음).
    /// 2026-09-05 에이투지체로 교체 (유저 지정). 무게 규칙: "진짜 강조만 두껍게, 나머지는 미디엄".
    ///   Title    = A2Z Bold   — 승패 배너·중앙 공지·거점 글자·튜토리얼 캡션 같은 진짜 강조
    ///   Hud      = A2Z Medium — 라벨·본문·이름표
    ///   HudHeavy = A2Z Medium — 숫자·킬피드·말풍선·데미지 (구 Bold. 두꺼운 건 Title만)
    /// 무게는 폰트 파일이 갖는다 — GUIStyle/TextMesh의 FontStyle.Bold(가짜 볼드, 번짐)는 Apply가 Normal로 되돌린다.
    /// 없으면 프리텐다드 → IBM Plex 순 폴백.
    /// </summary>
    public static class GameFonts
    {
        static Font title, hud;
        static bool loaded;

        public static Font Title { get { Load(); return title; } }
        public static Font Hud { get { Load(); return hud; } }
        public static Font HudHeavy => Hud; // 숫자·강조도 미디엄 — 두꺼운 건 Title(진짜 강조)만

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            title = Resources.Load<Font>("Fonts/A2Z-Bold");
            hud = Resources.Load<Font>("Fonts/A2Z-Medium");
            // 폰트가 아직 안 들어왔을 때만 구 폰트로 — 화면이 통째로 비지 않게
            if (title == null) title = Resources.Load<Font>("Fonts/Pretendard-Bold");
            if (hud == null) hud = Resources.Load<Font>("Fonts/Pretendard-Medium");
            if (title == null) title = Resources.Load<Font>("Fonts/IBMPlexSansKR-Regular");
            if (hud == null) hud = Resources.Load<Font>("Fonts/IBMPlexSansKR-Light");
        }

        /// <summary>TextMesh에 폰트 적용 — 머티리얼까지 교체해야 글자가 보인다.</summary>
        public static void Apply(TextMesh tm, Font font)
        {
            if (font == null || tm == null) return;
            tm.font = font;
            tm.fontStyle = FontStyle.Normal; // 무게는 파일이 — 가짜 볼드 금지
            var rend = tm.GetComponent<Renderer>();
            if (rend != null) rend.material = font.material;
        }

        /// <summary>
        /// uGUI Text 헬퍼용 — 요청한 무게를 파일 무게로 바꾼다. Bold 요청이면 Title(볼드 파일)+Normal,
        /// 아니면 주어진 폰트(없으면 Hud)+Normal. 가짜 볼드가 사라지고 무게 규칙이 한 곳에서 지켜진다.
        /// </summary>
        public static void Resolve(ref Font font, ref FontStyle style)
        {
            if (style == FontStyle.Bold || style == FontStyle.BoldAndItalic)
            {
                if (Title != null) { font = Title; style = style == FontStyle.BoldAndItalic ? FontStyle.Italic : FontStyle.Normal; }
            }
            else if (font == null) font = Hud;
        }

        /// <summary>GUIStyle에 폰트 적용 — null이면 기본 유지.</summary>
        public static void Apply(GUIStyle style, Font font)
        {
            if (font == null || style == null) return;
            style.font = font;
            // 가짜 볼드 제거 — Bold 요청은 Title(볼드 파일)로, 무게는 파일이 갖는다
            if (style.fontStyle == FontStyle.Bold && font != Title && Title != null) style.font = Title;
            if (style.fontStyle == FontStyle.Bold) style.fontStyle = FontStyle.Normal;
        }
    }
}
