using UnityEngine;
using System.Collections.Generic;
using Starbelter.Core;
using Starbelter.Space;
using Starbelter.AI;

namespace Starbelter.Arena
{
    /// <summary>
    /// Test manager for spawning and managing arenas during development.
    /// Use this to test arena/space systems without affecting the main GameManager.
    /// </summary>
    public class TestGameManager : MonoBehaviour
    {
        [Header("Managers")]
        [SerializeField] private bool createArenaManager = true;
        [SerializeField] private bool createSpaceManager = true;
        [SerializeField] private bool createCameraManager = true;

        [Header("Test Arena")]
        [Tooltip("Arena prefab to spawn on start")]
        [SerializeField] private GameObject testArenaPrefab;

        [Tooltip("Position to spawn the test arena")]
        [SerializeField] private Vector3 testArenaPosition = Vector3.zero;

        [Header("Test Units")]
        [Tooltip("Unit prefab for spawning test units")]
        [SerializeField] private GameObject testUnitPrefab;

        [Tooltip("Number of test units to spawn")]
        [SerializeField] private int testUnitCount = 4;

        [Tooltip("Team for test units")]
        [SerializeField] private Team testUnitTeam = Team.Federation;

        [Header("Test Space")]
        [Tooltip("Vessel prefab for spawning test vessels")]
        [SerializeField] private GameObject testVesselPrefab;

        [Header("Debug")]
        [SerializeField] private bool logInitialization = true;

        private Arena spawnedArena;
        private List<UnitController> spawnedUnits = new List<UnitController>();

        private void Start()
        {
            InitializeManagers();

            if (testArenaPrefab != null)
            {
                SpawnTestArena();
            }
        }

        private void InitializeManagers()
        {
            // Create ArenaManager if needed
            if (createArenaManager && ArenaManager.Instance == null)
            {
                var arenaManagerObj = new GameObject("ArenaManager");
                arenaManagerObj.AddComponent<ArenaManager>();
                if (logInitialization)
                    Debug.Log("[TestGameManager] Created ArenaManager");
            }

            // Create SpaceManager if needed
            if (createSpaceManager && SpaceManager.Instance == null)
            {
                var spaceManagerObj = new GameObject("SpaceManager");
                spaceManagerObj.AddComponent<SpaceManager>();
                if (logInitialization)
                    Debug.Log("[TestGameManager] Created SpaceManager");
            }

            // Create CameraManager if needed
            if (createCameraManager && CameraManager.Instance == null)
            {
                var cameraManagerObj = new GameObject("CameraManager");
                var camManager = cameraManagerObj.AddComponent<CameraManager>();

                // Try to find and assign the main camera
                var mainCam = Camera.main;
                if (mainCam != null)
                {
                    // CameraManager will use this as arena camera
                    // Space camera will be created automatically if needed
                }

                if (logInitialization)
                    Debug.Log("[TestGameManager] Created CameraManager");
            }
        }

        /// <summary>
        /// Spawn the test arena.
        /// </summary>
        public void SpawnTestArena()
        {
            if (testArenaPrefab == null)
            {
                Debug.LogWarning("[TestGameManager] No test arena prefab assigned");
                return;
            }

            if (ArenaManager.Instance == null)
            {
                Debug.LogError("[TestGameManager] ArenaManager not available");
                return;
            }

            spawnedArena = ArenaManager.Instance.SpawnArena(testArenaPrefab, testArenaPosition);

            if (spawnedArena != null && logInitialization)
            {
                Debug.Log($"[TestGameManager] Spawned test arena '{spawnedArena.ArenaId}' at {testArenaPosition}");

                // Spawn test units if configured
                if (testUnitPrefab != null && testUnitCount > 0)
                {
                    SpawnTestUnits();
                }
            }
        }

        /// <summary>
        /// Spawn test units in the current arena.
        /// </summary>
        public void SpawnTestUnits()
        {
            if (spawnedArena == null)
            {
                Debug.LogWarning("[TestGameManager] No arena spawned! Click 'Spawn Arena' first.");
                return;
            }
            if (testUnitPrefab == null)
            {
                Debug.LogWarning("[TestGameManager] No testUnitPrefab assigned in Inspector!");
                return;
            }

            var floor = spawnedArena.GetFloor(0); // Spawn on first floor
            if (floor == null || floor.Graph == null)
            {
                Debug.LogError("[TestGameManager] No floor or graph available for spawning!");
                return;
            }

            int spawned = 0;
            int attempts = 0;
            int maxAttempts = testUnitCount * 20; // Prevent infinite loop

            while (spawned < testUnitCount && attempts < maxAttempts)
            {
                attempts++;

                // Pick a random walkable node from the graph
                var graph = floor.Graph;
                var nodes = new System.Collections.Generic.List<global::Pathfinding.GraphNode>();
                graph.GetNodes(node => { if (node.Walkable) nodes.Add(node); });

                if (nodes.Count == 0)
                {
                    Debug.LogError("[TestGameManager] No walkable nodes found!");
                    return;
                }

                var randomNode = nodes[Random.Range(0, nodes.Count)];
                Vector3 spawnPos = (Vector3)randomNode.position;
                var tile = floor.WorldToTile(spawnPos);

                // Check if tile is available (not occupied)
                if (!floor.IsTileAvailable(tile))
                {
                    continue; // Skip occupied tiles
                }

                var unitObj = Instantiate(testUnitPrefab, spawnPos, Quaternion.identity);
                var unit = unitObj.GetComponent<UnitController>();

                if (unit != null)
                {
                    spawned++;
                    unit.name = $"TestUnit_{spawned}";
                    unit.SetTeam(testUnitTeam);
                    spawnedArena.RegisterUnit(unit);
                    floor.OccupyTile(unitObj, tile);
                    unit.SetArena(spawnedArena);
                    spawnedUnits.Add(unit);

                    if (logInitialization)
                        Debug.Log($"[TestGameManager] Spawned unit '{unit.name}' at {tile} (walkable node)");
                }
            }

            if (spawned < testUnitCount)
            {
                Debug.LogWarning($"[TestGameManager] Only spawned {spawned}/{testUnitCount} units after {attempts} attempts");
            }
        }

        /// <summary>
        /// Clean up spawned test objects.
        /// </summary>
        public void CleanupTest()
        {
            foreach (var unit in spawnedUnits)
            {
                if (unit != null)
                {
                    Destroy(unit.gameObject);
                }
            }
            spawnedUnits.Clear();

            if (spawnedArena != null)
            {
                ArenaManager.Instance?.DestroyArena(spawnedArena);
                spawnedArena = null;
            }

            Debug.Log("[TestGameManager] Test cleanup complete");
        }

        /// <summary>
        /// Set all spawned units to a specific behavior mode.
        /// </summary>
        private void SetAllUnitsModes(BehaviorMode mode)
        {
            foreach (var unit in spawnedUnits)
            {
                if (unit != null)
                {
                    unit.ChangeBehaviorMode(mode);
                }
            }
            Debug.Log($"[TestGameManager] Set all units to {mode} mode");
        }

        #region Debug UI

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 200, 450));

            GUILayout.Label("=== Test Game Manager ===");

            if (spawnedArena != null)
            {
                GUILayout.Label($"Arena: {spawnedArena.ArenaId}");
                GUILayout.Label($"Units: {spawnedArena.Units.Count}");
            }
            else
            {
                GUILayout.Label("No arena spawned");
            }

            GUILayout.Space(10);

            if (GUILayout.Button("Spawn Arena"))
            {
                SpawnTestArena();
            }

            if (GUILayout.Button("Spawn Units"))
            {
                SpawnTestUnits();
            }

            if (GUILayout.Button("Cleanup"))
            {
                CleanupTest();
            }

            GUILayout.Space(10);

            if (CameraManager.Instance != null)
            {
                GUILayout.Label($"View: {CameraManager.Instance.CurrentView}");

                if (GUILayout.Button("Toggle View"))
                {
                    CameraManager.Instance.ToggleView();
                }

                // Floor switching (only shown in Arena view)
                if (CameraManager.Instance.CurrentView == Core.ViewMode.Arena)
                {
                    GUILayout.Space(10);
                    GUILayout.Label($"Floor: {CameraManager.Instance.CurrentFloorIndex}");

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("▼ Down"))
                    {
                        CameraManager.Instance.FloorDown();
                    }
                    if (GUILayout.Button("▲ Up"))
                    {
                        CameraManager.Instance.FloorUp();
                    }
                    GUILayout.EndHorizontal();
                }
            }

            // Behavior Mode switching for all units
            if (spawnedUnits.Count > 0)
            {
                GUILayout.Space(10);
                GUILayout.Label("=== Unit Behavior Mode ===");

                var firstUnit = spawnedUnits[0];
                if (firstUnit != null)
                {
                    GUILayout.Label($"Mode: {firstUnit.CurrentMode}");
                    GUILayout.Label($"State: {firstUnit.CurrentStateType}");
                }

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Combat"))
                {
                    SetAllUnitsModes(BehaviorMode.Combat);
                }
                if (GUILayout.Button("OnDuty"))
                {
                    SetAllUnitsModes(BehaviorMode.OnDuty);
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("OffDuty"))
                {
                    SetAllUnitsModes(BehaviorMode.OffDuty);
                }
                if (GUILayout.Button("Alert"))
                {
                    SetAllUnitsModes(BehaviorMode.Alert);
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
        }

        #endregion
    }
}
