using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Juicer
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class Juicer_SpriteGhost : MonoBehaviour
    {
        public enum SampleType
        {
            OverTime = 0,
            OverDistance = 1
        }

        [Tooltip("Define how the ghost sprites will be emitted.")]
        public SampleType CurrentSampleType;

        [Tooltip("Create a ghost sprite every 'samplesOverTime' seconds.")]
        public float SamplesOverTime = 0.05f;

        [Tooltip("Create a ghost sprite every 'samplesOverDistance' units from the last one.")]
        public float SamplesOverDistance = 0.1f;

        [Tooltip("Amount of time the ghost sprite will be active for.")]
        public float Lifetime = 1.0f;

        [Tooltip("Begin the ghosting effect upon initialization.")]
        public bool StartOnAwake = false;

        [Tooltip("Change the transparency of the ghost sprite over the course of its life.")]
        public bool EnableAlphaOverTime = true;

        [Tooltip("Change the transparency of the ghost sprite over the course of its life.")]
        public AnimationCurve AlphaOverTime = AnimationCurve.EaseInOut(0, 1, 1, 0);

        [Tooltip("Change the color of the ghost sprite over the course of its life.")]
        public bool EnableColorOverTime = false;

        [Tooltip("Change the color of the ghost sprite over the course of its life.")]
        public Gradient ColorOverTime;

        [Tooltip("Change the scale of the ghost sprite over the course of its life.")]
        public bool EnableSizeOverTime = false;

        [Tooltip("Change the scale of the ghost sprite over the course of its life.")]
        public AnimationCurve SizeOverTime = AnimationCurve.EaseInOut(0, 1, 1, 0);

        [Tooltip("Pooling will reuse old ghost sprites instead of creating new ones. This can help greatly with performance.")]
        public bool PoolGhostSprites = true;

        private List<SpriteRenderer> pooledGhostSprites = new List<SpriteRenderer>();

        private bool isGhosting;
        private float stopTime;
        private float lastSpawnGhostTime;
        private Vector3 lastSpawnGhostPosition;
        private SpriteRenderer spriteRenderer;

        void Awake ()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        void Start ()
        {
            if(StartOnAwake)
                Play();
        }

        /// <summary>
        /// Begin the ghosting effect. Call Stop() to cease ghosting. Optionally, provide a duration to automatically stop after a certain amount of time.
        /// </summary>
        public void Play (float duration = 0)
        {
            isGhosting = true;
            lastSpawnGhostTime = float.MinValue;
            lastSpawnGhostPosition = transform.position + new Vector3(SamplesOverDistance + 1, 0, 0);

            if(duration == 0)
                stopTime = float.MaxValue;
            else
                stopTime = Time.time + duration;
        }

        /// <summary>
        /// Stop the ghosting effect.
        /// </summary>
        public void Stop ()
        {
            isGhosting = false;
        }

        void Update ()
        {
            if(!isGhosting)
                return;

            if(Time.time > stopTime)
            {
                Stop();
                return;
            }

            if(CurrentSampleType == SampleType.OverTime)
            {
                if(Time.time - lastSpawnGhostTime > SamplesOverTime)
                {
                    CreateNewGhost();
                }
            }
            else if(CurrentSampleType == SampleType.OverDistance)
            {
                if(Vector2.Distance(transform.position, lastSpawnGhostPosition) > SamplesOverDistance)
                {
                    CreateNewGhost();
                }
            }
        }

        /// <summary>
        /// Manually create a new ghost.
        /// </summary>
        public void CreateNewGhost ()
        {
            lastSpawnGhostTime = Time.time;
            lastSpawnGhostPosition = transform.position;

            SpriteRenderer ghost = null;

            if(PoolGhostSprites)
            {
                ghost = GetPooledGhost();

                if(ghost)
                {
                    ghost.gameObject.SetActive(true);
                    ghost.transform.parent = null;
                }
            }

            if(!ghost)
            {
                GameObject ghostObj = new GameObject($"{name}_GhostSprite");
                ghost = ghostObj.AddComponent<SpriteRenderer>();

                if(PoolGhostSprites)
                    pooledGhostSprites.Add(ghost);
            }

            ghost.transform.position = transform.position;
            ghost.transform.rotation = transform.rotation;
            ghost.transform.localScale = transform.localScale;

            ghost.sprite = spriteRenderer.sprite;
            ghost.color = spriteRenderer.color;
            ghost.flipX = spriteRenderer.flipX;
            ghost.flipY = spriteRenderer.flipY;
            ghost.material = spriteRenderer.material;

            ghost.sortingOrder = spriteRenderer.sortingOrder - 1;

            StartCoroutine(AnimateGhost(ghost));
        }

        SpriteRenderer GetPooledGhost ()
        {
            foreach(SpriteRenderer ghost in pooledGhostSprites)
            {
                if(!ghost.gameObject.activeSelf)
                    return ghost;
            }

            return null;
        }

        IEnumerator AnimateGhost (SpriteRenderer ghost)
        {
            float startTime = Time.time;
            Vector3 startScale = ghost.transform.localScale;

            while(Time.time < startTime + Lifetime)
            {
                float t = (Time.time - startTime) / Lifetime;
                Color newColor = ghost.color;

                if(EnableColorOverTime)
                    newColor = ColorOverTime.Evaluate(t);

                if(EnableAlphaOverTime)
                    newColor.a = AlphaOverTime.Evaluate(t);

                ghost.color = newColor;

                if(EnableSizeOverTime)
                    ghost.transform.localScale = startScale * SizeOverTime.Evaluate(t);

                yield return null;
            }

            if(PoolGhostSprites)
            {
                ghost.gameObject.SetActive(false);
                ghost.transform.parent = transform;
            }
            else
            {
                Destroy(ghost.gameObject);
            }
        }
    }
}