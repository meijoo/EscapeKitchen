using UnityEngine;

namespace EscapeBathroom
{
    // Added as scene overrides on the bathroom rig; the shared rig prefab is untouched.
    [DisallowMultipleComponent]
    public sealed class DuckFinderInteractor : MonoBehaviour
    {
        public Transform controllerAnchor;
        public Transform handAnchor;
        public OVRHand hand;
        public OVRSkeleton skeleton;
        public OVRInput.Controller controller;
        public HiddenQuackingDuck duck;
        [Min(.05f)] public float touchReach = .22f;
        [Min(.1f)] public float rayReach = 3.5f;
        public HiddenQuackingDuck HeldDuck => heldDuck;
        HiddenQuackingDuck heldDuck;
        bool wasPressed;
        Vector3 grabOffset;
        Quaternion grabRotation;
        float hapticEndTime;

        void Update()
        {
            if (hapticEndTime > 0f && Time.unscaledTime >= hapticEndTime)
            {
                if (OVRInput.IsControllerConnected(controller)) OVRInput.SetControllerVibration(0f, 0f, controller);
                hapticEndTime = 0f;
            }
            if (heldDuck != null && heldDuck.Holder != this) heldDuck = null;
            bool trackedHand = hand != null && hand.IsTracked && hand.IsDataValid;
            bool trackedController = OVRInput.IsControllerConnected(controller);
            var anchor = trackedHand ? handAnchor : controllerAnchor;
            if (anchor == null || (!trackedHand && !trackedController))
            {
                ReleaseHeld(); wasPressed = false; return;
            }
            Vector3 point = trackedHand ? PinchPosition(anchor.position) : anchor.position;
            bool pressed = trackedHand
                ? !hand.IsSystemGestureInProgress && hand.GetFingerIsPinching(OVRHand.HandFinger.Index)
                : OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, controller) > (wasPressed ? .35f : .65f);
            var pointer = trackedHand && hand.IsPointerPoseValid ? hand.PointerPose : anchor;
            if (pressed && !wasPressed) TryInteract(point, anchor.rotation, new Ray(pointer.position, pointer.forward));
            if (pressed && heldDuck != null) heldDuck.MoveHeld(point + anchor.rotation * grabOffset, anchor.rotation * grabRotation);
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

        public bool TryInteract(Vector3 position, Quaternion rotation, Ray ray)
        {
            if (duck == null || heldDuck != null || duck.IsHeld) return false;
            var collider = duck.GetComponent<Collider>();
            bool near = Vector3.Distance(position, collider.ClosestPoint(position)) <= touchReach;
            // Every pickup needs a clear line of sight. No collecting through the hamper or walls.
            if (near)
            {
                Vector3 center = collider.bounds.center;
                if (Physics.Linecast(position, center, out var obstruction, ~0, QueryTriggerInteraction.Ignore)
                    && obstruction.collider.GetComponentInParent<HiddenQuackingDuck>() != duck) return false;
            }
            else
            {
                if (!Physics.Raycast(ray, out var hit, rayReach, ~0, QueryTriggerInteraction.Ignore)
                    || hit.collider.GetComponentInParent<HiddenQuackingDuck>() != duck) return false;
            }
            if (!duck.TryGrab(this)) return false;
            heldDuck = duck;
            grabOffset = near ? Quaternion.Inverse(rotation) * (duck.transform.position - position) : new Vector3(0f, -.04f, .12f);
            grabRotation = Quaternion.Inverse(rotation) * duck.transform.rotation;
            if (OVRInput.IsControllerConnected(controller))
            {
                OVRInput.SetControllerVibration(.4f, .35f, controller);
                hapticEndTime = Time.unscaledTime + .12f;
            }
            return true;
        }

        public void ReleaseHeld()
        {
            if (heldDuck != null) heldDuck.Release(this);
            heldDuck = null;
            hapticEndTime = 0f;
            if (OVRInput.IsControllerConnected(controller)) OVRInput.SetControllerVibration(0f, 0f, controller);
        }

        void OnDisable() { ReleaseHeld(); }
    }
}
