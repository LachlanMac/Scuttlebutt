using UnityEngine;
using System.Collections.Generic;
using Starbelter.Core;
using Starbelter.AI;
using Starbelter.Space;

namespace Starbelter.Arena
{
    /// <summary>
    /// Manages all active arenas and handles transitions between them.
    /// </summary>
    public class ArenaManager : MonoBehaviour
    {
        public static ArenaManager Instance { get; private set; }

        [Header("Settings")]
        [Tooltip("The arena currently shown on main camera")]
        [SerializeField] private Arena focusedArena;

        [Header("Arena Prefabs")]
        [Tooltip("Default arena prefabs for spawning")]
        [SerializeField] private List<GameObject> arenaPrefabs = new List<GameObject>();

        // Runtime state
        private Dictionary<string, Arena> arenas = new Dictionary<string, Arena>();

        // Events
        public event System.Action<Arena> OnArenaRegistered;
        public event System.Action<Arena> OnArenaUnregistered;
        public event System.Action<Arena> OnFocusedArenaChanged;
        public event System.Action<UnitController, Arena, Arena> OnUnitTransitioned;

        // Properties
        public Arena FocusedArena => focusedArena;
        public IReadOnlyDictionary<string, Arena> Arenas => arenas;
        public int ArenaCount => arenas.Count;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        #region Arena Registration

        /// <summary>
        /// Register an arena with the manager.
        /// </summary>
        public void RegisterArena(Arena arena)
        {
            if (arena == null) return;

            if (arenas.ContainsKey(arena.ArenaId))
            {
                Debug.LogWarning($"[ArenaManager] Arena '{arena.ArenaId}' already registered");
                return;
            }

            arenas[arena.ArenaId] = arena;
            Debug.Log($"[ArenaManager] Registered arena '{arena.ArenaId}'");

            // Set as focused if first arena
            if (focusedArena == null)
            {
                SetFocusedArena(arena);
            }

            OnArenaRegistered?.Invoke(arena);
        }

        /// <summary>
        /// Unregister an arena from the manager.
        /// </summary>
        public void UnregisterArena(Arena arena)
        {
            if (arena == null) return;

            if (arenas.Remove(arena.ArenaId))
            {
                Debug.Log($"[ArenaManager] Unregistered arena '{arena.ArenaId}'");

                if (focusedArena == arena)
                {
                    // Find another arena to focus on
                    focusedArena = null;
                    foreach (var kvp in arenas)
                    {
                        SetFocusedArena(kvp.Value);
                        break;
                    }
                }

                OnArenaUnregistered?.Invoke(arena);
            }
        }

        /// <summary>
        /// Get an arena by ID.
        /// </summary>
        public Arena GetArena(string arenaId)
        {
            arenas.TryGetValue(arenaId, out var arena);
            return arena;
        }

        /// <summary>
        /// Check if an arena exists.
        /// </summary>
        public bool HasArena(string arenaId)
        {
            return arenas.ContainsKey(arenaId);
        }

        #endregion

        #region Focused Arena

        /// <summary>
        /// Set which arena the camera is focused on.
        /// All arenas continue to simulate regardless of focus.
        /// </summary>
        public void SetFocusedArena(Arena arena)
        {
            if (arena == focusedArena) return;

            var previous = focusedArena;
            focusedArena = arena;

            // Update camera focus
            if (CameraManager.Instance != null && arena != null)
            {
                CameraManager.Instance.FocusArenaOn(arena.Bounds.center);
            }

            Debug.Log($"[ArenaManager] Focused arena changed: {previous?.ArenaId ?? "none"} -> {arena?.ArenaId ?? "none"}");
            OnFocusedArenaChanged?.Invoke(arena);
        }

        /// <summary>
        /// Set focused arena by ID.
        /// </summary>
        public void SetFocusedArena(string arenaId)
        {
            if (arenas.TryGetValue(arenaId, out var arena))
            {
                SetFocusedArena(arena);
            }
        }

        #endregion

        #region Arena Spawning

        /// <summary>
        /// Spawn an arena from a prefab.
        /// </summary>
        public Arena SpawnArena(GameObject prefab, Vector3 position)
        {
            if (prefab == null)
            {
                Debug.LogError("[ArenaManager] Cannot spawn null prefab");
                return null;
            }

            var instance = Instantiate(prefab, position, Quaternion.identity);
            var arena = instance.GetComponent<Arena>();

            if (arena == null)
            {
                Debug.LogError($"[ArenaManager] Prefab '{prefab.name}' has no Arena component");
                Destroy(instance);
                return null;
            }

            return arena;
        }

        /// <summary>
        /// Spawn an arena by prefab index.
        /// </summary>
        public Arena SpawnArena(int prefabIndex, Vector3 position)
        {
            if (prefabIndex < 0 || prefabIndex >= arenaPrefabs.Count)
            {
                Debug.LogError($"[ArenaManager] Invalid prefab index: {prefabIndex}");
                return null;
            }

            return SpawnArena(arenaPrefabs[prefabIndex], position);
        }

        /// <summary>
        /// Destroy an arena.
        /// </summary>
        public void DestroyArena(Arena arena)
        {
            if (arena == null) return;

            // Unregister happens in Arena.OnDestroy
            Destroy(arena.gameObject);
        }

        /// <summary>
        /// Destroy an arena by ID.
        /// </summary>
        public void DestroyArena(string arenaId)
        {
            if (arenas.TryGetValue(arenaId, out var arena))
            {
                DestroyArena(arena);
            }
        }

        #endregion

        #region Transitions

        /// <summary>
        /// Transition a unit from one arena to another through portals.
        /// </summary>
        public void TransitionUnit(UnitController unit, Portal fromPortal, Portal toPortal)
        {
            if (unit == null || toPortal == null) return;

            Arena fromArena = fromPortal?.OwnerArena;
            Arena toArena = toPortal.OwnerArena;
            ArenaFloor toFloor = toPortal.OwnerFloor;

            if (toArena == null)
            {
                Debug.LogError("[ArenaManager] Target portal has no arena");
                return;
            }

            // Unregister from source arena
            fromArena?.UnregisterUnit(unit);

            // Move unit to destination
            Vector3 arrivalPos = toPortal.GetArrivalPosition();
            unit.transform.position = arrivalPos;

            // Register with destination arena and floor
            toArena.RegisterUnit(unit, toFloor);

            // Occupy tile on the correct floor
            if (toFloor != null)
            {
                toFloor.OccupyTile(unit.gameObject, arrivalPos);
            }
            else
            {
                toArena.OccupyTile(unit.gameObject, arrivalPos);
            }

            // Update unit's arena reference
            unit.SetArena(toArena);

            Debug.Log($"[ArenaManager] Unit '{unit.name}' transitioned: {fromArena?.ArenaId ?? "none"} -> {toArena.ArenaId} (floor: {toFloor?.FloorId ?? "auto"})");
            OnUnitTransitioned?.Invoke(unit, fromArena, toArena);
        }

        /// <summary>
        /// Transition a unit to space (from hangar, etc.)
        /// </summary>
        public void TransitionToSpace(UnitController unit, Portal exitPortal)
        {
            if (unit == null || exitPortal == null) return;

            Arena fromArena = exitPortal.OwnerArena;

            // Unregister from arena
            fromArena?.UnregisterUnit(unit);

            // Hand off to SpaceManager
            if (SpaceManager.Instance != null)
            {
                SpaceManager.Instance.SpawnVesselFromArena(unit, exitPortal);
            }
            else
            {
                Debug.LogWarning("[ArenaManager] No SpaceManager to handle space transition");
            }

            Debug.Log($"[ArenaManager] Unit '{unit.name}' transitioned to space from {fromArena?.ArenaId ?? "unknown"}");
        }

        /// <summary>
        /// Transition a unit from space to an arena.
        /// </summary>
        public void TransitionFromSpace(UnitController unit, Portal entryPortal)
        {
            if (unit == null || entryPortal == null) return;

            Arena toArena = entryPortal.OwnerArena;
            ArenaFloor toFloor = entryPortal.OwnerFloor;

            if (toArena == null)
            {
                Debug.LogError("[ArenaManager] Entry portal has no arena");
                return;
            }

            // Position unit at portal exit
            Vector3 arrivalPos = entryPortal.GetArrivalPosition();
            unit.transform.position = arrivalPos;

            // Register with arena and floor
            toArena.RegisterUnit(unit, toFloor);

            // Occupy tile on the correct floor
            if (toFloor != null)
            {
                toFloor.OccupyTile(unit.gameObject, arrivalPos);
            }
            else
            {
                toArena.OccupyTile(unit.gameObject, arrivalPos);
            }

            // Update unit's arena reference
            unit.SetArena(toArena);

            Debug.Log($"[ArenaManager] Unit '{unit.name}' entered arena {toArena.ArenaId} (floor: {toFloor?.FloorId ?? "auto"}) from space");
            OnUnitTransitioned?.Invoke(unit, null, toArena);
        }

        #endregion

        #region Queries

        /// <summary>
        /// Find which arena contains a world position.
        /// </summary>
        public Arena GetArenaAtPosition(Vector3 worldPosition)
        {
            foreach (var kvp in arenas)
            {
                if (kvp.Value.Bounds.Contains(worldPosition))
                {
                    return kvp.Value;
                }
            }
            return null;
        }

        /// <summary>
        /// Find which arena a unit is in.
        /// </summary>
        public Arena GetArenaForUnit(UnitController unit)
        {
            foreach (var kvp in arenas)
            {
                if (kvp.Value.ContainsUnit(unit))
                {
                    return kvp.Value;
                }
            }
            return null;
        }

        /// <summary>
        /// Get all arenas of a specific type.
        /// </summary>
        public List<Arena> GetArenasByType(ArenaType type)
        {
            var result = new List<Arena>();
            foreach (var kvp in arenas)
            {
                if (kvp.Value.Type == type)
                {
                    result.Add(kvp.Value);
                }
            }
            return result;
        }

        #endregion
    }
}
