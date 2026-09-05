using System;
using SeoYuGi.Battle;
using SeoYuGi.BattleView; // GameFonts
using UnityEngine;
using UnityEngine.UI;

namespace SeoYuGi.UI
{
    /// <summary>
    /// 캐릭터 카드 공용 빌더 — 캐릭터 선택 팝업·멀티 로비가 같은 카드를 쓴다.
    /// 네온 테두리 + 헤더 + 3:4 초상 + HP 바 + 공/방/기 도트 + 스킬 2행.
    /// 수치는 ClassCatalog 실데이터라 밸런스 패치가 자동 반영. scale로 크기 조절.
    /// </summary>
    public static class ClassCard
    {
        public const float BaseW = 280f, BaseH = 700f; // scale 1 기준

        // enum 순서: 이름·역할·네온 색
        public static readonly (string name, string roleEn, Color color)[] Meta =
        {
            ("너구리",      "TANKER",    new Color(1f, 0.54f, 0.16f)),
            ("고라니",      "RUNNER",    new Color(0.64f, 0.42f, 1f)),
            ("검은 고양이", "ASSASSIN",  new Color(0.21f, 0.84f, 1f)),
            ("비둘기",      "GRENADIER", new Color(0.29f, 0.87f, 0.37f)),
            ("까치",        "MARKSMAN",  new Color(0.23f, 0.51f, 0.96f)),
        };

        static readonly string[] Portraits =
            { "Card_Tank", "Card_Balance", "Card_Assassin", "Card_Grenadier", "Card_Sniper" };

        static readonly Color CardBg = new Color(0.02f, 0.03f, 0.07f, 0.97f);
        static readonly Color DimText = new Color(0.55f, 0.62f, 0.72f);
        static readonly Color EmptyDot = new Color(0.16f, 0.19f, 0.26f);

        /// <summary>카드 RectTransform에 클래스 i의 카드를 그린다. sizeDelta를 scale에 맞춰 설정한다.</summary>
        public static void Build(RectTransform card, int i, float scale = 1f)
        {
            var meta = Meta[i];
            var def = ClassCatalog.Get((UnitClass)i);
            float W = BaseW * scale, H = BaseH * scale;
            card.sizeDelta = new Vector2(W, H);

            // 프리팹의 구 텍스트(Name/Desc) 제거
            foreach (var n in new[] { "Name", "Desc" })
            {
                var old = card.Find(n);
                if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            }

            var rootImg = card.GetComponent<Image>();
            if (rootImg != null) { rootImg.sprite = null; rootImg.color = CardBg; }
            MakeNeonBorder(card, meta.color, scale);

            float top = H * 0.5f;
            var portraitTex = Resources.Load<Texture2D>("UI/" + Portraits[i]);

            // 상단 열기 글로우
            Img(card, null, new Color(meta.color.r, meta.color.g, meta.color.b, 0.16f),
                new Vector2(0f, top - 90f * scale), new Vector2(W - 8f * scale, 180f * scale));

            // 헤더: 이름 + 역할 + 우상단 썸네일 + 언더라인
            var nameColor = Color.Lerp(Color.white, meta.color, 0.25f);
            NeonTxt(card, meta.name, R(27, scale), nameColor, meta.color,
                new Vector2(-W / 2f + 18f * scale, top - 32f * scale), new Vector2(200f, 36f), GameFonts.Hud);
            Txt(card, meta.roleEn, R(13, scale), FontStyle.Bold, meta.color, TextAnchor.MiddleLeft,
                new Vector2(-W / 2f + 18f * scale, top - 58f * scale), new Vector2(200f, 18f), GameFonts.Hud);
            var uline = Img(card, null, meta.color,
                new Vector2(-W / 2f + 90f * scale, top - 74f * scale), new Vector2(150f * scale, 2f));
            AddGlow(uline, meta.color, 3f * scale);
            if (portraitTex != null)
                Img(card, ToSprite(portraitTex), Color.white,
                    new Vector2(W / 2f - 32f * scale, top - 34f * scale), new Vector2(40f * scale, 40f * scale), aspect: true);

            // 초상 슬롯 3:4
            float slotTop = 84f * scale;
            float slotW = W - 32f * scale;
            float slotH = slotW * 4f / 3f;
            float slotCenterY = top - slotTop - slotH * 0.5f;
            var slot = Img(card, null, new Color(0.05f, 0.07f, 0.12f, 1f),
                new Vector2(0f, slotCenterY), new Vector2(slotW, slotH));
            AddGlow(slot, meta.color, 2f * scale);
            if (portraitTex != null)
                Img(card, ToSprite(portraitTex), Color.white,
                    new Vector2(0f, slotCenterY), new Vector2(slotW - 6f * scale, slotH - 6f * scale));

            // HP 바
            float below = top - slotTop - slotH - 22f * scale;
            float y = below;
            float barLeft = -W / 2f + 52f * scale, barRight = W / 2f - 44f * scale;
            float barW = barRight - barLeft;
            Txt(card, "HP", R(17, scale), FontStyle.Bold, Color.white, TextAnchor.MiddleLeft,
                new Vector2(-W / 2f + 18f * scale, y), new Vector2(40f, 22f), GameFonts.Hud);
            Img(card, null, new Color(0.14f, 0.17f, 0.23f, 1f),
                new Vector2((barLeft + barRight) / 2f, y), new Vector2(barW, 12f * scale));
            float hpFrac = def.maxHp / 15f;
            var fill = Img(card, null, meta.color,
                new Vector2(barLeft + barW * hpFrac / 2f, y), new Vector2(barW * hpFrac, 12f * scale));
            AddGlow(fill, meta.color, 1.5f * scale);
            Txt(card, def.maxHp.ToString(), R(17, scale), FontStyle.Bold, Color.white, TextAnchor.MiddleRight,
                new Vector2(W / 2f - 16f * scale, y), new Vector2(44f, 22f), GameFonts.Hud, pivotRight: true);

            // 스탯 3행
            int atk = 0;
            foreach (var s in def.skills) atk = Math.Max(atk, s.damage);
            int dfn = Mathf.Clamp(Mathf.RoundToInt(def.maxHp / 3f), 1, 5);
            int mob = Mathf.Clamp(def.move.maxRange, 1, 5);
            StatRow(card, "공격", atk, meta.color, below - 34f * scale, W, scale);
            StatRow(card, "방어", dfn, new Color(0.35f, 0.85f, 0.45f), below - 62f * scale, W, scale);
            StatRow(card, "기동", mob, meta.color, below - 90f * scale, W, scale);

            // 구분선
            Img(card, null, new Color(0.2f, 0.25f, 0.33f, 1f),
                new Vector2(0f, below - 116f * scale), new Vector2(W - 32f * scale, 1.5f));

            // 스킬 2행
            SkillRow(card, def.skills[0], below - 142f * scale, W, scale);
            if (def.skills.Length > 1)
                SkillRow(card, def.skills[1], below - 176f * scale, W, scale);
        }

        /// <summary>선택 하이라이트 — 밝은 외곽 글로우 + 살짝 확대. 카드마다 1개만 유지.</summary>
        public static void SetSelected(RectTransform card, bool on, Color color)
        {
            var existing = card.Find("SelHalo");
            if (!on)
            {
                if (existing != null) UnityEngine.Object.Destroy(existing.gameObject);
                card.localScale = Vector3.one;
                return;
            }
            card.localScale = Vector3.one * 1.06f;
            if (existing != null) return;
            var bright = Color.Lerp(color, Color.white, 0.55f);
            var go = new GameObject("SelHalo", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(card, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-4f, -4f); rt.offsetMax = new Vector2(4f, 4f);
            var img = go.GetComponent<Image>();
            img.color = new Color(bright.r, bright.g, bright.b, 0f); // 본체 투명 — 글로우만
            img.raycastTarget = false;
            AddGlow(rt, bright, 5f);
            rt.SetAsLastSibling();
        }

        // ── 내부 ──────────────────────────────────────────────

        static int R(int size, float scale) => Mathf.Max(6, Mathf.RoundToInt(size * scale));

        static void StatRow(Transform card, string label, int value, Color color, float y, float W, float scale)
        {
            Txt(card, label, R(15, scale), FontStyle.Bold, DimText, TextAnchor.MiddleLeft,
                new Vector2(-W / 2f + 18f * scale, y), new Vector2(56f, 22f), GameFonts.Hud);
            var dotSprite = DotSprite();
            float d0 = 84f * scale, gap = 26f * scale, r = 14f * scale;
            for (int d = 0; d < 5; d++)
            {
                var dot = Img(card, dotSprite, d < value ? color : EmptyDot,
                    new Vector2(-W / 2f + d0 + d * gap, y), new Vector2(r, r));
                if (d < value) AddGlow(dot, color, 1.2f * scale);
            }
        }

        static void SkillRow(Transform card, SkillDef skill, float y, float W, float scale)
        {
            var iconTex = Resources.Load<Texture2D>("UI/" + SkillIconName(skill.kind));
            if (iconTex != null)
                Img(card, ToSprite(iconTex), Color.white,
                    new Vector2(-W / 2f + 30f * scale, y), new Vector2(26f * scale, 26f * scale), aspect: true);
            Txt(card, $"<b>{SkillName(skill.kind)}</b>  <color=#9aa5b5><size={R(12, scale)}>{SkillDesc(skill.kind)}</size></color>",
                R(16, scale), FontStyle.Normal, Color.white, TextAnchor.MiddleLeft,
                new Vector2(-W / 2f + 50f * scale, y), new Vector2(W - 62f * scale, 26f), GameFonts.Hud, rich: true);
        }

        static string SkillName(SkillKind k) => k switch
        {
            SkillKind.ShieldPush => "방패 밀기", SkillKind.Smash => "강타", SkillKind.Dash => "돌파",
            SkillKind.Scream => "비명", SkillKind.Blink => "도약", SkillKind.Claw => "발톱",
            SkillKind.Burst => "파열탄", SkillKind.BombDeliver => "폭탄 배달",
            SkillKind.KnockShot => "넉백샷", SkillKind.Snipe => "저격", _ => k.ToString()
        };

        static string SkillDesc(SkillKind k) => k switch
        {
            SkillKind.ShieldPush => "전방 밀치기", SkillKind.Smash => "밀침·벽충돌", SkillKind.Dash => "직선 2칸 대시",
            SkillKind.Scream => "주변 1초 스턴", SkillKind.Blink => "2칸 점멸", SkillKind.Claw => "고위력 근접",
            SkillKind.Burst => "십자 5칸", SkillKind.BombDeliver => "원거리 투척",
            SkillKind.KnockShot => "밀쳐내는 사격", SkillKind.Snipe => "1열 관통", _ => ""
        };

        static string SkillIconName(SkillKind k) => k switch
        {
            SkillKind.Smash => "Icon_Skill_Smash", SkillKind.Dash => "Icon_Skill_Dash",
            SkillKind.Blink => "Icon_Skill_Blink", SkillKind.Burst => "Icon_Skill_Burst",
            SkillKind.Snipe => "Icon_Skill_Snipe", SkillKind.ShieldPush => "Icon_Guard",
            SkillKind.Claw => "Icon_Attack", SkillKind.KnockShot => "Icon_Attack",
            SkillKind.BombDeliver => "Icon_Skill_BombDeliver", _ => "Icon_Skill_Generic"
        };

        static string Colored(string s, Color c) => $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{s}</color>";
        static Sprite ToSprite(Texture2D t) => Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f));

        static Sprite dotSprite;

        /// <summary>스탯 도트용 원형 스프라이트. 내장 Knob.psd는 에디터 전용이라 절차 생성.</summary>
        static Sprite DotSprite()
        {
            if (dotSprite != null) return dotSprite;

            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float a = Mathf.Clamp01(c - d);   // 가장자리 1px 안티에일리어싱
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            dotSprite = ToSprite(tex);
            return dotSprite;
        }

        static void MakeNeonBorder(Transform card, Color color, float scale)
        {
            var halo = color; halo.a = 0.35f;
            float hw = 7f * scale, bw = 3f * scale;
            Edge(card, halo, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, hw));
            Edge(card, halo, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, hw));
            Edge(card, halo, new Vector2(0, 0), new Vector2(0, 1), new Vector2(hw, 0));
            Edge(card, halo, new Vector2(1, 0), new Vector2(1, 1), new Vector2(hw, 0));
            var bright = Color.Lerp(color, Color.white, 0.45f);
            Edge(card, bright, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, bw));
            Edge(card, bright, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, bw));
            Edge(card, bright, new Vector2(0, 0), new Vector2(0, 1), new Vector2(bw, 0));
            Edge(card, bright, new Vector2(1, 0), new Vector2(1, 1), new Vector2(bw, 0));
        }

        static void Edge(Transform card, Color color, Vector2 aMin, Vector2 aMax, Vector2 size)
        {
            var go = new GameObject("Border", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(card, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.anchoredPosition = Vector2.zero; rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = color; img.raycastTarget = false;
        }

        static void AddGlow(RectTransform rt, Color color, float spread)
        {
            var g = color; g.a = 0.55f;
            foreach (var d in new[] { new Vector2(spread, spread), new Vector2(-spread, -spread),
                                       new Vector2(spread, -spread), new Vector2(-spread, spread) })
            {
                var s = rt.gameObject.AddComponent<Shadow>();
                s.effectColor = g; s.effectDistance = d;
            }
        }

        static void NeonTxt(Transform parent, string text, int size, Color color, Color glow,
            Vector2 pos, Vector2 sizeDelta, Font font)
        {
            Txt(parent, text, size, FontStyle.Normal, color, TextAnchor.MiddleLeft, pos, sizeDelta, font);
            var rt = (RectTransform)parent.GetChild(parent.childCount - 1);
            var g = glow; g.a = 0.4f;
            var sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = g; sh.effectDistance = new Vector2(1.5f, -1.5f);
        }

        static RectTransform Img(Transform parent, Sprite sprite, Color color, Vector2 pos, Vector2 size, bool aspect = false)
        {
            var go = new GameObject("Img", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.sprite = sprite; img.color = color; img.preserveAspect = aspect; img.raycastTarget = false;
            return rt;
        }

        static void Txt(Transform parent, string text, int size, FontStyle style, Color color,
            TextAnchor align, Vector2 pos, Vector2 sizeDelta, Font font, bool rich = false, bool pivotRight = false)
        {
            var go = new GameObject("Txt", typeof(RectTransform), typeof(Text));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = pivotRight ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = sizeDelta;
            var t = go.GetComponent<Text>();
            t.text = text;
            t.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size; t.fontStyle = style; t.color = color; t.alignment = align;
            t.supportRichText = rich;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
        }
    }
}
