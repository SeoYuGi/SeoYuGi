using SeoYuGi.Battle;
using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 유닛 머리 위 콜사인 + HP 바 (비율제 — 칸 핍은 가시성 문제로 폐기, 2026-09).
    /// 코드 생성 쿼드 + 카메라 빌보드. 감소는 잔상 바가 따라붙어 "얼마나 깎였나"가 읽힌다.
    /// </summary>
    public class UnitHpBar : MonoBehaviour
    {
        const float BarWidth = 0.8f;   // 가독성 패스: 0.62 → 0.8
        const float BarHeight = 0.12f; // 0.09 → 0.12
        const float Height = 1.35f;    // 유닛 위 높이 — 스킨 키 1.4에 맞춰 올림

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Color FullColor = new Color(0.3f, 0.9f, 0.4f);
        static readonly Color EmptyColor = new Color(0.13f, 0.13f, 0.16f);
        static readonly Color ChipColor = new Color(1f, 0.85f, 0.4f); // 감소 잔상 — 깎인 양 강조

        UnitState unit;
        Transform follow;
        Color pipColor = FullColor;
        bool alwaysShowPips;
        MaterialPropertyBlock mpb;
        Camera cam;

        Renderer backRend, fillRend, chipRend;
        Transform fillTr, chipTr;
        float shownFrac = 1f; // 즉시 반영되는 실제 비율
        float chipFrac = 1f;  // 천천히 따라오는 잔상 비율
        int lastHp = -1;          // 피격 감지용 — 줄어드는 순간 최근 피격 창 갱신
        float recentHitUntil;     // 이 시각까지만 바 표시 (내 유닛 제외)

        /// <param name="pipColor">바 색 — 아군 초록, 적 빨강 (색 규칙 G). default = 초록.</param>
        /// <param name="alwaysShowPips">true면 풀피여도 표시 (내 유닛). 나머지는 다쳤을 때만.</param>
        public static UnitHpBar Create(Transform parent, UnitState unit, Transform follow,
            string displayName = null, Color nameColor = default, Color pipColor = default, bool alwaysShowPips = false)
        {
            var go = new GameObject($"HpBar_{unit.id}");
            go.transform.SetParent(parent);
            var bar = go.AddComponent<UnitHpBar>();
            bar.unit = unit;
            bar.follow = follow;
            bar.pipColor = pipColor == default ? FullColor : pipColor;
            bar.alwaysShowPips = alwaysShowPips;
            bar.Build();
            if (!string.IsNullOrEmpty(displayName))
                bar.BuildName(displayName, nameColor == default ? Color.white : nameColor);
            return bar;
        }

        void BuildName(string displayName, Color color)
        {
            var go = new GameObject("Name");
            go.transform.SetParent(transform);
            go.transform.localPosition = new Vector3(0f, BarHeight * 1.2f, 0f);
            var tm = go.AddComponent<TextMesh>();
            tm.text = displayName;
            tm.fontSize = 48;              // 큰 폰트 + 작은 characterSize = 선명
            tm.characterSize = 0.06f; // 0.045 → 0.06 — 이름표도 같이 키움
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.Lerp(color, Color.white, 0.35f);
            GameFonts.Apply(tm, GameFonts.Hud); // 콜사인 = SUIT
        }

        Renderer MakeQuad(string name, float z)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>()); // 클릭 레이캐스트 방해 금지
            quad.name = name;
            quad.transform.SetParent(transform);
            quad.transform.localPosition = new Vector3(0f, 0f, z);
            var r = quad.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        void Build()
        {
            mpb = new MaterialPropertyBlock();
            cam = Camera.main;
            // z 겹침: 배경(뒤) > 잔상 > 채움(앞) — 카메라 빌보드라 -z가 카메라 쪽
            backRend = MakeQuad("Back", 0.002f);
            backRend.transform.localScale = new Vector3(BarWidth + 0.02f, BarHeight + 0.02f, 1f);
            chipRend = MakeQuad("Chip", 0.001f);
            chipTr = chipRend.transform;
            fillRend = MakeQuad("Fill", 0f);
            fillTr = fillRend.transform;
            shownFrac = chipFrac = Frac;
            Paint(backRend, EmptyColor);
        }

        float Frac => unit.maxHp <= 0 ? 0f : Mathf.Clamp01(unit.hp / (float)unit.maxHp);

        void Paint(Renderer r, Color c)
        {
            mpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(mpb);
        }

        /// <summary>왼쪽 고정 채움 — 폭 스케일 + 중심 보정.</summary>
        static void Layout(Transform tr, float frac)
        {
            float w = BarWidth * Mathf.Clamp01(frac);
            tr.localScale = new Vector3(Mathf.Max(w, 0.0001f), BarHeight, 1f);
            var p = tr.localPosition;
            p.x = -BarWidth * 0.5f + w * 0.5f;
            tr.localPosition = p;
        }

        /// <summary>가시성은 러너가 결정 — 사망·시야 밖이면 숨긴다.</summary>
        public void SetVisible(bool value)
        {
            if (gameObject.activeSelf != value) gameObject.SetActive(value);
        }

        void LateUpdate()
        {
            if (!unit.alive) return;

            transform.position = follow.position + Vector3.up * Height;
            if (cam != null) transform.rotation = cam.transform.rotation;

            // 탱고파이브식 다이어트 (가시성 패스 2026-09-05): 내 유닛 외에는
            // "최근 3초 안에 맞았을 때"만 바를 보여준다 — 상시 바 6개는 실루엣을 잡아먹는다.
            if (unit.hp < lastHp) recentHitUntil = Time.time + 3f;
            lastHp = unit.hp;
            bool show = alwaysShowPips || (unit.hp < unit.maxHp && Time.time < recentHitUntil);
            if (backRend.enabled != show)
            {
                backRend.enabled = show;
                fillRend.enabled = show;
                chipRend.enabled = show;
            }
            if (!show) return;

            shownFrac = Frac;
            // 잔상 바 — 깎인 직후 0.5초쯤 노랗게 남았다가 따라 내려온다 (회복은 즉시 동기화)
            chipFrac = chipFrac < shownFrac ? shownFrac : Mathf.MoveTowards(chipFrac, shownFrac, Time.deltaTime * 0.9f);

            Layout(fillTr, shownFrac);
            Layout(chipTr, chipFrac);

            // 낮은 체력은 색으로도 경고 — 30% 이하 맥동
            var c = pipColor;
            if (shownFrac <= 0.3f)
                c = Color.Lerp(pipColor, Color.white, 0.35f + 0.35f * Mathf.Sin(Time.time * 7f));
            Paint(fillRend, c);
            Paint(chipRend, ChipColor);
            Paint(backRend, EmptyColor);
        }
    }
}
