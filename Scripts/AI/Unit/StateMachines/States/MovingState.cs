using UnityEngine;

namespace Starbelter.AI
{
    /// <summary>
    /// Moving state - relocating to a destination.
    /// Handles pathfinding and arrival detection.
    /// </summary>
    public class MovingState : UnitState
    {
        private float stuckTimer;
        private Vector3 lastPosition;
        private const float STUCK_TIMEOUT = 2f;
        private const float SUPPRESSION_PIN_THRESHOLD = 80f;

        public override void Enter()
        {
            base.Enter();
            stuckTimer = 0f;
            lastPosition = Position;

            // Clear the pending request flag now that we're actually moving
            controller.ClearPendingFightingPositionRequest();

            // Start moving to pending destination
            // Use threat-aware path when moving to fighting positions
            if (controller.ShouldUseThreatAwarePath)
            {
                controller.StartThreatAwareMove();
            }
            else
            {
                controller.StartMoving();
            }
        }

        public override void Update()
        {
            if (!IsValid) return;

            // Check suppression - redirect to nearest tile, don't stop mid-tile
            if (controller.Suppression >= SUPPRESSION_PIN_THRESHOLD)
            {
                controller.InterruptMovement();
                controller.ChangeState(UnitStateType.Pinned);
                return;
            }

            // Check if arrived
            if (controller.HasArrivedAtDestination)
            {
                OnArrived();
                return;
            }

            // Check if stuck
            float moved = Vector3.Distance(Position, lastPosition);
            if (moved < 0.1f)
            {
                stuckTimer += Time.deltaTime;
                if (stuckTimer >= STUCK_TIMEOUT)
                {
                    // Stuck - give up and evaluate
                    OnArrived();
                    return;
                }
            }
            else
            {
                stuckTimer = 0f;
                lastPosition = Position;
            }

            // Opportunity fire while moving
            if (controller.IsTargetValid())
            {
                if (controller.HasLineOfSight(Position, controller.CurrentTarget.Position))
                {
                    if (controller.CanShoot)
                    {
                        controller.FireAtTarget();
                    }
                }
            }
        }

        private void OnArrived()
        {
            controller.StopMoving();

            // Check for enemies at perception range
            var enemy = controller.FindClosestVisibleEnemy(controller.PerceptionRange);

            if (enemy != null)
            {
                controller.SetTarget(enemy);
                // Have a target - enter combat and let CombatState handle repositioning if needed
                controller.ChangeState(UnitStateType.Combat);
            }
            else if (controller.Squad != null && controller.Squad.HasBeenEngaged)
            {
                // Squad engaged but we can't see anyone - go to Combat and let it handle repositioning
                // (CombatState has cooldown to prevent Combat <-> Moving cycling)
                controller.ChangeState(UnitStateType.Combat);
            }
            else
            {
                controller.ChangeState(UnitStateType.Ready);
            }
        }

        public override void Exit()
        {
            if (!controller.HasArrivedAtDestination)
            {
                controller.InterruptMovement();
            }
        }
    }
}
