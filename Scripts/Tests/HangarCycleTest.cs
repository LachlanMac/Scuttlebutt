using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Starbelter.Arena;
using Starbelter.Core;
using Starbelter.Space;
using Starbelter.Ship;

namespace Starbelter.Tests
{
    /// <summary>
    /// Test script for the full hangar cycle:
    /// Spawn parked ships → Launch → Patrol → Dock → Repeat
    ///
    /// Setup:
    /// 1. Attach to any GameObject in scene
    /// 2. Assign the mothership (SpaceVessel with HangarExit)
    /// 3. Assign the hangarEntrance (in the mothership's arena)
    /// 4. Set patrol waypoints (or leave empty for auto-generated patrol)
    /// </summary>
    public class HangarCycleTest : MonoBehaviour
    {
        [Header("References (auto-found if null)")]
        [SerializeField] private SpaceVessel mothership;
        [SerializeField] private HangarEntrance hangarEntrance;

        [Header("Test Settings")]
        [SerializeField] private int shipsToSpawn = 2;
        [SerializeField] private float delayBetweenSpawns = 3f;
        [SerializeField] private float patrolDistance = 50f;
        [SerializeField] private float timeAtWaypoint = 2f;
        [SerializeField] private bool autoStart = true;
        [SerializeField] private float startDelay = 5f; // Wait for scene to set up

        [Header("Patrol Waypoints (optional)")]
        [SerializeField] private Transform[] patrolWaypoints;

        // Track spawned ships
        private List<SpaceVessel> activeShips = new List<SpaceVessel>();

        private void Start()
        {
            if (autoStart)
            {
                StartCoroutine(DelayedStart());
            }
        }

        private IEnumerator DelayedStart()
        {
            // Wait for scene to spawn things
            yield return new WaitForSeconds(startDelay);
            StartCoroutine(RunTest());
        }

        /// <summary>
        /// Find mothership and hangar entrance if not assigned.
        /// Scene hierarchy: Space->Ships->[Children], Arenas->[Children]
        /// </summary>
        private bool FindReferences()
        {
            // Find hangar entrance first - look in Arenas
            if (hangarEntrance == null)
            {
                var arenasParent = GameObject.Find("Arenas");
                if (arenasParent != null)
                {
                    hangarEntrance = arenasParent.GetComponentInChildren<HangarEntrance>();
                    if (hangarEntrance != null)
                    {
                        Debug.Log($"[HangarCycleTest] Found hangar entrance in Arenas: {hangarEntrance.name}");
                    }
                }

                // Fallback: find any HangarEntrance in scene
                if (hangarEntrance == null)
                {
                    hangarEntrance = FindAnyObjectByType<HangarEntrance>();
                    if (hangarEntrance != null)
                    {
                        Debug.Log($"[HangarCycleTest] Found hangar entrance: {hangarEntrance.name}");
                    }
                }
            }

            // Find mothership - look in Space/Ships
            if (mothership == null)
            {
                var shipsParent = GameObject.Find("Space/Ships");
                if (shipsParent != null)
                {
                    Debug.Log($"[HangarCycleTest] Found Space/Ships with {shipsParent.transform.childCount} children");

                    // First pass: look for ships with hangar
                    foreach (Transform child in shipsParent.transform)
                    {
                        var vessel = child.GetComponent<SpaceVessel>();
                        if (vessel != null && (vessel.HasHangarBay || vessel.GetHangarExits().Length > 0))
                        {
                            mothership = vessel;
                            Debug.Log($"[HangarCycleTest] Found mothership with hangar: {vessel.name}");
                            break;
                        }
                    }

                    // Second pass: just take the first/largest SpaceVessel
                    if (mothership == null)
                    {
                        foreach (Transform child in shipsParent.transform)
                        {
                            var vessel = child.GetComponent<SpaceVessel>();
                            if (vessel != null)
                            {
                                mothership = vessel;
                                Debug.Log($"[HangarCycleTest] Using first SpaceVessel as mothership: {vessel.name}");
                                break;
                            }
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("[HangarCycleTest] Could not find Space/Ships in scene hierarchy");
                }

                // Fallback: search all SpaceVessels
                if (mothership == null)
                {
                    var vessels = FindObjectsByType<SpaceVessel>(FindObjectsSortMode.None);
                    Debug.Log($"[HangarCycleTest] Searching {vessels.Length} SpaceVessels in scene...");

                    foreach (var v in vessels)
                    {
                        if (v.HasHangarBay || v.GetHangarExits().Length > 0)
                        {
                            mothership = v;
                            Debug.Log($"[HangarCycleTest] Found mothership: {v.name}");
                            break;
                        }
                    }

                    // Last resort: just use first SpaceVessel
                    if (mothership == null && vessels.Length > 0)
                    {
                        mothership = vessels[0];
                        Debug.Log($"[HangarCycleTest] Using first SpaceVessel: {mothership.name}");
                    }
                }

                // Try ShipController if no SpaceVessel found
                if (mothership == null)
                {
                    var controllers = FindObjectsByType<ShipController>(FindObjectsSortMode.None);
                    Debug.Log($"[HangarCycleTest] Searching {controllers.Length} ShipControllers in scene...");

                    foreach (var c in controllers)
                    {
                        // Look for one with an arena (likely the mothership)
                        if (c.LinkedArena != null)
                        {
                            mothership = c.GetComponent<SpaceVessel>();
                            if (mothership == null)
                            {
                                // Add SpaceVessel component if missing
                                mothership = c.gameObject.AddComponent<SpaceVessel>();
                                Debug.Log($"[HangarCycleTest] Added SpaceVessel to ShipController: {c.name}");
                            }
                            else
                            {
                                Debug.Log($"[HangarCycleTest] Found mothership via ShipController: {c.name}");
                            }
                            break;
                        }
                    }

                    // Last resort: first ShipController
                    if (mothership == null && controllers.Length > 0)
                    {
                        var c = controllers[0];
                        mothership = c.GetComponent<SpaceVessel>();
                        if (mothership == null)
                        {
                            mothership = c.gameObject.AddComponent<SpaceVessel>();
                            Debug.Log($"[HangarCycleTest] Added SpaceVessel to first ShipController: {c.name}");
                        }
                    }
                }
            }

            if (mothership == null)
                Debug.LogError("[HangarCycleTest] No mothership found!");
            if (hangarEntrance == null)
                Debug.LogError("[HangarCycleTest] No hangar entrance found!");

            // Wire up Arena -> ParentVessel if not set
            if (mothership != null && hangarEntrance != null)
            {
                var arena = hangarEntrance.OwnerArena;
                if (arena != null && arena.ParentVessel == null)
                {
                    arena.SetParentVessel(mothership);
                    Debug.Log($"[HangarCycleTest] Wired Arena to mothership: {mothership.name}");
                }

                // Ensure mothership has a HangarExit - check directly, not cached
                var exits = mothership.GetComponentsInChildren<HangarExit>();
                if (exits == null || exits.Length == 0)
                {
                    // Create a HangarExit on the mothership
                    var exitObj = new GameObject("HangarExit");
                    exitObj.transform.SetParent(mothership.transform);
                    exitObj.transform.localPosition = Vector3.up * 10f; // Offset from center
                    var hangarExit = exitObj.AddComponent<HangarExit>();

                    // Also create approach vector
                    var approachObj = new GameObject("ApproachVector");
                    approachObj.transform.SetParent(exitObj.transform);
                    approachObj.transform.localPosition = Vector3.up * 30f;

                    // Clear cached exits so the new one is found
                    mothership.RefreshHangarExits();

                    Debug.Log($"[HangarCycleTest] Created HangarExit on mothership at {exitObj.transform.position}");
                }
                else
                {
                    Debug.Log($"[HangarCycleTest] Mothership has {exits.Length} HangarExit(s): {string.Join(", ", System.Array.ConvertAll(exits, e => e.ExitId))}");
                }
            }

            return mothership != null && hangarEntrance != null;
        }

        [ContextMenu("Run Test")]
        public void StartTest()
        {
            StartCoroutine(RunTest());
        }

        private IEnumerator RunTest()
        {
            Debug.Log("[HangarCycleTest] Starting hangar cycle test...");

            // Auto-find references if not assigned
            if (!FindReferences())
            {
                Debug.LogError("[HangarCycleTest] Could not find mothership or hangar entrance!");
                yield break;
            }

            // Subscribe to launch events
            hangarEntrance.OnShipLaunched += HandleShipLaunched;

            // Spawn ships
            for (int i = 0; i < shipsToSpawn; i++)
            {
                Debug.Log($"[HangarCycleTest] Spawning ship {i + 1}/{shipsToSpawn}...");
                var parkedShip = hangarEntrance.SpawnShip();

                if (parkedShip != null)
                {
                    // Wait for it to park, then launch
                    StartCoroutine(WaitAndLaunch(parkedShip));
                }

                yield return new WaitForSeconds(delayBetweenSpawns);
            }

            Debug.Log("[HangarCycleTest] All ships spawned. Waiting for launches...");
        }

        private IEnumerator WaitAndLaunch(ParkedShip parkedShip)
        {
            // Wait until parked
            while (parkedShip != null && !parkedShip.IsParked)
            {
                yield return null;
            }

            if (parkedShip == null) yield break;

            Debug.Log("[HangarCycleTest] Ship parked, launching in 2 seconds...");
            yield return new WaitForSeconds(2f);

            if (parkedShip != null)
            {
                parkedShip.Launch();
            }
        }

        private void HandleShipLaunched(GameObject spacePrefab, ShipState state, HangarExit hangarExit)
        {
            Debug.Log($"[HangarCycleTest] Ship launched! Spawning in space...");

            if (spacePrefab == null)
            {
                Debug.LogError("[HangarCycleTest] No space prefab!");
                return;
            }

            // Determine spawn position
            Vector3 spawnPos = hangarExit != null
                ? (Vector3)hangarExit.Position
                : mothership.transform.position + mothership.transform.up * 10f;

            float spawnRot = hangarExit != null
                ? hangarExit.ExitRotation
                : mothership.transform.eulerAngles.z;

            // Spawn the space vessel
            var spaceObj = Instantiate(spacePrefab, spawnPos, Quaternion.Euler(0, 0, spawnRot));
            var spaceVessel = spaceObj.GetComponent<SpaceVessel>();

            if (spaceVessel == null)
            {
                Debug.LogError("[HangarCycleTest] Spawned prefab has no SpaceVessel!");
                Destroy(spaceObj);
                return;
            }

            // Initialize with state
            spaceVessel.Initialize(state);

            // Add docking controller if needed
            var dockingController = spaceObj.GetComponent<DockingController>();
            if (dockingController == null)
            {
                dockingController = spaceObj.AddComponent<DockingController>();
            }

            activeShips.Add(spaceVessel);

            // Start patrol behavior
            StartCoroutine(PatrolAndDock(spaceVessel, dockingController));
        }

        private IEnumerator PatrolAndDock(SpaceVessel vessel, DockingController docking)
        {
            Debug.Log($"[HangarCycleTest] {vessel.VesselId} starting patrol...");

            // Generate patrol waypoints if not provided
            Vector2[] waypoints;
            if (patrolWaypoints != null && patrolWaypoints.Length > 0)
            {
                waypoints = new Vector2[patrolWaypoints.Length];
                for (int i = 0; i < patrolWaypoints.Length; i++)
                {
                    waypoints[i] = patrolWaypoints[i].position;
                }
            }
            else
            {
                // Auto-generate simple patrol around mothership
                Vector2 center = mothership.transform.position;
                waypoints = new Vector2[]
                {
                    center + Vector2.right * patrolDistance,
                    center + Vector2.up * patrolDistance,
                    center + Vector2.left * patrolDistance,
                };
            }

            // Patrol each waypoint
            foreach (var waypoint in waypoints)
            {
                if (vessel == null) yield break;

                Debug.Log($"[HangarCycleTest] {vessel.VesselId} moving to waypoint {waypoint}");
                vessel.MoveToward(waypoint);

                // Wait to reach waypoint (or timeout)
                float timeout = 15f;
                while (vessel != null && Vector2.Distance(vessel.transform.position, waypoint) > 5f && timeout > 0)
                {
                    timeout -= Time.deltaTime;
                    yield return null;
                }

                if (vessel == null) yield break;

                // Pause at waypoint
                vessel.Stop();
                Debug.Log($"[HangarCycleTest] {vessel.VesselId} reached waypoint, waiting...");
                yield return new WaitForSeconds(timeAtWaypoint);
            }

            // Return to dock
            if (vessel != null && docking != null)
            {
                Debug.Log($"[HangarCycleTest] {vessel.VesselId} patrol complete, requesting docking...");

                docking.OnDockingComplete += HandleDockingComplete;
                bool success = docking.RequestDocking(mothership, hangarEntrance.ExitId);

                if (!success)
                {
                    Debug.LogWarning($"[HangarCycleTest] {vessel.VesselId} docking request denied!");
                }
            }
        }

        private void HandleDockingComplete(DockingController controller, HangarExit exit)
        {
            Debug.Log($"[HangarCycleTest] Docking complete! Transitioning to hangar...");
            controller.OnDockingComplete -= HandleDockingComplete;

            // Remove from active list
            var vessel = controller.GetComponent<SpaceVessel>();
            if (vessel != null)
            {
                activeShips.Remove(vessel);
            }

            // Complete docking (spawns parked ship, destroys space vessel)
            controller.CompleteDocking();
        }

        private void OnDestroy()
        {
            if (hangarEntrance != null)
            {
                hangarEntrance.OnShipLaunched -= HandleShipLaunched;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Draw patrol waypoints
            if (patrolWaypoints != null && patrolWaypoints.Length > 0)
            {
                Gizmos.color = Color.cyan;
                for (int i = 0; i < patrolWaypoints.Length; i++)
                {
                    if (patrolWaypoints[i] != null)
                    {
                        Gizmos.DrawWireSphere(patrolWaypoints[i].position, 3f);
                        if (i > 0 && patrolWaypoints[i - 1] != null)
                        {
                            Gizmos.DrawLine(patrolWaypoints[i - 1].position, patrolWaypoints[i].position);
                        }
                    }
                }
            }
            else if (mothership != null)
            {
                // Draw auto-generated patrol
                Gizmos.color = Color.cyan;
                Vector2 center = mothership.transform.position;
                Vector2[] pts = {
                    center + Vector2.right * patrolDistance,
                    center + Vector2.up * patrolDistance,
                    center + Vector2.left * patrolDistance,
                };
                foreach (var pt in pts)
                {
                    Gizmos.DrawWireSphere(pt, 3f);
                }
                Gizmos.DrawLine(pts[0], pts[1]);
                Gizmos.DrawLine(pts[1], pts[2]);
            }
        }
#endif
    }
}
