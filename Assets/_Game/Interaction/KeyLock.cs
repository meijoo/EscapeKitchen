using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace EscapeKitchen.Interaction
{
    /// <summary>
    /// A lock that one particular key opens. Bring the key (held in a hand) to the keyhole: it
    /// slides in, turns, the shackle pops and the lock drops off the door. Then the door swings
    /// open, if one is set, and onUnlocked fires for anything else (lights, sounds, the next room).
    /// Assumes the key model points along its own +Z, bow to tip, with its collider on its root.
    /// </summary>
    [DisallowMultipleComponent]
    public class KeyLock : MonoBehaviour
    {
        [SerializeField] Grabbable key;
        [Tooltip("Where the key goes in. Its blue (forward) axis points into the lock.")]
        [SerializeField] Transform keyhole;
        [Tooltip("How close any part of the key must get to the keyhole.")]
        [SerializeField] float insertDistance = 0.06f;
        [Tooltip("Only a key in a hand opens it, not one dropped or thrown at the lock.")]
        [SerializeField] bool requireHeld = true;

        [Header("When unlocked")]
        [SerializeField] SwingDoor door;
        [Tooltip("Parts lifted to show the lock is open (the shackle).")]
        [SerializeField] Transform[] shackle;
        [SerializeField] float shackleLift = 0.025f;
        [Tooltip("Let the lock, with the key in it, fall off the door.")]
        [SerializeField] bool dropOff = true;
        [Tooltip("Seconds from the key going in to the door starting to swing.")]
        [SerializeField] float doorDelay = 0.7f;

        public UnityEvent onUnlocked = new UnityEvent();

        public bool IsUnlocked { get; private set; }
        public Grabbable Key => key;

        void Update()
        {
            if (IsUnlocked || key == null || keyhole == null) return;
            if (requireHeld && !key.IsHeld) return;
            if (DistanceToKeyhole() <= insertDistance) Unlock();
        }

        float DistanceToKeyhole()
        {
            float best = float.PositiveInfinity;
            Vector3 hole = keyhole.position;
            if (key.Colliders == null) return best;
            foreach (var c in key.Colliders)
                if (c && c.enabled) best = Mathf.Min(best, Vector3.Distance(c.ClosestPoint(hole), hole));
            return best;
        }

        /// <summary>Open the lock now (also usable from a UnityEvent, e.g. for testing).</summary>
        public void Unlock()
        {
            if (IsUnlocked) return;
            IsUnlocked = true;
            StartCoroutine(UnlockSequence());
        }

        IEnumerator UnlockSequence()
        {
            float started = Time.time;
            if (key != null && keyhole != null)
            {
                // Take the key out of the hand and slide it into the keyhole...
                Vector3 tip = KeyTipLocal();
                if (key.Holder) key.Holder.Release(false);
                key.IsGrabbable = false;
                key.Body.isKinematic = true;
                foreach (var c in key.Colliders) if (c) c.enabled = false;
                key.enabled = false;
                key.transform.SetParent(transform, true);

                Quaternion seated = keyhole.rotation;
                Vector3 inserted = keyhole.position + keyhole.forward * 0.03f - seated * tip;
                Vector3 outside = inserted - keyhole.forward * 0.04f;
                yield return Animate(0.15f, k => key.transform.SetPositionAndRotation(
                    Vector3.Lerp(outside, inserted, k), seated));

                // ...and turn it.
                yield return Animate(0.3f, k => key.transform.rotation =
                    Quaternion.AngleAxis(90f * k, keyhole.forward) * seated);
            }

            if (shackle != null)
                foreach (var part in shackle)
                    if (part) part.position += transform.up * shackleLift;

            onUnlocked.Invoke();

            if (dropOff) DropOff();

            float wait = doorDelay - (Time.time - started);
            if (wait > 0f) yield return new WaitForSeconds(wait);
            if (door) door.Open();
        }

        Vector3 KeyTipLocal()
        {
            // Tip = far +Z end of the key's box collider, on its centre line.
            foreach (var c in key.Colliders)
                if (c is BoxCollider box && box.transform == key.transform)
                    return Vector3.Scale(box.center + Vector3.forward * box.size.z * 0.5f, key.transform.lossyScale);
            return Vector3.zero;
        }

        void DropOff()
        {
            // Off the door (so it doesn't swing with it), staying inside the room's hierarchy.
            if (door && transform.IsChildOf(door.transform)) transform.SetParent(door.transform.parent, true);

            if (GetComponent<Collider>() == null) FitBoxCollider();
            var body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.mass = 0.4f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            Vector3 outOfLock = keyhole ? -keyhole.forward : -transform.forward;
            body.AddForce(outOfLock * 0.5f + Vector3.up * 0.4f, ForceMode.VelocityChange);
        }

        void FitBoxCollider()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            Bounds local = new Bounds(transform.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
            foreach (var r in renderers)
            {
                Bounds b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z);
                    local.Encapsulate(transform.InverseTransformPoint(corner));
                }
            }
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = local.center;
            box.size = local.size;
        }

        static IEnumerator Animate(float seconds, System.Action<float> apply)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                apply(Mathf.SmoothStep(0f, 1f, t / seconds));
                yield return null;
            }
            apply(1f);
        }

        void OnDrawGizmosSelected()
        {
            if (keyhole == null) return;
            Gizmos.color = IsUnlocked ? Color.green : new Color(1f, 0.8f, 0.2f);
            Gizmos.DrawWireSphere(keyhole.position, insertDistance);
            Gizmos.DrawRay(keyhole.position, keyhole.forward * 0.08f);
        }
    }
}
