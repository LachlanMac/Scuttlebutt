using UnityEngine;
using Starbelter.Core;

namespace Starbelter.AI
{
    /// <summary>
    /// Handles active combat - finding cover, peeking, shooting at targets.
    /// Uses a phase-based system for interruptible shooting sequence.
    /// </summary>
    public class CombatState : UnitState
    {
        public enum CombatPhase
        {
            SeekingCover,   // Moving to cover
            InCover,        // Safe behind cover, waiting to shoot
            Standing,       // Rising to shoot (exposed)
            Aiming,         // Lining up shot (exposed)
            Shooting,       // Firing (exposed)
            Ducking         // Returning to cover
        }

        // Phase timings (could be moved to config or influenced by stats)
        private const float STAND_TIME = 0.4f;
        private const float AIM_TIME_BASE = 0.3f;
        private const float SHOOT_TIME = 0.1f;
        private const float DUCK_TIME = 0.3f;
        private const float COVER_WAIT_TIME = 1.0f; // Time between shots

        private CombatPhase currentPhase;
        private float phaseTimer;
        private float coverWaitTimer;

        // Target
        private GameObject currentTarget;

        // Events for Overwatch system
        public static System.Action<UnitController> OnUnitStartedPeeking;
        public static System.Action<UnitController> OnUnitStoppedPeeking;

        public CombatPhase CurrentPhase => currentPhase;

        public override void Enter()
        {
            currentTarget = null;
            FindTarget();

            // Start by seeking cover if not already in cover
            if (controller.IsInCover)
            {
                StartPhase(CombatPhase.InCover);
            }
            else
            {
                StartPhase(CombatPhase.SeekingCover);
                SeekCover();
            }
        }

        public override void Update()
        {
            // Update phase timer
            if (phaseTimer > 0)
            {
                phaseTimer -= Time.deltaTime;
            }

            // Check if target is still valid
            if (currentTarget == null || !currentTarget.activeInHierarchy)
            {
                FindTarget();
                if (currentTarget == null)
                {
                    // No targets, exit combat
                    ChangeState<IdleState>();
                    return;
                }
            }

            // Phase logic
            switch (currentPhase)
            {
                case CombatPhase.SeekingCover:
                    UpdateSeekingCover();
                    break;

                case CombatPhase.InCover:
                    UpdateInCover();
                    break;

                case CombatPhase.Standing:
                    UpdateStanding();
                    break;

                case CombatPhase.Aiming:
                    UpdateAiming();
                    break;

                case CombatPhase.Shooting:
                    UpdateShooting();
                    break;

                case CombatPhase.Ducking:
                    UpdateDucking();
                    break;
            }
        }

        public override void Exit()
        {
            // Make sure we signal stopped peeking if we were exposed
            if (IsPeekingPhase(currentPhase))
            {
                OnUnitStoppedPeeking?.Invoke(controller);
            }
        }

        #region Phase Updates

        private void UpdateSeekingCover()
        {
            // Wait until we've arrived at cover
            if (!Movement.IsMoving)
            {
                if (controller.IsInCover)
                {
                    StartPhase(CombatPhase.InCover);
                }
                else
                {
                    // Couldn't find cover, fight in the open
                    StartPhase(CombatPhase.Standing);
                }
            }
        }

        private void UpdateInCover()
        {
            coverWaitTimer -= Time.deltaTime;

            if (coverWaitTimer <= 0 && currentTarget != null)
            {
                // Ready to peek and shoot
                StartPhase(CombatPhase.Standing);
            }
        }

        private void UpdateStanding()
        {
            if (phaseTimer <= 0)
            {
                StartPhase(CombatPhase.Aiming);
            }
        }

        private void UpdateAiming()
        {
            if (phaseTimer <= 0)
            {
                StartPhase(CombatPhase.Shooting);
            }
        }

        private void UpdateShooting()
        {
            if (phaseTimer <= 0)
            {
                // Fire the shot
                FireAtTarget();
                StartPhase(CombatPhase.Ducking);
            }
        }

        private void UpdateDucking()
        {
            if (phaseTimer <= 0)
            {
                StartPhase(CombatPhase.InCover);
            }
        }

        #endregion

        #region Phase Management

        private void StartPhase(CombatPhase newPhase)
        {
            bool wasPeeking = IsPeekingPhase(currentPhase);
            bool willBePeeking = IsPeekingPhase(newPhase);

            currentPhase = newPhase;

            // Fire peeking events
            if (!wasPeeking && willBePeeking)
            {
                OnUnitStartedPeeking?.Invoke(controller);
            }
            else if (wasPeeking && !willBePeeking)
            {
                OnUnitStoppedPeeking?.Invoke(controller);
            }

            // Set phase timer
            switch (newPhase)
            {
                case CombatPhase.Standing:
                    phaseTimer = STAND_TIME;
                    break;

                case CombatPhase.Aiming:
                    phaseTimer = CalculateAimTime();
                    break;

                case CombatPhase.Shooting:
                    phaseTimer = SHOOT_TIME;
                    break;

                case CombatPhase.Ducking:
                    phaseTimer = DUCK_TIME;
                    break;

                case CombatPhase.InCover:
                    phaseTimer = 0;
                    coverWaitTimer = COVER_WAIT_TIME;
                    break;

                default:
                    phaseTimer = 0;
                    break;
            }
        }

        private bool IsPeekingPhase(CombatPhase phase)
        {
            return phase == CombatPhase.Standing ||
                   phase == CombatPhase.Aiming ||
                   phase == CombatPhase.Shooting;
        }

        private float CalculateAimTime()
        {
            float aimTime = AIM_TIME_BASE;

            // Faster aim with higher accuracy stat
            if (controller.Character != null)
            {
                float accuracyMod = Character.StatToModifier(controller.Character.Accuracy);
                aimTime -= accuracyMod * 0.2f; // Accuracy can reduce aim time by up to 0.1s
            }

            return Mathf.Max(0.1f, aimTime);
        }

        #endregion

        #region Actions

        private void SeekCover()
        {
            if (ThreatManager != null)
            {
                var threatDir = ThreatManager.GetHighestThreatDirection();
                if (threatDir.HasValue)
                {
                    Movement.MoveToCover(controller.transform.position +
                        new Vector3(threatDir.Value.x, threatDir.Value.y, 0) * 10f);
                    return;
                }
            }

            // No threat direction, use target position
            if (currentTarget != null)
            {
                Movement.MoveToCover(currentTarget.transform.position);
            }
        }

        private void FindTarget()
        {
            // Simple target finding: nearest enemy
            // TODO: Replace with proper target selection (visibility, priority, orders, etc.)

            GameObject[] potentialTargets;

            if (controller.Team == Team.Ally)
            {
                potentialTargets = GameObject.FindGameObjectsWithTag("Enemy");
            }
            else
            {
                potentialTargets = GameObject.FindGameObjectsWithTag("Ally");
            }

            float nearestDist = float.MaxValue;
            GameObject nearest = null;

            foreach (var target in potentialTargets)
            {
                float dist = Vector3.Distance(controller.transform.position, target.transform.position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = target;
                }
            }

            currentTarget = nearest;
        }

        private void FireAtTarget()
        {
            if (currentTarget == null) return;

            // TODO: Implement actual shooting
            // - Spawn projectile
            // - Calculate hit chance based on accuracy, distance, target cover
            // - Apply damage or miss

            Debug.Log($"[CombatState] {controller.name} fires at {currentTarget.name}!");

            // For now, just log - we'll implement projectile spawning later
        }

        #endregion

        #region Interrupts

        /// <summary>
        /// Called when unit takes damage - immediately duck.
        /// </summary>
        public void OnDamageTaken()
        {
            if (currentPhase != CombatPhase.SeekingCover)
            {
                StartPhase(CombatPhase.Ducking);
            }
        }

        /// <summary>
        /// Called when unit needs to move - abort shooting sequence.
        /// </summary>
        public void OnForceMove()
        {
            StartPhase(CombatPhase.SeekingCover);
        }

        #endregion
    }
}
