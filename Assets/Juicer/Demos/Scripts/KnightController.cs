using UnityEngine;

namespace Juicer.Demos
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class KnightController : MonoBehaviour
    {
        [SerializeField]
        private float moveSpeed;

        [SerializeField]
        private SpriteRenderer sr;

        [SerializeField]
        private Animator anim;

        private Vector2 moveInput;
        private bool canMove = true;
        private float lastAttackTime;

        private Rigidbody2D rig;

        void Awake ()
        {
            rig = GetComponent<Rigidbody2D>();
        }

        void Update ()
        {
            moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")).normalized;

            if(Input.GetKeyDown(KeyCode.Space) && Time.time - lastAttackTime > 0.6f)
            {
                Attack();
            }
        }

        void FixedUpdate ()
        {
            if(canMove)
                rig.linearVelocity = moveInput * moveSpeed;
            else
                rig.linearVelocity = Vector2.zero;

            if(moveInput.x != 0)
            {
                sr.flipX = moveInput.x < 0;
            }

            anim.SetBool("Moving", rig.linearVelocity.magnitude > 0.1f);
        }

        void Attack ()
        {
            lastAttackTime = Time.time;
            anim.SetTrigger("Attack");
            Invoke(nameof(DetectHit), 0.25f);
        }

        void DetectHit ()
        {
            Vector2 dir = sr.flipX ? Vector2.left : Vector2.right;
            RaycastHit2D hit = Physics2D.Raycast((Vector2)transform.position + (dir / 2), dir, 0.5f);

            if(hit.collider != null && hit.collider.TryGetComponent<Hittable>(out var hittable))
            {
                hittable.Hit();
            }
        }
    }
}