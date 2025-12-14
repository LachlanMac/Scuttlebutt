using UnityEngine;
using System.Collections;
using Starbelter.Ship;
using Starbelter.Space;

namespace Starbelter.Arena
{
    /// <summary>
    /// Hangar entrance point where ships spawn and begin navigating to their landing zone.
    /// Links to a HangarExit on the parent SpaceVessel for space-side spawning.
    /// </summary>
    public class HangarEntrance : MonoBehaviour
    {
        [Header("Space Link")]
        [Tooltip("ID to match with HangarExit on the parent SpaceVessel")]
        [SerializeField] private string exitId = "hangar_main";

        [Header("Testing")]
        [SerializeField] private bool runSpawnTest = false;

        [Header("References")]
        [Tooltip("Available landing zones this entrance can route ships to")]
        [SerializeField] private LandingZone[] landingZones;

        [Tooltip("Prefab for the parked ship visual")]
        [SerializeField] private GameObject parkedShipPrefab;

        [Header("Settings")]
        [Tooltip("Speed at which ships move to the landing zone")]
        [SerializeField] private float approachSpeed = 3f;

        [Tooltip("Speed at which ships rotate (degrees per second)")]
        [SerializeField] private float rotationSpeed = 90f;

        // Cached references
        private Arena ownerArena;

        // Events
        public event System.Action<GameObject, ShipState, HangarExit> OnShipLaunched;

        // Properties
        public string ExitId => exitId;
        public LandingZone[] LandingZones => landingZones;
        public int AvailableSlots => GetAvailableZoneCount();
        public Arena OwnerArena => ownerArena;

        private void Start()
        {
            // Find owner arena
            ownerArena = GetComponentInParent<Arena>();

            if (runSpawnTest)
            {
                StartCoroutine(TestSpawnSequence());
            }
        }

        private IEnumerator TestSpawnSequence()
        {
            Debug.Log("[HangarEntrance] Test: Spawning first ship in 10 seconds...");
            yield return new WaitForSeconds(10f);
            SpawnShip();

            Debug.Log("[HangarEntrance] Test: Spawning second ship in 10 seconds...");
            yield return new WaitForSeconds(10f);
            SpawnShip();

            Debug.Log("[HangarEntrance] Test: Spawn test complete.");
        }

        /// <summary>
        /// Spawn a ship at this entrance with existing state (for landing from space).
        /// </summary>
        public ParkedShip SpawnShipWithState(ShipState state, GameObject parkedPrefab = null)
        {
            if (landingZones == null || landingZones.Length == 0)
            {
                Debug.LogError("[HangarEntrance] No landing zones assigned!");
                return null;
            }

            var availableZone = GetFirstAvailableZone();
            if (availableZone == null)
            {
                Debug.LogWarning("[HangarEntrance] All landing zones are occupied!");
                return null;
            }

            // Use provided prefab or default
            var prefab = parkedPrefab ?? parkedShipPrefab;
            if (prefab == null)
            {
                Debug.LogError("[HangarEntrance] No parked ship prefab available!");
                return null;
            }

            // Spawn at entrance position
            var shipObj = Instantiate(prefab, transform.position, Quaternion.identity);
            var parkedShip = shipObj.GetComponent<ParkedShip>();

            if (parkedShip == null)
            {
                parkedShip = shipObj.AddComponent<ParkedShip>();
            }

            // Initialize with state
            parkedShip.Initialize(availableZone, this, approachSpeed, rotationSpeed, state);

            // Subscribe to launch event
            parkedShip.OnLaunched += HandleShipLaunched;

            Debug.Log($"[HangarEntrance] Spawned ship (from space) heading to {availableZone.name}");
            return parkedShip;
        }

        /// <summary>
        /// Spawn a ship at this entrance and send it to an available landing zone.
        /// </summary>
        public ParkedShip SpawnShip()
        {
            if (landingZones == null || landingZones.Length == 0)
            {
                Debug.LogError("[HangarEntrance] No landing zones assigned!");
                return null;
            }

            var availableZone = GetFirstAvailableZone();
            if (availableZone == null)
            {
                Debug.LogWarning("[HangarEntrance] All landing zones are occupied!");
                return null;
            }

            if (parkedShipPrefab == null)
            {
                Debug.LogError("[HangarEntrance] No parked ship prefab assigned!");
                return null;
            }

            // Spawn at entrance position
            var shipObj = Instantiate(parkedShipPrefab, transform.position, Quaternion.identity);
            var parkedShip = shipObj.GetComponent<ParkedShip>();

            if (parkedShip == null)
            {
                parkedShip = shipObj.AddComponent<ParkedShip>();
            }

            // Initialize and start approach
            parkedShip.Initialize(availableZone, this, approachSpeed, rotationSpeed);

            // Subscribe to launch event
            parkedShip.OnLaunched += HandleShipLaunched;

            Debug.Log($"[HangarEntrance] Spawned ship heading to {availableZone.name}");
            return parkedShip;
        }

        /// <summary>
        /// Get the first unoccupied landing zone.
        /// </summary>
        private LandingZone GetFirstAvailableZone()
        {
            if (landingZones == null) return null;

            foreach (var zone in landingZones)
            {
                if (zone != null && !zone.IsOccupied)
                    return zone;
            }
            return null;
        }

        /// <summary>
        /// Get the count of available (unoccupied) landing zones.
        /// </summary>
        private int GetAvailableZoneCount()
        {
            if (landingZones == null) return 0;

            int count = 0;
            foreach (var zone in landingZones)
            {
                if (zone != null && !zone.IsOccupied)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Check if there's at least one available landing zone.
        /// </summary>
        public bool HasAvailableSlot()
        {
            return GetFirstAvailableZone() != null;
        }

        /// <summary>
        /// Handle a ship launching from this hangar.
        /// </summary>
        private void HandleShipLaunched(ParkedShip parkedShip, ShipState state)
        {
            if (parkedShip.SpacePrefab == null)
            {
                Debug.LogError("[HangarEntrance] Parked ship has no space prefab assigned!");
                return;
            }

            // Find the HangarExit on the parent vessel
            HangarExit hangarExit = null;
            if (ownerArena != null && ownerArena.ParentVessel != null)
            {
                hangarExit = ownerArena.ParentVessel.GetHangarExit(exitId);
                if (hangarExit == null)
                {
                    Debug.LogWarning($"[HangarEntrance] No HangarExit with ID '{exitId}' found on parent vessel!");
                }
            }
            else
            {
                Debug.LogWarning("[HangarEntrance] No parent vessel found - cannot determine space exit position");
            }

            Debug.Log($"[HangarEntrance] Ship launched! Exit: {hangarExit?.name ?? "unknown"}");

            // Fire event for external systems to handle space spawning
            OnShipLaunched?.Invoke(parkedShip.SpacePrefab, state, hangarExit);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // Draw entrance point
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.5f);

            // Draw lines to all landing zones
            if (landingZones != null)
            {
                foreach (var zone in landingZones)
                {
                    if (zone != null)
                    {
                        Gizmos.color = zone.IsOccupied ? Color.red : Color.yellow;
                        Gizmos.DrawLine(transform.position, zone.Position);
                    }
                }
            }
        }
#endif
    }
}
