using UnityEngine;
using Starbelter.Core;

namespace Starbelter.Tactics.States
{
    /// <summary>
    /// Combat state - actively engaging an enemy.
    /// Shoots at target, evaluates position, may reposition.
    /// </summary>
    public class CombatState : TacticalState
    {
        private float lastEvalTime;
        private float lastShotTime;

        public override void Enter(TacticalUnit unit)
        {
            base.Enter(unit);
            lastEvalTime = 0f;
            lastShotTime = 0f;
        }

        public override void Update()
        {
            // Check suppression first
            if (unit.Suppression >= TacticalConstants.SuppressionPinThreshold)
            {
                unit.ChangeState(TacticalStateType.Pinned);
                return;
            }

            // Validate target
            if (unit.CurrentTarget == null || unit.CurrentTarget.IsDead)
            {
                unit.ClearTarget();
                unit.ChangeState(TacticalStateType.Idle);
                return;
            }

            // Check if we still have LOS
            if (!TacticalQueries.HasLineOfSight(unit.Position, unit.CurrentTarget.Position))
            {
                // Lost sight - need to reposition
                unit.RequestAdvancePosition();
                unit.ChangeState(TacticalStateType.Moving);
                return;
            }

            // Shoot at target
            TryShoot();

            // Periodic tactical evaluation
            if (Time.time - lastEvalTime >= TacticalConstants.EvaluationInterval)
            {
                lastEvalTime = Time.time;
                EvaluateTacticalPosition();
            }
        }

        private void TryShoot()
        {
            if (!unit.CanShoot) return;

            float range = TacticalQueries.GetDistance(unit.Position, unit.CurrentTarget.Position);
            if (range <= unit.EffectiveRange)
            {
                unit.FireAtTarget();
                lastShotTime = Time.time;
            }
        }

        private void EvaluateTacticalPosition()
        {
            // In high threat? Find better position
            if (TacticalQueries.IsInDanger(unit.Position, unit.Team))
            {
                unit.RequestCoverPosition();
                if (unit.HasPendingDestination && CanTransition)
                {
                    unit.ChangeState(TacticalStateType.Moving);
                    return;
                }
            }

            // Not in cover? Try to get there
            if (!unit.IsInCover)
            {
                unit.RequestCoverPosition();
                if (unit.HasPendingDestination && CanTransition)
                {
                    unit.ChangeState(TacticalStateType.Moving);
                }
            }
        }
    }
}
