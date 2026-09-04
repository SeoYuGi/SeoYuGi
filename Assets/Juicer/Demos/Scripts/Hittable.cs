using UnityEngine;

namespace Juicer.Demos
{
    public class Hittable : MonoBehaviour
    {
        [SerializeField]
        private Juicer_HitFlash hitFlash;

        [SerializeField]
        private Juicer_ScreenShake screenShake;

        public void Hit ()
        {
            if(hitFlash)
                hitFlash.Flash();

            if(screenShake)
                screenShake.Shake(0.1f, 0.2f);
        }
    }
}