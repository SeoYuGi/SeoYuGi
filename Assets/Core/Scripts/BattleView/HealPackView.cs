using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 힐팩 픽업 연출 — Resources의 GLB(prop_heal_pack) 우선, 없으면 절차 생성 구급상자.
    /// 천천히 회전 + 둥실. 소모되면 숨고 리스폰 때 다시 나타난다(SetAvailable).
    /// 콜라이더는 전부 제거 — 이동 클릭 레이캐스트를 막으면 안 된다.
    /// </summary>
    public class HealPackView : MonoBehaviour
    {
        const float SpinDegPerSec = 70f;
        const float BobHeight = 0.07f;
        const float TargetHeight = 0.35f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        GameObject body;
        Vector3 basePos;
        bool available = true;

        public static HealPackView Create(Transform parent, Vector3 worldPos)
        {
            var go = new GameObject("HealPack");
            go.transform.SetParent(parent);
            go.transform.position = worldPos + Vector3.up * 0.22f;
            var view = go.AddComponent<HealPackView>();
            view.Build();
            return view;
        }

        void Build()
        {
            basePos = transform.position;

            var prefab = Resources.Load<GameObject>("prop_heal_pack");
            if (prefab != null)
            {
                body = Instantiate(prefab, transform);
                FitHeight(body, TargetHeight);
            }
            else
            {
                body = BuildFallback();
            }

            foreach (var col in GetComponentsInChildren<Collider>(true))
                Destroy(col);
        }

        /// <summary>폴백: 흰 상자 + 초록 십자 — 모델 미도착 시에도 즉시 읽히게.</summary>
        GameObject BuildFallback()
        {
            var root = new GameObject("Fallback");
            root.transform.SetParent(transform, false);

            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.SetParent(root.transform, false);
            box.transform.localScale = new Vector3(0.3f, 0.18f, 0.3f);
            Tint(box, Color.white);

            var crossA = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crossA.transform.SetParent(root.transform, false);
            crossA.transform.localPosition = Vector3.up * 0.095f;
            crossA.transform.localScale = new Vector3(0.2f, 0.02f, 0.07f);
            Tint(crossA, new Color(0.15f, 0.85f, 0.35f));

            var crossB = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crossB.transform.SetParent(root.transform, false);
            crossB.transform.localPosition = Vector3.up * 0.095f;
            crossB.transform.localScale = new Vector3(0.07f, 0.02f, 0.2f);
            Tint(crossB, new Color(0.15f, 0.85f, 0.35f));

            return root;
        }

        static void Tint(GameObject go, Color color)
        {
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(BaseColorId, color);
            go.GetComponent<Renderer>().SetPropertyBlock(mpb);
        }

        /// <summary>UnitSkinApplier.FitToUnit과 같은 바운즈 정규화 — 모델 크기와 무관하게 목표 높이로.</summary>
        static void FitHeight(GameObject go, float targetHeight)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            if (b.size.y > 0.001f)
                go.transform.localScale *= targetHeight / b.size.y;

            b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            var anchor = go.transform.parent.position; // HealPackView 루트 = 칸 위 부양 지점
            var pos = go.transform.position;
            pos.x += anchor.x - b.center.x;
            pos.z += anchor.z - b.center.z;
            pos.y += anchor.y - b.center.y;
            go.transform.position = pos;
        }

        public void SetAvailable(bool value)
        {
            if (available == value) return;
            available = value;
            if (body != null) body.SetActive(value);
        }

        void Update()
        {
            if (!available) return;
            transform.Rotate(0f, SpinDegPerSec * Time.deltaTime, 0f);
            var p = basePos;
            p.y += Mathf.Sin(Time.time * 2.2f) * BobHeight;
            transform.position = p;
        }
    }
}
