using UnityEngine;
using Starbelter.Combat;
using Starbelter.Core;

namespace Starbelter.AI
{
    /// <summary>
    /// Pinned state - under heavy threat and unable to act effectively.
    /// Can peek and fire snap shots when threat is moderate (20-30).
    /// Stays ducked when threat is severe (30+).
    /// </summary>
    public class PinnedState : UnitState
    {
        // Pop-up shooting timers
        private float peekCooldown;
        private float nextPeekTime;
        private bool isPeeking;
        private float peekStartTime;
        private const float PEEK_AIM_TIME = 0.4f;  // Brief aim time for snap shot while peeking
        private const float MIN_PEEK_COOLDOWN = 2f;
        private const float MAX_PEEK_COOLDOWN = 4f;

        // Damage interrupt tracking
        private bool wasHitWhilePeeking;

        public override void Enter()
        {
            base.Enter();
            controller.InterruptMovement();
            controller.SetDucked(true); // Cowering behind cover

            // Reset peek state
            isPeeking = false;
            wasHitWhilePeeking = false;
            ResetPeekCooldown();

            // Subscribe to damage events for interrupt
            if (controller.Health != null)
            {
                controller.Health.OnDamageTaken += OnDamageTaken;
            }

            float threat = controller.GetThreatAtPosition(controller.transform.position);
            Debug.Log($"[{controller.name}] PINNED! Threat={threat:F1} (threshold={controller.ThreatPinThreshold}, severe={controller.ThreatSevere})");
        }

        public override void Exit()
        {
            base.Exit();
            controller.SetDucked(false); // Standing back up
            isPeeking = false;

            // Unsubscribe from damage events
            if (controller.Health != null)
            {
                controller.Health.OnDamageTaken -= OnDamageTaken;
            }
        }

        private void OnDamageTaken(float damage)
        {
            // If we were peeking, interrupt and duck
            if (isPeeking)
            {
                wasHitWhilePeeking = true;
                Debug.Log($"[{controller.name}] HIT while peeking! Ducking immediately.");
                InterruptPeek();
            }
        }

        private float debugLogTimer;

        public override void Update()
        {
            if (!IsValid) return;

            float threat = controller.GetThreatAtPosition(controller.transform.position);

            // Debug: log threat every 2 seconds
            debugLogTimer += Time.deltaTime;
            if (debugLogTimer >= 2f)
            {
                debugLogTimer = 0f;
                string peekStatus = isPeeking ? "PEEKING" : (controller.CanPeekWhilePinned() ? "can-peek" : "ducked");
                Debug.Log($"[{controller.name}] PINNED - Threat={threat:F1} (unpin<{controller.ThreatUnpinThreshold}, severe>={controller.ThreatSevere}) [{peekStatus}]");
            }

            // Check if we can recover (threat dropped below unpin threshold)
            if (threat < controller.ThreatUnpinThreshold)
            {
                Recover();
                return;
            }

            // Handle peeking state
            if (isPeeking)
            {
                UpdatePeeking();
                return;
            }

            // Check if we should try to peek
            if (controller.CanPeekWhilePinned() && Time.time >= nextPeekTime)
            {
                TryStartPeek();
            }
        }

        private void TryStartPeek()
        {
            // Need a target to shoot at
            ITargetable target = controller.CurrentTarget;
            if (target == null || target.IsDead)
            {
                // Try to find a threat (someone shooting at us)
                target = controller.FindThreatTarget(controller.WeaponRange);
            }
            if (target == null)
            {
                // Try to find best target
                target = controller.FindBestTarget(controller.WeaponRange);
            }
            if (target == null)
            {
                // No target, reset cooldown and wait
                ResetPeekCooldown();
                return;
            }

            // Check if we need ammo
            if (controller.NeedsReload)
            {
                // Can't shoot, stay ducked
                ResetPeekCooldown();
                return;
            }

            // Start peeking
            controller.SetTarget(target);
            isPeeking = true;
            peekStartTime = Time.time;
            controller.SetDucked(false);  // Pop up
            wasHitWhilePeeking = false;

            Debug.Log($"[{controller.name}] PEEK! Popping up to snap shot at {target.Transform.name}");
        }

        private void UpdatePeeking()
        {
            // Check if we got hit (interrupt)
            if (wasHitWhilePeeking)
            {
                return; // Already handled in OnDamageTaken
            }

            // Check if threat became severe while peeking
            if (controller.IsThreatSevere())
            {
                Debug.Log($"[{controller.name}] Threat became severe while peeking! Ducking.");
                InterruptPeek();
                return;
            }

            // Check if target is still valid
            if (!controller.IsTargetValid())
            {
                Debug.Log($"[{controller.name}] Target lost while peeking, ducking.");
                InterruptPeek();
                return;
            }

            // Check if aim time is complete
            if (Time.time >= peekStartTime + PEEK_AIM_TIME)
            {
                CompletePeekShot();
            }
        }

        private void CompletePeekShot()
        {
            // Fire snap shot
            if (controller.IsTargetValid() && controller.CanShoot)
            {
                controller.FireShot(ShotType.Snap);
                Debug.Log($"[{controller.name}] PEEK SHOT fired!");
            }

            // Immediately duck back down
            InterruptPeek();
        }

        private void InterruptPeek()
        {
            isPeeking = false;
            controller.SetDucked(true);
            ResetPeekCooldown();
        }

        private void ResetPeekCooldown()
        {
            // Randomize cooldown between peeks
            // Could factor in bravery here to reduce cooldown for brave units
            int bravery = controller.Character?.Bravery ?? 10;
            float braveryMod = 1f - (Mathf.Max(0, bravery - 10) * 0.05f);  // -5% per point above 10
            braveryMod = Mathf.Clamp(braveryMod, 0.5f, 1f);  // Cap at 50% reduction

            float baseCooldown = Random.Range(MIN_PEEK_COOLDOWN, MAX_PEEK_COOLDOWN);
            peekCooldown = baseCooldown * braveryMod;
            nextPeekTime = Time.time + peekCooldown;
        }

        private void Recover()
        {
            // Check for threats at weapon range - use best target now
            var enemy = controller.FindBestTarget(controller.WeaponRange);

            Debug.Log($"[{controller.name}] Recovering from PINNED - VisibleEnemy={enemy != null}, IsInDanger={controller.IsInDanger()}");

            if (enemy != null)
            {
                controller.SetTarget(enemy);
                controller.ChangeState(UnitStateType.Combat);
            }
            else if (controller.IsInDanger())
            {
                // Still in danger, find cover
                controller.RequestCoverPosition();
                if (controller.HasPendingDestination)
                {
                    controller.ChangeState(UnitStateType.Moving);
                }
                else
                {
                    Debug.Log($"[{controller.name}] No cover found, going to Ready despite danger");
                    controller.ChangeState(UnitStateType.Ready);
                }
            }
            else
            {
                controller.ChangeState(UnitStateType.Ready);
            }
        }
    }
}
