using UnityEngine;
using UnityEngine.Events;

namespace EscapeKitchen.Flow
{
    /// <summary>
    /// Slides a door's parts (panel, handle) aside when <see cref="Open"/> is called, e.g. from a puzzle's win event.
    /// The parts must not be Batching Static, or they stay drawn where they started.
    /// </summary>
    public sealed class SlidingDoor : MonoBehaviour
    {
        public Transform[] parts;
        [Tooltip("How far the parts slide when open, in this object's local space.")]
        public Vector3 openOffset = new Vector3(0f, 0f, 1.2f);
        public float duration = 1.5f;
        public AudioClip openSound;
        public UnityEvent onOpened = new UnityEvent();

        public bool IsOpen { get; private set; }
        Vector3[] closed;
        float progress;

        void Awake()
        {
            closed = new Vector3[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (parts[i] != null) closed[i] = transform.InverseTransformPoint(parts[i].position);
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            if (openSound != null) AudioSource.PlayClipAtPoint(openSound, transform.position, .7f);
            onOpened.Invoke();
        }

        void Update()
        {
            if (!IsOpen || progress >= 1f) return;
            progress = Mathf.MoveTowards(progress, 1f, Time.deltaTime / Mathf.Max(.01f, duration));
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            for (int i = 0; i < parts.Length; i++)
                if (parts[i] != null) parts[i].position = transform.TransformPoint(closed[i] + openOffset * eased);
        }
    }
}
