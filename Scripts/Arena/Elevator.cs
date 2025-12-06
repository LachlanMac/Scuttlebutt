using UnityEngine;
using System.Collections.Generic;
using Pathfinding;
using Starbelter.AI;

namespace Starbelter.Arena
{
    /// <summary>
    /// Multi-floor elevator that creates pathfinding links between all connected floors.
    /// Place ElevatorStop transforms on each floor, and this component auto-generates NodeLink2 connections.
    /// </summary>
    public class Elevator : MonoBehaviour
    {
        [Header("Elevator Identity")]
        [SerializeField] private string elevatorId;

        [Header("Stops")]
        [Tooltip("Place these transforms at the elevator entrance on each floor")]
        [SerializeField] private List<Transform> stops = new List<Transform>();

        [Header("Settings")]
        [Tooltip("Base cost for traveling one floor")]
        [SerializeField] private float costPerFloor = 1f;

        [Tooltip("Time to travel one floor (for animations/waiting)")]
        [SerializeField] private float timePerFloor = 1f;

        [Tooltip("If true, all connections are bidirectional")]
        [SerializeField] private bool bidirectional = true;

        [Header("Visuals")]
        [Tooltip("Layer name for elevator visuals (should be in all floor culling masks)")]
        [SerializeField] private string sharedLayerName = "FloorShared";

        [Tooltip("If true, applies shared layer to this GameObject and children")]
        [SerializeField] private bool applySharedLayer = true;

        // Runtime
        private Arena parentArena;
        private List<ElevatorStopData> stopData = new List<ElevatorStopData>();
        private List<NodeLink2> generatedLinks = new List<NodeLink2>();
        private bool isInitialized;

        // Properties
        public string ElevatorId => elevatorId;
        public int StopCount => stops.Count;
        public bool IsInitialized => isInitialized;

        private struct ElevatorStopData
        {
            public Transform Transform;
            public ArenaFloor Floor;
            public int FloorIndex;
        }

        private void Awake()
        {
            if (string.IsNullOrEmpty(elevatorId))
            {
                elevatorId = $"Elevator_{System.Guid.NewGuid().ToString().Substring(0, 8)}";
            }
        }

        /// <summary>
        /// Initialize the elevator. Called by Arena after floors are initialized.
        /// </summary>
        public void Initialize(Arena arena)
        {
            if (isInitialized) return;

            parentArena = arena;

            // Apply shared layer for multi-floor visibility
            if (applySharedLayer)
            {
                ApplySharedLayer();
            }

            // Build stop data with floor references
            BuildStopData();

            if (stopData.Count < 2)
            {
                Debug.LogWarning($"[Elevator] '{elevatorId}' has fewer than 2 valid stops. No links created.");
                return;
            }

            // Create NodeLink2 between all pairs
            CreateNodeLinks();

            isInitialized = true;
            Debug.Log($"[Elevator] '{elevatorId}' initialized with {stopData.Count} stops, {generatedLinks.Count} links");
        }

        private void ApplySharedLayer()
        {
            int layer = LayerMask.NameToLayer(sharedLayerName);
            if (layer < 0)
            {
                Debug.LogWarning($"[Elevator] Layer '{sharedLayerName}' not found. Create it in Project Settings > Tags and Layers");
                return;
            }

            SetLayerRecursive(gameObject, layer);
        }

        private void SetLayerRecursive(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }

        private void BuildStopData()
        {
            stopData.Clear();

            foreach (var stop in stops)
            {
                if (stop == null) continue;

                // Find which floor this stop is on
                ArenaFloor floor = DetectFloorForStop(stop);
                if (floor == null)
                {
                    Debug.LogWarning($"[Elevator] Stop '{stop.name}' could not determine floor. " +
                        "Make it a child of an ArenaFloor, or set its layer to Floor0/Floor1/etc.");
                    continue;
                }

                stopData.Add(new ElevatorStopData
                {
                    Transform = stop,
                    Floor = floor,
                    FloorIndex = floor.FloorIndex
                });
            }

            // Sort by floor index for consistent ordering
            stopData.Sort((a, b) => a.FloorIndex.CompareTo(b.FloorIndex));
        }

        /// <summary>
        /// Detect which floor a stop belongs to.
        /// Priority: 1) Parent ArenaFloor, 2) Layer name, 3) Position bounds
        /// </summary>
        private ArenaFloor DetectFloorForStop(Transform stop)
        {
            // Method 1: Check if stop is a child of an ArenaFloor
            var parentFloor = stop.GetComponentInParent<ArenaFloor>();
            if (parentFloor != null)
            {
                return parentFloor;
            }

            // Method 2: Check layer name (e.g., "Floor2" → floorIndex 2)
            string layerName = LayerMask.LayerToName(stop.gameObject.layer);
            if (layerName.StartsWith("Floor") && layerName.Length > 5)
            {
                string indexStr = layerName.Substring(5); // "Floor2" → "2"
                if (int.TryParse(indexStr, out int floorIndex))
                {
                    // Find the ArenaFloor with this index
                    foreach (var floor in parentArena.Floors)
                    {
                        if (floor.FloorIndex == floorIndex)
                        {
                            return floor;
                        }
                    }
                }
            }

            // Method 3: Fall back to position-based (works if floors don't overlap)
            return parentArena.GetFloorAtPosition(stop.position);
        }

        private void CreateNodeLinks()
        {
            generatedLinks.Clear();

            // Create links between all pairs of stops
            for (int i = 0; i < stopData.Count; i++)
            {
                for (int j = i + 1; j < stopData.Count; j++)
                {
                    var from = stopData[i];
                    var to = stopData[j];

                    // Calculate cost based on floor distance
                    int floorDistance = Mathf.Abs(to.FloorIndex - from.FloorIndex);
                    float cost = costPerFloor * floorDistance;

                    // Create the link
                    var link = CreateLink(from, to, cost);
                    if (link != null)
                    {
                        generatedLinks.Add(link);
                    }
                }
            }
        }

        private NodeLink2 CreateLink(ElevatorStopData from, ElevatorStopData to, float cost)
        {
            // Create a child GameObject for this link
            var linkObj = new GameObject($"ElevatorLink_{from.FloorIndex}_to_{to.FloorIndex}");
            linkObj.transform.SetParent(transform);
            linkObj.transform.position = from.Transform.position;

            // Add NodeLink2
            var nodeLink = linkObj.AddComponent<NodeLink2>();
            nodeLink.end = to.Transform;
            nodeLink.oneWay = !bidirectional;
            nodeLink.costFactor = cost;

            return nodeLink;
        }

        /// <summary>
        /// Get the stop transform for a specific floor.
        /// </summary>
        public Transform GetStopForFloor(int floorIndex)
        {
            foreach (var stop in stopData)
            {
                if (stop.FloorIndex == floorIndex)
                    return stop.Transform;
            }
            return null;
        }

        /// <summary>
        /// Get the stop transform for a specific floor.
        /// </summary>
        public Transform GetStopForFloor(ArenaFloor floor)
        {
            foreach (var stop in stopData)
            {
                if (stop.Floor == floor)
                    return stop.Transform;
            }
            return null;
        }

        /// <summary>
        /// Calculate travel time between two floors.
        /// </summary>
        public float GetTravelTime(int fromFloorIndex, int toFloorIndex)
        {
            return Mathf.Abs(toFloorIndex - fromFloorIndex) * timePerFloor;
        }

        /// <summary>
        /// Transition a unit to a specific floor via this elevator.
        /// </summary>
        public void TransitionUnit(UnitController unit, ArenaFloor targetFloor)
        {
            if (unit == null || targetFloor == null) return;

            var targetStop = GetStopForFloor(targetFloor);
            if (targetStop == null)
            {
                Debug.LogWarning($"[Elevator] No stop for floor {targetFloor.FloorId}");
                return;
            }

            // Move unit to target position
            unit.transform.position = targetStop.position;

            // Update floor registration
            parentArena.SetUnitFloor(unit, targetFloor);

            // Update tile occupancy
            targetFloor.OccupyTile(unit.gameObject, targetStop.position);

            Debug.Log($"[Elevator] Unit '{unit.name}' moved to {targetFloor.FloorId}");
        }

        private void OnDestroy()
        {
            // Clean up generated link objects
            foreach (var link in generatedLinks)
            {
                if (link != null)
                {
                    Destroy(link.gameObject);
                }
            }
            generatedLinks.Clear();
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;

            // Draw stops
            foreach (var stop in stops)
            {
                if (stop == null) continue;
                Gizmos.DrawWireSphere(stop.position, 0.4f);
            }

            // Draw connections between all stops
            Gizmos.color = new Color(0f, 1f, 1f, 0.5f);
            for (int i = 0; i < stops.Count; i++)
            {
                for (int j = i + 1; j < stops.Count; j++)
                {
                    if (stops[i] == null || stops[j] == null) continue;
                    Gizmos.DrawLine(stops[i].position, stops[j].position);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (stops.Count == 0) return;

            // Draw elevator ID
            Vector3 labelPos = transform.position;
            if (stops.Count > 0 && stops[0] != null)
            {
                labelPos = stops[0].position + Vector3.up * 0.5f;
            }
            UnityEditor.Handles.Label(labelPos, elevatorId);
        }
#endif
    }
}
