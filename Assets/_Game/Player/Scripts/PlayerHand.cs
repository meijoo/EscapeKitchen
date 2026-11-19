using System.Collections.Generic;
using EscapeKitchen.Interaction;
using UnityEngine;
using UnityEngine.Events;

namespace EscapeKitchen.Player
{
    /// <summary>
    /// One tracked hand. Reads the Meta OVRHand + OVRSkeleton on the same GameObject, exposes
    /// palm / fingertip poses for locomotion and pushing, and handles grabbing <see cref="Grabbable"/>s
    /// with either a pinch or a closed fist.
    /// Put it on the "[BuildingBlock] Hand Tracking left/right" objects of the PlayerRig.
    /// </summary>
    [DefaultExecutionOrder(60)] // after CrawlLocomotion (50) has moved the rig this frame
    public class PlayerHand : MonoBehaviour
    {
        public enum Handedness { Left, Right }

        public enum Joint { Palm, Wrist, ThumbTip, IndexTip, MiddleTip, RingTip, LittleTip }

        [Header("Tracking")]
        [SerializeField] OVRHand ovrHand;
        [SerializeField] OVRSkeleton ovrSkeleton;
        [SerializeField] Handedness handedness = Handedness.Right;
        [Tooltip("Treat low-confidence tracking frames as 'not tracked'.")]
        [SerializeField] bool requireHighConfidence = true;

        [Header("Grab gesture")]
        [SerializeField, Range(0f, 1f)] float pinchGrabThreshold = 0.85f;
        [SerializeField, Range(0f, 1f)] float pinchReleaseThreshold = 0.5f;
        [SerializeField, Range(0f, 1f)] float gripGrabThreshold = 0.7f;
        [SerializeField, Range(0f, 1f)] float gripReleaseThreshold = 0.4f;
        [Tooltip("Fingertip-to-palm distance, as a multiple of palm length, for a straight finger.")]
        [SerializeField] float openFingerRatio = 1.2f;
        [Tooltip("Fingertip-to-palm distance, as a multiple of palm length, for a fully curled finger.")]
        [SerializeField] float curledFingerRatio = 0.55f;
        [Tooltip("A grab still counts if the hand closed this many seconds before touching the object.")]
        [SerializeField] float grabGestureGrace = 0.15f;

        [Header("Grab search")]
        [Tooltip("Search radius around the palm (whole-hand grab).")]
        [SerializeField] float grabRadius = 0.08f;
        [Tooltip("Search radius around the thumb-index pinch point.")]
        [SerializeField] float pinchGrabRadius = 0.04f;
        [Tooltip("Objects within this distance count as 'near' (HandPusher stops pushing them while you reach to grab).")]
        [SerializeField] float nearRadius = 0.16f;
        [SerializeField] LayerMask grabMask = ~0;
        [Tooltip("Seconds to keep holding while tracking is lost before the object is dropped.")]
        [SerializeField] float lostTrackingDropDelay = 0.75f;

        [Header("Throw")]
        [SerializeField] float throwScale = 1f;
        [SerializeField] int velocitySamples = 6;

        [Header("Events")]
        public UnityEvent<Grabbable> onGrab = new UnityEvent<Grabbable>();
        public UnityEvent<Grabbable> onRelease = new UnityEvent<Grabbable>();

        // --- public state -------------------------------------------------------------------
        public Handedness Side => handedness;
        public bool IsTracked { get; private set; }
        public Vector3 PalmPosition { get; private set; }
        public Quaternion PalmRotation { get; private set; } = Quaternion.identity;
        /// <summary>World direction the palm faces (out of the palm, away from the back of the hand).</summary>
        public Vector3 PalmNormal { get; private set; } = Vector3.down;
        public Vector3 PinchPoint { get; private set; }
        public float PinchStrength { get; private set; }
        /// <summary>0 = open hand, 1 = fist (index, middle and ring curl).</summary>
        public float GripStrength { get; private set; }
        /// <summary>World-space velocity of the palm, averaged over the last few frames.</summary>
        public Vector3 Velocity { get; private set; }
        public Grabbable HeldObject { get; private set; }
        public Grabbable HoveredObject { get; private set; }
        public bool IsHolding => HeldObject != null;
        public bool IsGrabGesture { get; private set; }
        /// <summary>Grabbables within <see cref="nearRadius"/> of the palm this frame.</summary>
        public IReadOnlyCollection<Grabbable> NearbyGrabbables => _nearby;
        public CrawlLocomotion Locomotion => _locomotion;

        /// <summary>
        /// Something this hand holds through another interaction system (e.g. the kitchen's
        /// KitchenInteractor) rather than as a <see cref="Grabbable"/>.
        /// </summary>
        public Object ExternalHold { get; private set; }
        /// <summary>Holding anything (a Grabbable or an external hold): the hand doesn't grip the floor or push.</summary>
        public bool IsBusy => IsHolding || ExternalHold != null;
        /// <summary>
        /// Something is within grabbing reach, from this hand's own search or another interaction
        /// system: closing the hand picks that up instead of gripping the floor.
        /// </summary>
        public bool IsHoveringAnything => HoveredObject != null || Time.frameCount - _externalHoverFrame <= 1;

        // --- internals ----------------------------------------------------------------------
        Transform _palm, _wrist, _thumbTip, _indexTip, _middleTip, _ringTip, _littleTip;
        Transform _indexProximal, _middleProximal, _littleProximal;
        bool _bonesReady;

        CrawlLocomotion _locomotion;
        OVRCameraRig _rig;
        readonly HashSet<Grabbable> _nearby = new HashSet<Grabbable>();
        readonly Collider[] _overlap = new Collider[32];
        readonly Queue<(Vector3 pos, Quaternion rot, float time)> _samples = new Queue<(Vector3, Quaternion, float)>();
        float _gestureStartTime = -1f;
        float _lostTrackingTime;
        int _externalHoverFrame = -10;
        Collider[] _externalColliders;

        void Reset()
        {
            ovrHand = GetComponent<OVRHand>();
            ovrSkeleton = GetComponent<OVRSkeleton>();
            if (name.ToLowerInvariant().Contains("left")) handedness = Handedness.Left;
        }

        void Awake()
        {
            if (!ovrHand) ovrHand = GetComponent<OVRHand>();
            if (!ovrSkeleton) ovrSkeleton = GetComponent<OVRSkeleton>();
            // Trust the tracked hand over the inspector value (the palm-normal math depends on it).
            if (ovrHand) handedness = ovrHand.GetHand() == OVRPlugin.Hand.HandLeft ? Handedness.Left : Handedness.Right;
            _locomotion = GetComponentInParent<CrawlLocomotion>();
            _rig = GetComponentInParent<OVRCameraRig>();
        }

        void OnEnable()
        {
            if (_rig) _rig.UpdatedAnchors += OnRigAnchorsUpdated;
        }

        void OnDisable()
        {
            if (_rig) _rig.UpdatedAnchors -= OnRigAnchorsUpdated;
            if (HeldObject) Release(false);
        }

        void Update()
        {
            CacheBones();
            UpdateTracking();
            UpdateNearbyAndHover();
            UpdateGrab();
        }

        void LateUpdate() => FollowHeldObject();

        // The rig re-poses the hand anchors right before rendering (late latching). Re-pose the
        // held object then too, so it doesn't trail the hand by a frame.
        void OnRigAnchorsUpdated(OVRCameraRig rig) => FollowHeldObject();

        // ------------------------------------------------------------------------------------
        // Tracking
        // ------------------------------------------------------------------------------------

        void CacheBones()
        {
            if (_bonesReady || ovrSkeleton == null || !ovrSkeleton.IsInitialized) return;
            var bones = ovrSkeleton.Bones;
            if (bones == null || bones.Count == 0) return;

            foreach (var bone in bones)
            {
                switch (bone.Id)
                {
                    // OpenXR hand skeleton (what the PlayerRig uses)
                    case OVRSkeleton.BoneId.XRHand_Palm: _palm = bone.Transform; break;
                    case OVRSkeleton.BoneId.XRHand_Wrist: _wrist = bone.Transform; break;
                    case OVRSkeleton.BoneId.XRHand_ThumbTip: _thumbTip = bone.Transform; break;
                    case OVRSkeleton.BoneId.XRHand_IndexTip: _indexTip = bone.Transform; break;
                    case OVRSkeleton.BoneId.XRHand_MiddleTip: _middleTip = bone.Transform; break;
                    case OVRSkeleton.BoneId.XRHand_RingTip: _ringTip = bone.Transform; break;
                    case OVRSkeleton.BoneId.XRHand_LittleTip: _littleTip = bone.Transform; break;
                    case OVRSkeleton.BoneId.XRHand_IndexProximal: _indexProximal = bone.Transform; break;
                    case OVRSkeleton.BoneId.XRHand_MiddleProximal: _middleProximal = bone.Transform; break;
                    case OVRSkeleton.BoneId.XRHand_LittleProximal: _littleProximal = bone.Transform; break;
                }
            }

            // Fall back to the legacy OVR hand skeleton ids if the OpenXR ones weren't there.
            if (_wrist == null)
            {
                foreach (var bone in bones)
                {
                    switch (bone.Id)
                    {
                        case OVRSkeleton.BoneId.Hand_WristRoot: _wrist = bone.Transform; break;
                        case OVRSkeleton.BoneId.Hand_ThumbTip: _thumbTip = bone.Transform; break;
                        case OVRSkeleton.BoneId.Hand_IndexTip: _indexTip = bone.Transform; break;
                        case OVRSkeleton.BoneId.Hand_MiddleTip: _middleTip = bone.Transform; break;
                        case OVRSkeleton.BoneId.Hand_RingTip: _ringTip = bone.Transform; break;
                        case OVRSkeleton.BoneId.Hand_PinkyTip: _littleTip = bone.Transform; break;
                        case OVRSkeleton.BoneId.Hand_Index1: _indexProximal = bone.Transform; break;
                        case OVRSkeleton.BoneId.Hand_Middle1: _middleProximal = bone.Transform; break;
                        case OVRSkeleton.BoneId.Hand_Pinky1: _littleProximal = bone.Transform; break;
                    }
                }
            }

            _bonesReady = _wrist && _indexTip && _thumbTip && _middleTip && _ringTip
                          && _indexProximal && _middleProximal && _littleProximal;
        }

        void UpdateTracking()
        {
            bool tracked = ovrHand != null && ovrHand.IsTracked && _bonesReady
                           && (!requireHighConfidence || ovrHand.IsDataHighConfidence)
                           && (ovrSkeleton == null || ovrSkeleton.IsDataValid);
            IsTracked = tracked;
            if (!tracked)
            {
                _samples.Clear();
                Velocity = Vector3.zero;
                IsGrabGesture = false;
                return;
            }

            Vector3 wrist = _wrist.position;
            Vector3 middleProx = _middleProximal.position;
            PalmPosition = _palm ? _palm.position : Vector3.Lerp(wrist, middleProx, 0.5f);

            // Palm normal from the knuckle line, so it doesn't depend on per-skeleton bone axes.
            Vector3 toIndex = _indexProximal.position - wrist;
            Vector3 toLittle = _littleProximal.position - wrist;
            Vector3 normal = handedness == Handedness.Left
                ? Vector3.Cross(toIndex, toLittle)
                : Vector3.Cross(toLittle, toIndex);
            if (normal.sqrMagnitude > 1e-8f) PalmNormal = normal.normalized;

            Vector3 forward = middleProx - wrist;
            PalmRotation = forward.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(forward.normalized, -PalmNormal)
                : _wrist.rotation;

            PinchPoint = Vector3.Lerp(_thumbTip.position, _indexTip.position, 0.5f);
            PinchStrength = ovrHand.GetFingerPinchStrength(OVRHand.HandFinger.Index);

            float palmLength = Mathf.Max(forward.magnitude, 0.03f);
            float curl = FingerCurl(_indexTip.position, palmLength)
                         + FingerCurl(_middleTip.position, palmLength)
                         + FingerCurl(_ringTip.position, palmLength);
            GripStrength = curl / 3f;

            // Velocity (world space, so crawling motion is included in throws).
            _samples.Enqueue((PalmPosition, PalmRotation, Time.time));
            while (_samples.Count > Mathf.Max(2, velocitySamples)) _samples.Dequeue();
            Velocity = ComputeVelocity(out _);

            bool gesture = IsClosed(IsHolding);
            if (gesture && !IsGrabGesture) _gestureStartTime = Time.time;
            IsGrabGesture = gesture;
        }

        /// <summary>
        /// Is the hand closed (fist or pinch)? Pass <c>true</c> while something is already gripped
        /// (an object, or the floor) so it uses the looser release thresholds and doesn't flicker.
        /// </summary>
        public bool IsClosed(bool alreadyGripping) =>
            IsTracked && (alreadyGripping
                ? !(PinchStrength < pinchReleaseThreshold && GripStrength < gripReleaseThreshold)
                : PinchStrength >= pinchGrabThreshold || GripStrength >= gripGrabThreshold);

        float FingerCurl(Vector3 tip, float palmLength)
        {
            float ratio = Vector3.Distance(tip, PalmPosition) / palmLength;
            return Mathf.InverseLerp(openFingerRatio, curledFingerRatio, ratio);
        }

        Vector3 ComputeVelocity(out Vector3 angularVelocity)
        {
            angularVelocity = Vector3.zero;
            if (_samples.Count < 2) return Vector3.zero;
            var first = _samples.Peek();
            (Vector3 pos, Quaternion rot, float time) last = first;
            foreach (var s in _samples) last = s;
            float dt = last.time - first.time;
            if (dt <= 1e-4f) return Vector3.zero;

            Quaternion delta = last.rot * Quaternion.Inverse(first.rot);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (!float.IsNaN(axis.x) && !float.IsInfinity(axis.x))
                angularVelocity = axis * (angle * Mathf.Deg2Rad / dt);
            return (last.pos - first.pos) / dt;
        }

        // ------------------------------------------------------------------------------------
        // Grabbing
        // ------------------------------------------------------------------------------------

        void UpdateNearbyAndHover()
        {
            _nearby.Clear();
            HoveredObject = null;
            if (!IsTracked) return;

            Vector3 palmGrabPoint = PalmPosition + PalmNormal * 0.03f;
            int count = Physics.OverlapSphereNonAlloc(PalmPosition, nearRadius, _overlap, grabMask,
                QueryTriggerInteraction.Ignore);

            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var col = _overlap[i];
                var g = FindGrabbable(col);
                if (g == null || !g.isActiveAndEnabled) continue;
                _nearby.Add(g);
                if (IsBusy || !g.CanBeGrabbedBy(this)) continue;

                // Whole-hand grab around the palm, or a precise pinch between thumb and index.
                float palmDist = DistanceTo(col, palmGrabPoint);
                float pinchDist = DistanceTo(col, PinchPoint);
                float score = float.MaxValue;
                if (palmDist <= grabRadius) score = palmDist;
                if (pinchDist <= pinchGrabRadius) score = Mathf.Min(score, pinchDist);
                if (score < best)
                {
                    best = score;
                    HoveredObject = g;
                }
            }
        }

        void UpdateGrab()
        {
            if (IsHolding)
            {
                if (!IsTracked)
                {
                    _lostTrackingTime += Time.deltaTime;
                    if (_lostTrackingTime >= lostTrackingDropDelay) Release(false);
                    return;
                }
                _lostTrackingTime = 0f;
                if (!IsGrabGesture) Release(true);
                return;
            }

            _lostTrackingTime = 0f;
            if (!IsTracked || !IsGrabGesture || HoveredObject == null) return;
            if (Time.time - _gestureStartTime > grabGestureGrace) return; // closed the hand elsewhere first
            Grab(HoveredObject);
        }

        /// <summary>Grab an object directly (also used by the grab gesture).</summary>
        public void Grab(Grabbable target)
        {
            if (target == null || target == HeldObject) return;
            if (HeldObject) Release(false);
            HeldObject = target;
            target.BeginGrab(this, new Pose(PalmPosition, PalmRotation));
            if (_locomotion) _locomotion.SetBodyCollisionIgnored(target, true);
            onGrab.Invoke(target);
        }

        /// <summary>Let go of the held object. <paramref name="applyThrow"/> carries the hand's velocity.</summary>
        public void Release(bool applyThrow)
        {
            var target = HeldObject;
            if (target == null) return;
            HeldObject = null;

            Vector3 velocity = Vector3.zero, angular = Vector3.zero;
            if (applyThrow)
            {
                velocity = ComputeVelocity(out angular) * throwScale;
            }
            target.EndGrab(this, velocity, angular);
            if (_locomotion) _locomotion.SetBodyCollisionIgnored(target, false);
            onRelease.Invoke(target);
        }

        // ------------------------------------------------------------------------------------
        // Other interaction systems
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// Another interaction system has something within this hand's reach. Call it every frame
        /// while that's true; crawling then leaves a closing hand to that system.
        /// </summary>
        public void ReportExternalHover() => _externalHoverFrame = Time.frameCount;

        /// <summary>
        /// Another interaction system picked something up with this hand. Until
        /// <see cref="EndExternalHold"/> the hand doesn't grip the floor, push or grab Grabbables,
        /// and the body doesn't collide with <paramref name="colliders"/>.
        /// </summary>
        public void BeginExternalHold(Object held, Collider[] colliders)
        {
            if (held == null) return;
            if (HeldObject) Release(false);
            if (ExternalHold != null) EndExternalHold(ExternalHold);
            ExternalHold = held;
            _externalColliders = colliders;
            if (_locomotion && colliders != null) _locomotion.SetBodyCollisionIgnored(held, colliders, true);
        }

        public void EndExternalHold(Object held)
        {
            if (held == null || ExternalHold != held) return;
            ExternalHold = null;
            if (_locomotion && _externalColliders != null) _locomotion.SetBodyCollisionIgnored(held, _externalColliders, false);
            _externalColliders = null;
        }

        /// <summary>Called by a Grabbable when another hand takes it from this one.</summary>
        internal void OnTakenByOtherHand(Grabbable target)
        {
            if (HeldObject != target) return;
            HeldObject = null;
            onRelease.Invoke(target);
        }

        void FollowHeldObject()
        {
            if (HeldObject == null || !IsTracked) return;
            // Re-read the palm in case the anchors moved since Update (late latching / crawling).
            Vector3 palm = _palm ? _palm.position : Vector3.Lerp(_wrist.position, _middleProximal.position, 0.5f);
            Vector3 forward = _middleProximal.position - _wrist.position;
            Quaternion rot = forward.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(forward.normalized, -PalmNormal)
                : PalmRotation;
            HeldObject.FollowHand(new Pose(palm, rot));
        }

        /// <summary>World position of a hand joint, if the hand is tracked.</summary>
        public bool TryGetJoint(Joint joint, out Vector3 position)
        {
            position = default;
            if (!IsTracked) return false;
            Transform t = joint switch
            {
                Joint.Wrist => _wrist,
                Joint.ThumbTip => _thumbTip,
                Joint.IndexTip => _indexTip,
                Joint.MiddleTip => _middleTip,
                Joint.RingTip => _ringTip,
                Joint.LittleTip => _littleTip,
                _ => null,
            };
            if (joint == Joint.Palm)
            {
                position = PalmPosition;
                return true;
            }
            if (t == null) return false;
            position = t.position;
            return true;
        }

        static Grabbable FindGrabbable(Collider col)
        {
            if (col.attachedRigidbody != null)
            {
                var g = col.attachedRigidbody.GetComponent<Grabbable>();
                if (g != null) return g;
            }
            return col.GetComponentInParent<Grabbable>();
        }

        static float DistanceTo(Collider col, Vector3 point)
        {
            // ClosestPoint only supports primitive and convex mesh colliders.
            bool supported = !(col is MeshCollider mc) || mc.convex;
            Vector3 closest = supported ? col.ClosestPoint(point) : col.bounds.ClosestPoint(point);
            return Vector3.Distance(closest, point);
        }

        void OnDrawGizmosSelected()
        {
            if (!IsTracked) return;
            Gizmos.color = IsHolding ? Color.green : (HoveredObject ? Color.yellow : Color.cyan);
            Gizmos.DrawWireSphere(PalmPosition + PalmNormal * 0.03f, grabRadius);
            Gizmos.DrawWireSphere(PinchPoint, pinchGrabRadius);
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(PalmPosition, PalmPosition + PalmNormal * 0.1f);
        }
    }
}
