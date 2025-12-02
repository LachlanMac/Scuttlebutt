using UnityEngine;
using Starbelter.Core;

namespace Starbelter.Tactics.States
{
    /// <summary>
    /// Moving state - relocating to a destination.
    /// Handles pathfinding and arrival detection.
    /// </summary>
    public class MovingState : TacticalState
    {
        private float stuckTimer;
        private Vector3 lastPosition;

        public override void Enter(TacticalUnit unit)
        {
            base.Enter(unit);
            stuckTimer = 0f;
            lastPosition = unit.Position;

            // Start moving to pending destination
            unit.StartMoving();
        }

        public override void Update()
        {
            // Check suppression
            if (unit.Suppression >= TacticalConstants.SuppressionPinThreshold)
            {
                unit.StopMoving();
                unit.ChangeState(TacticalStateType.Pinned);
                return;
            }

            // Check if arrived
            if (unit.HasArrivedAtDestination)
            {
                OnArrived();
                return;
            }

            // Check if stuck
            float moved = Vector3.Distance(unit.Position, lastPosition);
            if (moved < 0.1f)
            {
                stuckTimer += Time.deltaTime;
                if (stuckTimer >= TacticalConstants.StuckTimeout)
                {
                    // Stuck - recalculate or give up
                    unit.RecalculatePath();
                    stuckTimer = 0f;
                }
            }
            else
            {
                stuckTimer = 0f;
                lastPosition = unit.Position;
            }

            // Opportunity fire while moving
            if (unit.CurrentTarget != null && !unit.CurrentTarget.IsDead)
            {
                if (TacticalQueries.HasLineOfSight(unit.Position, unit.CurrentTarget.Position))
                {
                    if (unit.CanShoot)
                    {
                        unit.FireAtTarget();
                    }
                }
            }
        }

        private void OnArrived()
        {
            unit.StopMoving();

            // Check for enemies
            var enemy = TacticalQueries.FindClosestVisibleEnemy(
                unit.Position,
                unit.Team,
                TacticalConstants.MaxEngageRange);

            if (enemy != null)
            {
                unit.SetTarget(enemy);
                unit.ChangeState(TacticalStateType.Combat);
            }
            else
            {
                unit.ChangeState(TacticalStateType.Idle);
            }
        }

        public override void Exit()
        {
            // Ensure we stop moving when leaving this state
            if (!unit.HasArrivedAtDestination)
            {
                unit.StopMoving();
            }
        }
    }
}
