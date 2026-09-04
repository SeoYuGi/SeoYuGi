using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// "지금 내 칸에 적 예고가 떨어진다" 경고 — 바닥 틴트는 내 유닛 모델에 가려 안 보이므로
    /// 유닛 주위 빨간 링 + 머리 위 "!"로 알린다. 판정이 가까울수록 빨리 뛰고 커진다.
    /// 매치 내내 1개 유지, 위협 없으면 숨김. 코드 생성 (프리팹·씬 배선 불필요).
    /// </summary>
    public class ThreatWarning : MonoBehaviour
    {
        static readonly Color Red = new Color(1f, 0.22f, 0.14f);
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        Transform ring;
        Renderer ringRend;
        MaterialPropertyBlock ringMpb;
        TextMesh bang;
        bool shown;

        public static ThreatWarning Create(Transform parent)
        {
            var go = new GameObject("ThreatWarning");
            go.transform.SetParent(parent);
            var w = go.AddComponent<ThreatWarning>();
            w.Build();
            go.SetActive(false);
            return w;
        }

        void Build()
        {
            // 바닥 링 — 생성 홀로 링 텍스처가 있으면 그걸로, 없으면 절차 고리 텍스처
            var ringGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(ringGo.GetComponent<Collider>());
            ringGo.name = "Ring";
            ringGo.transform.SetParent(transform, false);
            ringGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ring = ringGo.transform;
            ringRend = ringGo.GetComponent<Renderer>();
            ringRend.sharedMaterial = VfxTextures.Ring != null
                ? VfxTextures.Ring
                : new Material(Shader.Find("Sprites/Default")) { mainTexture = RingTexture() };
            ringRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ringRend.receiveShadows = false;
            ringMpb = new MaterialPropertyBlock();

            // 머리 위 느낌표 — FloatingText와 같은 폰트·빌보드
            var bangGo = new GameObject("Bang");
            bangGo.transform.SetParent(transform, false);
            bang = bangGo.AddComponent<TextMesh>();
            bang.text = "!";
            bang.fontSize = 72;
            bang.characterSize = 0.16f; // 0.09는 유닛 스킨에 묻혀 안 읽혔다
            bang.anchor = TextAnchor.LowerCenter;
            bang.alignment = TextAlignment.Center;
            bang.color = Red;
            GameFonts.Apply(bang, GameFonts.HudHeavy);
        }

        /// <summary>remainSeconds &lt; 0 이면 위협 없음 → 숨김.</summary>
        public void Set(Vector3 groundPos, Vector3 headPos, float remainSeconds)
        {
            bool on = remainSeconds >= 0f;
            if (on != shown)
            {
                shown = on;
                gameObject.SetActive(on);
            }
            if (!on) return;

            // 급박도 0(여유)~1(판정 직전): 뛰는 속도와 진폭이 같이 오른다
            float urgency = 1f - Mathf.Clamp01(remainSeconds / 0.8f);
            float rate = Mathf.Lerp(5f, 22f, urgency);
            float beat = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * rate);

            ring.position = groundPos + Vector3.up * 0.07f;
            float radius = Mathf.Lerp(1.3f, 1.8f, beat * (0.4f + 0.6f * urgency)); // 칸보다 크게 — 유닛 발밑에서 삐져나와야 보인다
            ring.localScale = new Vector3(radius, radius, 1f);
            var c = Red;
            c.a = Mathf.Lerp(0.55f, 1f, beat);
            ringMpb.SetColor(ColorId, c);
            ringMpb.SetColor(BaseColorId, c);
            ringRend.SetPropertyBlock(ringMpb);

            bang.transform.position = headPos + Vector3.up * (0.55f + 0.12f * beat);
            bang.transform.localScale = Vector3.one * (1f + 0.35f * beat * urgency);
            if (Camera.main != null) bang.transform.rotation = Camera.main.transform.rotation; // 빌보드
            bang.color = new Color(1f, Mathf.Lerp(0.22f, 0.9f, beat * urgency), 0.14f); // 판정 직전엔 노랗게 번쩍
        }

        static Texture2D ringTex;
        /// <summary>폴백 고리 텍스처 — 반지름 0.72~1.0 사이만 불투명, 가장자리 부드럽게.</summary>
        static Texture2D RingTexture()
        {
            if (ringTex != null) return ringTex;
            const int n = 64;
            ringTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.86f) / 0.14f);
                ringTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            ringTex.Apply();
            return ringTex;
        }
    }
}
