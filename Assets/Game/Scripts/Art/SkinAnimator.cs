using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using SeoYuGi.BattleView;

namespace SeoYuGi.Art
{
    /// <summary>
    /// 스킨 모델의 대기↔달리기 애니메이션 크로스페이드.
    /// AnimatorController 없이 Playables로 직접 재생 — 클립은 GLB 서브에셋에서 온다.
    /// UnitView.IsMoving을 보고 자동 전환. run이 없으면 idle만 루프.
    /// </summary>
    public class SkinAnimator : MonoBehaviour
    {
        const float BlendSpeed = 8f;

        UnitView view;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        bool hasRun;
        float blend;

        public void Init(UnitView view, AnimationClip idle, AnimationClip run)
        {
            this.view = view;
            hasRun = run != null;

            var animator = GetComponent<Animator>();
            if (animator == null) animator = gameObject.AddComponent<Animator>();

            graph = PlayableGraph.Create("SkinAnimator");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            mixer = AnimationMixerPlayable.Create(graph, 2);

            var idlePlayable = AnimationClipPlayable.Create(graph, idle);
            graph.Connect(idlePlayable, 0, mixer, 0);
            if (hasRun)
            {
                var runPlayable = AnimationClipPlayable.Create(graph, run);
                graph.Connect(runPlayable, 0, mixer, 1);
            }
            mixer.SetInputWeight(0, 1f);

            var output = AnimationPlayableOutput.Create(graph, "out", animator);
            output.SetSourcePlayable(mixer);
            graph.Play();
        }

        void Update()
        {
            if (!hasRun || !graph.IsValid()) return;
            float target = view != null && view.IsMoving ? 1f : 0f;
            blend = Mathf.MoveTowards(blend, target, Time.deltaTime * BlendSpeed);
            mixer.SetInputWeight(0, 1f - blend);
            mixer.SetInputWeight(1, blend);
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
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
