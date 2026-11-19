using System.Collections;
using System.Collections.Generic;
using EscapeKitchen.Interaction;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EscapeKitchen.Player
{
    /// <summary>
    /// Hand-tracked crawling, climbing style. Close a hand (fist or pinch) near the floor and it
    /// grips the floor: pulling it toward you drags your body along 1:1, like hauling yourself
    /// hand over hand. Open the hand and you stop dead, no sliding. Alternate hands to crawl.
    /// Close a hand on a <see cref="Climbable"/> (chair, box, sofa) and pulling down lifts you up
    /// onto it; let go in the air and you drop. (AnchorGesture can also allow a flat palm pressed
    /// on the floor.) Lives on the PlayerRig root
    /// next to OVRCameraRig, and moves the rig through a CharacterController so walls and furniture block you.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(50)] // after OVRCameraRig / OVRSkeleton update, before PlayerHand (60)
    public class CrawlLocomotion : MonoBehaviour
    {
        public enum HeightMode
        {
            /// <summary>Shift the view so the eyes sit at crawl height whatever the player's real posture.</summary>
            MatchCrawlHeight,
            /// <summary>No shift: the real floor is the game floor (player physically crawls).</summary>
            RealWorld,
        }

        [Header("References")]
        [SerializeField] OVRCameraRig cameraRig;
        [SerializeField] PlayerHand leftHand;
        [SerializeField] PlayerHand rightHand;

        [Header("Height")]
        [SerializeField] HeightMode heightMode = HeightMode.MatchCrawlHeight;
        [Tooltip("Eye height above the floor while crawling (MatchCrawlHeight mode).")]
        [SerializeField] float crawlEyeHeight = 0.4f;
        [Tooltip("If the eyes stay this far from crawl height (player stood up / sat down)...")]
        [SerializeField] float postureChangeDistance = 0.3f;
        [Tooltip("...for this many seconds, re-fit the view to crawl height.")]
        [SerializeField] float postureChangeDelay = 1.5f;
        [SerializeField] float refitSpeed = 1.5f;
        [Tooltip("Never let the eyes go below this height (no peeking under the floor).")]
        [SerializeField] float minEyeHeight = 0.2f;

        [Header("Body")]
        [Tooltip("Collision radius of the body. Smaller squeezes through tighter gaps.")]
        [SerializeField] float bodyRadius = 0.12f;
        [Tooltip("Space kept between the eyes and the top of the body capsule.")]
        [SerializeField] float headClearance = 0.05f;
        [SerializeField] float gravity = -9.81f;
        [SerializeField] float maxFallSpeed = 6f;
        [Tooltip("Fall below this world height and you are put back where you started.")]
        [SerializeField] float respawnBelowY = -5f;

        public enum AnchorGesture
        {
            /// <summary>Close the hand (fist or pinch) near the floor to grip it, open it to let go.</summary>
            Grab,
            /// <summary>Press a flat palm on the floor, lift it to let go.</summary>
            PalmDown,
            /// <summary>Either of the above.</summary>
            Either,
        }

        [Header("Gripping the floor")]
        [Tooltip("Grab = fist or pinch on the floor and pull, like climbing. PalmDown = press a flat hand on the floor.")]
        [SerializeField] AnchorGesture anchorGesture = AnchorGesture.Grab;
        [Tooltip("Grab: a closed hand this close to the floor (or below it) grips the floor.")]
        [SerializeField] float grabReach = 0.2f;
        [Tooltip("PalmDown: a flat palm this close to the floor (or below it) grips the floor.")]
        [SerializeField] float plantHeight = 0.12f;
        [Tooltip("A gripping hand lets go when lifted this much higher than where it could grip.")]
        [SerializeField] float liftMargin = 0.08f;
        [Tooltip("PalmDown: how much the palm must face down (1 = straight down, 0 = sideways).")]
        [SerializeField, Range(-1f, 1f)] float minPalmDown = 0.25f;

        [Header("Climbing")]
        [Tooltip("Close a hand on a Climbable object and pull down to climb it.")]
        [SerializeField] bool allowClimbing = true;
        [Tooltip("Testing: every solid surface counts as climbable, not just objects with Climbable.")]
        [SerializeField] bool climbAnything = false;
        [Tooltip("How close a closed hand must be to a climbable surface to grip it.")]
        [SerializeField] float climbGripRadius = 0.07f;
        [SerializeField] LayerMask climbMask = ~0;
        [Tooltip("Highest grip you can haul yourself up from, measured from what you last stood on. " +
                 "A toddler pulls up onto a stool or a sofa, not a counter: taller things need a step.")]
        [SerializeField] float maxClimbHeight = 0.6f;
        [Tooltip("While hanging on, how far above the grip your feet can rise: enough to get onto the ledge, no higher.")]
        [SerializeField] float climbOverMargin = 0.05f;

        [Header("Crawl motion")]
        [Tooltip("1 = the gripping hand stays glued to the floor (recommended). Higher goes further per pull, but the hand visibly slides.")]
        [SerializeField] float pullGain = 1f;
        [Tooltip("Speed kept after letting go. 0 = stop dead, like friction on hands and knees.")]
        [SerializeField, Range(0f, 1f)] float releaseMomentum = 0f;
        [Tooltip("How quickly any kept momentum fades (per second).")]
        [SerializeField] float momentumDamping = 10f;
        [SerializeField] float maxSpeed = 3f;
        [Tooltip("Ignore single-frame hand jumps bigger than this (tracking glitches).")]
        [SerializeField] float maxHandStep = 0.12f;
        [Tooltip("Smoothing of hand-tracking jitter while gripping (Hz). Lower = steadier when the hand is still.")]
        [SerializeField] float jitterCutoff = 2f;
        [Tooltip("How quickly the smoothing gets out of the way when the hand moves fast.")]
        [SerializeField] float jitterResponsiveness = 10f;

        [Header("Turning")]
        [Tooltip("With both hands on the floor, twisting them turns you (like pivoting on your hands).")]
        [SerializeField] bool twoHandTurn = false;
        [SerializeField] float turnGain = 1f;

        [Header("Pushing with the body")]
        [Tooltip("Crawling into a dynamic rigidbody shoves it at this fraction of your speed.")]
        [SerializeField] float bodyPushStrength = 1f;

#pragma warning disable CS0414 // only read in the Editor; kept in builds so serialization matches
        [Header("Editor testing")]
        [Tooltip("In the Editor: WASD / arrows crawl, Q/E turn, R re-fits crawl height.")]
        [SerializeField] bool keyboardInEditor = true;
        [SerializeField] float keyboardSpeed = 1f;
#pragma warning restore CS0414

        [Header("Events")]
        public UnityEvent<PlayerHand> onHandPlanted = new UnityEvent<PlayerHand>();
        public UnityEvent<PlayerHand> onHandLifted = new UnityEvent<PlayerHand>();

        class HandState
        {
            public bool planted;
            public bool byGrab;          // gripped with a fist/pinch rather than a flat palm
            public bool climbing;        // holding a climbable: pulls move you in 3D, no gravity
            public float gripY;          // world height of the grip, caps how high a climb lifts you
            public int plantOrder;       // the most recent grip drives the motion
            public Vector3 lastLocal;    // smoothed palm position in tracking space
            public Vector3 frameDelta;   // smoothed palm movement this frame
            public Vector3 filtered, filterSpeed; // jitter filter state
        }

        CharacterController _body;
        readonly HandState _left = new HandState();
        readonly HandState _right = new HandState();
        Vector3 _velocity;     // horizontal crawl velocity (world)
        bool _wasGripping;
        int _plantCounter;
        float _verticalSpeed;
        float _heightOffset, _targetHeightOffset;
        bool _calibrated;
        float _offHeightTimer;
        readonly Collider[] _climbHits = new Collider[16];
        Vector3 _spawnPosition;
        Quaternion _spawnRotation;
        readonly Dictionary<Object, Coroutine> _pendingRestores = new Dictionary<Object, Coroutine>();
        float _lastGroundY;             // feet height the last time we stood on something
        Rigidbody _standingOn;          // rigidbody under our feet (null = static ground or airborne)
        Rigidbody _groundBodyThisMove;
        bool _groundHitThisMove;

        public CharacterController Body => _body ? _body : (_body = GetComponent<CharacterController>());
        public Vector3 Velocity => _velocity;
        public bool IsCrawling => _left.planted || _right.planted;
        public bool IsClimbing => (_left.planted && _left.climbing) || (_right.planted && _right.climbing);
        public float FloorY => transform.position.y;
        /// <summary>The rigidbody you're standing on (a stool, a box), or null on static ground or in the air.</summary>
        public Rigidbody StandingOn => _standingOn;
        public bool IsPlanted(PlayerHand hand) => StateFor(hand)?.planted ?? false;

        void Reset()
        {
            cameraRig = GetComponent<OVRCameraRig>();
            foreach (var hand in GetComponentsInChildren<PlayerHand>(true))
            {
                if (hand.Side == PlayerHand.Handedness.Left) leftHand = hand;
                else rightHand = hand;
            }
        }

        void Awake()
        {
            if (!cameraRig) cameraRig = GetComponent<OVRCameraRig>();
            _body = GetComponent<CharacterController>();
            _body.radius = bodyRadius;
            _body.stepOffset = Mathf.Min(_body.stepOffset, 0.1f);
            _body.minMoveDistance = 0f;
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            _lastGroundY = transform.position.y;
        }

        void OnEnable()
        {
            if (OVRManager.display != null) OVRManager.display.RecenteredPose += OnRecentered;
        }

        void OnDisable()
        {
            if (OVRManager.display != null) OVRManager.display.RecenteredPose -= OnRecentered;
        }

        void OnRecentered() => RefitHeight();

        /// <summary>Snap the view back to crawl height on the next tracked frame.</summary>
        public void RefitHeight() => _calibrated = false;

        void Update()
        {
            if (cameraRig == null || cameraRig.trackingSpace == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            UpdateHeight(dt);
            SyncBodyToHead();
            if (Body.isGrounded && !IsClimbing) _lastGroundY = FloorY;

            UpdateHand(leftHand, _left, dt);
            UpdateHand(rightHand, _right, dt);

            // Hand over hand, like climbing: the hand that gripped most recently drives, and the
            // other one just rests until it lets go and reaches for the next grip.
            HandState driver = null;
            if (_left.planted) driver = _left;
            if (_right.planted && (driver == null || _right.plantOrder > driver.plantOrder)) driver = _right;

            Vector3 move;
            if (driver != null)
            {
                // Keep the gripping hand where it is: the body moves the other way. On the floor
                // only sideways; on a climbable, up and down too (pull down to rise).
                move = -cameraRig.trackingSpace.TransformVector(driver.frameDelta) * pullGain;
                if (!driver.climbing) move.y = 0f;
                else move.y = Mathf.Min(move.y, Mathf.Max(driver.gripY + climbOverMargin - FloorY, 0f));
                move = Vector3.ClampMagnitude(move, maxSpeed * dt);
                _velocity = new Vector3(move.x, 0f, move.z) / dt;
                _wasGripping = true;
            }
            else
            {
                if (_wasGripping)
                {
                    _velocity *= releaseMomentum; // 0 = no slide after letting go
                    _wasGripping = false;
                }
                _velocity *= Mathf.Exp(-momentumDamping * dt);
                if (_velocity.sqrMagnitude < 1e-4f) _velocity = Vector3.zero;
                move = _velocity * dt;
            }

            move += KeyboardMove(dt);

            if (twoHandTurn && _left.planted && _right.planted) ApplyTwoHandTurn();

            if (IsClimbing)
                _verticalSpeed = 0f; // hanging on: no gravity
            else
                _verticalSpeed = Body.isGrounded ? -1f : Mathf.Max(_verticalSpeed + gravity * dt, -maxFallSpeed);
            move.y += _verticalSpeed * dt;
            _groundHitThisMove = false;
            _groundBodyThisMove = null;
            Body.Move(move);
            if (_groundHitThisMove) _standingOn = _groundBodyThisMove;
            else if (!Body.isGrounded) _standingOn = null;

            if (transform.position.y < respawnBelowY) Respawn();
        }

        // ------------------------------------------------------------------------------------
        // Height & body
        // ------------------------------------------------------------------------------------

        void UpdateHeight(float dt)
        {
            Transform space = cameraRig.trackingSpace;
            if (heightMode == HeightMode.RealWorld)
            {
                _heightOffset = 0f;
                space.localPosition = Vector3.zero;
                return;
            }

            float rawEye = cameraRig.centerEyeAnchor.localPosition.y; // floor-level tracking origin
            bool headTracked = OVRManager.isHmdPresent && rawEye > 0.15f;

            if (!_calibrated)
            {
                if (headTracked)
                {
                    _heightOffset = _targetHeightOffset = crawlEyeHeight - rawEye;
                    _offHeightTimer = 0f;
                    _calibrated = true;
                }
            }
            else if (headTracked)
            {
                // Player changed posture (stood up, sat down)? Smoothly re-fit after a moment, so
                // quick ducks and peeks still feel 1:1.
                float eye = rawEye + _targetHeightOffset;
                _offHeightTimer = Mathf.Abs(eye - crawlEyeHeight) > postureChangeDistance
                    ? _offHeightTimer + dt
                    : 0f;
                if (_offHeightTimer > postureChangeDelay)
                {
                    _targetHeightOffset = crawlEyeHeight - rawEye;
                    _offHeightTimer = 0f;
                }
                _heightOffset = Mathf.MoveTowards(_heightOffset, _targetHeightOffset, refitSpeed * dt);
            }

            if (rawEye + _heightOffset < minEyeHeight) _heightOffset = minEyeHeight - rawEye;
            space.localPosition = new Vector3(0f, _heightOffset, 0f);
        }

        void SyncBodyToHead()
        {
            // Keep the capsule under the head so room-scale movement carries the body along.
            Vector3 head = transform.InverseTransformPoint(cameraRig.centerEyeAnchor.position);
            float eyeHeight = Mathf.Clamp(head.y, minEyeHeight, 2f);
            float height = Mathf.Max(eyeHeight + headClearance, bodyRadius * 2f);
            Body.height = height;
            Body.center = new Vector3(head.x, height * 0.5f + Body.skinWidth, head.z);
        }

        // ------------------------------------------------------------------------------------
        // Hands
        // ------------------------------------------------------------------------------------

        void UpdateHand(PlayerHand hand, HandState state, float dt)
        {
            state.frameDelta = Vector3.zero;
            if (hand == null) return;
            if (!hand.IsTracked || hand.IsBusy)
            {
                SetPlanted(hand, state, false);
                return;
            }

            Transform space = cameraRig.trackingSpace;
            Vector3 raw = space.InverseTransformPoint(hand.PalmPosition);
            float height = hand.PalmPosition.y - FloorY;
            float palmDown = Vector3.Dot(hand.PalmNormal, Vector3.down);

            if (!state.planted)
            {
                // A hand closing over a grabbable is picking that up, not gripping anything.
                bool closed = anchorGesture != AnchorGesture.PalmDown
                              && hand.IsClosed(false) && !hand.IsHoveringAnything;
                bool grounded = Body.isGrounded;
                // "Floor" is whatever you're standing on (the floor, or the top of the sofa).
                bool floorGrab = closed && grounded && height <= grabReach;
                bool climbGrab = closed && !floorGrab && allowClimbing
                                 && hand.PalmPosition.y - _lastGroundY <= maxClimbHeight
                                 && TouchingClimbable(hand);
                bool palm = anchorGesture != AnchorGesture.Grab && grounded
                            && palmDown >= minPalmDown && height <= plantHeight;
                if (!floorGrab && !climbGrab && !palm) return;

                state.byGrab = floorGrab || climbGrab;
                state.climbing = climbGrab;
                state.gripY = hand.PalmPosition.y;
                state.plantOrder = ++_plantCounter;
                state.filtered = state.lastLocal = raw;
                state.filterSpeed = Vector3.zero;
                SetPlanted(hand, state, true);
                return;
            }

            bool release = state.climbing
                ? !hand.IsClosed(true)
                : state.byGrab
                    ? !hand.IsClosed(true) || height > grabReach + liftMargin
                    : height > plantHeight + liftMargin || palmDown < minPalmDown - 0.2f;
            if (release)
            {
                SetPlanted(hand, state, false);
                return;
            }

            Vector3 local = FilterJitter(state, raw, dt);
            Vector3 delta = local - state.lastLocal;
            state.lastLocal = local;
            if (delta.magnitude > maxHandStep) return; // tracking glitch, skip this frame
            state.frameDelta = delta;
        }

        bool TouchingClimbable(PlayerHand hand)
        {
            Vector3 grip = hand.PalmPosition + hand.PalmNormal * 0.03f;
            int count = Physics.OverlapSphereNonAlloc(grip, climbGripRadius, _climbHits, climbMask,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var col = _climbHits[i];
                if (col == Body || HandPusher.IsHandCollider(col)) continue;
                if (col.GetComponentInParent<Grabbable>() != null) continue; // those get picked up
                if (climbAnything) return true;
                var climbable = col.GetComponentInParent<Climbable>();
                if (climbable != null && climbable.isActiveAndEnabled) return true;
            }
            return false;
        }

        /// <summary>
        /// One Euro filter: heavy smoothing while the hand is nearly still (kills tracking jitter,
        /// so you don't drift while resting on a grip), light smoothing when it moves fast.
        /// </summary>
        Vector3 FilterJitter(HandState s, Vector3 value, float dt)
        {
            Vector3 speed = (value - s.filtered) / dt;
            s.filterSpeed = Vector3.Lerp(s.filterSpeed, speed, SmoothingAlpha(1f, dt));
            float cutoff = jitterCutoff + jitterResponsiveness * s.filterSpeed.magnitude;
            s.filtered = Vector3.Lerp(s.filtered, value, SmoothingAlpha(cutoff, dt));
            return s.filtered;
        }

        static float SmoothingAlpha(float cutoffHz, float dt)
        {
            float tau = 1f / (2f * Mathf.PI * Mathf.Max(cutoffHz, 1e-3f));
            return 1f / (1f + tau / dt);
        }

        void SetPlanted(PlayerHand hand, HandState state, bool planted)
        {
            if (state.planted == planted) return;
            state.planted = planted;
            if (planted) onHandPlanted.Invoke(hand);
            else onHandLifted.Invoke(hand);
        }

        HandState StateFor(PlayerHand hand) =>
            hand == leftHand ? _left : hand == rightHand ? _right : null;

        void ApplyTwoHandTurn()
        {
            // Yaw change, in tracking space, of the line between the two planted palms.
            Transform space = cameraRig.trackingSpace;
            Vector3 line = space.InverseTransformPoint(rightHand.PalmPosition)
                           - space.InverseTransformPoint(leftHand.PalmPosition);
            line.y = 0f;
            if (line.sqrMagnitude < 0.01f) return; // hands too close together to read a twist

            if (!_hasTurnLine)
            {
                _turnLine = line;
                _hasTurnLine = true;
                return;
            }
            float angle = Vector3.SignedAngle(_turnLine, line, Vector3.up);
            _turnLine = line;
            if (Mathf.Abs(angle) > 15f) return; // tracking glitch
            // Hands twist one way, so you turn the other (the floor stays put under your hands).
            transform.RotateAround(cameraRig.centerEyeAnchor.position, Vector3.up, -angle * turnGain);
        }

        Vector3 _turnLine;
        bool _hasTurnLine;

        void LateUpdate()
        {
            if (!(_left.planted && _right.planted)) _hasTurnLine = false;
        }

        // ------------------------------------------------------------------------------------
        // Collisions
        // ------------------------------------------------------------------------------------

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            Rigidbody rb = hit.collider.attachedRigidbody;
            if (hit.normal.y > 0.5f)
            {
                // Something we're standing on, not something we bumped into.
                _groundHitThisMove = true;
                _groundBodyThisMove = rb;
                return;
            }
            if (rb == null || rb.isKinematic) return;
            if (IsClimbing) return; // hanging on to things, not shoving them
            // Perched on a stool or box: no leverage, so bumping the next box doesn't shove it away
            // (otherwise you could never climb from one onto the other).
            if (_standingOn != null && !_standingOn.isKinematic) return;
            Vector3 dir = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
            if (dir.magnitude < 0.5f) return; // mostly moving up/down along it (or standing on it)
            float speed = Mathf.Max(new Vector3(_velocity.x, 0f, _velocity.z).magnitude, 0.3f) * bodyPushStrength;

            var pushable = rb.GetComponent<Pushable>();
            if (pushable != null)
            {
                pushable.Push(dir, speed);
                return;
            }
            Vector3 v = rb.linearVelocity;
            float along = Vector3.Dot(v, dir.normalized);
            if (along < speed) rb.AddForceAtPosition(dir.normalized * (speed - along), hit.point, ForceMode.VelocityChange);
        }

        /// <summary>
        /// Stop (or resume) the body capsule colliding with a held object, so you can crawl
        /// while carrying it. Resuming waits until the object is clear of the body.
        /// </summary>
        public void SetBodyCollisionIgnored(Grabbable target, bool ignored)
        {
            if (target == null) return;
            SetBodyCollisionIgnored(target, target.Colliders, ignored);
        }

        /// <summary>
        /// Same, for something held through another interaction system (keyed by the held object,
        /// so a hand-to-hand pass cancels the pending restore).
        /// </summary>
        public void SetBodyCollisionIgnored(Object held, Collider[] colliders, bool ignored)
        {
            if (held == null || colliders == null) return;
            if (_pendingRestores.TryGetValue(held, out var pending))
            {
                if (pending != null) StopCoroutine(pending);
                _pendingRestores.Remove(held);
            }

            if (ignored)
            {
                foreach (var c in colliders)
                    if (c) Physics.IgnoreCollision(Body, c, true);
            }
            else if (isActiveAndEnabled)
            {
                _pendingRestores[held] = StartCoroutine(RestoreWhenClear(held, colliders));
            }
        }

        IEnumerator RestoreWhenClear(Object held, Collider[] colliders)
        {
            var wait = new WaitForSeconds(0.1f);
            float giveUp = Time.time + 3f;
            while (held != null && Time.time < giveUp && OverlapsBody(colliders))
                yield return wait;

            if (held != null && !(held is Grabbable g && g.IsHeld))
                foreach (var c in colliders)
                    if (c) Physics.IgnoreCollision(Body, c, false);
            if (held != null) _pendingRestores.Remove(held);
        }

        bool OverlapsBody(Collider[] colliders)
        {
            Bounds body = Body.bounds;
            foreach (var c in colliders)
                if (c && c.enabled && c.bounds.Intersects(body)) return true;
            return false;
        }

        void Respawn()
        {
            Body.enabled = false;
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            Body.enabled = true;
            _velocity = Vector3.zero;
            _verticalSpeed = 0f;
            _lastGroundY = _spawnPosition.y;
            _standingOn = null;
        }

        // ------------------------------------------------------------------------------------
        // Editor keyboard fallback
        // ------------------------------------------------------------------------------------

        Vector3 KeyboardMove(float dt)
        {
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            if (!keyboardInEditor) return Vector3.zero;
            var kb = Keyboard.current;
            if (kb == null) return Vector3.zero;

            float x = 0f, z = 0f, turn = 0f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) z += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) z -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
            if (kb.eKey.isPressed) turn += 1f;
            if (kb.qKey.isPressed) turn -= 1f;
            if (kb.rKey.wasPressedThisFrame) RefitHeight();

            if (turn != 0f)
                transform.RotateAround(cameraRig.centerEyeAnchor.position, Vector3.up, turn * 90f * dt);

            Vector3 forward = Vector3.ProjectOnPlane(cameraRig.centerEyeAnchor.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 1e-4f) forward = transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 dir = Vector3.ClampMagnitude(forward * z + right * x, 1f);
            return dir * keyboardSpeed * dt;
#else
            return Vector3.zero;
#endif
        }

        void OnDrawGizmosSelected()
        {
            if (cameraRig == null) return;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.5f);
            Vector3 floor = transform.position;
            Gizmos.DrawWireCube(floor + Vector3.up * plantHeight * 0.5f, new Vector3(1.5f, plantHeight, 1.5f));
        }
    }
}
