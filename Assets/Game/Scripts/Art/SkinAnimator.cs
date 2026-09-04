using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using SeoYuGi.BattleView;

namespace SeoYuGi.Art
{
    /// <summary>
    /// 클립 하나를 무한 루프 재생 (AnimatorController 불필요, GLB 서브에셋 클립용).
    /// </summary>
    public class SkinLoopAnimator : MonoBehaviour
    {
        PlayableGraph graph;
        AnimationClipPlayable playable;
        float length;

        /// <param name="speed">클래스별 성격 부여용 재생 배속 (묵직=느리게, 날렵=빠르게)</param>
        public void Init(AnimationClip clip, float speed = 1f)
        {
            var animator = GetComponent<Animator>();
            if (animator == null) animator = gameObject.AddComponent<Animator>();

            length = clip.length;
            graph = PlayableGraph.Create("SkinLoop");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetSpeed(speed);
            playable.SetTime(Random.Range(0f, length)); // 유닛끼리 군무 방지 — 시작점 흩뜨리기
            var output = AnimationPlayableOutput.Create(graph, "out", animator);
            output.SetSourcePlayable(playable);
            graph.Play();
        }

        /// <summary>처음부터 재생 (공격 원샷용).</summary>
        public void Restart()
        {
            if (graph.IsValid()) playable.SetTime(0);
        }

        public float ClipLength => length;

        void Update()
        {
            // 임포트 클립의 랩 모드에 기대지 않고 수동 루프
            if (graph.IsValid() && length > 0f && playable.GetTime() >= length)
                playable.SetTime(playable.GetTime() % length);
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }
    }

    /// <summary>
    /// 대기 ↔ 이동 ↔ 공격 모델 스왑 상태기.
    /// 공격은 PlayAttack() 트리거로 클립 길이만큼 노출 후 복귀 (원샷).
    /// 이동·공격 모델이 없으면 각각 대기 모델로 폴백.
    /// </summary>
    public class MoveSwapSkin : MonoBehaviour
    {
        UnitView view;
        GameObject idleGo;
        GameObject runGo;
        GameObject atkGo;
        SkinLoopAnimator atkAnim;
        float attackUntil = -1f;
        GameObject shown;

        public void Init(UnitView view, GameObject idleGo, GameObject runGo,
            GameObject atkGo = null, SkinLoopAnimator atkAnim = null)
        {
            this.view = view;
            this.idleGo = idleGo;
            this.runGo = runGo;
            this.atkGo = atkGo;
            this.atkAnim = atkAnim;
            if (runGo != null) runGo.SetActive(false);
            if (atkGo != null) atkGo.SetActive(false);
            shown = idleGo;
        }

        /// <summary>공격·스킬 캐스팅 순간 호출 — 공격 모션 원샷.</summary>
        /// <param name="maxSeconds">노출 상한 — 긴 클립은 앞부분만 (까치 사격은 뽑는 동작까지 길게)</param>
        public void PlayAttack(float maxSeconds = 1.2f)
        {
            if (atkGo == null) return;
            attackUntil = Time.time + (atkAnim != null ? Mathf.Min(atkAnim.ClipLength, maxSeconds) : 0.8f);
            atkAnim?.Restart();
        }

        void Update()
        {
            GameObject want;
            if (atkGo != null && Time.time < attackUntil) want = atkGo;
            else if (runGo != null && view != null && view.IsMoving) want = runGo;
            else want = idleGo;

            if (want == shown) return;
            if (shown != null) shown.SetActive(false);
            shown = want;
            if (shown != null) shown.SetActive(true);
        }
    }

    /// <summary>
    /// 손 본에 절차 생성 라이플 부착 — 까치 사격 모션이 맨손이라(총이 등 메시에 박힘) 소품으로 보정.
    /// 쿼터뷰 줌 기준의 실루엣용 프리미티브 — 근사치면 충분하다.
    /// </summary>
    public static class HandRifle
    {
        public static void Attach(GameObject model, float worldLength)
        {
            var hand = FindDeep(model.transform, "RightHand");
            if (hand == null) return;

            var root = new GameObject("HandRifle");
            root.transform.SetParent(hand, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.16f, 0.22f, 1f);

            var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(barrel.GetComponent<Collider>());
            barrel.transform.SetParent(root.transform, false);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            barrel.transform.localPosition = new Vector3(0f, 0.06f, 0.62f);
            barrel.transform.localScale = new Vector3(0.07f, 0.35f, 0.07f);

            var dark = new Color(0.14f, 0.15f, 0.18f);
            var teal = new Color(0.12f, 0.48f, 0.55f);
            Tint(body, dark);
            Tint(barrel, teal);

            // 손 본 로컬 공간은 모델 스케일을 승계 — 월드 길이 기준으로 정규화
            float current = root.transform.lossyScale.z;
            if (current > 0.0001f)
                root.transform.localScale = Vector3.one * (worldLength / current);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
        }

        static void Tint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", c);
            r.SetPropertyBlock(mpb);
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var f = FindDeep(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }
    }

    /// <summary>
    /// 리깅이 불가능한 캐릭터(비둘기)용 절차적 이동 연출 —
    /// 빠른 스쿼시&스트레치 + 뒤뚱 롤로 "파닥이며 통통 뛰는" 느낌을 낸다.
    /// </summary>
    public class WaddleBounce : MonoBehaviour
    {
        const float Frequency = 14f;   // 파닥 주기
        const float Squash = 0.12f;    // 위아래 찌그러짐 폭
        const float RollDeg = 6f;      // 뒤뚱 좌우 롤

        UnitView view;
        Transform target;
        Vector3 baseScale;
        Quaternion baseRot;
        float t;

        public void Init(UnitView view, GameObject targetGo)
        {
            this.view = view;
            target = targetGo.transform;
            baseScale = target.localScale;
            baseRot = target.localRotation;
        }

        void Update()
        {
            if (target == null) return;
            bool moving = view != null && view.IsMoving;
            if (!moving)
            {
                // 대기 숨쉬기 — 느린 미세 팽창/수축으로 뻣뻣함 제거
                float bt = Time.time * 2.2f;
                float breath = 1f + Mathf.Sin(bt) * 0.02f;
                var idleScale = new Vector3(baseScale.x, baseScale.y * breath, baseScale.z);
                target.localScale = Vector3.Lerp(target.localScale, idleScale, Time.deltaTime * 10f);
                target.localRotation = Quaternion.Slerp(target.localRotation, baseRot, Time.deltaTime * 10f);
                return;
            }

            t += Time.deltaTime * Frequency;
            float s = Mathf.Sin(t);
            float squash = 1f + s * Squash;
            float side = 1f / Mathf.Sqrt(squash); // 부피 보존 — 눌리면 옆으로 퍼짐
            target.localScale = new Vector3(baseScale.x * side, baseScale.y * squash, baseScale.z * side);
            target.localRotation = baseRot * Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.5f) * RollDeg);
        }
    }

    /// <summary>드론류 기계의 공중 부유 연출 — 사인파 상하 + 미세 기울기.</summary>
    public class HoverBob : MonoBehaviour
    {
        [SerializeField] float amplitude = 0.08f;
        [SerializeField] float frequency = 1.6f;

        Vector3 basePos;
        float seed;

        void Start()
        {
            basePos = transform.localPosition;
            seed = Random.Range(0f, 10f);
        }

        void Update()
        {
            float t = Time.time * frequency + seed;
            var p = basePos;
            p.y += Mathf.Sin(t) * amplitude;
            transform.localPosition = p;
            transform.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.7f) * 2f, 0f, Mathf.Cos(t * 0.9f) * 2f);
        }
    }
}
