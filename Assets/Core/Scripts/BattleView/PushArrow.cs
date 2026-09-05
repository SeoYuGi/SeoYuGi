using System.Collections.Generic;
using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 그리드 위 밀침 화살표 — "이 공격이 맞으면 대상이 여기로 밀린다"를 바닥에 그린다.
    /// 연계(방밀 → 폭격)의 전제: 밀릴 자리가 미리 보여야 폭격 유닛이 그 자리를 선점할 수 있다.
    /// 절차 메시(자루 + 촉) 한 장. 매트한 알파 블렌드 — 발광은 정보를 묻는다.
    /// 벽꿍이면 촉 대신 충돌 표시(짧은 이중선)로 "여기서 막히고 추가 피해"를 알린다.
    /// </summary>
    public class PushArrow : MonoBehaviour
    {
        const float Height = 0.07f;   // 타일 위, 힐팩 마커(0.05)보다 살짝 위
        const float Shaft = 0.16f;    // 자루 두께 (월드)
        const float HeadLen = 0.34f;  // 촉 길이
        const float HeadWide = 0.42f; // 촉 폭

        Material mat;

        /// <summary>from→to 바닥 화살표. wallCrash면 촉 대신 충돌 표시. 반환 오브젝트는 호출부가 수명 관리.</summary>
        public static PushArrow Create(Transform parent, Vector3 from, Vector3 to, Color color, bool wallCrash)
        {
            var go = new GameObject("PushArrow");
            go.transform.SetParent(parent);
            var arrow = go.AddComponent<PushArrow>();
            arrow.Build(from, to, color, wallCrash);
            return arrow;
        }

        void Build(Vector3 from, Vector3 to, Color color, bool wallCrash)
        {
            var a = new Vector3(from.x, Mathf.Max(from.y, to.y) + Height, from.z);
            var b = new Vector3(to.x, a.y, to.z);
            var delta = b - a;
            float len = delta.magnitude;

            transform.position = a;
            if (len > 0.001f)
                transform.rotation = Quaternion.LookRotation(delta / len, Vector3.up);

            var mesh = wallCrash ? BuildCrash(len) : BuildArrow(len);
            gameObject.AddComponent<MeshFilter>().mesh = mesh;
            var rend = gameObject.AddComponent<MeshRenderer>();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            mat = new Material(Shader.Find("Sprites/Default")); // 알파 블렌드 — 매트
            mat.color = color;
            rend.material = mat;
        }

        /// <summary>로컬 +Z가 진행 방향. 자루 사각형 + 삼각 촉.</summary>
        static Mesh BuildArrow(float len)
        {
            float head = Mathf.Min(HeadLen, len * 0.55f);
            float shaftEnd = Mathf.Max(0f, len - head);
            float hw = Shaft * 0.5f;

            var v = new List<Vector3>
            {
                new Vector3(-hw, 0f, 0f), new Vector3(hw, 0f, 0f),
                new Vector3(hw, 0f, shaftEnd), new Vector3(-hw, 0f, shaftEnd),
                new Vector3(-HeadWide * 0.5f, 0f, shaftEnd),
                new Vector3(HeadWide * 0.5f, 0f, shaftEnd),
                new Vector3(0f, 0f, len),
            };
            var t = new List<int> { 0, 3, 1, 1, 3, 2, 4, 6, 5 };
            return Make(v, t);
        }

        /// <summary>벽꿍 — 진행 끝에 가로 이중선. "여기서 막힌다 + 추가 피해".</summary>
        static Mesh BuildCrash(float len)
        {
            float hw = Shaft * 0.5f;
            var v = new List<Vector3>
            {
                new Vector3(-hw, 0f, 0f), new Vector3(hw, 0f, 0f),
                new Vector3(hw, 0f, len), new Vector3(-hw, 0f, len),
            };
            var t = new List<int> { 0, 3, 1, 1, 3, 2 };
            AddBar(v, t, len + 0.06f, HeadWide * 0.62f, 0.05f);
            AddBar(v, t, len + 0.17f, HeadWide * 0.42f, 0.05f);
            return Make(v, t);
        }

        static void AddBar(List<Vector3> v, List<int> t, float z, float halfWide, float halfThick)
        {
            int b = v.Count;
            v.Add(new Vector3(-halfWide, 0f, z - halfThick));
            v.Add(new Vector3(halfWide, 0f, z - halfThick));
            v.Add(new Vector3(halfWide, 0f, z + halfThick));
            v.Add(new Vector3(-halfWide, 0f, z + halfThick));
            t.AddRange(new[] { b, b + 3, b + 1, b + 1, b + 3, b + 2 });
        }

        static Mesh Make(List<Vector3> verts, List<int> tris)
        {
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
        }
    }
}
