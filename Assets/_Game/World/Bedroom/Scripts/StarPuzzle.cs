using UnityEngine;
using UnityEngine.Events;

// Bedroom puzzle: put each glowing star into its matching outline on the floor before time runs out.
public class StarPuzzle : MonoBehaviour
{
    [System.Serializable]
    public class StarSlot
    {
        public Rigidbody star;      // the grabbable star
        public Transform outline;   // where this star belongs
        [HideInInspector] public bool placed;
    }

    [Header("Puzzle")]
    public StarSlot[] slots;
    [Tooltip("How close (meters) a released star must be to its own outline to snap in.")]
    public float snapDistance = 0.2f;

    [Header("Timer")]
    [Tooltip("Off = no countdown; the puzzle only ends when all stars are placed.")]
    public bool useTimer = false;
    [Tooltip("Seconds the player has to finish the puzzle.")]
    public float timeLimit = 90f;
    public TextMesh statusText;

    [Header("Results")]
    public GameObject nightLight;   // turned on when the player wins
    public UnityEvent onWin;
    public UnityEvent onLose;

    float timeLeft;
    bool finished;

    void Start()
    {
        timeLeft = timeLimit;
        if (nightLight) nightLight.SetActive(false);
        UpdateText();
    }

    void Update()
    {
        if (finished) return;

        if (useTimer) timeLeft -= Time.deltaTime;

        foreach (var s in slots)
        {
            if (s.placed || s.star == null || s.outline == null) continue;
            // Grabbable makes the star kinematic while it is held, so non-kinematic = released.
            bool released = !s.star.isKinematic;
            if (released && Vector3.Distance(s.star.position, s.outline.position) < snapDistance)
                Place(s);
        }

        if (PlacedCount() == slots.Length) { Win(); return; }
        if (useTimer && timeLeft <= 0f) { Lose(); return; }
        UpdateText();
    }

    void Place(StarSlot s)
    {
        s.placed = true;
        s.star.isKinematic = true;
        s.star.transform.SetPositionAndRotation(s.outline.position, s.outline.rotation);
        SetGrabbable(s.star.gameObject, false);
        s.outline.gameObject.SetActive(false);
    }

    void SetGrabbable(GameObject star, bool on)
    {
        // Works with Zhi's PlayerRig grab system and Meta's: turn off any component named Grabbable.
        foreach (var b in star.GetComponents<Behaviour>())
            if (b != null && b.GetType().Name == "Grabbable") b.enabled = on;
        foreach (Transform child in star.transform)
            if (child.name.Contains("GrabInteraction")) child.gameObject.SetActive(on);
    }

    int PlacedCount()
    {
        int n = 0;
        foreach (var s in slots) if (s.placed) n++;
        return n;
    }

    void Win()
    {
        finished = true;
        if (nightLight) nightLight.SetActive(true);
        UpdateText(); // final count; the run's own "You escaped" message comes at the very end
        onWin.Invoke();
    }

    void Lose()
    {
        finished = true;
        timeLeft = 0f;
        foreach (var s in slots) if (!s.placed && s.star) SetGrabbable(s.star.gameObject, false);
        if (statusText) statusText.text = "Time's up!";
        onLose.Invoke();
    }

    void UpdateText()
    {
        if (!statusText) return;
        if (!useTimer) { statusText.text = string.Format("Stars {0}/{1}", PlacedCount(), slots.Length); return; }
        int t = Mathf.CeilToInt(Mathf.Max(timeLeft, 0f));
        statusText.text = string.Format("Stars {0}/{1}   {2}:{3:00}", PlacedCount(), slots.Length, t / 60, t % 60);
    }
}
