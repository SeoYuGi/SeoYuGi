using UnityEngine;

namespace Juicer.Demos
{
    public class PlayerFish : MonoBehaviour
    {
        [SerializeField]
        private float moveSpeed;

        private float targetRotation;

        void Update ()
        {
            Vector3 moveInput = new Vector3(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"), 0).normalized;
            transform.position += moveInput * moveSpeed * Time.deltaTime;

            if(moveInput.sqrMagnitude > 0)
            {
                targetRotation = Mathf.Atan2(moveInput.y, moveInput.x) * Mathf.Rad2Deg + 180;
            }

            float zRot = Mathf.LerpAngle(transform.eulerAngles.z, targetRotation, Time.deltaTime * 20);
            transform.eulerAngles = new Vector3(0, 0, zRot);
        }
    }
}