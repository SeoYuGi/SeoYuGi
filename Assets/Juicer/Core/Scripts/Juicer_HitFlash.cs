using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Juicer
{
    public class Juicer_HitFlash : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The SpriteRenderer component you wish to affect with the hit flash effect.")]
        private SpriteRenderer spriteRenderer;

        [SerializeField]
        [Tooltip("The color the sprite will be set to when flashing. This can be overriden when calling the Flash function.")]
        private Color flashColor = Color.white;

        [SerializeField]
        [Tooltip("Amount of time the flash will last for. This can be overriden when calling the Flash function.")]
        private float flashDuration = 0.2f;

        [SerializeField]
        [Tooltip("Falloff curve when going from 100% flash to 0% flash. Samples in reverse, so a time of 1.0 will be peak brightness.")]
        private AnimationCurve flashCurve = AnimationCurve.Linear(0, 0, 1, 1);

        [SerializeField]
        [Tooltip("Also applies the flash to any children SpriteRenderers with the Juicer_SpriteUnlit shader.")]
        private bool affectChildren = false;

        private List<SpriteRenderer> affectedRenderers = new List<SpriteRenderer>();

        private Coroutine flashCoroutine;

        [SerializeField]
        private Shader requiredShader;

        void Start ()
        {
            UpdateRendererList();
        }

        void Update ()
        {
            if(Input.GetKeyDown(KeyCode.Alpha1))
            {
                Flash();
                Juicer_FreezeFrame.Trigger(0.3f);
            }
        }

        /// <summary>
        /// Call this if you have enabled 'affectChildren' and wish to update which SpriteRenderers are being tracked.
        /// Useful for if you add/remove children renderers.
        /// </summary>
        public void UpdateRendererList ()
        {
            affectedRenderers.Clear();

            if(spriteRenderer)
            {
                if(spriteRenderer.material.shader == requiredShader)
                {
                    spriteRenderer.material.SetFloat("_EnableHitFlash", 1);
                    affectedRenderers.Add(spriteRenderer);
                }
                else
                {
                    Debug.LogError("Sprite Renderer must have a material with the Juicer_SpriteUnlit shader.", this);
                }
            }

            if(affectChildren)
            {
                SpriteRenderer[] childrenSRs = GetComponentsInChildren<SpriteRenderer>(true);

                for(int i = 0; i < childrenSRs.Length; i++)
                {
                    if(childrenSRs[i].material.shader != requiredShader)
                        continue;

                    childrenSRs[i].material.SetFloat("_EnableHitFlash", 1);
                    affectedRenderers.Add(childrenSRs[i]);
                }
            }
        }

        public void Flash (Color color, float inDuration, float holdDuration, float outDuration)
        {
            if(flashCoroutine != null)
                StopCoroutine(flashCoroutine);

            flashCoroutine = StartCoroutine(FlashProcess(color, inDuration, holdDuration, outDuration));
        }

        public void Flash (Color color, float inDuration, float outDuration)
        {
            Flash(color, inDuration, 0, outDuration);
        }

        public void Flash (Color color, float duration)
        {
            Flash(color, 0, 0, duration);
        }

        public void Flash (Color color)
        {
            Flash(color, 0, 0, flashDuration);
        }

        public void Flash (float duration)
        {
            Flash(flashColor, 0, 0, duration);
        }

        public void Flash ()
        {
            Flash(flashColor, 0, 0, flashDuration);
        }

        IEnumerator FlashProcess (Color color, float inDuration, float holdDuration, float outDuration)
        {
            SetFlashColor(color);

            float t = 0;

            if(inDuration > 0)
            {
                SetFlashAmount(t);

                while(t < 1)
                {
                    t = Mathf.MoveTowards(t, 1, (1.0f / inDuration) * Time.deltaTime);
                    SetFlashAmount(t);
                    yield return null;
                }
            }
            else
            {
                t = 1;
                SetFlashAmount(t);
            }

            if(holdDuration > 0)
                yield return new WaitForSeconds(holdDuration);

            while(t > 0)
            {
                t = Mathf.MoveTowards(t, 0, (1.0f / outDuration) * Time.deltaTime);
                SetFlashAmount(flashCurve.Evaluate(t));
                yield return null;
            }
        }

        void SetFlashAmount (float amount)
        {
            for(int i = 0; i < affectedRenderers.Count; i++)
            {
                if(!affectedRenderers[i])
                    continue;

                affectedRenderers[i].material.SetFloat("_HitFlashAmount", amount);
            }
        }

        void SetFlashColor (Color color)
        {
            for(int i = 0; i < affectedRenderers.Count; i++)
            {
                if(!affectedRenderers[i])
                    continue;

                affectedRenderers[i].material.SetColor("_HitFlashColor", color);
            }
        }
    }
}