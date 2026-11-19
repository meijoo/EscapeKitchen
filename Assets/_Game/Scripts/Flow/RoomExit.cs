using UnityEngine;
using UnityEngine.Events;

namespace EscapeKitchen.Flow
{
    /// <summary>
    /// The way out of a room: a box just past the room's exit door. Once unlocked (wire <see cref="Unlock"/> to
    /// the room's solved event), putting your head into the box loads <see cref="nextScene"/>, or wins the game
    /// when nextScene is empty. Also starts the run timer (see <see cref="EscapeRun"/>) when the room loads.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoomExit : MonoBehaviour
    {
        [Tooltip("Scene to load when the player goes through (it must be in the build's scene list). " +
                 "Leave empty on the last exit: going through it wins.")]
        public string nextScene;
        [Tooltip("Size of the box, in this object's local space, centered on this object.")]
        public Vector3 size = new Vector3(1f, 2.4f, 1f);
        [Tooltip("Open from the start instead of waiting for Unlock().")]
        public bool startUnlocked;
        public UnityEvent onPassed = new UnityEvent();

        public bool Unlocked { get; private set; }
        bool passed;

        void Awake()
        {
            Unlocked = startUnlocked;
            EscapeRun.Ensure();
        }

        /// <summary>Let the player through (wire this to the room's solved / door-opened event).</summary>
        public void Unlock() => Unlocked = true;
        public void Lock() => Unlocked = false;

        void Update()
        {
            if (!Unlocked || passed) return;
            var run = EscapeRun.Instance;
            var head = run != null ? run.Head : null;
            if (head != null && Contains(head.position)) Pass();
        }

        public bool Contains(Vector3 worldPoint)
        {
            var p = transform.InverseTransformPoint(worldPoint);
            return Mathf.Abs(p.x) <= size.x * .5f && Mathf.Abs(p.y) <= size.y * .5f && Mathf.Abs(p.z) <= size.z * .5f;
        }

        /// <summary>Go through now (also handy from the component's menu while testing in Play mode).</summary>
        [ContextMenu("Go Through Now")]
        public void Pass()
        {
            if (passed || !Application.isPlaying) return;
            passed = true;
            onPassed.Invoke();
            var run = EscapeRun.Ensure();
            if (string.IsNullOrEmpty(nextScene)) run.Win();
            else run.GoTo(nextScene);
        }

        void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Application.isPlaying && Unlocked ? new Color(.2f, 1f, .3f, .9f) : new Color(1f, .6f, .1f, .9f);
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }
}
