using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace EscapeKitchen.Flow
{
    /// <summary>
    /// One escape attempt across all the room scenes. The timer starts when the first room loads and keeps
    /// running while <see cref="RoomExit"/>s load the next rooms. It shows at the bottom-left of the view,
    /// rooms fade through black, and the end shows "You escaped in m:ss".
    /// Created by the first RoomExit and kept across scene loads; nothing needs to be placed in a scene.
    /// </summary>
    public sealed class EscapeRun : MonoBehaviour
    {
        public static EscapeRun Instance { get; private set; }

        /// <summary>Seconds since the run started (stops when you escape).</summary>
        public float Elapsed { get; private set; }
        public bool Escaped { get; private set; }
        public bool Loading { get; private set; }

        const float FadeTime = .35f;
        // The HUD is a canvas 0.5 m in front of the eye; one canvas unit is 0.5 mm.
        const float HudDistance = .5f, HudScale = .0005f, HudSize = 4000f;

        Transform hud;
        Text timerText, winText;
        Image fade;
        GameObject winPanel;
        Material overlayMaterial;
        AudioSource audioSource;
        AudioClip winSound;
        Camera head;
        bool holdHud;

        public static EscapeRun Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("EscapeRun");
            DontDestroyOnLoad(go);
            return go.AddComponent<EscapeRun>();
        }

        /// <summary>The player's eye in the current room, or null.</summary>
        public Transform Head
        {
            get { var c = FindHead(); return c != null ? c.transform : null; }
        }

        public static string Format(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            winSound = MakeChime();
            BuildHud();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (hud != null) Destroy(hud.gameObject);
            if (overlayMaterial != null) Destroy(overlayMaterial);
            if (winSound != null) Destroy(winSound);
        }

        void Update()
        {
            if (!Escaped) Elapsed += Time.unscaledDeltaTime;
            AttachHud();
            timerText.text = Format(Elapsed);
        }

        /// <summary>Fade out, load the next room, fade back in. The timer keeps running.</summary>
        public void GoTo(string sceneName)
        {
            if (Loading || Escaped) return;
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"EscapeRun: scene '{sceneName}' isn't in the build's scene list, so the exit can't load it.", this);
                return;
            }
            StartCoroutine(GoToRoutine(sceneName));
        }

        /// <summary>Stop the timer and show the escape time.</summary>
        public void Win()
        {
            if (Escaped) return;
            Escaped = true;
            if (hud == null) BuildHud();
            string time = Format(Elapsed);
            timerText.text = time;
            winText.text = "You escaped in " + time;
            winPanel.SetActive(true);
            audioSource.PlayOneShot(winSound, .6f);
            Debug.Log("EscapeRun: escaped in " + time, this);
        }

        IEnumerator GoToRoutine(string sceneName)
        {
            Loading = true;
            yield return Fade(1f);
            // Keep the HUD (and the black) out of the room being unloaded.
            holdHud = true;
            hud.SetParent(transform, false);
            var load = SceneManager.LoadSceneAsync(sceneName);
            while (load != null && !load.isDone) yield return null;
            holdHud = false;
            head = null;
            AttachHud();
            yield return null; // let the new rig take its first tracked pose
            yield return Fade(0f);
            Loading = false;
        }

        IEnumerator Fade(float to)
        {
            if (hud == null) BuildHud();
            float from = fade.color.a;
            for (float t = 0f; t < FadeTime; t += Time.unscaledDeltaTime)
            {
                SetFade(Mathf.Lerp(from, to, t / FadeTime));
                yield return null;
            }
            SetFade(to);
        }

        void SetFade(float alpha)
        {
            fade.color = new Color(0f, 0f, 0f, alpha);
            fade.enabled = alpha > .001f;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            head = null;
            if (!holdHud) AttachHud();
        }

        Camera FindHead()
        {
            if (head == null || !head.isActiveAndEnabled) head = Camera.main;
            return head;
        }

        // Head-locked: parented to the eye camera of whichever room is loaded.
        void AttachHud()
        {
            if (hud == null) BuildHud();
            if (holdHud) return;
            var cam = FindHead();
            if (cam == null || hud.parent == cam.transform) return;
            hud.SetParent(cam.transform, false);
            hud.localPosition = new Vector3(0f, 0f, HudDistance);
            hud.localRotation = Quaternion.identity;
            hud.localScale = Vector3.one * HudScale;
        }

        void BuildHud()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (overlayMaterial == null)
            {
                // Always drawn on top, so a wall or table close to your face never hides the timer.
                overlayMaterial = new Material(Graphic.defaultGraphicMaterial) { name = "Escape HUD (overlay)" };
                overlayMaterial.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            }

            var root = new GameObject("Escape HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            hud = root.transform;
            hud.SetParent(transform, false);
            hud.localScale = Vector3.one * HudScale;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 1000;
            root.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f; // sharper text
            ((RectTransform)hud).sizeDelta = new Vector2(HudSize, HudSize);

            fade = MakeImage(hud, "Fade", Vector2.zero, new Vector2(HudSize, HudSize), new Color(0f, 0f, 0f, 0f));
            fade.enabled = false;

            // Bottom-left of the view: about 20 degrees left and 15 degrees down.
            timerText = MakeText(hud, "Timer", new Vector2(-380f, -270f), new Vector2(500f, 110f), 64, TextAnchor.MiddleLeft, font);
            ((RectTransform)timerText.transform).pivot = new Vector2(0f, .5f);
            timerText.text = Format(Elapsed);

            var panel = MakeImage(hud, "Escaped", new Vector2(0f, 60f), new Vector2(1500f, 240f), new Color(0f, 0f, 0f, .6f));
            winPanel = panel.gameObject;
            winText = MakeText(panel.transform, "Text", Vector2.zero, new Vector2(1500f, 240f), 96, TextAnchor.MiddleCenter, font);
            winPanel.SetActive(Escaped);
            if (Escaped) winText.text = "You escaped in " + Format(Elapsed);
        }

        Image MakeImage(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.material = overlayMaterial;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        Text MakeText(Transform parent, string name, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment, Font font)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.material = overlayMaterial;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, .85f);
            outline.effectDistance = new Vector2(3f, -3f);
            return text;
        }

        // A short rising arpeggio, generated so the project needs no audio file.
        static AudioClip MakeChime()
        {
            const int rate = 22050;
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
            const float step = .16f, tail = .7f;
            var samples = new float[Mathf.CeilToInt(rate * (step * (notes.Length - 1) + tail))];
            for (int n = 0; n < notes.Length; n++)
            {
                int start = Mathf.RoundToInt(n * step * rate);
                int length = Mathf.RoundToInt(tail * rate);
                for (int i = 0; i < length && start + i < samples.Length; i++)
                {
                    float t = i / (float)rate;
                    float envelope = Mathf.Min(1f, t * 80f) * Mathf.Exp(-t * 5f);
                    samples[start + i] += Mathf.Sin(2f * Mathf.PI * notes[n] * t) * envelope * .22f;
                }
            }
            var clip = AudioClip.Create("Escaped", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
