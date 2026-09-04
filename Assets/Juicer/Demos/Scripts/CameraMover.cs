using UnityEngine;

namespace Juicer.Demos
{
    public class CameraMover : MonoBehaviour
    {
        [SerializeField]
        private float speed;

        [SerializeField]
        private bool moveX = true;

        [SerializeField]
        private bool moveY = true;

        void Update ()
        {
            float xInput = Input.GetAxisRaw("Horizontal");
            float yInput = Input.GetAxisRaw("Vertical");

            Vector3 moveInput = new Vector3(xInput, yInput, 0);

            if(xInput != 0 && yInput != 0)
                moveInput.Normalize();

            transform.position += moveInput * speed * Time.deltaTime;
        }
    }
}