using UnityEngine;

namespace EscapeBathroom
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DoorScript.Door))]
    public sealed class BathroomWinDoor : MonoBehaviour
    {
        public HiddenQuackingDuck hunt;
        public DoorScript.Door door;
        public bool OpenedForWin { get; private set; }

        void Awake()
        {
            if (door == null) door = GetComponent<DoorScript.Door>();
            door.open = false;
        }

        void OnEnable()
        {
            if (hunt != null) hunt.onFound.AddListener(OpenForWin);
        }

        void Start()
        {
            // Handles enabling this exit after the hunt has already completed.
            if (hunt != null && hunt.Found) OpenForWin();
        }

        public void OpenForWin()
        {
            if (OpenedForWin || hunt == null || !hunt.Found || door == null) return;
            OpenedForWin = true;
            if (!door.open)
            {
                if (door.asource == null) door.asource = door.GetComponent<AudioSource>();
                door.OpenDoor();
            }
        }

        void Update()
        {
            // The duck's Restart Duck Hunt command also closes and re-locks the exit.
            if (OpenedForWin && hunt != null && !hunt.Found)
            {
                OpenedForWin = false;
                door.open = false;
            }
        }

        void OnDisable()
        {
            if (hunt != null) hunt.onFound.RemoveListener(OpenForWin);
        }
    }
}
