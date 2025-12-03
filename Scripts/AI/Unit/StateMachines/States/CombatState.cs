using UnityEngine;
using Starbelter.Combat;

namespace Starbelter.AI
{
    /// <summary>
    /// Combat state - actively engaging an enemy.
    /// Shoots at target, evaluates position, may reposition.
    /// </summary>
    public class CombatState : UnitState
    {
        private float lastEvalTime;
        private const float EVAL_INTERVAL = 0.5f;
        private const float SUPPRESSION_PIN_THRESHOLD = 80f;

        public override void Enter()
        {
            base.Enter();
            lastEvalTime = 0f;
            // Don't reset reposition cooldown - it persists across state changes to prevent cycling
        }

        public override void Update()
        {
            if (!IsValid) return;

            // Check suppression first
            if (controller.Suppression >= SUPPRESSION_PIN_THRESHOLD)
            {
                controller.ChangeState(UnitStateType.Pinned);
                return;
            }

            // Validate target (handles Unity's destroyed objects)
            if (!controller.IsTargetValid())
            {
                // Check if target died (vs just became invalid)
                if (controller.CurrentTarget != null && controller.CurrentTarget.IsDead)
                {
                    controller.NotifySquadEnemyKilled();
                }

                controller.ClearTarget();

                // Try to find a new target at perception range
                var newEnemy = controller.FindClosestVisibleEnemy(controller.PerceptionRange);
                if (newEnemy != null)
                {
                    controller.SetTarget(newEnemy);

                    // Check if in weapon range or need to find fighting position
                    float distance = Vector3.Distance(Position, newEnemy.Position);
                    if (distance > controller.WeaponRange && !controller.IsRepositionOnCooldown)
                    {
                        // Enemy spotted but out of range - find fighting position (with cooldown)
                        controller.ResetRepositionCooldown();
                        // NOTE: RequestFightingPosition is ASYNC - callback will trigger Moving if position found
                        controller.RequestFightingPosition();
                    }
                    // Stay in combat with new target; callback will switch to Moving if needed
                }
                else if (controller.Squad != null && controller.Squad.HasBeenEngaged)
                {
                    // Squad has made contact but we can't see anyone - find fighting position (with cooldown)
                    if (!controller.IsRepositionOnCooldown)
                    {
                        controller.ResetRepositionCooldown();
                        // NOTE: RequestFightingPosition is ASYNC - callback will trigger Moving if position found
                        controller.RequestFightingPosition();
                    }
                    // If no fighting position or on cooldown, stay in CombatState and keep scanning
                }
                else
                {
                    controller.ChangeState(UnitStateType.Ready);
                }
                return;
            }

            // Check if we can shoot - only reposition if FULLY blocked (not half cover)
            var los = CombatUtils.CheckLineOfSight(Position, controller.CurrentTarget.Position);

            // Ducked target behind half cover = effectively full cover (can't see them)
            bool targetDuckedBehindCover = controller.CurrentTarget.IsDucked && los.IsPartialCover;

            if (los.IsBlocked || targetDuckedBehindCover)
            {
                // Full cover blocking shot (or ducked behind half cover) - can't shoot, try to reposition
                if (!controller.IsRepositionOnCooldown)
                {
                    controller.ResetRepositionCooldown();
                    // NOTE: RequestFightingPosition is ASYNC - callback will trigger Moving if position found
                    controller.RequestFightingPosition();
                }
                // Don't try to shoot - LOS is fully blocked
            }
            else
            {
                // LOS is clear or only partial cover (and target not ducked) - shoot at target
                TryShoot();
            }

            // Periodic tactical evaluation
            if (Time.time - lastEvalTime >= EVAL_INTERVAL)
            {
                lastEvalTime = Time.time;
                EvaluateTacticalPosition();
            }
        }

        private void TryShoot()
        {
            if (controller.CurrentTarget == null) return;

            // Check if we need to reload (empty magazine)
            if (controller.NeedsReload)
            {
                controller.ChangeState(UnitStateType.Reloading);
                return;
            }

            if (!controller.CanShoot) return;

            float range = Vector3.Distance(Position, controller.CurrentTarget.Position);
            if (range <= controller.WeaponRange)
            {
                controller.FireAtTarget();
            }
        }

        private void EvaluateTacticalPosition()
        {
            // Don't evaluate if we already have a pending fighting position request
            if (controller.HasPendingDestination) return;

            // Check the reposition cooldown first
            if (controller.IsRepositionOnCooldown) return;
            if (!CanTransition) return;

            // Tactical reload opportunity - in cover and low ammo
            if (controller.ShouldTacticalReload)
            {
                controller.ChangeState(UnitStateType.Reloading);
                return;
            }

            // Deadly threat - MUST move regardless of cover (being in cover doesn't help if kill zone)
            if (controller.IsInDeadlyDanger())
            {
                controller.ResetRepositionCooldown();
                controller.RequestFightingPosition();
                // Callback will trigger Moving if position found
                return;
            }

            // Dangerous threat and not in cover - find better position
            if (controller.IsInDanger() && !controller.IsInCover)
            {
                controller.ResetRepositionCooldown();
                controller.RequestFightingPosition();
                // Callback will trigger Moving if position found
            }
        }
    }
}
