using UnityEngine;
using System.Collections.Generic;
using Starbelter.Core;
using Starbelter.AI;
using Starbelter.Arena;

namespace Starbelter.Space
{
    /// <summary>
    /// Manages the space layer - ships, fighters, space combat.
    /// This is a separate system from Arena (not tile-based).
    /// </summary>
    public class SpaceManager : MonoBehaviour
    {
        public static SpaceManager Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private string spaceLayerName = "Space";

        [Header("References")]
        [Tooltip("Parent transform for space entities")]
        [SerializeField] private Transform spaceRoot;

        // Runtime state
        private List<SpaceVessel> vessels = new List<SpaceVessel>();
        private int spaceLayer;

        // Events
        public event System.Action<SpaceVessel> OnVesselSpawned;
        public event System.Action<SpaceVessel> OnVesselDestroyed;
        public event System.Action<SpaceVessel, Arena.Arena> OnVesselEnteredArena;

        // Properties
        public IReadOnlyList<SpaceVessel> Vessels => vessels;
        public int SpaceLayer => spaceLayer;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            spaceLayer = LayerMask.NameToLayer(spaceLayerName);
            if (spaceLayer == -1)
            {
                Debug.LogWarning($"[SpaceManager] Layer '{spaceLayerName}' not found. Space entities may not work correctly.");
            }

            if (spaceRoot == null)
            {
                spaceRoot = transform;
            }
        }

        #region Vessel Management

        /// <summary>
        /// Register a vessel with the space manager.
        /// </summary>
        public void RegisterVessel(SpaceVessel vessel)
        {
            if (vessel == null) return;

            if (!vessels.Contains(vessel))
            {
                vessels.Add(vessel);
                SetVesselLayer(vessel.gameObject);
                Debug.Log($"[SpaceManager] Registered vessel '{vessel.name}'");
            }
        }

        /// <summary>
        /// Unregister a vessel from the space manager.
        /// </summary>
        public void UnregisterVessel(SpaceVessel vessel)
        {
            if (vessel == null) return;

            if (vessels.Remove(vessel))
            {
                Debug.Log($"[SpaceManager] Unregistered vessel '{vessel.name}'");
                OnVesselDestroyed?.Invoke(vessel);
            }
        }

        /// <summary>
        /// Set the layer for a space entity and all children.
        /// </summary>
        private void SetVesselLayer(GameObject obj)
        {
            if (spaceLayer >= 0)
            {
                obj.layer = spaceLayer;
                foreach (Transform child in obj.transform)
                {
                    SetVesselLayer(child.gameObject);
                }
            }
        }

        #endregion

        #region Launch/Land

        /// <summary>
        /// Launch a vessel from an arena (e.g., fighter launching from hangar).
        /// Uses WorldManager for spawning.
        /// </summary>
        public void LaunchVesselFromArena(UnitController unit, Portal exitPortal, GameObject vesselPrefab)
        {
            if (exitPortal == null || exitPortal.OwnerArena == null)
            {
                Debug.LogError("[SpaceManager] Invalid exit portal for launch");
                return;
            }

            // Get the parent ship's space position
            Vector2 spacePosition = (Vector2)exitPortal.transform.position + exitPortal.SpaceExitOffset;

            // Use WorldManager to spawn
            if (WorldManager.Instance != null)
            {
                var entity = WorldManager.Instance.SpawnSpaceOnly(vesselPrefab, spacePosition, unit.name);
                Debug.Log($"[SpaceManager] Launched '{unit.name}' at {spacePosition}");
            }
            else
            {
                Debug.LogError("[SpaceManager] WorldManager not available for launching vessel");
            }
        }

        #endregion

        #region Space-to-Arena Transitions

        /// <summary>
        /// Land a vessel at an arena (planet surface, station dock, etc.)
        /// </summary>
        public void LandVessel(SpaceVessel vessel, Portal entryPortal)
        {
            if (vessel == null || entryPortal == null) return;

            // TODO: Full implementation
            // 1. Stop vessel movement
            // 2. Play landing sequence
            // 3. Convert vessel to arena unit
            // 4. Position at portal exit
            // 5. Register with arena

            Debug.Log($"[SpaceManager] Vessel '{vessel.name}' landing at portal '{entryPortal.PortalId}'");

            // Unregister from space
            UnregisterVessel(vessel);

            // Hand off to ArenaManager
            if (ArenaManager.Instance != null)
            {
                // TODO: Get or create unit from vessel
                // ArenaManager.Instance.TransitionFromSpace(unit, entryPortal);
            }

            OnVesselEnteredArena?.Invoke(vessel, entryPortal.OwnerArena);
        }

        #endregion

        #region Queries

        /// <summary>
        /// Find vessels within a radius.
        /// </summary>
        public List<SpaceVessel> GetVesselsInRadius(Vector2 center, float radius)
        {
            var result = new List<SpaceVessel>();
            float radiusSqr = radius * radius;

            foreach (var vessel in vessels)
            {
                if (vessel == null) continue;

                float distSqr = ((Vector2)vessel.transform.position - center).sqrMagnitude;
                if (distSqr <= radiusSqr)
                {
                    result.Add(vessel);
                }
            }

            return result;
        }

        /// <summary>
        /// Find the closest vessel to a position.
        /// </summary>
        public SpaceVessel GetClosestVessel(Vector2 position, Team? teamFilter = null)
        {
            SpaceVessel closest = null;
            float closestDistSqr = float.MaxValue;

            foreach (var vessel in vessels)
            {
                if (vessel == null) continue;
                if (teamFilter.HasValue && vessel.Team != teamFilter.Value) continue;

                float distSqr = ((Vector2)vessel.transform.position - position).sqrMagnitude;
                if (distSqr < closestDistSqr)
                {
                    closestDistSqr = distSqr;
                    closest = vessel;
                }
            }

            return closest;
        }

        #endregion
    }
}
