using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace EscapeKitchen.Interaction
{
    /// <summary>
    /// A door that swings open around its hinge when told to (e.g. by a <see cref="KeyLock"/>).
    /// The hinge is this object's pivot; the door turns around its local Y axis.
    /// </summary>
    [DisallowMultipleComponent]
    public class SwingDoor : MonoBehaviour
    {
        [Tooltip("Degrees around the hinge. Try the sign both ways: one swings into the room, the other out of it.")]
        [SerializeField] float openAngle = -100f;
        [SerializeField] float openSeconds = 1.6f;

        public UnityEvent onOpened = new UnityEvent();

        public bool IsOpen { get; private set; }

        Quaternion _closed;

        void Awake()
        {
            _closed = transform.localRotation;
            // Moving colliders belong on a kinematic rigidbody.
            if (GetComponent<Rigidbody>() == null)
            {
                var body = gameObject.AddComponent<Rigidbody>();
                body.isKinematic = true;
            }
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            StartCoroutine(Swing());
        }

        IEnumerator Swing()
        {
            for (float t = 0f; t < openSeconds; t += Time.deltaTime)
            {
                // Quick start, gentle stop, like a door given a shove.
                float k = 1f - Mathf.Pow(1f - t / openSeconds, 3f);
                transform.localRotation = _closed * Quaternion.Euler(0f, openAngle * k, 0f);
                yield return null;
            }
            transform.localRotation = _closed * Quaternion.Euler(0f, openAngle, 0f);
            onOpened.Invoke();
        }
    }
}
