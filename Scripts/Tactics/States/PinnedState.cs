using UnityEngine;
using Starbelter.Core;

namespace Starbelter.Tactics.States
{
    /// <summary>
    /// Pinned state - suppressed and unable to act effectively.
    /// Waits for suppression to decay before recovering.
    /// </summary>
    public class PinnedState : TacticalState
    {
        public override void Enter(TacticalUnit unit)
        {
            base.Enter(unit);

            // Stop all actions
            unit.StopMoving();

            // Visual feedback (duck/cower animation could trigger here)
        }

        public override void Update()
        {
            // Suppression decays over time (handled by TacticalUnit)
            // Check if we can recover
            if (unit.Suppression < TacticalConstants.SuppressionPinThreshold * 0.5f)
            {
                // Recovered enough to act
                Recover();
            }
        }

        private void Recover()
        {
            // Check threats
            var enemy = TacticalQueries.FindClosestVisibleEnemy(
                unit.Position,
                unit.Team,
                TacticalConstants.MaxEngageRange);

            if (enemy != null)
            {
                unit.SetTarget(enemy);
                unit.ChangeState(TacticalStateType.Combat);
            }
            else if (TacticalQueries.IsInDanger(unit.Position, unit.Team))
            {
                // Still in danger, find cover
                unit.RequestCoverPosition();
                if (unit.HasPendingDestination)
                {
                    unit.ChangeState(TacticalStateType.Moving);
                }
                else
                {
                    unit.ChangeState(TacticalStateType.Idle);
                }
            }
            else
            {
                unit.ChangeState(TacticalStateType.Idle);
            }
        }
    }
}
