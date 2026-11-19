using UnityEngine;

namespace EscapeKitchen
{
    public sealed class BottleSocket : MonoBehaviour
    {
        public BottleColor color;
        public Transform snapPoint;
        public KitchenSwingDoor[] accessDoors;
        [Tooltip("A bottle of this color touching this socket (within this gap) counts as placed, whether it's held, dropped or pushed there.")]
        public float touchDistance = .01f;
        public KitchenBottle PlacedBottle { get; private set; }
        Collider[] ownColliders;
        static readonly Collider[] overlaps = new Collider[16];
        public bool Accessible
        {
            get
            {
                foreach (var door in accessDoors) if (door != null && !door.IsOpen) return false;
                return true;
            }
        }
        public bool CanAccept(KitchenBottle bottle)
        {
            if (bottle == null || bottle.IsPlaced || bottle.color != color || PlacedBottle != null || !Accessible) return false;
            // The mesh's origin is at its base when upright. Reject sideways or distant drops.
            var delta = bottle.transform.position - snapPoint.position;
            return new Vector2(delta.x, delta.z).magnitude <= .135f && Mathf.Abs(delta.y) <= .18f;
        }
        public bool TryAccept(KitchenBottle bottle)
        {
            if (!CanAccept(bottle)) return false;
            Accept(bottle);
            return true;
        }
        /// <summary>Place the bottle if it's the right color and touching this socket.</summary>
        public bool TryAcceptTouching(KitchenBottle bottle)
        {
            if (bottle == null || bottle.IsPlaced || bottle.color != color || PlacedBottle != null || !IsTouching(bottle)) return false;
            Accept(bottle);
            return true;
        }
        public bool IsTouching(KitchenBottle bottle)
        {
            var box = bottle != null ? bottle.GetComponent<BoxCollider>() : null;
            if (box == null || !box.enabled) return false;
            if (ownColliders == null) ownColliders = GetComponentsInChildren<Collider>();
            Transform t = box.transform;
            Vector3 scale = t.lossyScale;
            Vector3 half = Vector3.Scale(box.size * .5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            // The box grown by touchDistance on every side, in the bottle's own orientation.
            int count = gameObject.scene.GetPhysicsScene().OverlapBox(t.TransformPoint(box.center), half + Vector3.one * touchDistance,
                overlaps, t.rotation, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
                foreach (var own in ownColliders)
                    if (own == overlaps[i] && own.enabled) return true;
            return false;
        }
        void Accept(KitchenBottle bottle)
        {
            PlacedBottle = bottle;
            bottle.Place(snapPoint.position);
        }
    }
}
