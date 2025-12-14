using UnityEngine;
using Starbelter.Arena;

namespace Starbelter.Space
{
    /// <summary>
    /// Test script for docking functionality.
    /// Spawns a fighter and attempts to dock with a target vessel.
    /// </summary>
    public class DockingTest : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The mothership to dock with")]
        [SerializeField] private SpaceVessel mothership;

        [Tooltip("Fighter prefab to spawn (must have SpaceVessel and DockingController)")]
        [SerializeField] private GameObject fighterPrefab;

        [Header("Test Settings")]
        [Tooltip("Distance from mothership to spawn fighter")]
        [SerializeField] private float spawnDistance = 100f;

        [Tooltip("Hangar exit ID to dock at")]
        [SerializeField] private string exitId = "hangar_main";

        [Tooltip("Delay before requesting docking")]
        [SerializeField] private float dockingDelay = 2f;

        [Header("Auto Test")]
        [SerializeField] private bool runTestOnStart = false;

        private SpaceVessel spawnedFighter;
        private DockingController dockingController;

        private void Start()
        {
            if (runTestOnStart)
            {
                StartCoroutine(RunTest());
            }
        }

        private System.Collections.IEnumerator RunTest()
        {
            Debug.Log("[DockingTest] Starting docking test...");

            // Spawn fighter
            SpawnFighter();

            // Wait a bit
            yield return new WaitForSeconds(dockingDelay);

            // Request docking
            RequestDocking();
        }

        [ContextMenu("Spawn Fighter")]
        public void SpawnFighter()
        {
            if (fighterPrefab == null)
            {
                Debug.LogError("[DockingTest] No fighter prefab assigned!");
                return;
            }

            if (mothership == null)
            {
                Debug.LogError("[DockingTest] No mothership assigned!");
                return;
            }

            // Spawn at offset from mothership
            Vector2 spawnPos = (Vector2)mothership.transform.position + Vector2.right * spawnDistance;
            var fighterObj = Instantiate(fighterPrefab, spawnPos, Quaternion.identity);

            spawnedFighter = fighterObj.GetComponent<SpaceVessel>();
            dockingController = fighterObj.GetComponent<DockingController>();

            if (dockingController == null)
            {
                dockingController = fighterObj.AddComponent<DockingController>();
            }

            // Subscribe to docking complete
            dockingController.OnDockingComplete += HandleDockingComplete;

            Debug.Log($"[DockingTest] Fighter spawned at {spawnPos}");
        }

        [ContextMenu("Request Docking")]
        public void RequestDocking()
        {
            if (dockingController == null)
            {
                Debug.LogError("[DockingTest] No fighter spawned! Spawn first.");
                return;
            }

            if (mothership == null)
            {
                Debug.LogError("[DockingTest] No mothership assigned!");
                return;
            }

            bool success = dockingController.RequestDocking(mothership, exitId);
            Debug.Log($"[DockingTest] Docking request: {(success ? "APPROVED" : "DENIED")}");
        }

        private void HandleDockingComplete(DockingController controller, HangarExit exit)
        {
            Debug.Log($"[DockingTest] Docking complete at {exit.ExitId}!");

            // Complete the docking (transition to arena)
            controller.CompleteDocking();
        }

        private void OnDestroy()
        {
            if (dockingController != null)
            {
                dockingController.OnDockingComplete -= HandleDockingComplete;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (mothership != null)
            {
                // Draw spawn position
                Vector2 spawnPos = (Vector2)mothership.transform.position + Vector2.right * spawnDistance;
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(spawnPos, 3f);
                Gizmos.DrawLine(mothership.transform.position, spawnPos);

                UnityEditor.Handles.Label(spawnPos + Vector2.up * 5f, "Fighter Spawn");
            }
        }
#endif
    }
}
