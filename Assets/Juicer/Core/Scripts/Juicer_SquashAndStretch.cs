using UnityEngine;
using System.Collections;

namespace Juicer
{
    public class Juicer_SquashAndStretch : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The Transform to apply the squash and stretch to.")]
        private Transform affect;

        [SerializeField]
        [Tooltip("Max scale offset.")]
        private float maxStretch = 1.2f;

        [SerializeField]
        [Tooltip("Total duration of the squash/stretch animation.")]
        private float duration = 0.35f;

        [Tooltip("Determines the rate of change for the squash/stretch.")]
        [SerializeField]
        private AnimationCurve animateCurve = new AnimationCurve(new Keyframe[]{
            new Keyframe(0, 0, 9.25f, 9.25f, 0, 0.053f),
            new Keyframe(0.2f, 1, 0, 0, 0.3333f, 0.3333f),
            new Keyframe(1, 0, 0, 0)
        });

        private Vector3 startScale;
        private Coroutine playCoroutine;

        void Start ()
        {
            if(affect == null)
            {
                affect = transform;
            }

            startScale = affect.transform.localScale;
        }

        void Update ()
        {
            if(Input.GetKeyDown(KeyCode.Alpha1))
                Squash();

            if(Input.GetKeyDown(KeyCode.Alpha2))
                Squash(0.5f, 2.0f);

            if(Input.GetKeyDown(KeyCode.Alpha3))
                Stretch();

            if(Input.GetKeyDown(KeyCode.Alpha4))
                Stretch(0.5f, 2.0f);
        }

        /// <summary>
        /// Squash the transform based on the component's properties.
        /// </summary>
        public void Squash ()
        {
            Squash(duration, maxStretch);
        }

        /// <summary>
        /// Squash the transform and override the component's properties.
        /// </summary>
        public void Squash (float duration, float maxStretch)
        {
            TryPlay(duration, maxStretch, true, false);
        }

        /// <summary>
        /// Stretch the transform based on the component's properties.
        /// </summary>
        public void Stretch ()
        {
            TryPlay(duration, maxStretch, false, true);
        }

        /// <summary>
        /// Stretch the transform and override the component's properties.
        /// </summary>
        public void Stretch (float duration, float maxStretch)
        {
            TryPlay(duration, maxStretch, false, true);
        }

        void TryPlay (float duration, float maxStretch, bool affectX, bool affectY)
        {
            if(playCoroutine != null)
                StopCoroutine(playCoroutine);

            playCoroutine = StartCoroutine(Play(duration, maxStretch, affectX, affectY));
        }

        IEnumerator Play (float duration, float maxStretch, bool affectX, bool affectY)
        {
            float time = 0.0f;
            
            while(time < duration)
            {
                time += Time.deltaTime;

                float value = animateCurve.Evaluate(time / duration);
                float trueValue = 1.0f + (value * (maxStretch - 1.0f));

                if(Mathf.Abs(trueValue) < 0.001f)
                    trueValue = 0.001f;

                Vector3 s = Vector3.one;

                if(affectX)
                    s.x = startScale.x * trueValue;
                else
                    s.x = startScale.x / trueValue;

                if(affectY)
                    s.y = startScale.y * trueValue;
                else
                    s.y = startScale.y / trueValue;

                affect.localScale = s;

                yield return null;
            }
        }
    }
}