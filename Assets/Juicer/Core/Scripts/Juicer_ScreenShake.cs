using UnityEngine;

namespace Juicer
{
    public class Juicer_ScreenShake : MonoBehaviour
    {
        public enum ShakeType
        {
            Rigid = 0,
            Smooth = 1
        }

        [SerializeField]
        [Tooltip("Applies shake to the X axis.")]
        private bool affectX = true;

        [SerializeField]
        [Tooltip("Applies shake to the Y axis.")]
        private bool affectY = true;

        [SerializeField]
        [Tooltip("Method of applying shake.\n\n<b>Rigid:</b> Set position to a random offset each frame.\n\n<b>Smooth:</b> Lerp between random offsets over time.")]
        private ShakeType shakeType = ShakeType.Rigid;
        private ShakeType currentShakeType;

        [SerializeField]
        [Tooltip("Speed at which a smooth shake will move at.")]
        private float smoothShakeRate = 3;

        private float shakeForce;
        private float shakeRate;

        private Vector3 startPos;
        private Vector3 smoothTargetOffset;

        void Start ()
        {
            startPos = transform.localPosition;
        }

        void Update ()
        {
            if(shakeForce <= 0)
                return;

            shakeForce = Mathf.MoveTowards(shakeForce, 0.0f, shakeRate * Time.deltaTime);

            if(currentShakeType == ShakeType.Smooth)
            {
                if(transform.localPosition != startPos + smoothTargetOffset)
                {
                    transform.localPosition = Vector3.MoveTowards(transform.localPosition, startPos + smoothTargetOffset, Time.deltaTime * smoothShakeRate);
                }
                else
                {
                    smoothTargetOffset = GetRandomOffset(shakeForce);
                }

                if(shakeForce == 0)
                    transform.localPosition = startPos;
            }
            else if(currentShakeType == ShakeType.Rigid)
            {
                Vector3 offset = GetRandomOffset(shakeForce);
                transform.localPosition = startPos + offset;
            }
        }

        /// <summary>
        /// Shake the camera by amount for duration seconds.
        /// </summary>
        public void Shake (float amount, float duration)
        {
            Shake(amount, duration, shakeType);
        }

        /// <summary>
        /// Shake the camera by amount for duration seconds, overriding the shake type.
        /// </summary>
        public void Shake (float amount, float duration, ShakeType shakeType)
        {
            shakeForce = amount;
            shakeRate = amount / duration;

            currentShakeType = shakeType;

            if(currentShakeType == ShakeType.Smooth)
                smoothTargetOffset = GetRandomOffset(shakeForce);
        }

        Vector3 GetRandomOffset (float magnitude)
        {
            float x = affectX ? Random.Range(-magnitude, magnitude) : 0;
            float y = affectY ? Random.Range(-magnitude, magnitude) : 0;

            return new Vector3(x, y, 0);
        }
    }
}