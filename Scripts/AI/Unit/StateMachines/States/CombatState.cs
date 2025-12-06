using UnityEngine;
using Starbelter.Combat;

namespace Starbelter.AI
{
    /// <summary>
    /// Combat state - actively engaging an enemy.
    /// Shoots at target, evaluates position, may reposition.
    /// Handles shot type selection: Snap, Aimed, Burst.
    /// </summary>
    public class CombatState : UnitState
    {
        private float lastEvalTime;
        private const float EVAL_INTERVAL = 0.5f;

        // Aiming state
        private bool isAiming;
        private float aimStartTime;
        private float aimDuration;

        public override void Enter()
        {
            base.Enter();
            lastEvalTime = 0f;
            CancelAiming();
            // Don't reset reposition cooldown - it persists across state changes to prevent cycling
        }

        public override void Exit()
        {
            base.Exit();
            CancelAiming();
            controller.CancelBurst();
        }

        public override void Update()
        {
            if (!IsValid) return;

            // Check if threat is high enough to pin us
            if (controller.IsPinned)
            {
                controller.ChangeState(UnitStateType.Pinned);
                return;
            }

            // If we're mid-burst, let it finish
            if (controller.IsFiringBurst)
            {
                return;
            }

            // If we're aiming, check if aim is complete
            if (isAiming)
            {
                UpdateAiming();
                return;
            }

            // Validate target (handles Unity's destroyed objects)
            if (!controller.IsTargetValid())
            {
                HandleInvalidTarget();
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

        private void HandleInvalidTarget()
        {
            // Check if target died (vs just became invalid)
            if (controller.CurrentTarget != null && controller.CurrentTarget.IsDead)
            {
                controller.NotifySquadEnemyKilled();
            }

            controller.ClearTarget();

            // Try to find best target at perception range (prioritizes exposed enemies)
            var newEnemy = controller.FindBestTarget(controller.PerceptionRange);
            if (newEnemy != null)
            {
                controller.SetTarget(newEnemy);

                // Check if in weapon range or need to find fighting position
                float distance = Vector3.Distance(Position, newEnemy.Position);
                if (distance > controller.WeaponRange && !controller.IsRepositionOnCooldown)
                {
                    controller.ResetRepositionCooldown();
                    controller.RequestFightingPosition();
                }
            }
            else if (controller.Squad != null && controller.Squad.HasBeenEngaged)
            {
                if (!controller.IsRepositionOnCooldown)
                {
                    controller.ResetRepositionCooldown();
                    controller.RequestFightingPosition();
                }
            }
            else
            {
                controller.ChangeState(UnitStateType.Ready);
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
            if (range > controller.WeaponRange) return;

            // Select shot type based on tactical situation
            ShotType selectedShot = SelectShotType();

            if (selectedShot == ShotType.Aimed)
            {
                // Start aiming - will fire when aim completes
                StartAiming();
            }
            else if (selectedShot == ShotType.Burst)
            {
                // Fire burst
                controller.FireBurst();
            }
            else
            {
                // Snap shot - fire immediately
                controller.FireShot(ShotType.Snap);
            }
        }

        /// <summary>
        /// Select the best shot type based on tactical situation.
        /// Priority:
        /// 1. AIMED: Default when safe (low threat, in cover) - careful, deadly shots
        /// 2. BURST: Target exposed and moving at close range - volume of fire
        /// 3. SNAP: Reactive shots when under pressure
        /// </summary>
        private ShotType SelectShotType()
        {
            var target = controller.CurrentTarget;
            var weapon = controller.Character?.MainWeapon;
            if (target == null || weapon == null) return ShotType.Snap;

            float range = Vector3.Distance(Position, target.Position);
            float halfRange = controller.WeaponRange * 0.5f;
            float threat = controller.GetThreatAtPosition(Position);

            // Check target state
            bool targetIsMoving = false;
            bool targetHasCover = false;

            // Try to get target's movement and cover state
            var targetController = target.Transform?.GetComponent<UnitController>();
            if (targetController != null)
            {
                targetIsMoving = targetController.Movement != null && targetController.Movement.IsMoving;
                targetHasCover = targetController.IsInCover;
            }

            // BURST: Target is moving AND has no cover AND within half weapon range AND weapon can burst
            // This takes priority over aimed because it's a fleeting opportunity
            if (weapon.CanBurst && targetIsMoving && !targetHasCover && range <= halfRange)
            {
                Debug.Log($"[{controller.name}] Selecting BURST - target moving, no cover, close range");
                return ShotType.Burst;
            }

            // AIMED: Default when safe - low threat (< dangerous) AND in cover AND weapon supports it
            // This is the preferred shot type when we have the luxury of time
            if (weapon.CanAimedShot && threat < 10f && controller.IsInCover)
            {
                Debug.Log($"[{controller.name}] Selecting AIMED - low threat ({threat:F1}), in cover");
                return ShotType.Aimed;
            }

            // SNAP: Reactive shots when under pressure or exposed
            // Used when we don't have time for aimed shots
            return ShotType.Snap;
        }

        /// <summary>
        /// Check if we should switch to a better target.
        /// Called periodically during tactical evaluation.
        /// </summary>
        private void ConsiderBetterTarget()
        {
            if (controller.CurrentTarget == null) return;

            // Get all valid targets and see if there's a significantly better one
            var targets = controller.GetAllValidTargets(controller.WeaponRange);
            if (targets.Count <= 1) return;

            // Find current target's score
            float currentScore = 0f;
            foreach (var t in targets)
            {
                if (t.Target == controller.CurrentTarget)
                {
                    currentScore = t.Score;
                    break;
                }
            }

            // Find best target
            UnitController.TargetInfo best = targets[0];
            foreach (var t in targets)
            {
                if (t.Score > best.Score) best = t;
            }

            // Only switch if significantly better (20+ points difference)
            // This prevents constant target switching
            if (best.Target != controller.CurrentTarget && best.Score > currentScore + 20f)
            {
                Debug.Log($"[{controller.name}] Switching to better target: {best.Target.Transform.name} (score {best.Score:F0} vs {currentScore:F0})");
                controller.SetTarget(best.Target);
            }
        }

        #region Aiming

        private void StartAiming()
        {
            var weapon = controller.Character?.MainWeapon;
            if (weapon == null || !weapon.CanAimedShot)
            {
                // Can't aim, fall back to snap
                controller.FireShot(ShotType.Snap);
                return;
            }

            isAiming = true;
            aimStartTime = Time.time;
            aimDuration = weapon.AimTime;

            // Must stand up to aim (can't aim while ducked)
            controller.SetDucked(false);

            Debug.Log($"[{controller.name}] Starting aimed shot - {aimDuration:F1}s aim time");
        }

        private void UpdateAiming()
        {
            // Check if we should abort aiming
            if (!controller.IsTargetValid() || controller.IsDead || controller.IsPinned)
            {
                Debug.Log($"[{controller.name}] Aim interrupted!");
                CancelAiming();
                return;
            }

            // Check if aim time is complete
            if (Time.time >= aimStartTime + aimDuration)
            {
                CompleteAimedShot();
            }
        }

        private void CompleteAimedShot()
        {
            isAiming = false;

            if (!controller.IsTargetValid())
            {
                Debug.Log($"[{controller.name}] Aimed shot cancelled - target invalid");
                return;
            }

            Debug.Log($"[{controller.name}] Aimed shot FIRING!");
            controller.FireShot(ShotType.Aimed);
        }

        private void CancelAiming()
        {
            isAiming = false;
        }

        #endregion

        private void EvaluateTacticalPosition()
        {
            // Don't evaluate if we're aiming or mid-burst
            if (isAiming || controller.IsFiringBurst) return;

            // Don't evaluate if we already have a pending fighting position request
            if (controller.HasPendingDestination) return;

            // Consider switching to a better target (exposed enemy, threat, etc.)
            ConsiderBetterTarget();

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
                return;
            }

            // Dangerous threat and not in cover - find better position
            if (controller.IsInDanger() && !controller.IsInCover)
            {
                controller.ResetRepositionCooldown();
                controller.RequestFightingPosition();
            }
        }
    }
}
