using UnityEngine;
using System.Collections.Generic;

namespace Juicer
{
    public class Juicer_Parallax : MonoBehaviour
    {
        public enum ParallaxReference
        {
            MainCamera = 0,
            Transform = 1
        }

        public enum UpdateMethod
        {
            Update = 0,
            LateUpdate = 1,
            FixedUpdate = 2,
            Custom = 3
        }

        [SerializeField]
        [Tooltip("The point of reference parallax should occur from.\n\n<b>Main Camera:</b> Camera.main.\n\n<b>Transform:</b> A custom Transform component.")]
        private ParallaxReference referenceType = ParallaxReference.MainCamera;

        [SerializeField]
        [Tooltip("The Transform which parallax will occur from. This will almost always be the camera.")]
        private Transform referenceComponent;

        [SerializeField]
        [Range(0.0f, 1.0f)]
        [Tooltip("Amount of parallax to apply to the X axis. 0 = closest, 1 = infinite distance.")]
        private float xDistance = 0.0f;

        [SerializeField]
        [Range(0.0f, 1.0f)]
        [Tooltip("Amount of parallax to apply to the Y axis. 0 = closest, 1 = infinite distance.")]
        private float yDistance = 0.0f;

        [SerializeField]
        [Tooltip("Teleport the object so that it continues to loop when the camera goes beyond its bounds.")]
        private bool infiniteScrolling;

        [SerializeField]
        [Tooltip("Enable infinite scrolling on the X axis.")]
        private bool scrollX = true;

        [SerializeField]
        [Tooltip("Enable infinite scrolling on the Y axis.")]
        private bool scrollY = false;

        [SerializeField]
        [Tooltip("Distance from the camera that the object should teleport ahead on the X axis. The amount teleported is twice this amount.")]
        private float scrollXThreshold = 1.0f;

        [SerializeField]
        [Tooltip("Distance from the camera that the object should teleport ahead on the Y axis. The amount teleported is twice this amount.")]
        private float scrollYThreshold = 1.0f;

        [SerializeField]
        [Tooltip("The method we'll use to process the parallax.")]
        private UpdateMethod updateMethod = UpdateMethod.Update;

        [SerializeField]
        [Tooltip("Rate of updating the parallax if Update Method is Custom.")]
        private float customUpdateMethodRate = 0.05f;
        private float lastCustomUpdateTime;

        private Vector3 lastFrameRefPos;

        void Start ()
        {
            TryGetReferenceComponent();
            lastFrameRefPos = referenceComponent.position;
        }

        void TryGetReferenceComponent ()
        {
            if(referenceType == ParallaxReference.MainCamera && Camera.main != null)
            {
                referenceComponent = Camera.main.transform;
            }
            else if(referenceType == ParallaxReference.Transform)
            {
                // Default to the main camera if no transform has been set as the reference component
                if(referenceComponent == null)
                    referenceComponent = Camera.main.transform;
            }
        }

        void Update ()
        {
            if(updateMethod == UpdateMethod.Update)
            {
                Process();
            }
            else if(updateMethod == UpdateMethod.Custom)
            {
                if(Time.time - lastCustomUpdateTime > customUpdateMethodRate)
                {
                    lastCustomUpdateTime = Time.time;
                    Process();
                }
            }
        }

        void LateUpdate ()
        {
            if(updateMethod == UpdateMethod.LateUpdate)
            {
                Process();
            }
        }

        void FixedUpdate ()
        {
            if(updateMethod == UpdateMethod.FixedUpdate)
            {
                Process();
            }
        }

        void Process ()
        {
            // If we have no reference component, try and get one
            if(referenceComponent == null)
            {
                TryGetReferenceComponent();
                return;
            }

            // Calculate how far and in what direction our reference component moved last frame
            Vector3 moveDelta = referenceComponent.position - lastFrameRefPos;
            moveDelta.z = 0;

            float x = xDistance * moveDelta.x;
            float y = yDistance * moveDelta.y;

            Vector3 movement = new Vector3(x, y, 0);

            transform.Translate(movement);

            lastFrameRefPos = referenceComponent.position;

            // Infinite scrolling background
            if(!infiniteScrolling)
                return;

            Vector3 offset = transform.position - referenceComponent.position;

            if(scrollX)
            {
                if(offset.x < -scrollXThreshold)
                    transform.position += new Vector3(scrollXThreshold * 2, 0, 0);
                else if(offset.x > scrollXThreshold)
                    transform.position -= new Vector3(scrollXThreshold * 2, 0, 0);
            }

            if(scrollY)
            {
                if(offset.y < -scrollXThreshold)
                    transform.position += new Vector3(0, scrollYThreshold * 2, 0);
                else if(offset.y > scrollXThreshold)
                    transform.position -= new Vector3(0, scrollYThreshold * 2, 0);
            }
        }
    }
}