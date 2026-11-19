using UnityEngine;

namespace EscapeKitchen
{
    public enum BottleColor { Blue, Red, Green }

    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class KitchenBottle : MonoBehaviour
    {
        public BottleColor color;
        public BottlePuzzle puzzle;
        public bool IsPlaced { get; private set; }
        public bool IsHeld => holder != null;
        public Object Holder => holder;
        public Rigidbody Body { get; private set; }
        Object holder;
        Vector3 homePosition;
        Quaternion homeRotation;
        Vector3 targetPosition;
        Quaternion targetRotation;
        Vector3 previousPosition;
        Vector3 releaseVelocity;
        float outsideTime;
        public static Quaternion Upright => Quaternion.Euler(-90f, 0f, 0f);

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            homePosition = transform.position;
            homeRotation = transform.rotation;
        }

        public bool TryGrab(Object owner)
        {
            if (owner == null || IsPlaced || IsHeld) return false;
            holder = owner;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.isKinematic = true;
            targetPosition = previousPosition = transform.position;
            targetRotation = transform.rotation;
            releaseVelocity = Vector3.zero;
            return true;
        }

        public void MoveHeld(Vector3 position, Quaternion rotation)
        {
            if (!IsHeld) return;
            releaseVelocity = Vector3.ClampMagnitude((position - previousPosition) / Mathf.Max(Time.deltaTime, .001f), 3f);
            previousPosition = position;
            targetPosition = position;
            targetRotation = rotation;
        }

        public bool Release()
        {
            if (!IsHeld) return false;
            holder = null;
            if (puzzle != null && puzzle.TryPlace(this)) return true;
            Body.isKinematic = false;
            Body.linearVelocity = releaseVelocity;
            return false;
        }

        public void Place(Vector3 position)
        {
            holder = null;
            IsPlaced = true;
            Body.isKinematic = true;
            Body.position = position;
            Body.rotation = Upright;
            transform.SetPositionAndRotation(position, Upright);
        }

        void FixedUpdate()
        {
            if (IsHeld)
            {
                var next = Vector3.MoveTowards(Body.position, targetPosition, 6f * Time.fixedDeltaTime);
                var travel = next - Body.position;
                if (travel.sqrMagnitude > .000001f && Body.SweepTest(travel.normalized, out var hit, travel.magnitude + .004f, QueryTriggerInteraction.Ignore))
                    next = Body.position + travel.normalized * Mathf.Max(0f, hit.distance - .004f);
                Body.MovePosition(next);
                Body.MoveRotation(Quaternion.RotateTowards(Body.rotation, targetRotation, 720f * Time.fixedDeltaTime));
            }
            else if (!IsPlaced)
            {
                // Recover a dropped bottle if physics ever carries it outside the room.
                bool outside = transform.position.y < -.25f || Mathf.Abs(transform.position.x) > 3.65f || Mathf.Abs(transform.position.z) > 3.65f;
                outsideTime = outside ? outsideTime + Time.fixedDeltaTime : 0f;
                if (outsideTime > 1f)
                {
                    Body.position = homePosition;
                    Body.rotation = homeRotation;
                    Body.linearVelocity = Body.angularVelocity = Vector3.zero;
                    outsideTime = 0f;
                }
            }
        }

        void OnDisable()
        {
            if (IsHeld) Release();
        }
    }
}
