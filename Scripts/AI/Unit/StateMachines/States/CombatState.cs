using UnityEngine;
using System.Linq;
using Starbelter.Core;
using Starbelter.Combat;
using Starbelter.Pathfinding;

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
        private float currentTargetPriority;

        // Priority threshold - below this, target is in full cover, go to overwatch
        private const float FULL_COVER_THRESHOLD = 0.15f;

        // Events for Overwatch system
        public static System.Action<UnitController> OnUnitStartedPeeking;
        public static System.Action<UnitController> OnUnitStoppedPeeking;

        // If true, skip to shooting immediately (from Overwatch reaction)
        private bool immediateShot;
        private GameObject overwatchTarget;

        public CombatPhase CurrentPhase => currentPhase;

        /// <summary>
        /// Default constructor - normal combat behavior.
        /// </summary>
        public CombatState()
        {
            immediateShot = false;
            overwatchTarget = null;
        }

        /// <summary>
        /// Constructor for Overwatch reaction - shoot immediately.
        /// </summary>
        public CombatState(GameObject target)
        {
            immediateShot = true;
            overwatchTarget = target;
        }

        public override void Enter()
        {
            Debug.Log($"[CombatState] {controller.name} entering. Unit at {(Vector2)controller.transform.position}, fire pos at {(Vector2)controller.FirePosition}, immediateShot={immediateShot}");

            if (immediateShot && overwatchTarget != null)
            {
                // Coming from Overwatch/Flank - shoot immediately from current position
                currentTarget = overwatchTarget;
                currentTargetPriority = 1f;
                Debug.Log($"[CombatState] {controller.name} immediate shot at {overwatchTarget.name}");
                StartPhase(CombatPhase.Shooting);
                return;
            }

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
                // Re-check target priority before peeking
                var los = CombatUtils.CheckLineOfSight(
                    controller.transform.position,
                    currentTarget.transform.position
                );

                if (los.IsBlocked)
                {
                    // Target is in full cover - can't shoot them
                    // Look for a better target first
                    FindTarget();

                    if (currentTarget == null || currentTargetPriority < FULL_COVER_THRESHOLD)
                    {
                        // No good targets - decide: Flank or Overwatch?
                        DecideFlankOrOverwatch();
                        return;
                    }
                }

                // Ready to peek and shoot
                StartPhase(CombatPhase.Standing);
            }
        }

        /// <summary>
        /// Decide whether to flank or overwatch based on threat level and bravery.
        /// </summary>
        private void DecideFlankOrOverwatch()
        {
            int bravery = controller.Character?.Bravery ?? 10;

            // Check if it's safe to flank
            bool shouldFlank = CombatUtils.ShouldAttemptFlank(ThreatManager, bravery);

            if (shouldFlank && currentTarget != null)
            {
                // Try to find a flank position
                var flankResult = CombatUtils.FindFlankPosition(
                    controller.transform.position,
                    currentTarget.transform.position,
                    controller.WeaponRange,
                    CoverQuery.Instance
                );

                if (flankResult.Found)
                {
                    // Flank position available - go for it
                    Debug.Log($"[CombatState] {controller.name} deciding to flank");
                    var flankState = new FlankState(currentTarget);
                    stateMachine.ChangeState(flankState);
                    return;
                }
            }

            // Can't or shouldn't flank - overwatch
            Debug.Log($"[CombatState] {controller.name} deciding to overwatch");
            var overwatchState = new OverwatchState(currentTarget);
            stateMachine.ChangeState(overwatchState);
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
                Debug.Log($"[CombatState] {controller.name} about to fire. Unit at {(Vector2)controller.transform.position}, fire pos at {(Vector2)controller.FirePosition}");
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
            // Find best enemy target using priority scoring
            ITargetable[] allTargets = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .OfType<ITargetable>()
                .ToArray();

            float weaponRange = controller.WeaponRange;
            float bestPriority = 0f;
            GameObject bestTarget = null;

            foreach (var target in allTargets)
            {
                // Skip self
                if (target.Transform == controller.transform) continue;

                // Skip same team (and neutrals don't fight)
                if (target.Team == controller.Team) continue;
                if (controller.Team == Team.Neutral) continue;

                // Skip dead targets
                if (target.IsDead) continue;

                // Calculate priority based on distance and cover
                float priority = CombatUtils.CalculateTargetPriority(
                    controller.transform.position,
                    target.Transform.position,
                    weaponRange
                );

                if (priority > bestPriority)
                {
                    bestPriority = priority;
                    bestTarget = target.Transform.gameObject;
                }
            }

            currentTarget = bestTarget;
            currentTargetPriority = bestPriority;
        }

        private void FireAtTarget()
        {
            if (currentTarget == null) return;
            if (controller.ProjectilePrefab == null)
            {
                Debug.LogWarning($"[CombatState] {controller.name} has no projectile prefab!");
                return;
            }

            // Determine fire position
            Vector3 firePos = controller.FirePoint != null
                ? controller.FirePoint.position
                : controller.transform.position;

            // Calculate direction to target (with accuracy spread)
            Vector3 targetPos = currentTarget.transform.position;
            Vector2 baseDirection = (targetPos - firePos).normalized;

            // Apply accuracy spread
            float spread = CalculateSpread();
            float angle = Mathf.Atan2(baseDirection.y, baseDirection.x);
            angle += Random.Range(-spread, spread);
            Vector2 finalDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            // Spawn and fire projectile
            GameObject projectileObj = Object.Instantiate(controller.ProjectilePrefab, firePos, Quaternion.identity);
            Projectile projectile = projectileObj.GetComponent<Projectile>();

            if (projectile != null)
            {
                projectile.Fire(finalDirection, controller.Team);
                Debug.Log($"[CombatState] {controller.name} fires at {currentTarget.name} from {firePos} toward {targetPos}, direction {finalDirection}");
            }
            else
            {
                Debug.LogWarning($"[CombatState] Projectile prefab missing Projectile component!");
                Object.Destroy(projectileObj);
            }
        }

        /// <summary>
        /// Calculate spread angle in radians based on accuracy stat.
        /// </summary>
        private float CalculateSpread()
        {
            // Base spread of ~5 degrees, accuracy reduces it
            float baseSpread = 5f * Mathf.Deg2Rad;

            if (controller.Character != null)
            {
                // High accuracy (20) = nearly no spread, low accuracy (1) = high spread
                float accuracyMult = 1f - Character.StatToMultiplier(controller.Character.Accuracy);
                return baseSpread * (0.2f + accuracyMult * 1.5f); // 0.2x to 1.7x spread
            }

            return baseSpread;
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
