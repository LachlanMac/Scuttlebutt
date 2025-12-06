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
            if (spawnedArena == null || testUnitPrefab == null) return;

            var bounds = spawnedArena.Bounds;

            for (int i = 0; i < testUnitCount; i++)
            {
                // Random position within arena bounds
                float x = Random.Range(bounds.min.x + 1, bounds.max.x - 1);
                float y = Random.Range(bounds.min.y + 1, bounds.max.y - 1);
                Vector3 spawnPos = new Vector3(x, y, 0);

                // Snap to tile
                var tile = spawnedArena.WorldToTile(spawnPos);
                spawnPos = spawnedArena.TileToWorld(tile);

                // Check if tile is available
                if (!spawnedArena.IsTileAvailable(tile))
                {
                    continue; // Skip occupied tiles
                }

                var unitObj = Instantiate(testUnitPrefab, spawnPos, Quaternion.identity);
                var unit = unitObj.GetComponent<UnitController>();

                if (unit != null)
                {
                    unit.name = $"TestUnit_{i + 1}";
                    unit.SetTeam(testUnitTeam);
                    spawnedArena.RegisterUnit(unit);
                    spawnedArena.OccupyTile(unitObj, tile);
                    unit.SetArena(spawnedArena);
                    spawnedUnits.Add(unit);

                    if (logInitialization)
                        Debug.Log($"[TestGameManager] Spawned unit '{unit.name}' at {tile}");
                }
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

        #region Debug UI

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 200, 300));

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

            GUILayout.EndArea();
        }

        #endregion
    }
}
