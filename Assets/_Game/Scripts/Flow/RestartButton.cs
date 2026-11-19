using EscapeKitchen.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace EscapeKitchen.Flow
{
    /// <summary>
    /// A "Hold to restart" button that floats above a hand when you turn that palm toward your face.
    /// Touch it with the other hand's index finger and hold to start the escape over: back to the first
    /// room with the timer at 0:00. Palms face the floor while crawling, so it stays out of the way.
    /// Added to the EscapeRun object automatically; works in every room.
    /// </summary>
    [RequireComponent(typeof(EscapeRun))]
    public sealed class RestartButton : MonoBehaviour
    {
        [Tooltip("Seconds the finger must stay on the button.")]
        public float holdSeconds = 1f;
        [Tooltip("How directly the palm must face your eyes for the button to appear (1 = straight at them).")]
        [Range(0f, 1f)] public float showFacing = .7f;
        [Tooltip("Once shown, it stays until the palm turns further away than this.")]
        [Range(0f, 1f)] public float hideFacing = .45f;
        [Tooltip("The hand must be raised this close to your head (metres).")]
        public float maxDistanceFromHead = .65f;
        [Tooltip("How far above the palm the button floats (metres).")]
        public float heightAbovePalm = .07f;

        const float Scale = .0005f;                              // canvas units -> metres (0.5 mm)
        static readonly Vector2 Size = new Vector2(200f, 90f);   // 10 x 4.5 cm
        static readonly Color Idle = new Color(.72f, .16f, .16f, .92f);
        static readonly Color Touched = new Color(.95f, .3f, .25f, .97f);

        EscapeRun run;
        PlayerHand[] hands = new PlayerHand[0];
        PlayerHand shownOn;
        Transform button;
        Image background;
        RectTransform fill;
        float held;

        void Awake()
        {
            run = GetComponent<EscapeRun>();
            Build();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (button != null) Destroy(button.gameObject);
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            hands = new PlayerHand[0];
            Hide();
        }

        void LateUpdate()
        {
            var head = run.Head;
            if (head == null || run.Loading) { Hide(); return; }
            if (hands.Length == 0 || hands[0] == null) hands = FindObjectsByType<PlayerHand>(FindObjectsSortMode.None);

            // The hand (if any) turned toward your face shows the button.
            PlayerHand palmUp = null;
            float bestFacing = 0f;
            foreach (var h in hands)
            {
                if (h == null || !h.isActiveAndEnabled || !h.IsTracked || h.IsBusy) continue;
                Vector3 toHead = head.position - h.PalmPosition;
                if (toHead.magnitude > maxDistanceFromHead) continue;
                float facing = Vector3.Dot(h.PalmNormal, toHead.normalized);
                if (facing >= (h == shownOn ? hideFacing : showFacing) && facing > bestFacing) { palmUp = h; bestFacing = facing; }
            }
            if (palmUp == null) { Hide(); return; }
            shownOn = palmUp;

            Vector3 position = palmUp.PalmPosition + palmUp.PalmNormal * heightAbovePalm;
            button.SetPositionAndRotation(position, Quaternion.LookRotation(position - head.position, head.up));
            if (!button.gameObject.activeSelf) button.gameObject.SetActive(true);

            // The other hand's index fingertip presses it.
            bool touching = false;
            foreach (var h in hands)
            {
                if (h == null || h == palmUp || !h.IsTracked || !h.TryGetJoint(PlayerHand.Joint.IndexTip, out var tip)) continue;
                Vector3 p = button.InverseTransformPoint(tip); // canvas units; +z is away from your eyes
                if (Mathf.Abs(p.x) <= Size.x * .5f + 20f && Mathf.Abs(p.y) <= Size.y * .5f + 20f && p.z >= -50f && p.z <= 90f)
                    touching = true;
            }

            held = touching ? held + Time.unscaledDeltaTime : Mathf.Max(0f, held - 2f * Time.unscaledDeltaTime);
            background.color = touching ? Touched : Idle;
            fill.sizeDelta = new Vector2(Size.x * Mathf.Clamp01(held / holdSeconds), fill.sizeDelta.y);
            if (held >= holdSeconds)
            {
                Hide();
                run.Restart();
            }
        }

        void Hide()
        {
            held = 0f;
            shownOn = null;
            if (button != null && button.gameObject.activeSelf) button.gameObject.SetActive(false);
        }

        void Build()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = new GameObject("Restart Button", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            button = root.transform;
            button.SetParent(transform, false);
            button.localScale = Vector3.one * Scale;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 900;
            root.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f; // sharp text this close up
            ((RectTransform)button).sizeDelta = Size;

            background = NewGraphic<Image>(button, "Background");
            Stretch((RectTransform)background.transform);
            background.color = Idle;

            var bar = NewGraphic<Image>(button, "Progress");
            fill = (RectTransform)bar.transform;
            fill.anchorMin = fill.anchorMax = fill.pivot = Vector2.zero;
            fill.anchoredPosition = Vector2.zero;
            fill.sizeDelta = new Vector2(0f, 12f);
            bar.color = Color.white;

            var label = NewGraphic<Text>(button, "Label");
            Stretch((RectTransform)label.transform);
            label.font = font;
            label.fontSize = 26;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.text = "Hold to restart";
            label.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, .6f);

            root.SetActive(false);
        }

        static T NewGraphic<T>(Transform parent, string name) where T : Graphic
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(T));
            go.transform.SetParent(parent, false);
            var g = go.GetComponent<T>();
            g.raycastTarget = false;
            return g;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
