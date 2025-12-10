using UnityEngine;
using System.Collections.Generic;
using Pathfinding;
using Starbelter.AI;

namespace Starbelter.Arena
{
    /// <summary>
    /// Multi-floor elevator that handles unit transitions between floors.
    /// Uses manual two-phase pathing: units path to elevator, wait, then teleport to target floor.
    /// </summary>
    public class Elevator : MonoBehaviour
    {
        [Header("Elevator Identity")]
        [SerializeField] private string elevatorId;

        [Header("Stops")]
        [Tooltip("Place these transforms at the elevator entrance on each floor")]
        [SerializeField] private List<Transform> stops = new List<Transform>();

        [Header("Settings")]
        [Tooltip("Time to travel one floor (seconds)")]
        [SerializeField] private float timePerFloor = 2f;

        [Header("Visuals")]
        [Tooltip("Layer name for elevator visuals (should be in all floor culling masks)")]
        [SerializeField] private string sharedLayerName = "FloorShared";

        [Tooltip("If true, applies shared layer to this GameObject and children")]
        [SerializeField] private bool applySharedLayer = true;

        // Runtime
        private Arena parentArena;
        private List<ElevatorStopData> stopData = new List<ElevatorStopData>();
        private bool isInitialized;

        // Properties
        public string ElevatorId => elevatorId;
        public int StopCount => stops.Count;
        public bool IsInitialized => isInitialized;
        public float TimePerFloor => timePerFloor;

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

            // Build stop data FIRST - before ApplySharedLayer overwrites the floor layers!
            BuildStopData();

            // Apply shared layer for multi-floor visibility (after we've read the stop layers)
            if (applySharedLayer)
            {
                ApplySharedLayer();
            }

            if (stopData.Count < 2)
            {
                Debug.LogWarning($"[Elevator] '{elevatorId}' has fewer than 2 valid stops.");
                return;
            }

            isInitialized = true;
            Debug.Log($"[Elevator] '{elevatorId}' initialized with {stopData.Count} stops");
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
                    Debug.LogWarning($"[Elevator] Stop '{stop.name}' at {stop.position} could not determine floor. " +
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
        /// Priority: 1) Layer name, 2) Parent ArenaFloor, 3) Position bounds
        /// </summary>
        private ArenaFloor DetectFloorForStop(Transform stop)
        {
            // Method 1: Check layer name (e.g., "Floor0", "Floor1")
            string layerName = LayerMask.LayerToName(stop.gameObject.layer);

            if (layerName.StartsWith("Floor"))
            {
                string indexStr = layerName.Substring(5);
                if (int.TryParse(indexStr, out int floorIndex))
                {
                    foreach (var floor in parentArena.Floors)
                    {
                        if (floor.FloorIndex == floorIndex)
                        {
                            return floor;
                        }
                    }
                }
            }

            // Method 2: Check if stop is a child of an ArenaFloor
            var parentFloor = stop.GetComponentInParent<ArenaFloor>();
            if (parentFloor != null)
            {
                return parentFloor;
            }

            // Method 3: Fall back to position-based
            return parentArena.GetFloorAtPosition(stop.position);
        }

        /// <summary>
        /// Get the stop transform for a specific floor index.
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
        /// Check if this elevator connects two floors.
        /// </summary>
        public bool ConnectsFloors(ArenaFloor floorA, ArenaFloor floorB)
        {
            bool hasA = false, hasB = false;
            foreach (var stop in stopData)
            {
                if (stop.Floor == floorA) hasA = true;
                if (stop.Floor == floorB) hasB = true;
            }
            return hasA && hasB;
        }

        /// <summary>
        /// Calculate travel time between two floors.
        /// </summary>
        public float GetTravelTime(int fromFloorIndex, int toFloorIndex)
        {
            return Mathf.Abs(toFloorIndex - fromFloorIndex) * timePerFloor;
        }

        /// <summary>
        /// Calculate travel time between two floors.
        /// </summary>
        public float GetTravelTime(ArenaFloor fromFloor, ArenaFloor toFloor)
        {
            if (fromFloor == null || toFloor == null) return timePerFloor;
            return GetTravelTime(fromFloor.FloorIndex, toFloor.FloorIndex);
        }

        /// <summary>
        /// Transition a unit to a specific floor via this elevator.
        /// Call this after the unit has waited at the elevator.
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

            // Get source floor for logging
            var sourceFloor = parentArena?.GetFloorForUnit(unit);
            string sourceFloorId = sourceFloor?.FloorId ?? "unknown";

            // Move unit to target position, adjusting Z to match target floor's graph
            Vector3 newPos = targetStop.position;
            if (targetFloor.Graph != null)
            {
                var gridGraph = targetFloor.Graph as GridGraph;
                if (gridGraph != null)
                {
                    newPos.z = gridGraph.center.z;
                }
            }
            unit.transform.position = newPos;

            // Update floor registration
            parentArena.SetUnitFloor(unit, targetFloor);

            // Update tile occupancy on new floor
            targetFloor.OccupyTile(unit.gameObject, newPos);

            // Change unit's layer to target floor
            int targetLayer = targetFloor.Layer;
            if (targetLayer >= 0)
            {
                SetLayerRecursive(unit.gameObject, targetLayer);
            }

            Debug.Log($"[Elevator] '{unit.name}' transitioned {sourceFloorId} -> {targetFloor.FloorId}");
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

            Vector3 labelPos = transform.position;
            if (stops[0] != null)
            {
                labelPos = stops[0].position + Vector3.up * 0.5f;
            }
            UnityEditor.Handles.Label(labelPos, elevatorId);
        }
#endif
    }
}
