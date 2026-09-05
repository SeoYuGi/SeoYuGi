using System.Collections.Generic;
using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 힐팩 픽업 연출 — Resources의 GLB(prop_heal_pack) 우선, 없으면 절차 생성 구급상자.
    /// 천천히 회전 + 둥실. 소모되면 숨고 리스폰 때 다시 나타난다(SetState).
    /// 바닥의 초록 링+십자 마커는 팩 유무와 무관하게 상시 표기 — 대기 중엔 흐릿해지고 남은 초를 띄운다.
    /// 콜라이더는 전부 제거 — 이동 클릭 레이캐스트를 막으면 안 된다.
    /// </summary>
    public class HealPackView : MonoBehaviour
    {
        const float SpinDegPerSec = 70f;
        const float BobHeight = 0.07f;
        const float TargetHeight = 0.35f;
        const float MarkerHeight = 0.05f;      // 타일 위, 거점 게이지(0.08) 아래
        const float CountdownHeight = 0.9f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        GameObject body;
        GameObject marker;                     // 바닥 마커 — 루트 회전/둥실 안 따라가게 형제로 둔다
        Material markerMat;
        TextMesh countdown;
        Vector3 basePos;
        bool available = true;

        public static HealPackView Create(Transform parent, Vector3 worldPos, float tileSize)
        {
            var go = new GameObject("HealPack");
            go.transform.SetParent(parent);
            go.transform.position = worldPos + Vector3.up * 0.22f;
            var view = go.AddComponent<HealPackView>();
            view.Build(tileSize);
            return view;
        }

        void Build(float tileSize)
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

            BuildMarker(tileSize);
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

        /// <summary>바닥 마커 — 초록 링 + 십자. 팩이 없어도 상시 표기(여기가 힐팩 자리라는 표식) + 리스폰 카운트다운.</summary>
        void BuildMarker(float tileSize)
        {
            marker = new GameObject("HealPackMarker");
            marker.transform.SetParent(transform.parent);
            marker.transform.position = new Vector3(basePos.x, basePos.y - 0.22f + MarkerHeight, basePos.z);

            marker.AddComponent<MeshFilter>().mesh = BuildMarkerMesh(tileSize);
            var rend = marker.AddComponent<MeshRenderer>();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            markerMat = new Material(Shader.Find("Sprites/Default")); // ZoneCaptureDisc와 동일 — URP 반투명
            rend.material = markerMat;

            var textGo = new GameObject("Countdown");
            textGo.transform.SetParent(marker.transform, false);
            textGo.transform.localPosition = Vector3.up * CountdownHeight;
            countdown = textGo.AddComponent<TextMesh>();
            countdown.text = "";
            countdown.fontSize = 56;
            countdown.characterSize = 0.05f;
            countdown.anchor = TextAnchor.MiddleCenter;
            countdown.alignment = TextAlignment.Center;
            GameFonts.Apply(countdown, GameFonts.HudHeavy);
        }

        /// <summary>XZ 평면 링(고리) + 십자 막대 2장을 한 메시로. 법선 +Y.</summary>
        static Mesh BuildMarkerMesh(float tileSize)
        {
            const int Seg = 48;
            float inner = tileSize * 0.36f, outer = tileSize * 0.46f;
            var verts = new List<Vector3>();
            var tris = new List<int>();

            for (int i = 0; i <= Seg; i++)
            {
                float a = i / (float)Seg * Mathf.PI * 2f;
                float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                verts.Add(new Vector3(cos * inner, 0f, sin * inner));
                verts.Add(new Vector3(cos * outer, 0f, sin * outer));
            }
            for (int i = 0; i < Seg; i++)
            {
                int b = i * 2;
                tris.Add(b); tris.Add(b + 3); tris.Add(b + 1);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }

            float arm = tileSize * 0.22f, thick = tileSize * 0.055f;
            AddQuad(verts, tris, arm, thick);   // 가로 막대
            AddQuad(verts, tris, thick, arm);   // 세로 막대

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        static void AddQuad(List<Vector3> verts, List<int> tris, float halfX, float halfZ)
        {
            int b = verts.Count;
            verts.Add(new Vector3(-halfX, 0f, -halfZ));
            verts.Add(new Vector3( halfX, 0f, -halfZ));
            verts.Add(new Vector3( halfX, 0f,  halfZ));
            verts.Add(new Vector3(-halfX, 0f,  halfZ));
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
            tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
        }

        /// <summary>매 프레임 호출. secondsLeft = 리스폰까지 남은 초(팩이 있으면 무시).</summary>
        public void SetState(bool value, float secondsLeft)
        {
            if (available != value)
            {
                available = value;
                if (body != null) body.SetActive(value);
            }

            if (available)
            {
                float pulse = 0.68f + Mathf.Sin(Time.time * 2.4f) * 0.12f;  // 은은한 맥동 — 시선만 끈다
                markerMat.color = new Color(0.25f, 0.95f, 0.45f, pulse);
                if (countdown.text.Length > 0) countdown.text = "";
            }
            else
            {
                markerMat.color = new Color(0.25f, 0.6f, 0.35f, 0.3f);      // 대기 중 = 흐릿
                var t = Mathf.Max(0, Mathf.CeilToInt(secondsLeft)).ToString();
                if (countdown.text != t) countdown.text = t;
                countdown.color = new Color(0.5f, 0.85f, 0.6f, 0.85f);
            }
        }

        void Update()
        {
            if (!available && Camera.main != null)
                countdown.transform.rotation = Camera.main.transform.rotation; // 카운트다운 빌보드

            if (!available) return;
            transform.Rotate(0f, SpinDegPerSec * Time.deltaTime, 0f);
            var p = basePos;
            p.y += Mathf.Sin(Time.time * 2.2f) * BobHeight;
            transform.position = p;
        }

        void OnDestroy()
        {
            if (marker != null) Destroy(marker); // 형제라 루트 파괴에 안 딸려간다
        }
    }
}
