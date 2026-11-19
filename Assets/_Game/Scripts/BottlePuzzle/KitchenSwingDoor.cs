using UnityEngine;

namespace EscapeKitchen
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class KitchenSwingDoor : MonoBehaviour
    {
        public float openAngle = 105f;
        public float speed = 130f;
        public bool locked;
        public Vector3 handleLocalPosition;
        public float Angle { get; private set; }
        public bool IsOpen => Mathf.Abs(Angle) >= 55f;
        public bool IsHeld => holder != null;
        public Vector3 HandlePosition => transform.TransformPoint(handleLocalPosition);
        public bool OpenRequested => Mathf.Abs(targetAngle) > 1f;
        Quaternion closedRotation;
        Rigidbody body;
        Object holder;
        float targetAngle;
        Vector3 grabStartDirection;
        float grabStartAngle;
        bool keepOpen;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            closedRotation = transform.localRotation;
        }

        public bool TryGrab(Object owner, Vector3 handPosition)
        {
            if (locked || keepOpen || IsHeld || owner == null) return false;
            holder = owner;
            grabStartDirection = Vector3.ProjectOnPlane(handPosition - transform.position, Vector3.up);
            if (grabStartDirection.sqrMagnitude < .001f) grabStartDirection = Vector3.ProjectOnPlane(HandlePosition - transform.position, Vector3.up);
            grabStartAngle = Angle;
            targetAngle = Angle;
            return true;
        }

        public void Drag(Vector3 handPosition)
        {
            if (!IsHeld) return;
            var direction = Vector3.ProjectOnPlane(handPosition - transform.position, Vector3.up);
            if (direction.sqrMagnitude < .001f) return;
            SetTargetAngle(grabStartAngle + Vector3.SignedAngle(grabStartDirection, direction, Vector3.up));
        }

        public void Release() { holder = null; }
        public void Toggle()
        {
            if (!locked && !keepOpen && !IsHeld) targetAngle = OpenRequested ? 0f : openAngle;
        }
        public void OpenPermanently()
        {
            holder = null;
            locked = false;
            keepOpen = true;
            targetAngle = openAngle;
        }
        public void SetTargetAngle(float angle)
        {
            if (locked || keepOpen) return;
            targetAngle = Mathf.Clamp(angle, Mathf.Min(0f, openAngle), Mathf.Max(0f, openAngle));
        }
        void FixedUpdate()
        {
            Angle = Mathf.MoveTowards(Angle, targetAngle, speed * Time.fixedDeltaTime);
            var local = closedRotation * Quaternion.Euler(0f, Angle, 0f);
            body.MoveRotation(transform.parent == null ? local : transform.parent.rotation * local);
        }
    }
}
