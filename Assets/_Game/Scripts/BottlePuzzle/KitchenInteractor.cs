using UnityEngine;

namespace EscapeKitchen
{
    // Uses the project's existing Meta rig: controller grip or tracked index pinch.
    public sealed class KitchenInteractor : MonoBehaviour
    {
        public Transform controllerAnchor;
        public Transform handAnchor;
        public OVRHand hand;
        public OVRSkeleton skeleton;
        public OVRInput.Controller controller;
        public float nearReach = .22f;
        public float rayReach = 4.5f;
        public KitchenBottle HeldBottle => bottle;
        public KitchenSwingDoor HeldDoor => door;
        KitchenBottle bottle;
        KitchenSwingDoor door;
        bool wasPressed;
        Vector3 grabOffset;
        Quaternion grabRotation;
        readonly Collider[] nearby = new Collider[64];

        void Update()
        {
            bool handMode = hand != null && hand.IsTracked && hand.IsDataValid;
            bool connected = OVRInput.IsControllerConnected(controller);
            Transform anchor = handMode ? handAnchor : controllerAnchor;
            if (anchor == null || (!handMode && !connected)) { ReleaseHeld(); wasPressed = false; return; }
            Vector3 position = handMode ? PinchPosition(anchor.position) : anchor.position;
            bool pressed = handMode
                ? !hand.IsSystemGestureInProgress && hand.GetFingerIsPinching(OVRHand.HandFinger.Index)
                : OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, controller) > (wasPressed ? .35f : .65f);
            Transform pointer = handMode && hand.IsPointerPoseValid ? hand.PointerPose : anchor;
            if (pressed && !wasPressed) BeginGrab(position, anchor.rotation, new Ray(pointer.position, pointer.forward));
            if (pressed)
            {
                if (bottle != null) bottle.MoveHeld(position + anchor.rotation * grabOffset, anchor.rotation * grabRotation);
                if (door != null) door.Drag(position);
            }
            if (!pressed && wasPressed) ReleaseHeld();
            wasPressed = pressed;
        }
        Vector3 PinchPosition(Vector3 fallback)
        {
            if (skeleton == null || !skeleton.IsDataValid) return fallback;
            Transform thumb = null, index = null;
            foreach (var bone in skeleton.Bones)
            {
                if (bone.Id == OVRSkeleton.BoneId.Hand_ThumbTip || bone.Id == OVRSkeleton.BoneId.XRHand_ThumbTip) thumb = bone.Transform;
                if (bone.Id == OVRSkeleton.BoneId.Hand_IndexTip || bone.Id == OVRSkeleton.BoneId.XRHand_IndexTip) index = bone.Transform;
            }
            return thumb != null && index != null ? (thumb.position + index.position) * .5f : fallback;
        }
        public bool BeginGrab(Vector3 position, Quaternion rotation, Ray ray)
        {
            if (bottle != null || door != null) return false;
            float best = nearReach;
            Collider candidate = null;
            int count = Physics.OverlapSphereNonAlloc(position, nearReach, nearby, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var c = nearby[i];
                var b = c.GetComponentInParent<KitchenBottle>();
                var d = c.GetComponentInParent<KitchenSwingDoor>();
                if ((b == null || b.IsHeld || b.IsPlaced) && (d == null || d.locked || d.IsHeld)) continue;
                float distance = Vector3.Distance(position, c.ClosestPoint(position));
                if (distance > best) continue;
                // Prevent a near grab reaching through a closed cabinet leaf or furniture.
                Vector3 destination = c.bounds.center;
                if (Physics.Linecast(position, destination, out var hit, ~0, QueryTriggerInteraction.Ignore) && hit.collider != c) continue;
                best = distance; candidate = c;
            }
            bool distant = candidate == null;
            if (distant && Physics.Raycast(ray, out var rayHit, rayReach, ~0, QueryTriggerInteraction.Ignore)) candidate = rayHit.collider;
            if (candidate == null) return false;
            var selectedBottle = candidate.GetComponentInParent<KitchenBottle>();
            if (selectedBottle != null && selectedBottle.TryGrab(this))
            {
                bottle = selectedBottle;
                grabOffset = distant ? new Vector3(0f, -.05f, .12f) : Quaternion.Inverse(rotation) * (bottle.transform.position - position);
                grabRotation = distant ? Quaternion.Inverse(rotation) * KitchenBottle.Upright : Quaternion.Inverse(rotation) * bottle.transform.rotation;
                return true;
            }
            var selectedDoor = candidate.GetComponentInParent<KitchenSwingDoor>();
            if (selectedDoor == null || selectedDoor.locked) return false;
            if (distant) { selectedDoor.Toggle(); return true; }
            if (!selectedDoor.TryGrab(this, position)) return false;
            door = selectedDoor;
            return true;
        }
        public void ReleaseHeld()
        {
            if (bottle != null) bottle.Release();
            if (door != null) door.Release();
            bottle = null; door = null;
        }
        void OnDisable() { ReleaseHeld(); }
    }
}
