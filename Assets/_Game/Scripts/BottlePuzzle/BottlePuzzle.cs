using UnityEngine;

namespace EscapeKitchen
{
    public sealed class BottlePuzzle : MonoBehaviour
    {
        public BottleSocket[] sockets;
        public KitchenSwingDoor exitDoor;
        public bool Solved { get; private set; }
        public int PlacedCount
        {
            get { int count = 0; foreach (var s in sockets) if (s != null && s.PlacedBottle != null) count++; return count; }
        }
        AudioSource audioSource;
        AudioClip placedSound;
        AudioClip solvedSound;
        void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            placedSound = MakeTone("Bottle placed", .18f, 660f, 880f);
            solvedSound = MakeTone("Kitchen unlocked", .8f, 523f, 1046f);
        }
        // Let go of a bottle close to its socket (with the cabinet open).
        public bool TryPlace(KitchenBottle bottle) => Place(bottle, false);
        // A bottle touching its matching socket, however it got there.
        public bool TryPlaceTouching(KitchenBottle bottle) => Place(bottle, true);
        bool Place(KitchenBottle bottle, bool touching)
        {
            if (Solved || bottle == null || bottle.IsPlaced) return false;
            foreach (var socket in sockets)
            {
                if (socket == null || !(touching ? socket.TryAcceptTouching(bottle) : socket.TryAccept(bottle))) continue;
                if (audioSource != null) { audioSource.transform.position = socket.transform.position; audioSource.PlayOneShot(placedSound, .35f); }
                if (PlacedCount == 3)
                {
                    Solved = true;
                    exitDoor.OpenPermanently();
                    if (audioSource != null) audioSource.PlayOneShot(solvedSound, .45f);
                    Debug.Log("EscapeKitchen: all three matching bottles placed; door beside the fridge unlocked.", this);
                }
                return true;
            }
            return false;
        }
        static AudioClip MakeTone(string clipName, float duration, float start, float end)
        {
            const int rate = 22050;
            var samples = new float[Mathf.CeilToInt(rate * duration)];
            float phase = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)samples.Length;
                phase += 2f * Mathf.PI * Mathf.Lerp(start, end, t) / rate;
                samples[i] = Mathf.Sin(phase) * Mathf.Sin(Mathf.PI * t) * .3f;
            }
            var clip = AudioClip.Create(clipName, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
        void OnDestroy()
        {
            if (placedSound != null) Destroy(placedSound);
            if (solvedSound != null) Destroy(solvedSound);
        }
    }
}
