using UnityEngine;

namespace Juicer.Demos
{
    public class FollowerFish : MonoBehaviour
    {
        [SerializeField]
        private Transform target;

        [SerializeField]
        private float moveSpeed;

        [SerializeField]
        private float stopDistance;

        private float targetRotation;

        void Update ()
        {
            if(target == null)
                return;

            Vector3 targetDirection = (target.position - transform.position).normalized;
            float targetDistance = Vector2.Distance(transform.position, target.position);

            if(targetDistance > stopDistance)
                transform.position += targetDirection * moveSpeed * Time.deltaTime;

            targetRotation = Mathf.Atan2(targetDirection.y, targetDirection.x) * Mathf.Rad2Deg + 180;

            float zRot = Mathf.LerpAngle(transform.eulerAngles.z, targetRotation, Time.deltaTime * 20);
            transform.eulerAngles = new Vector3(0, 0, zRot);
        }
    }
}