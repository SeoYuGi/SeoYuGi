using UnityEngine;

namespace Juicer.Demos
{
    public class CameraFollow : MonoBehaviour
    {
        [SerializeField]
        private Transform target;

        [SerializeField]
        private Vector3 offset;

        [SerializeField]
        private float followRate;

        void LateUpdate()
        {
            Vector3 targetPos = target.transform.position + offset;
            targetPos.z = -10;

            transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * followRate);
        }
    }
}
