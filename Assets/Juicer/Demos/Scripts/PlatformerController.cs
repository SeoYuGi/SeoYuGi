using UnityEngine;

namespace Juicer.Demos
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlatformerController : MonoBehaviour
    {
        [SerializeField]
        private float moveSpeed;

        [SerializeField]
        private float jumpForce;

        [SerializeField]
        private Animator anim;

        [SerializeField]
        private SpriteRenderer spriteRenderer;

        [SerializeField]
        private Juicer_SquashAndStretch squashAndStretch;

        private Rigidbody2D rig;

        private float moveInput;
        private bool jumpInput;

        private bool grounded;
        private float lastJumpTime;

        private Vector2 velocityLastFrame;

        void Awake ()
        {
            rig = GetComponent<Rigidbody2D>();
        }

        void Update ()
        {
            moveInput = Input.GetAxisRaw("Horizontal");
            jumpInput = Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W);

            if(moveInput != 0)
            {
                spriteRenderer.flipX = moveInput > 0;
            }
        }

        void FixedUpdate ()
        {
            GroundCheck();

            rig.linearVelocityX = moveInput * moveSpeed;

            if(jumpInput && grounded && Time.time - lastJumpTime > 0.2f)
            {
                Jump();
            }

            UpdateAnimation();

            velocityLastFrame = rig.linearVelocity;
        }

        void Jump ()
        {
            lastJumpTime = Time.time;
            rig.linearVelocityY = jumpForce;
            grounded = false;

            // Stretch when we jump
            if(squashAndStretch)
                squashAndStretch.Stretch();
        }

        // Raycast down to see if we're standing on the ground
        void GroundCheck ()
        {
            bool wasGrounded = grounded;

            RaycastHit2D hit = Physics2D.Raycast(transform.position - new Vector3(0, 0.05f, 0), Vector2.down, 0.02f);
            grounded = hit.collider != null;

            // If we landed this frame, squash.
            if(!wasGrounded && grounded && squashAndStretch)
            {
                float impact = Mathf.InverseLerp(-5, -20, velocityLastFrame.y);
                float squashMax = Mathf.Lerp(1.1f, 1.6f, impact);

                // We can override the component's properties with out own
                squashAndStretch.Squash(0.3f, squashMax);
            }    
        }

        void UpdateAnimation ()
        {
            anim.SetBool("Moving", rig.linearVelocityX != 0);
            anim.SetBool("InAir", !grounded);
            anim.SetInteger("VerticalDir", (int)rig.linearVelocityY);
        }
    }
}