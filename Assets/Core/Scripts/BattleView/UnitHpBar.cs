using SeoYuGi.Battle;
using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 유닛 머리 위 콜사인 + HP 핍 (임시 — 아트 HUD 붙으면 교체). 코드 생성 쿼드 + 카메라 빌보드.
    /// </summary>
    public class UnitHpBar : MonoBehaviour
    {
        const float PipSize = 0.14f;
        const float PipGap = 0.17f;
        const float Height = 1.05f; // 유닛 위 높이

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Color FullColor = new Color(0.3f, 0.9f, 0.4f);
        static readonly Color EmptyColor = new Color(0.13f, 0.13f, 0.16f);

        UnitState unit;
        Transform follow;
        Renderer[] pips;
        MaterialPropertyBlock mpb;
        Camera cam;

        public static UnitHpBar Create(Transform parent, UnitState unit, Transform follow,
            string displayName = null, Color nameColor = default)
        {
            var go = new GameObject($"HpBar_{unit.id}");
            go.transform.SetParent(parent);
            var bar = go.AddComponent<UnitHpBar>();
            bar.unit = unit;
            bar.follow = follow;
            bar.Build();
            if (!string.IsNullOrEmpty(displayName))
                bar.BuildName(displayName, nameColor == default ? Color.white : nameColor);
            return bar;
        }

        void BuildName(string displayName, Color color)
        {
            var go = new GameObject("Name");
            go.transform.SetParent(transform);
            go.transform.localPosition = new Vector3(0f, PipSize * 0.8f, 0f);
            var tm = go.AddComponent<TextMesh>();
            tm.text = displayName;
            tm.fontSize = 48;              // 큰 폰트 + 작은 characterSize = 선명
            tm.characterSize = 0.045f;
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.Lerp(color, Color.white, 0.35f);
        }

        void Build()
        {
            mpb = new MaterialPropertyBlock();
            cam = Camera.main;
            pips = new Renderer[unit.maxHp];
            for (int i = 0; i < unit.maxHp; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(quad.GetComponent<Collider>()); // 클릭 레이캐스트 방해 금지
                quad.transform.SetParent(transform);
                quad.transform.localPosition = new Vector3((i - (unit.maxHp - 1) * 0.5f) * PipGap, 0f, 0f);
                quad.transform.localScale = new Vector3(PipSize, PipSize, 1f);
                pips[i] = quad.GetComponent<Renderer>();
            }
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

            for (int i = 0; i < pips.Length; i++)
            {
                mpb.SetColor(BaseColorId, i < unit.hp ? FullColor : EmptyColor);
                pips[i].SetPropertyBlock(mpb);
            }
        }
    }
}
