using System;
using System.Collections.Generic;
using EscapeKitchen.Player;
using UnityEngine;
using UnityEngine.Events;

namespace EscapeKitchen.Interaction
{
    /// <summary>
    /// An object the player can pick up with a tracked hand (pinch or fist). Needs a Rigidbody and
    /// at least one primitive or convex collider. While held it is kinematic and follows the palm,
    /// keeping the offset it had when it was grabbed; on release it gets the hand's velocity.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class Grabbable : MonoBehaviour
    {
        static readonly List<Grabbable> s_all = new List<Grabbable>();
        public static IReadOnlyList<Grabbable> All => s_all;
        /// <summary>Raised when a Grabbable is enabled (HandPusher uses it to set up collision filtering).</summary>
        public static event Action<Grabbable> Registered;

        [SerializeField] bool grabbable = true;
        [Tooltip("Let an open hand push this object around. Off = hands pass through it until you grab it.")]
        [SerializeField] bool pushableByHands = false;
        [Tooltip("Multiplier on the hand's velocity when this is let go.")]
        [SerializeField] float throwMultiplier = 1f;
        [SerializeField] float maxThrowSpeed = 5f;
        [Tooltip("Hang where it was placed (no gravity) until first picked up, like a key on a hook.")]
        [SerializeField] bool holdInPlaceUntilGrabbed = false;
        [Tooltip("If it falls below this height (out of the world), put it back where it started.")]
        [SerializeField] float returnHomeBelowY = -2f;

        public UnityEvent<PlayerHand> onGrabbed = new UnityEvent<PlayerHand>();
        public UnityEvent<PlayerHand> onReleased = new UnityEvent<PlayerHand>();

        public Rigidbody Body { get; private set; }
        public Collider[] Colliders { get; private set; }
        public PlayerHand Holder { get; private set; }
        public bool IsHeld => Holder != null;
        public bool PushableByHands => pushableByHands;
        public float LastReleaseTime { get; private set; } = float.NegativeInfinity;

        public bool IsGrabbable
        {
            get => grabbable;
            set
            {
                grabbable = value;
                if (!value && Holder) Holder.Release(false);
            }
        }

        Vector3 _localPosition;
        Quaternion _localRotation = Quaternion.identity;
        bool _wasKinematic;
        RigidbodyInterpolation _interpolation;
        Pose _home;
        bool _homeKinematic;
        bool _waitingForFirstGrab;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Colliders = GetComponentsInChildren<Collider>(true);
            _home = new Pose(transform.position, transform.rotation);
            _homeKinematic = Body.isKinematic;
            if (holdInPlaceUntilGrabbed)
            {
                Body.isKinematic = true;
                _waitingForFirstGrab = true;
            }
        }

        void Update()
        {
            if (!IsHeld && transform.position.y < returnHomeBelowY) ReturnHome();
        }

        /// <summary>Put it back where it started (and back on its hook, if it hangs until grabbed).</summary>
        public void ReturnHome()
        {
            if (Holder) Holder.Release(false);
            if (!Body.isKinematic)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }
            Body.isKinematic = holdInPlaceUntilGrabbed || _homeKinematic;
            _waitingForFirstGrab = holdInPlaceUntilGrabbed;
            transform.SetPositionAndRotation(_home.position, _home.rotation);
            Body.position = _home.position;
            Body.rotation = _home.rotation;
        }

        void OnEnable()
        {
            s_all.Add(this);
            Registered?.Invoke(this);
        }

        void OnDisable()
        {
            s_all.Remove(this);
            if (Holder) Holder.Release(false);
        }

        public bool CanBeGrabbedBy(PlayerHand hand) => grabbable && isActiveAndEnabled;

        internal void BeginGrab(PlayerHand hand, Pose handPose)
        {
            if (Holder != null && Holder != hand)
            {
                // Hand-to-hand transfer: the old hand just lets go, physics state stays "held".
                var previous = Holder;
                Holder = null;
                previous.OnTakenByOtherHand(this);
                if (previous.Locomotion) previous.Locomotion.SetBodyCollisionIgnored(this, false);
            }
            else
            {
                // Something hanging on a hook falls normally once it's been picked up.
                _wasKinematic = !_waitingForFirstGrab && Body.isKinematic;
                _waitingForFirstGrab = false;
                _interpolation = Body.interpolation;
            }

            Holder = hand;
            if (!Body.isKinematic)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }
            Body.isKinematic = true;
            Body.interpolation = RigidbodyInterpolation.None; // we pose it every frame ourselves

            Quaternion inv = Quaternion.Inverse(handPose.rotation);
            _localPosition = inv * (transform.position - handPose.position);
            _localRotation = inv * transform.rotation;
            onGrabbed.Invoke(hand);
        }

        internal void FollowHand(Pose handPose)
        {
            transform.SetPositionAndRotation(
                handPose.position + handPose.rotation * _localPosition,
                handPose.rotation * _localRotation);
        }

        internal void EndGrab(PlayerHand hand, Vector3 velocity, Vector3 angularVelocity)
        {
            if (Holder != hand) return;
            Holder = null;
            LastReleaseTime = Time.time;

            Body.isKinematic = _wasKinematic;
            Body.interpolation = _interpolation;
            if (!Body.isKinematic)
            {
                velocity *= throwMultiplier;
                Body.linearVelocity = Vector3.ClampMagnitude(velocity, maxThrowSpeed);
                Body.angularVelocity = angularVelocity;
            }
            onReleased.Invoke(hand);
        }
    }
}
