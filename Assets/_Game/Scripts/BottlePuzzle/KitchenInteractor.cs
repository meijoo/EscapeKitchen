using EscapeKitchen.Player;
using UnityEngine;

namespace EscapeKitchen
{
    // Uses the project's existing Meta rig: controller grip or tracked index pinch.
    // On the crawling PlayerRig, set playerHand: the same hand also crawls, so grabbing shares it.
    [DefaultExecutionOrder(70)] // after CrawlLocomotion (50) and PlayerHand (60)
    public sealed class KitchenInteractor : MonoBehaviour
    {
        public Transform controllerAnchor;
        public Transform handAnchor;
        public OVRHand hand;
        public OVRSkeleton skeleton;
        public OVRInput.Controller controller;
        public float nearReach = .22f;
        public float rayReach = 4.5f;
        [Header("Crawling PlayerRig (optional)")]
        [Tooltip("The PlayerHand on the crawling PlayerRig. When set, a pinch or fist grabs (like the rest of the game), " +
                 "a hand gripping the floor never grabs, and closing a hand next to a bottle or door grabs it instead of the floor.")]
        public PlayerHand playerHand;
        [Tooltip("With a PlayerHand: a far (ray) grab needs a pinch with the palm at least this high above the floor, " +
                 "so a fist on the floor while crawling never grabs something across the room.")]
        public float rayMinHandHeight = .3f;
        public KitchenBottle HeldBottle => bottle;
        public KitchenSwingDoor HeldDoor => door;
        KitchenBottle bottle;
        KitchenSwingDoor door;
        bool wasPressed;
        Vector3 grabOffset;
        Quaternion grabRotation;
        readonly Collider[] nearby = new Collider[64];
        readonly RaycastHit[] lineHits = new RaycastHit[16];

        void Update()
        {
            // The bottle snapped into its socket while still in the hand: the hand is free again.
            if (bottle != null && !bottle.IsHeld)
            {
                if (playerHand != null) playerHand.EndExternalHold(bottle);
                bottle = null;
            }
            if (playerHand != null) { UpdateWithPlayerHand(); return; }
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

        void UpdateWithPlayerHand()
        {
            var ph = playerHand;
            if (!ph.isActiveAndEnabled || !ph.IsTracked) { ReleaseHeld(); wasPressed = false; return; }
            bool holding = bottle != null || door != null;
            // Same gesture as the rest of the game: pinch or fist, with release hysteresis.
            bool pressed = ph.IsClosed(wasPressed) && !(hand != null && hand.IsSystemGestureInProgress);
            Vector3 position = ph.PinchPoint;
            Quaternion rotation = ph.PalmRotation;
            bool planted = ph.Locomotion != null && ph.Locomotion.IsPlanted(ph);
            bool free = !holding && !ph.IsBusy && !planted;

            // Something to grab right here: tell crawling, so closing the hand grabs it, not the floor.
            if (free && FindNear(position) != null) ph.ReportExternalHover();

            if (pressed && !wasPressed && free)
            {
                bool pinching = ph.PinchStrength >= .85f && ph.GripStrength < .5f;
                bool raised = ph.Locomotion == null || ph.PalmPosition.y - ph.Locomotion.FloorY >= rayMinHandHeight;
                bool allowRay = pinching && raised && hand != null && hand.IsPointerPoseValid;
                Ray ray = allowRay ? new Ray(hand.PointerPose.position, hand.PointerPose.forward) : default;
                if (BeginGrab(position, rotation, ray, allowRay))
                {
                    if (bottle != null) ph.BeginExternalHold(bottle, bottle.GetComponentsInChildren<Collider>());
                    else if (door != null) ph.BeginExternalHold(door, door.GetComponentsInChildren<Collider>());
                }
            }
            if (pressed)
            {
                if (bottle != null) bottle.MoveHeld(position + rotation * grabOffset, rotation * grabRotation);
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

        public bool BeginGrab(Vector3 position, Quaternion rotation, Ray ray) => BeginGrab(position, rotation, ray, true);

        bool BeginGrab(Vector3 position, Quaternion rotation, Ray ray, bool allowRay)
        {
            if (bottle != null || door != null) return false;
            Collider candidate = FindNear(position);
            bool distant = candidate == null;
            if (distant && allowRay && Physics.Raycast(ray, out var rayHit, rayReach, ~0, QueryTriggerInteraction.Ignore)) candidate = rayHit.collider;
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

        // Closest bottle or door within nearReach that isn't behind something.
        Collider FindNear(Vector3 position)
        {
            float best = nearReach;
            Collider candidate = null;
            int count = Physics.OverlapSphereNonAlloc(position, nearReach, nearby, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var c = nearby[i];
                var b = c.GetComponentInParent<KitchenBottle>();
                var d = c.GetComponentInParent<KitchenSwingDoor>();
                bool bottleOk = b != null && !b.IsHeld && !b.IsPlaced;
                bool doorOk = d != null && (playerHand != null ? d.CanGrab : !d.locked && !d.IsHeld);
                if (!bottleOk && !doorOk) continue;
                float distance = Vector3.Distance(position, c.ClosestPoint(position));
                if (distance > best) continue;
                // Prevent a near grab reaching through a closed cabinet leaf or furniture.
                if (Blocked(position, c)) continue;
                best = distance; candidate = c;
            }
            return candidate;
        }

        bool Blocked(Vector3 position, Collider target)
        {
            Vector3 destination = target.bounds.center;
            if (playerHand == null)
                return Physics.Linecast(position, destination, out var hit, ~0, QueryTriggerInteraction.Ignore) && hit.collider != target;
            // Crawling rig: same test, but its own body and hand colliders don't count as in the way.
            Vector3 line = destination - position;
            float length = line.magnitude;
            if (length < 1e-4f) return false;
            int count = Physics.RaycastNonAlloc(position, line / length, lineHits, length, ~0, QueryTriggerInteraction.Ignore);
            var body = playerHand.Locomotion != null ? playerHand.Locomotion.Body : null;
            Collider first = null;
            float firstDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var c = lineHits[i].collider;
                if (c == body || HandPusher.IsHandCollider(c) || lineHits[i].distance >= firstDistance) continue;
                first = c;
                firstDistance = lineHits[i].distance;
            }
            return first != null && first != target;
        }

        public void ReleaseHeld()
        {
            if (playerHand != null)
            {
                if (bottle != null) playerHand.EndExternalHold(bottle);
                if (door != null) playerHand.EndExternalHold(door);
            }
            if (bottle != null) bottle.Release();
            if (door != null) door.Release();
            bottle = null; door = null;
        }
        void OnDisable() { ReleaseHeld(); }
    }
}
