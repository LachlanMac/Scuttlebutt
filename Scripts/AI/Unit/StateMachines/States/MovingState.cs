using UnityEngine;
using Starbelter.Combat;
using Starbelter.Core;
using Starbelter.Pathfinding;

namespace Starbelter.AI
{
    /// <summary>
    /// Moving state - relocating to a destination.
    /// Handles pathfinding, arrival detection, and combat movement.
    /// Combat move: Fire while moving at half speed (when aggressive or low threat).
    /// </summary>
    public class MovingState : UnitState
    {
        // Stuck detection
        private float stuckTimer;
        private Vector3 lastPosition;
        private const float STUCK_TIMEOUT = 2f;

        // Combat move settings
        private bool isCombatMove;
        private float lastCombatShotTime;
        private const float COMBAT_SHOT_INTERVAL = 1.5f;
        private const float COMBAT_MOVE_SPEED = 0.5f;   // Half speed
        private const float LOW_AMMO_THRESHOLD = 0.3f;  // 30% - need ammo on arrival
        private const float THREAT_THRESHOLD = 10f;     // Below this = low threat

        public override void Enter()
        {
            base.Enter();
            stuckTimer = 0f;
            lastPosition = Position;
            lastCombatShotTime = 0f;

            // Clear the pending request flag now that we're actually moving
            controller.ClearPendingFightingPositionRequest();

            // Determine if this should be a combat move
            isCombatMove = ShouldCombatMove();

            if (isCombatMove)
            {
                controller.Movement.SpeedMultiplier = COMBAT_MOVE_SPEED;
                Debug.Log($"[{controller.name}] COMBAT MOVE - firing while advancing at half speed");
            }
            else
            {
                controller.Movement.SpeedMultiplier = 1f;
            }

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

        /// <summary>
        /// Determine if we should do a combat move (fire while moving).
        /// Requires: valid target, enough ammo, and (aggressive posture OR low threat).
        /// </summary>
        private bool ShouldCombatMove()
        {
            // Must have a valid target to shoot at
            if (!controller.IsTargetValid())
            {
                // Try to find a target
                var target = controller.FindThreatTarget(controller.WeaponRange);
                if (target == null)
                {
                    target = controller.FindBestTarget(controller.WeaponRange);
                }
                if (target != null)
                {
                    controller.SetTarget(target);
                }
                else
                {
                    return false;
                }
            }

            // Must have enough ammo (don't arrive empty)
            var weapon = controller.Character?.MainWeapon;
            if (weapon == null) return false;

            float ammoPercent = (float)weapon.CurrentAmmo / weapon.MagazineSize;
            if (ammoPercent < LOW_AMMO_THRESHOLD)
            {
                Debug.Log($"[{controller.name}] No combat move - low ammo ({ammoPercent:P0})");
                return false;
            }

            // Aggressive posture OR low threat = combat move
            bool isAggressive = (controller.Posture == Posture.Aggressive);
            float threat = controller.GetThreatAtPosition(Position);
            bool isLowThreat = (threat < THREAT_THRESHOLD);

            if (isAggressive)
            {
                Debug.Log($"[{controller.name}] Combat move enabled - aggressive posture");
                return true;
            }

            if (isLowThreat)
            {
                Debug.Log($"[{controller.name}] Combat move enabled - low threat ({threat:F1})");
                return true;
            }

            return false;
        }

        public override void Update()
        {
            if (!IsValid) return;

            float threat = controller.GetThreatAtPosition(Position);

            // Check if threat is high enough to pin us
            if (controller.IsPinned)
            {
                // Check if there's cover nearby we can duck into
                if (TryFindNearbyCover())
                {
                    // Will redirect to cover, continue moving
                    Debug.Log($"[{controller.name}] High threat while moving - redirecting to nearby cover");
                }
                else
                {
                    // No cover, just get pinned
                    controller.InterruptMovement();
                    controller.ChangeState(UnitStateType.Pinned);
                    return;
                }
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

            // Combat move shooting
            if (isCombatMove)
            {
                UpdateCombatMove();
            }
        }

        /// <summary>
        /// Try to find nearby cover when threat spikes.
        /// Returns true if cover found and we're redirecting to it.
        /// </summary>
        private bool TryFindNearbyCover()
        {
            var coverQuery = CoverQuery.Instance;
            if (coverQuery == null) return false;

            // Get threat direction (who's shooting at us)
            Vector3 threatPos = Position + Vector3.right * 10f;
            var threatTarget = controller.FindThreatTarget(controller.PerceptionRange);
            if (threatTarget != null)
            {
                threatPos = threatTarget.Position;
            }
            else if (controller.CurrentTarget != null)
            {
                threatPos = controller.CurrentTarget.Position;
            }

            // Search for cover within 5 units - use defensive mode (doesn't require LOS to enemy)
            var searchParams = CoverSearchParams.Default;
            searchParams.Mode = CoverMode.Defensive;

            var coverResult = coverQuery.FindBestCover(Position, threatPos, searchParams, 5f, controller.gameObject);
            if (coverResult.HasValue)
            {
                // Redirect to cover
                controller.Movement.MoveTo(coverResult.Value.WorldPosition);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Handle combat move shooting.
        /// </summary>
        private void UpdateCombatMove()
        {
            // Re-check if we should still be combat moving
            if (!ShouldStillCombatMove())
            {
                isCombatMove = false;
                controller.Movement.SpeedMultiplier = 1f;
                Debug.Log($"[{controller.name}] Ending combat move - conditions changed");
                return;
            }

            // Check shot interval
            if (Time.time < lastCombatShotTime + COMBAT_SHOT_INTERVAL)
            {
                return;
            }

            // Refresh target - prioritize threats
            RefreshCombatMoveTarget();

            // Need valid target
            if (!controller.IsTargetValid())
            {
                return;
            }

            // Need LOS to target
            if (!controller.HasLineOfSight(Position, controller.CurrentTarget.Position))
            {
                return;
            }

            // Need to be able to shoot
            if (!controller.CanShoot)
            {
                return;
            }

            // Select and fire
            ShotType shotType = SelectCombatMoveShot();

            if (shotType == ShotType.Burst)
            {
                controller.FireBurst();
            }
            else
            {
                controller.FireShot(ShotType.Snap);
            }

            lastCombatShotTime = Time.time;
        }

        /// <summary>
        /// Check if combat move should continue (lighter check than initial).
        /// </summary>
        private bool ShouldStillCombatMove()
        {
            // Check ammo
            var weapon = controller.Character?.MainWeapon;
            if (weapon == null) return false;

            float ammoPercent = (float)weapon.CurrentAmmo / weapon.MagazineSize;
            if (ammoPercent < LOW_AMMO_THRESHOLD)
            {
                return false;
            }

            // Check threat hasn't spiked (but don't require low threat to continue)
            // If we started combat moving while aggressive, continue even if threat rises
            if (controller.Posture == Posture.Aggressive)
            {
                return true;
            }

            // For non-aggressive, abort if threat is now dangerous
            float threat = controller.GetThreatAtPosition(Position);
            return threat < 15f;  // Allow slightly higher threat than initial
        }

        /// <summary>
        /// Refresh target for combat move - prioritize threats.
        /// </summary>
        private void RefreshCombatMoveTarget()
        {
            // First priority: whoever is shooting at us
            var threat = controller.FindThreatTarget(controller.WeaponRange);
            if (threat != null)
            {
                controller.SetTarget(threat);
                return;
            }

            // Second priority: best target (exposed, close, etc.)
            if (!controller.IsTargetValid())
            {
                var best = controller.FindBestTarget(controller.WeaponRange);
                if (best != null)
                {
                    controller.SetTarget(best);
                }
            }
        }

        /// <summary>
        /// Select shot type for combat move.
        /// Burst if close and target exposed/moving, otherwise snap.
        /// </summary>
        private ShotType SelectCombatMoveShot()
        {
            var target = controller.CurrentTarget;
            var weapon = controller.Character?.MainWeapon;
            if (target == null || weapon == null) return ShotType.Snap;

            float range = Vector3.Distance(Position, target.Position);
            float halfRange = controller.WeaponRange * 0.5f;

            // Check target state
            bool targetExposed = false;
            bool targetMoving = false;

            var targetController = target.Transform?.GetComponent<UnitController>();
            if (targetController != null)
            {
                targetExposed = !targetController.IsInCover;
                targetMoving = targetController.Movement != null && targetController.Movement.IsMoving;
            }

            // Burst: close range + (exposed OR moving) + weapon can burst
            if (range <= halfRange && (targetExposed || targetMoving) && weapon.CanBurst)
            {
                Debug.Log($"[{controller.name}] Combat move BURST - close range, target exposed/moving");
                return ShotType.Burst;
            }

            // Default: snap shot
            return ShotType.Snap;
        }

        private void OnArrived()
        {
            controller.StopMoving();

            // Reset speed multiplier
            controller.Movement.SpeedMultiplier = 1f;

            // Check for enemies at perception range - use best target
            var enemy = controller.FindBestTarget(controller.PerceptionRange);

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
            // Reset speed multiplier
            controller.Movement.SpeedMultiplier = 1f;

            if (!controller.HasArrivedAtDestination)
            {
                controller.InterruptMovement();
            }
        }
    }
}
