using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>타격·사망·방어 순간 칸 위 플래시. 알파 대신 스케일 축소 — 불투명 머티리얼로 충분.</summary>
    public class CellFlash : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        float duration;
        float elapsed;
        Vector3 startScale;

        public static void Spawn(Vector3 worldPos, Color color, float duration = 0.35f, float size = 0.85f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "CellFlash";
            Destroy(go.GetComponent<Collider>()); // 클릭 레이캐스트 방해 금지
            go.transform.position = worldPos + Vector3.up * 0.12f;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // 바닥에 눕힘
            go.transform.localScale = Vector3.one * size;

            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(BaseColorId, color);
            go.GetComponent<Renderer>().SetPropertyBlock(mpb);

            var f = go.AddComponent<CellFlash>();
            f.duration = duration;
            f.startScale = go.transform.localScale;
        }

        void Update()
        {
            elapsed += Time.deltaTime;
            if (elapsed >= duration) { Destroy(gameObject); return; }
            transform.localScale = startScale * (1f - elapsed / duration);
        }
    }
}
