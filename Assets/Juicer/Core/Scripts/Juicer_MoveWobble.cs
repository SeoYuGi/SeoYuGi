using UnityEngine;

namespace Juicer
{
    public class Juicer_MoveWobble : MonoBehaviour
    {
        [Tooltip("Speed of rotation.")]
        public float Frequency = 15.0f;

        [Tooltip("Amount of rotation.")]
        public float Amplitude = 4.0f;

        [Tooltip("Minimum velocity required to begin wobble animation.")]
        public float Threshold = 0.1f;

        [Tooltip("Lerp strength applied to rotation. Keep this at around 25. Any lower and it will look lazier.")]
        public float Smoothing = 25.0f;

        private Vector3 velocity;
        private Vector3 lastFramePos;
        private float randomOffset;

        void Start ()
        {
            lastFramePos = transform.position;
            randomOffset = Random.value;
        }

        void Update ()
        {
            AnimateWobble();
        }

        void FixedUpdate ()
        {
            velocity = (transform.position - lastFramePos) / Time.deltaTime;
            lastFramePos = transform.position;
        }

        void AnimateWobble ()
        {
            float time = (Time.time + randomOffset) * Frequency;
            float wobbleRot = Mathf.Sin(time) * Amplitude;
            float targetRot = velocity.magnitude > Threshold ? wobbleRot : 0;
            float curRot = Mathf.LerpAngle(transform.localEulerAngles.z, targetRot, Time.deltaTime * Smoothing);

            transform.localEulerAngles = new Vector3(transform.localEulerAngles.x, transform.localEulerAngles.y, curRot);
        }
    }
}