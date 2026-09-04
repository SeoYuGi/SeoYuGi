using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 이동기 잔상 — 유닛 메시를 그 자리에 구워 두고 빠르게 페이드.
    /// 고라니 돌파(슈슈슉 관통감), 검은냥 도약(검은 그림자)의 시각 언어.
    /// 스킨드 메시는 현재 포즈로 베이크, 일반 메시는 공유 메시 재사용.
    /// </summary>
    public static class GhostTrail
    {
        /// <summary>from→to 경로 위에 잔상 count개 — 목적지에 가까울수록 늦게 사라진다.</summary>
        public static void Spawn(GameObject source, Vector3 from, Vector3 to, int count, Color tint)
        {
            if (source == null || count <= 0) return;
            for (int i = 0; i < count; i++)
            {
                float k = (i + 1) / (float)(count + 1);
                var pos = Vector3.Lerp(from, to, k);
                float life = 0.22f + k * 0.18f; // 뒤 잔상이 먼저 사라짐 — 진행감
                SpawnOne(source, pos - source.transform.position, tint, life);
            }
        }

        /// <summary>제자리 잔상 1개 (점멸 출발지 등).</summary>
        public static void SpawnAt(GameObject source, Vector3 pos, Color tint, float life = 0.35f)
        {
            if (source == null) return;
            SpawnOne(source, pos - source.transform.position, tint, life);
        }

        static void SpawnOne(GameObject source, Vector3 offset, Color tint, float life)
        {
            var root = new GameObject("Ghost");
            root.transform.position = source.transform.position + offset;
            var fade = root.AddComponent<GhostFade>();
            fade.tint = tint;
            fade.life = life;

            foreach (var smr in source.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var baked = new Mesh();
                smr.BakeMesh(baked);
                AddPiece(root, fade, baked, smr.transform, offset, ownsMesh: true);
            }
            foreach (var mf in source.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || mf.GetComponent<Renderer>() == null) continue;
                AddPiece(root, fade, mf.sharedMesh, mf.transform, offset, ownsMesh: false);
                var piece = root.transform.GetChild(root.transform.childCount - 1);
                piece.localScale = mf.transform.lossyScale; // 공유 메시 — 스케일 수동 반영
            }

            if (root.transform.childCount == 0) Object.Destroy(root); // 렌더러 없는 유닛 — 조용히 생략
        }

        static void AddPiece(GameObject root, GhostFade fade, Mesh mesh, Transform src, Vector3 offset, bool ownsMesh)
        {
            var go = new GameObject("GhostPiece");
            go.transform.SetParent(root.transform, worldPositionStays: false);
            go.transform.position = src.position + offset;
            go.transform.rotation = src.rotation; // BakeMesh는 스케일을 정점에 굽는다

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = GhostFade.SharedMaterial;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            fade.Register(rend, ownsMesh ? mesh : null);
        }
    }

    /// <summary>잔상 페이드 — MPB로 알파만 줄인다. 구운 메시는 소멸 시 함께 파괴.</summary>
    public class GhostFade : MonoBehaviour
    {
        public Color tint = new Color(0.2f, 0.1f, 0.35f);
        public float life = 0.3f;

        static Material sharedMat;

        public static Material SharedMaterial
        {
            get
            {
                if (sharedMat != null) return sharedMat;
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                sharedMat = new Material(shader);
                sharedMat.SetFloat("_Surface", 1f);
                sharedMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                sharedMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                sharedMat.SetInt("_ZWrite", 0);
                sharedMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                return sharedMat;
            }
        }

        readonly System.Collections.Generic.List<Renderer> pieces = new System.Collections.Generic.List<Renderer>();
        readonly System.Collections.Generic.List<Mesh> ownedMeshes = new System.Collections.Generic.List<Mesh>();
        MaterialPropertyBlock mpb;
        float t;

        public void Register(Renderer rend, Mesh ownedMesh)
        {
            pieces.Add(rend);
            if (ownedMesh != null) ownedMeshes.Add(ownedMesh);
        }

        void LateUpdate()
        {
            t += Time.deltaTime;
            float k = t / life;
            if (k >= 1f)
            {
                foreach (var m in ownedMeshes) Destroy(m);
                Destroy(gameObject);
                return;
            }

            if (mpb == null) mpb = new MaterialPropertyBlock();
            var c = tint;
            c.a = tint.a * (1f - k * k);
            mpb.SetColor("_BaseColor", c);
            mpb.SetColor("_Color", c);
            foreach (var r in pieces) r.SetPropertyBlock(mpb);
        }
    }
}
