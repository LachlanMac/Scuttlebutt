using UnityEngine;

namespace Starbelter.Arena
{
    /// <summary>
    /// Place this on a child GameObject with a trigger collider.
    /// Forwards trigger events to the parent Door.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class DoorTriggerZone : MonoBehaviour
    {
        private Door parentDoor;

        private void Awake()
        {
            parentDoor = GetComponentInParent<Door>();

            if (parentDoor == null)
            {
                Debug.LogError($"[DoorTriggerZone] No Door component found in parents of {gameObject.name}");
            }

            // Ensure collider is a trigger
            var col = GetComponent<Collider2D>();
            if (!col.isTrigger)
            {
                Debug.LogWarning($"[DoorTriggerZone] Collider on {gameObject.name} should be set as trigger");
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (parentDoor != null)
            {
                parentDoor.OnUnitEnteredZone(other);
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (parentDoor != null)
            {
                parentDoor.OnUnitExitedZone(other);
            }
        }
    }
}
