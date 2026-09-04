using UnityEngine;

namespace Juicer
{
    public class Juicer_Bobbing : MonoBehaviour
    {
        public enum BobType
        {
            Smooth = 0,
            Custom = 1
        }

        public enum FakeRotationType
        {
            Smooth = 0,
            Linear = 1
        }

        public enum TimeScale
        {
            Scaled = 0,
            Unscaled = 1
        }

        [Tooltip("Speed at which the object will move over time.")]
        public float BobSpeed = 3.0f;

        [Tooltip("The offset from its starting position the object will move to.")]
        public Vector2 BobTargetOffset = new Vector2(0, 0.1f);

        [Tooltip("Determines whether the bobbing will run based on scaled or unscaled time.\n\n<b>Scaled:</b> Moves based on Time.time.\n\n<b>Unscaled:</b> Moves based on Time.unscaledTime (useful if you want the bobbing to occur when the game is paused).")]
        public TimeScale CurrentTimeScale = TimeScale.Scaled;

        [Tooltip("Changes how the object moves.\n\n<b>Smooth:</b> Ease in and out with a sin wave.\n\n<b>Custom:</b> Use an AnimationCurve to determine motion over time.")]
        public BobType CurrentBobType = BobType.Smooth;

        [Tooltip("Rate of bob motion over time. Automatically ping pongs so it will loop.")]
        public AnimationCurve CustomBobCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Tooltip("Apply looping to the object's X scale, simulating Y axis rotation.")]
        public bool EnableFakeRotation = false;

        [Tooltip("Speed at which it will simulate the fake rotation.")]
        public float FakeRotationSpeed = 2.0f;

        [Tooltip("Changes how the rotation looks.\n\n<b>Smooth:</b> Ease in and out with a sin wave.\n\n<b>Linear:</b> Rotate linearly using Mathf.PingPong.")]
        public FakeRotationType CurrentFakeRotationType = FakeRotationType.Smooth;

        private Vector2 startPosition;
        private Vector3 startScale;
        private float timeOffset = 0.0f;

        private float time => (timeOffset - (CurrentTimeScale == TimeScale.Scaled ? Time.time : Time.unscaledTime));

        void Start ()
        {
            startPosition = transform.localPosition;
            startScale = transform.localScale;

            timeOffset = CurrentTimeScale == TimeScale.Scaled ? Time.time : Time.unscaledTime;

            CustomBobCurve.preWrapMode = WrapMode.PingPong;
            CustomBobCurve.postWrapMode = WrapMode.PingPong;
        }

        void Update ()
        {
            Bobbing();

            if(EnableFakeRotation)
                FakeRotation();
        }

        void Bobbing ()
        {
            float t = time * BobSpeed;
            float bob = 0;

            if(CurrentBobType == BobType.Smooth)
            {
                bob = Mathf.Sin(t);
                bob = (bob + 1) / 2;
            }
            else if(CurrentBobType == BobType.Custom)
            {
                bob = CustomBobCurve.Evaluate(t);
            }

            transform.localPosition = startPosition + (BobTargetOffset * bob);
        }

        void FakeRotation ()
        {
            float t = time * FakeRotationSpeed;
            float xRot = 0;

            if(CurrentFakeRotationType == FakeRotationType.Smooth)
            {
                xRot = Mathf.Sin(t);
            }
            else if(CurrentFakeRotationType == FakeRotationType.Linear)
            {
                xRot = 1.0f - Mathf.PingPong(t, 2.0f);
            }

            transform.localScale = new Vector3(xRot, startScale.y, startScale.z);
        }
    }
}