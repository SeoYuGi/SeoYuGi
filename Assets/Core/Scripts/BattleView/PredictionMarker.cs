using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 적 AI의 "내 다음 칸 예측"을 바닥에 상시 표시 — 보라 링 + "예측" 라벨.
    /// "AI가 나를 학습한다"가 말이 아니라 눈에 보이게: 이 칸을 피해 가면 예측을 배신하는 것이고,
    /// 이 칸으로 가면 예측 사격을 맞는다. 학습 루프를 플레이어의 선택으로 바꾸는 장치 (2026-09-05).
    /// 보라 = 예측 색 (예측 사격 링·기둥과 동일 언어).
    /// </summary>
    public class PredictionMarker : MonoBehaviour
    {
        static readonly Color Purple = new Color(0.75f, 0.4f, 1f, 0.9f);

        Renderer ring;
        MaterialPropertyBlock mpb;
        TextMesh label;
        Vector3 targetPos;
        bool shown;

        public static PredictionMarker Create(Transform parent)
        {
            var go = new GameObject("PredictionMarker");
            go.transform.SetParent(parent, false);
            var m = go.AddComponent<PredictionMarker>();
            m.Build();
            go.SetActive(false);
            return m;
        }

        void Build()
        {
            var ringGo = new GameObject("Ring");
            ringGo.transform.SetParent(transform, false);
            ringGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var mat = VfxTextures.Ring;
            if (mat != null)
            {
                ringGo.AddComponent<MeshFilter>().sharedMesh = RingWave.QuadMesh;
                ring = ringGo.AddComponent<MeshRenderer>();
                ring.sharedMaterial = mat;
            }
            else
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(ringGo.transform, false);
                ring = q.GetComponent<Renderer>();
            }
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            mpb = new MaterialPropertyBlock();

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(transform, false);
            textGo.transform.localPosition = Vector3.up * 0.55f;
            label = textGo.AddComponent<TextMesh>();
            label.text = "예측";
            label.fontSize = 56;
            label.characterSize = 0.05f;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = Purple;
            label.fontStyle = FontStyle.Bold;
            var lr = textGo.GetComponent<MeshRenderer>();
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
        }

        /// <summary>예측 칸 갱신 — 칸이 바뀌면 부드럽게 미끄러진다 (툭툭 튀면 소음).</summary>
        public void Show(Vector3 worldPos)
        {
            targetPos = worldPos + Vector3.up * 0.1f;
            if (!shown)
            {
                shown = true;
                transform.position = targetPos;
                gameObject.SetActive(true);
            }
        }

        public void Hide()
        {
            if (!shown) return;
            shown = false;
            gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            transform.position = Vector3.Lerp(transform.position, targetPos, 1f - Mathf.Exp(-12f * Time.deltaTime));

            // 숨쉬는 링 — 조여들었다 풀리기를 반복 (레티클 언어)
            float k = Mathf.PingPong(Time.time * 1.6f, 1f);
            float radius = Mathf.Lerp(0.72f, 0.58f, k);
            ring.transform.localScale = new Vector3(radius, radius, 1f);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, Time.time * 40f);
            var c = Purple * Mathf.Lerp(0.7f, 1f, k);
            c.a = Purple.a;
            mpb.SetColor("_Color", c);
            mpb.SetColor("_BaseColor", c);
            ring.SetPropertyBlock(mpb);

            // 라벨은 카메라를 본다
            var cam = Camera.main;
            if (cam != null) label.transform.rotation = cam.transform.rotation;
        }
    }
}
