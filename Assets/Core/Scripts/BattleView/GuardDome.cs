using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 방어 실드 돔 — 유닛 자식으로 붙어 따라다니고, 지속시간이 끝나면 축소 소멸.
    /// 방어는 '피해 무효 + 제자리 고정'이라 구체가 감싸는 그림이 규칙을 그대로 읽어준다.
    /// 코드 생성 (프리팹·씬 배선 불필요).
    /// </summary>
    public class GuardDome : MonoBehaviour
    {
        const float PopIn = 0.12f;
        const float PopOut = 0.15f;

        static Material domeMat;

        float duration;
        float elapsed;
        Renderer rend;
        MaterialPropertyBlock mpb;
        Color color;

        /// <summary>parent = 유닛 뷰 트랜스폼. 같은 유닛에 이미 돔이 있으면 시간만 갱신한다.</summary>
        public static void Attach(Transform parent, Color color, float duration)
        {
            if (parent == null) return;

            var existing = parent.GetComponentInChildren<GuardDome>();
            if (existing != null)
            {
                existing.duration = duration;
                existing.elapsed = 0f;
                return;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "GuardDome";
            Destroy(go.GetComponent<Collider>()); // 클릭 레이캐스트 방해 금지
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = Mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var d = go.AddComponent<GuardDome>();
            d.duration = duration;
            d.color = color;
        }

        void Awake()
        {
            rend = GetComponent<Renderer>();
            mpb = new MaterialPropertyBlock();
            transform.localScale = Vector3.zero;
        }

        void LateUpdate()
        {
            elapsed += Time.unscaledDeltaTime;
            if (elapsed >= duration)
            {
                Destroy(gameObject);
                return;
            }

            // 톡 튀어나왔다가 끝에서 쪼그라든다 — 시작·해제가 둘 다 읽히게
            float scale = 1f;
            if (elapsed < PopIn) scale = Mathf.Lerp(0.2f, 1.15f, elapsed / PopIn);
            else if (elapsed > duration - PopOut) scale = Mathf.Lerp(1f, 0f, (elapsed - (duration - PopOut)) / PopOut);
            else scale = 1f + Mathf.Sin(Time.unscaledTime * 14f) * 0.04f; // 미세 맥동

            transform.localScale = Vector3.one * (scale * 1.5f);

            var c = color;
            c.a = color.a * Mathf.Clamp01(scale);
            mpb.SetColor("_Color", c);
            rend.SetPropertyBlock(mpb);
        }

        static Material Mat
        {
            get
            {
                if (domeMat == null) domeMat = new Material(Shader.Find("Sprites/Default"));
                return domeMat;
            }
        }
    }
}
