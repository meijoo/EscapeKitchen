using UnityEngine;

namespace EscapeKitchen
{
    public sealed class BottleSocket : MonoBehaviour
    {
        public BottleColor color;
        public Transform snapPoint;
        public KitchenSwingDoor[] accessDoors;
        public KitchenBottle PlacedBottle { get; private set; }
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
            PlacedBottle = bottle;
            bottle.Place(snapPoint.position);
            return true;
        }
    }
}
