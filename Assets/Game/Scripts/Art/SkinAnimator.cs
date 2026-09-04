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

        public void Init(AnimationClip clip)
        {
            var animator = GetComponent<Animator>();
            if (animator == null) animator = gameObject.AddComponent<Animator>();

            length = clip.length;
            graph = PlayableGraph.Create("SkinLoop");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            playable = AnimationClipPlayable.Create(graph, clip);
            var output = AnimationPlayableOutput.Create(graph, "out", animator);
            output.SetSourcePlayable(playable);
            graph.Play();
        }

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
    /// 대기(정적 모델) ↔ 이동(달리기 애니 모델) 스왑.
    /// 대기 애니 없이도 살아 보이게 — 이동 시작 순간에 바꿔서 전환이 안 튄다.
    /// </summary>
    public class MoveSwapSkin : MonoBehaviour
    {
        UnitView view;
        GameObject idleGo;
        GameObject runGo;
        bool moving;

        public void Init(UnitView view, GameObject idleGo, GameObject runGo)
        {
            this.view = view;
            this.idleGo = idleGo;
            this.runGo = runGo;
            runGo.SetActive(false);
        }

        void Update()
        {
            bool now = view != null && view.IsMoving;
            if (now == moving) return;
            moving = now;
            idleGo.SetActive(!moving);
            runGo.SetActive(moving);
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
                target.localScale = Vector3.Lerp(target.localScale, baseScale, Time.deltaTime * 10f);
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
