using UnityEngine;

namespace Juicer
{
    public class Juicer_SquishyCollider : MonoBehaviour
    {
        public enum InfluenceType
        {
            Kinematic = 0,
            PhysicsAddForce = 1,
            ForceOuput = 2
        }

        public enum UpdateType
        {
            FixedUpdate = 0,
            Update = 1,
            CustomRate = 2
        }

        [SerializeField]
        [Tooltip("How the calculated force will be applied.\n" +
            "\n<b>Kinematic:<b> Direct position translation.\n" +
            "\n<b>PhysicsAddForce:</b> Add force to the rigidbody.\n" +
            "\n<b>ForceOutput:</b> Use your own custom implementation by reading the 'PushForce' property.")]
        private InfluenceType influenceType;

        [Tooltip("Radius from which the repulsion will begin.")]
        public float Radius = 1.0f;

        [Tooltip("Amount of force to apply to self.")]
        public float Strength = 5.0f;

        [SerializeField]
        [Tooltip("Rate of force depending on distance from collision.")]
        public AnimationCurve StrengthCurve = AnimationCurve.Linear(0, 0, 1, 1);

        [SerializeField]
        [Tooltip("Layers to check for collision.")]
        public LayerMask CollisionLayerMask;

        [SerializeField]
        [Tooltip("How often squishy collision is calculated.\n" +
            "\n<b>FixedUpdate:</b> Using the FixedUpdate function.\n" +
            "\n<b>Update:</b> Using the Update function (not recommended).\n" +
            "\n<b>Custom Rate:</b> Using a custom update rate (recommended for best performance).")]
        private UpdateType updateType;

        [Tooltip("Rate at which the collision will be updated. 0.1 = 10 times per second.")]
        public float CustomUpdateRate = 0.1f;
        private float lastUpdateTime;

        /// <summary>
        /// The velocity applied this frame from all other squishy colliders acting upon this one.
        /// </summary>
        public Vector2 PushForce { get; private set; }

        private Rigidbody2D rig;
        public Rigidbody2D Rig
        {
            get
            {
                if(!rig)
                    rig = GetComponent<Rigidbody2D>();

                return rig;
            }
        }

        void Reset ()
        {
            // When the component is first attached to an object, change the default InfluenceType depending on if there's a Rigidbody component.
            if(Rig)
                influenceType = InfluenceType.PhysicsAddForce;
            else
                influenceType = InfluenceType.Kinematic;
        }

        void Update ()
        {
            if(updateType == UpdateType.Update)
            {
                UpdateCollision();
            }
            else if(updateType == UpdateType.CustomRate)
            {
                if(Time.time - lastUpdateTime > CustomUpdateRate)
                    UpdateCollision();
            }

            if(influenceType == InfluenceType.Kinematic)
                transform.position += (Vector3)PushForce * Time.deltaTime;
        }

        void FixedUpdate ()
        {
            if(updateType == UpdateType.FixedUpdate)
                UpdateCollision();

            if(influenceType == InfluenceType.PhysicsAddForce)
                rig.AddForce(PushForce);
        }

        void UpdateCollision ()
        {
            lastUpdateTime = Time.time;

            // Reset the push force each time we calculate, so it doesn't accumulate additively
            PushForce = Vector2.zero;

            // Get all the collision neighbors to take into consideration
            Collider2D[] neighbors = Physics2D.OverlapCircleAll(transform.position, Radius, CollisionLayerMask);
            
            foreach(Collider2D col in neighbors)
            {
                // Continue if this is us
                if(col.gameObject == gameObject)
                    continue;

                Vector2 point = (Vector2)col.transform.position + col.offset;
                Vector2 direction = (Vector2)transform.position - point;
                float distance = direction.magnitude;

                // Clamp distance to prevent division by 0
                if(distance < 0.001f)
                    distance = 0.001f;

                // Calculate overlap and net strength
                float overlap = Radius - distance;
                float netStrength = StrengthCurve.Evaluate(overlap) * Strength;
                
                // Apply the force
                Vector2 force = direction.normalized * overlap * netStrength;
                PushForce += force;
            }
        }
    }
}