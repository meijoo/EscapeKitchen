using UnityEngine;

namespace EscapeKitchen.Interaction
{
    /// <summary>
    /// Marks an object (and its child colliders) as something the baby can climb: close a hand
    /// on it and pull down to haul yourself up, then crawl onto the top. Chairs, boxes, the sofa,
    /// low cabinets. Disable the component to make it unclimbable (e.g. until a puzzle step).
    /// </summary>
    [DisallowMultipleComponent]
    public class Climbable : MonoBehaviour
    {
    }
}
