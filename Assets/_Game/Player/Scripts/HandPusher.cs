using System.Collections.Generic;
using EscapeKitchen.Interaction;
using UnityEngine;

namespace EscapeKitchen.Player
{
    /// <summary>
    /// Gives a tracked hand a physical presence: small kinematic spheres on the palm and fingertips
    /// that shove dynamic rigidbodies (e.g. <see cref="Pushable"/> furniture, doors, toys).
    /// They never collide with the player's own body, and they ignore <see cref="Grabbable"/>s
    /// unless that object opts in with "Pushable By Hands" (and even then not while you reach to grab it).
    /// </summary>
    [RequireComponent(typeof(PlayerHand))]
    public class HandPusher : MonoBehaviour
    {
        [SerializeField] float palmRadius = 0.035f;
        [SerializeField] float fingertipRadius = 0.012f;
        [SerializeField] bool fingertips = true;
        [Tooltip("If the hand jumps further than this in one physics step (tracking pop), teleport instead of sweeping through things.")]
        [SerializeField] float maxStep = 0.25f;
        [Tooltip("Above this pinch/grip the hand counts as 'reaching to grab' and stops pushing nearby grabbables.")]
        [SerializeField, Range(0f, 1f)] float closingThreshold = 0.3f;
        [Tooltip("Seconds after letting go before the hand can push that object again.")]
        [SerializeField] float releaseIgnoreSeconds = 0.4f;
        [Tooltip("A hand closed past this is gripping, not pushing: it stops shoving anything, so reaching " +
                 "for the edge of a stool doesn't push the stool away. Open hand = push, closing hand = grip.")]
        [SerializeField, Range(0f, 1f)] float gripIntentThreshold = 0.5f;

        struct Probe
        {
            public PlayerHand.Joint joint;
            public Rigidbody body;
            public SphereCollider collider;
        }

        static readonly HashSet<Collider> s_probeColliders = new HashSet<Collider>();
        static readonly Collider[] s_overlaps = new Collider[16];

        /// <summary>Is this collider one of the players' hand spheres? (So hands don't grip themselves.)</summary>
        public static bool IsHandCollider(Collider c) => c != null && s_probeColliders.Contains(c);

        PlayerHand _hand;
        readonly List<Probe> _probes = new List<Probe>();
        readonly Dictionary<Grabbable, bool> _ignored = new Dictionary<Grabbable, bool>();
        Transform _root;
        bool _started;

        void Awake()
        {
            _hand = GetComponent<PlayerHand>();

            // Probes live outside the rig hierarchy so they are moved only by MovePosition and
            // hand their real velocity to whatever they hit.
            _root = new GameObject($"{name} Push Colliders").transform;
            _root.gameObject.hideFlags = HideFlags.DontSave;
            AddProbe(PlayerHand.Joint.Palm, palmRadius);
            if (fingertips)
            {
                AddProbe(PlayerHand.Joint.ThumbTip, fingertipRadius);
                AddProbe(PlayerHand.Joint.IndexTip, fingertipRadius);
                AddProbe(PlayerHand.Joint.MiddleTip, fingertipRadius);
                AddProbe(PlayerHand.Joint.RingTip, fingertipRadius);
                AddProbe(PlayerHand.Joint.LittleTip, fingertipRadius);
            }
        }

        void Start()
        {
            Grabbable.Registered += OnGrabbableRegistered;
            _started = true;
            RebuildFiltering();
        }

        /// <summary>
        /// Unity drops IgnoreCollision pairs when a collider is deactivated, so this re-applies them
        /// all (player body + grabbables) after the probes come back.
        /// </summary>
        void RebuildFiltering()
        {
            var body = _hand.Locomotion ? _hand.Locomotion.Body : null;
            if (body)
                foreach (var p in _probes) Physics.IgnoreCollision(p.collider, body, true);

            _ignored.Clear();
            foreach (var g in Grabbable.All) OnGrabbableRegistered(g);
        }

        void OnDestroy()
        {
            Grabbable.Registered -= OnGrabbableRegistered;
            foreach (var p in _probes) s_probeColliders.Remove(p.collider);
            if (_root) Destroy(_root.gameObject);
        }

        void OnEnable()
        {
            if (_root) _root.gameObject.SetActive(true);
            if (_started) RebuildFiltering();
        }

        void OnDisable()
        {
            if (_root) _root.gameObject.SetActive(false);
        }

        void AddProbe(PlayerHand.Joint joint, float radius)
        {
            var go = new GameObject($"{joint}");
            go.transform.SetParent(_root, false);
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.detectCollisions = false;
            var col = go.AddComponent<SphereCollider>();
            col.radius = radius;
            s_probeColliders.Add(col);
            _probes.Add(new Probe { joint = joint, body = body, collider = col });
        }

        void FixedUpdate()
        {
            // A hand gripping the floor or a climbable (or closing to grip one) shouldn't also shove
            // what it's holding on to.
            bool gripping = Mathf.Max(_hand.PinchStrength, _hand.GripStrength) >= gripIntentThreshold
                            || _hand.IsBusy
                            || (_hand.Locomotion && _hand.Locomotion.IsPlanted(_hand));
            bool active = _hand.IsTracked && !gripping;
            for (int i = 0; i < _probes.Count; i++)
            {
                var p = _probes[i];
                if (!active || !_hand.TryGetJoint(p.joint, out Vector3 target))
                {
                    // Turning detectCollisions off (rather than disabling the collider) keeps the
                    // IgnoreCollision pairs set up below.
                    p.body.detectCollisions = false;
                    continue;
                }

                if (!p.body.detectCollisions || Vector3.Distance(p.body.position, target) > maxStep)
                {
                    // Coming back (hand opened, tracking returned): wait until the probe is clear,
                    // or it would pop out of whatever it's inside and fling it.
                    if (InsideDynamicBody(target, p.collider.radius)) { p.body.detectCollisions = false; continue; }
                    p.body.position = target;
                    p.body.transform.position = target;
                    p.body.detectCollisions = true;
                }
                else
                {
                    p.body.MovePosition(target);
                }
            }

            UpdateGrabbableFiltering();
        }

        static bool InsideDynamicBody(Vector3 position, float radius)
        {
            int n = Physics.OverlapSphereNonAlloc(position, radius, s_overlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = s_overlaps[i];
                if (s_probeColliders.Contains(c)) continue;
                var rb = c.attachedRigidbody;
                if (rb == null || rb.isKinematic) continue;
                var g = rb.GetComponent<Grabbable>();
                if (g != null && !g.PushableByHands) continue; // hands pass through those anyway
                return true;
            }
            return false;
        }

        void OnGrabbableRegistered(Grabbable g)
        {
            if (g == null) return;
            // A re-enabled grabbable lost its IgnoreCollision pairs, so forget the cached state.
            _ignored.Remove(g);
            // Grabbables that don't opt in are never pushed by hands, so reaching for them
            // doesn't knock them away.
            SetIgnored(g, !g.PushableByHands);
        }

        void UpdateGrabbableFiltering()
        {
            bool closing = Mathf.Max(_hand.PinchStrength, _hand.GripStrength) >= closingThreshold;
            var all = Grabbable.All;
            for (int i = 0; i < all.Count; i++)
            {
                var g = all[i];
                if (!g.PushableByHands) continue;
                bool ignore = g.IsHeld
                              || Time.time - g.LastReleaseTime < releaseIgnoreSeconds
                              || (closing && ContainsNearby(g));
                SetIgnored(g, ignore);
            }
        }

        bool ContainsNearby(Grabbable g)
        {
            foreach (var n in _hand.NearbyGrabbables)
                if (n == g) return true;
            return false;
        }

        void SetIgnored(Grabbable g, bool ignore)
        {
            if (_ignored.TryGetValue(g, out bool current) && current == ignore) return;
            _ignored[g] = ignore;
            if (g.Colliders == null) return;
            foreach (var p in _probes)
                foreach (var c in g.Colliders)
                    if (c) Physics.IgnoreCollision(p.collider, c, ignore);
        }
    }
}
