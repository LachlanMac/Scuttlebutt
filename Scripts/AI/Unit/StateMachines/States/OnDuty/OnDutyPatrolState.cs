using UnityEngine;
using Starbelter.Arena;

namespace Starbelter.AI
{
    /// <summary>
    /// OnDuty Patrol - Unit walks a patrol route or between random points.
    /// Alert but not actively searching. Will respond to suspicious activity.
    /// Supports multi-floor patrol via elevators.
    /// </summary>
    public class OnDutyPatrolState : UnitState
    {
        private float waitTime;
        private bool isWaiting;
        private int waypointIndex;

        // Cross-floor patrol settings
        private const float CROSS_FLOOR_CHANCE = 1.0f; // 100% for debugging - change back to 0.3f later

        // Cross-floor journey state
        private ArenaFloor currentFloor; // Floor before transition (for travel time calc)
        private ArenaFloor targetFloor;
        private Elevator targetElevator;
        private Vector3 finalDestination;
        private Vector3 elevatorPosition;
        private const float ELEVATOR_ARRIVAL_DISTANCE = 1.0f;

        // Retry handling for unreachable destinations
        private int pathRetryCount;
        private const int MAX_PATH_RETRIES = 2;

        private enum CrossFloorPhase { None, GoingToElevator, WaitingAtElevator, GoingToDestination }
        private CrossFloorPhase crossFloorPhase = CrossFloorPhase.None;

        public override void Enter()
        {
            base.Enter();
            isWaiting = false;
            waypointIndex = 0;
            pathRetryCount = 0;
            crossFloorPhase = CrossFloorPhase.None;
            MoveToNextPoint();
        }

        public override void Update()
        {
            if (!IsValid) return;

            // TODO: Check for threats/suspicious activity -> switch to Alert mode

            // Handle elevator wait separately (unit is stationary but not in normal wait)
            if (crossFloorPhase == CrossFloorPhase.WaitingAtElevator)
            {
                if (Time.time >= waitTime)
                {
                    // Done waiting - transition to target floor
                    OnElevatorWaitComplete();
                }
                return;
            }

            if (isWaiting)
            {
                if (Time.time >= waitTime)
                {
                    isWaiting = false;
                    MoveToNextPoint();
                }
            }
            else if (!Movement.IsMoving)
            {
                // Arrived somewhere - handle based on cross-floor phase
                switch (crossFloorPhase)
                {
                    case CrossFloorPhase.GoingToElevator:
                        float distToElevator = Vector3.Distance(controller.transform.position, elevatorPosition);
                        if (distToElevator <= ELEVATOR_ARRIVAL_DISTANCE)
                        {
                            OnArrivedAtElevator();
                        }
                        else
                        {
                            // Path failed or didn't start yet - retry
                            controller.Movement.MoveTo(elevatorPosition, false);
                        }
                        break;

                    case CrossFloorPhase.GoingToDestination:
                        float distToDest = Vector3.Distance(controller.transform.position, finalDestination);
                        if (distToDest <= ELEVATOR_ARRIVAL_DISTANCE)
                        {
                            // Arrived at final destination
                            pathRetryCount = 0;
                            crossFloorPhase = CrossFloorPhase.None;
                            isWaiting = true;
                            waitTime = Time.time + UnitActions.RandomWaitTime(2f, 5f);
                        }
                        else
                        {
                            pathRetryCount++;
                            if (pathRetryCount > MAX_PATH_RETRIES)
                            {
                                // Destination unreachable - pick a new one on current floor
                                Debug.LogWarning($"[{controller.name}] Patrol: Destination unreachable after {MAX_PATH_RETRIES} retries, picking new destination");
                                pathRetryCount = 0;
                                var floor = controller.CurrentArena?.GetFloorForUnit(controller);
                                if (floor != null)
                                {
                                    finalDestination = GetRandomWalkablePosition(floor);
                                }
                            }
                            controller.Movement.MoveTo(finalDestination, false);
                        }
                        break;

                    default:
                        // Normal patrol - wait then move on
                        isWaiting = true;
                        waitTime = Time.time + UnitActions.RandomWaitTime(2f, 5f);
                        break;
                }
            }
        }

        public override void Exit()
        {
            base.Exit();
            crossFloorPhase = CrossFloorPhase.None;
            UnitActions.StopMovement(controller);
        }

        private void MoveToNextPoint()
        {
            var arena = controller.CurrentArena;
            currentFloor = arena?.GetFloorForUnit(controller);

            if (currentFloor == null || currentFloor.Graph == null)
            {
                Debug.LogWarning($"[{controller.name}] Patrol: No floor/graph available!");
                return;
            }

            // Decide if we should patrol to a different floor
            bool crossFloor = arena.FloorCount > 1 && Random.value < CROSS_FLOOR_CHANCE;

            if (crossFloor)
            {
                StartCrossFloorJourney(arena, currentFloor);
            }
            else
            {
                MoveToRandomPointOnFloor(currentFloor);
            }

            waypointIndex++;
        }

        private void StartCrossFloorJourney(Starbelter.Arena.Arena arena, ArenaFloor fromFloor)
        {
            // Pick a random different floor
            var otherFloors = new System.Collections.Generic.List<ArenaFloor>();
            foreach (var floor in arena.Floors)
            {
                if (floor != fromFloor && floor.Graph != null)
                {
                    otherFloors.Add(floor);
                }
            }

            if (otherFloors.Count == 0)
            {
                MoveToRandomPointOnFloor(fromFloor);
                return;
            }

            targetFloor = otherFloors[Random.Range(0, otherFloors.Count)];

            // Find elevator that connects current floor to target floor
            targetElevator = FindElevatorToFloor(arena, fromFloor, targetFloor);
            if (targetElevator == null)
            {
                Debug.LogWarning($"[{controller.name}] Patrol: No elevator connects {fromFloor.FloorId} to {targetFloor.FloorId}!");
                MoveToRandomPointOnFloor(fromFloor);
                return;
            }

            // Pick final destination on target floor
            finalDestination = GetRandomWalkablePosition(targetFloor);
            if (finalDestination == Vector3.zero)
            {
                Debug.LogWarning($"[{controller.name}] Patrol: No walkable position on {targetFloor.FloorId}!");
                MoveToRandomPointOnFloor(fromFloor);
                return;
            }

            // Get elevator stop on current floor
            var elevatorStop = targetElevator.GetStopForFloor(fromFloor);
            if (elevatorStop == null)
            {
                Debug.LogWarning($"[{controller.name}] Patrol: Elevator has no stop on {fromFloor.FloorId}!");
                MoveToRandomPointOnFloor(fromFloor);
                return;
            }

            Debug.Log($"[{controller.name}] Patrol: Cross-floor journey to {targetFloor.FloorId} via elevator");

            // Phase 1: Go to elevator
            elevatorPosition = elevatorStop.position;
            crossFloorPhase = CrossFloorPhase.GoingToElevator;
            controller.Movement.MoveTo(elevatorPosition, false);
        }

        private void OnArrivedAtElevator()
        {
            // Calculate travel time based on floor distance
            float travelTime = targetElevator.GetTravelTime(currentFloor, targetFloor);

            Debug.Log($"[{controller.name}] Patrol: At elevator, waiting {travelTime:F1}s to travel to {targetFloor.FloorId}");

            // Phase 2: Wait at elevator for travel time
            crossFloorPhase = CrossFloorPhase.WaitingAtElevator;
            waitTime = Time.time + travelTime;
        }

        private void OnElevatorWaitComplete()
        {
            Debug.Log($"[{controller.name}] Patrol: Elevator arrived at {targetFloor.FloorId}");

            // Teleport to target floor
            targetElevator.TransitionUnit(controller, targetFloor);

            // Phase 3: Go to final destination on new floor
            pathRetryCount = 0;
            crossFloorPhase = CrossFloorPhase.GoingToDestination;
            controller.Movement.MoveTo(finalDestination, false);
        }

        private void MoveToRandomPointOnFloor(ArenaFloor floor)
        {
            Vector3 targetPos = GetRandomWalkablePosition(floor);
            if (targetPos == Vector3.zero)
            {
                Debug.LogWarning($"[{controller.name}] Patrol: No walkable nodes on {floor.FloorId}!");
                return;
            }

            crossFloorPhase = CrossFloorPhase.None;
            controller.Movement.MoveTo(targetPos, false);
        }

        private Vector3 GetRandomWalkablePosition(ArenaFloor floor)
        {
            var graph = floor.Graph;
            if (graph == null) return Vector3.zero;

            var walkableNodes = new System.Collections.Generic.List<global::Pathfinding.GraphNode>();
            graph.GetNodes(node => { if (node.Walkable) walkableNodes.Add(node); });

            if (walkableNodes.Count == 0) return Vector3.zero;

            var targetNode = walkableNodes[Random.Range(0, walkableNodes.Count)];
            return (Vector3)targetNode.position;
        }

        private Elevator FindElevatorToFloor(Starbelter.Arena.Arena arena, ArenaFloor fromFloor, ArenaFloor toFloor)
        {
            Elevator bestElevator = null;
            float bestDistance = float.MaxValue;

            foreach (var elevator in arena.Elevators)
            {
                if (elevator.ConnectsFloors(fromFloor, toFloor))
                {
                    var fromStop = elevator.GetStopForFloor(fromFloor);
                    if (fromStop != null)
                    {
                        float dist = Vector3.Distance(controller.transform.position, fromStop.position);
                        if (dist < bestDistance)
                        {
                            bestDistance = dist;
                            bestElevator = elevator;
                        }
                    }
                }
            }

            return bestElevator;
        }
    }
}
