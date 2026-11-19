using UnityEngine;

namespace EscapeKitchen.Interaction
{
    /// <summary>
    /// A heavy object the player can shove along the floor, with their hands (HandPusher) or by
    /// crawling into it (CrawlLocomotion). Keeps it upright and caps how fast it can slide so it
    /// feels heavy. Not grabbable unless you also add a Grabbable.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class Pushable : MonoBehaviour
    {
        [Tooltip("Top sliding speed in m/s, however hard it is pushed.")]
        [SerializeField] float maxSpeed = 0.6f;
        [Tooltip("0 = moves as fast as you push it, 1 = won't budge when crawled into.")]
        [SerializeField, Range(0f, 1f)] float resistance = 0.4f;
        [SerializeField] float mass = 12f;
        [Tooltip("How quickly it stops sliding once you stop pushing.")]
        [SerializeField] float slideDamping = 4f;
        [Tooltip("Lock tipping over (rotation around X and Z).")]
        [SerializeField] bool keepUpright = true;
        [Tooltip("Lock spinning around the vertical axis too.")]
        [SerializeField] bool lockYaw = false;

        public Rigidbody Body { get; private set; }

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Body.mass = mass;
            Body.linearDamping = slideDamping;
            Body.angularDamping = Mathf.Max(Body.angularDamping, 2f);
            Body.maxLinearVelocity = maxSpeed;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var constraints = Body.constraints;
            if (keepUpright) constraints |= RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            if (lockYaw) constraints |= RigidbodyConstraints.FreezeRotationY;
            Body.constraints = constraints;
        }

        /// <summary>Push along the floor in <paramref name="direction"/> at up to <paramref name="speed"/>.</summary>
        public void Push(Vector3 direction, float speed)
        {
            if (Body.isKinematic) return;
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f) return;
            direction.Normalize();

            float target = Mathf.Min(speed * (1f - resistance), maxSpeed);
            Vector3 v = Body.linearVelocity;
            float along = Vector3.Dot(v, direction);
            if (along >= target) return; // already moving that way fast enough
            Body.AddForce(direction * (target - along), ForceMode.VelocityChange);
        }
    }
}
