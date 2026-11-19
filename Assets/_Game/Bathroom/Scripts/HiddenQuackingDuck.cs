using UnityEngine;
using UnityEngine.Events;
using EscapeKitchen.Interaction;
using EscapeKitchen.Player;

namespace EscapeBathroom
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource), typeof(Rigidbody))]
    public sealed class HiddenQuackingDuck : MonoBehaviour
    {
        public AudioClip quackClip;
        public AudioClip foundClip;
        [Tooltip("The crawling rig's hand-grabbable duck. Picking it up is the only win requirement.")]
        public Grabbable handGrabbable;
        [Min(.5f)] public float minimumQuackInterval = 2.2f;
        [Min(.5f)] public float maximumQuackInterval = 3.1f;
        public UnityEvent onFound = new UnityEvent();
        public bool Found { get; private set; }
        public bool IsHeld => owner != null || (handGrabbable != null && handGrabbable.IsHeld);
        public Object Holder => owner != null ? owner : handGrabbable != null ? handGrabbable.Holder : null;
        public int QuackCount { get; private set; }
        public int FoundCount { get; private set; }
        public float NextQuackTime { get; private set; }
        public AudioSource Voice { get; private set; }
        public Rigidbody Body { get; private set; }
        Object owner;
        Vector3 homePosition;
        Quaternion homeRotation;
        Vector3 targetPosition;
        Quaternion targetRotation;
        Vector3 previousTarget;
        Vector3 releaseVelocity;

        void Awake()
        {
            Voice = GetComponent<AudioSource>();
            Body = GetComponent<Rigidbody>();
            if (handGrabbable == null) handGrabbable = GetComponent<Grabbable>();
            homePosition = transform.position;
            homeRotation = transform.rotation;
            NextQuackTime = Time.time + .7f;
        }

        void OnEnable()
        {
            if (handGrabbable != null) handGrabbable.onGrabbed.AddListener(OnHandGrabbed);
        }

        void OnHandGrabbed(PlayerHand hand)
        {
            // A real pickup must already own the duck; merely reaching it is not a win.
            if (hand != null && handGrabbable != null && handGrabbable.Holder == hand)
                CompleteFind();
        }

        void CompleteFind()
        {
            if (Found) return;
            Found = true;
            FoundCount++;
            Voice.Stop();
            Voice.pitch = 1f;
            if (foundClip != null) Voice.PlayOneShot(foundClip, .8f);
            onFound.Invoke();
            Debug.Log("Bathroom: hidden duck picked up; exit unlocked.", this);
        }

        void Update()
        {
            if (!Found && Time.time >= NextQuackTime) QuackNow();
        }

        public bool QuackNow()
        {
            if (Found || quackClip == null || !isActiveAndEnabled) return false;
            Voice.pitch = Random.Range(.96f, 1.04f);
            Voice.clip = quackClip;
            Voice.Play();
            QuackCount++;
            float interval = Random.Range(Mathf.Max(.8f, minimumQuackInterval), Mathf.Max(minimumQuackInterval, maximumQuackInterval));
            // Play the supplied recording in full, with a short pause between repeats.
            NextQuackTime = Time.time + Mathf.Max(interval, quackClip.length / Voice.pitch + .35f);
            return true;
        }

        public bool TryGrab(Object newOwner)
        {
            if (handGrabbable != null || newOwner == null || IsHeld || !isActiveAndEnabled) return false;
            owner = newOwner;
            Body.isKinematic = true;
            Body.useGravity = false;
            targetPosition = previousTarget = Body.position;
            targetRotation = Body.rotation;
            releaseVelocity = Vector3.zero;
            CompleteFind();
            return true;
        }

        public void MoveHeld(Vector3 position, Quaternion rotation)
        {
            if (owner == null) return;
            releaseVelocity = Vector3.ClampMagnitude((position - previousTarget) / Mathf.Max(.001f, Time.deltaTime), 2f);
            previousTarget = position;
            targetPosition = position;
            targetRotation = rotation;
        }

        public bool Release(Object currentOwner)
        {
            if (owner == null || currentOwner != owner) return false;
            owner = null;
            Body.isKinematic = false;
            Body.useGravity = true;
            Body.linearVelocity = releaseVelocity;
            return true;
        }

        void FixedUpdate()
        {
            if (owner == null) return;
            var next = Vector3.MoveTowards(Body.position, targetPosition, 5f * Time.fixedDeltaTime);
            var delta = next - Body.position;
            if (delta.sqrMagnitude > .000001f && Body.SweepTest(delta.normalized, out var hit, delta.magnitude + .003f, QueryTriggerInteraction.Ignore))
                next = Body.position + delta.normalized * Mathf.Max(0f, hit.distance - .003f);
            Body.MovePosition(next);
            Body.MoveRotation(Quaternion.RotateTowards(Body.rotation, targetRotation, 720f * Time.fixedDeltaTime));
        }

        [ContextMenu("Restart Duck Hunt")]
        public void RestartHunt()
        {
            if (!Application.isPlaying) return;
            if (handGrabbable != null) handGrabbable.ReturnHome();
            owner = null;
            Found = false;
            FoundCount = QuackCount = 0;
            Voice.Stop();
            Body.isKinematic = true;
            Body.useGravity = handGrabbable != null;
            Body.position = homePosition;
            Body.rotation = homeRotation;
            transform.SetPositionAndRotation(homePosition, homeRotation);
            NextQuackTime = Time.time + .7f;
        }

        void OnDisable()
        {
            if (Voice != null) Voice.Stop();
            if (handGrabbable != null) handGrabbable.onGrabbed.RemoveListener(OnHandGrabbed);
            if (owner != null) Release(owner);
        }
    }
}
