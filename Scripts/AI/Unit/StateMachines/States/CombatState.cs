using UnityEngine;
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

        // If true, assume we're already at cover (coming from SeekCoverState)
        private bool arrivedAtCover;

        // Track if we've started moving to cover (for retry logic)
        private bool startedMovingToCover;
        private float seekCoverRetryTimer;
        private const float SEEK_COVER_RETRY_INTERVAL = 0.5f;

        // Cooldown after arriving at cover - don't immediately re-evaluate and run away
        private float coverCommitTimer;
        private const float COVER_COMMIT_TIME = 2f;

        public CombatPhase CurrentPhase => currentPhase;

        /// <summary>
        /// Default constructor - normal combat behavior.
        /// </summary>
        public CombatState()
        {
            immediateShot = false;
            overwatchTarget = null;
            arrivedAtCover = false;
        }

        /// <summary>
        /// Constructor for Overwatch reaction - shoot immediately.
        /// </summary>
        public CombatState(GameObject target)
        {
            immediateShot = true;
            overwatchTarget = target;
            arrivedAtCover = false;
        }

        /// <summary>
        /// Constructor for when arriving from SeekCoverState - skip seeking cover.
        /// </summary>
        public CombatState(bool alreadyAtCover)
        {
            immediateShot = false;
            overwatchTarget = null;
            arrivedAtCover = alreadyAtCover;
        }

        public override void Enter()
        {
            if (immediateShot && overwatchTarget != null)
            {
                // Coming from Overwatch/Flank - shoot immediately from current position
                currentTarget = overwatchTarget;
                currentTargetPriority = 1f;
                StartPhase(CombatPhase.Shooting);
                return;
            }

            currentTarget = null;
            FindTarget();

            // Start by seeking cover if not already in cover
            if (arrivedAtCover || controller.IsInCover)
            {
                StartPhase(CombatPhase.InCover);
                // If we arrived at cover (from SeekCover/Reposition), commit to it briefly
                coverCommitTimer = arrivedAtCover ? COVER_COMMIT_TIME : 0f;

                // Reset threat when arriving at new position - old threat is stale
                // Use small value (not 0) so squad.IsEngaged still works
                if (arrivedAtCover && ThreatManager != null)
                {
                    ThreatManager.ResetThreats(0.01f);
                }
            }
            else
            {
                StartPhase(CombatPhase.SeekingCover);
                startedMovingToCover = SeekCover();
                seekCoverRetryTimer = SEEK_COVER_RETRY_INTERVAL;
                coverCommitTimer = 0f;
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
            if (currentTarget == null || !currentTarget.activeInHierarchy || IsTargetDead())
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
            // If we haven't started moving yet (path was throttled), retry
            if (!startedMovingToCover && !Movement.IsMoving)
            {
                seekCoverRetryTimer -= Time.deltaTime;
                if (seekCoverRetryTimer <= 0f)
                {
                    seekCoverRetryTimer = SEEK_COVER_RETRY_INTERVAL;
                    startedMovingToCover = SeekCover();
                }
                return;
            }

            // Wait until we've arrived at cover
            if (!Movement.IsMoving)
            {
                // Check if we have cover from threat, or from target if no threat yet
                bool hasCover = controller.IsInCover;
                if (!hasCover && currentTarget != null)
                {
                    // No threat registered yet - check cover from target direction
                    var coverQuery = CoverQuery.Instance;
                    if (coverQuery != null)
                    {
                        var coverCheck = coverQuery.CheckCoverAt(
                            controller.transform.position,
                            currentTarget.transform.position
                        );
                        hasCover = coverCheck.HasCover;
                    }
                }

                if (hasCover)
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
            // Tick down commit timer
            if (coverCommitTimer > 0f)
            {
                coverCommitTimer -= Time.deltaTime;
            }

            // Check if we're still covered from active threat directions
            // Threats can change (enemies flank, new shooters)
            // But only flee if COMPLETELY exposed from ALL major threats
            if (ThreatManager != null && ThreatManager.IsUnderFire())
            {
                var coverQuery = Pathfinding.CoverQuery.Instance;
                if (coverQuery != null)
                {
                    // Check cover against all active threats, not just the highest
                    // This prevents false negatives from threat bucket direction misalignment
                    var activeThreats = ThreatManager.GetActiveThreats(1f);
                    bool hasAnyCover = false;
                    float uncoveredThreat = 0f;

                    foreach (var threat in activeThreats)
                    {
                        Vector3 threatWorldPos = CombatUtils.ThreatDirectionToWorldPos(
                            controller.transform.position, threat.Direction);
                        var coverCheck = coverQuery.CheckCoverAt(controller.transform.position, threatWorldPos);

                        if (coverCheck.HasCover)
                        {
                            hasAnyCover = true;
                        }
                        else
                        {
                            uncoveredThreat += threat.ThreatLevel;
                        }
                    }

                    // Only consider fleeing if we have NO cover from ANY direction AND high uncovered threat
                    if (!hasAnyCover && uncoveredThreat > 0f)
                    {
                        int bravery = controller.Character?.Bravery ?? 10;
                        float baseThreatThreshold = CombatUtils.CalculateThreatThreshold(
                            CombatUtils.FLEE_THREAT_BASE, CombatUtils.FLEE_THREAT_BRAVERY_MULT, bravery);

                        // If we just repositioned, require much higher threat to move again
                        float threatMultiplier = coverCommitTimer > 0f ? 2f : 1f;
                        float requiredThreat = baseThreatThreshold * threatMultiplier;

                        if (uncoveredThreat > requiredThreat)
                        {
                            Debug.Log($"[{controller.name}] CombatState: No cover from any threat ({uncoveredThreat:F1} > {requiredThreat:F1}), seeking cover");
                            ChangeState<SeekCoverState>();
                            return;
                        }
                    }
                }
            }

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
                        DecideNextTactic();
                        return;
                    }
                }

                // Ready to peek and shoot
                StartPhase(CombatPhase.Standing);
            }
        }

        /// <summary>
        /// Decide whether to flank, suppress, or overwatch based on threat level and bravery.
        /// </summary>
        private void DecideNextTactic()
        {
            int bravery = controller.Character?.Bravery ?? 10;

            // Check if it's safe to flank
            bool shouldFlank = CombatUtils.ShouldAttemptFlank(ThreatManager, bravery);

            // Leaders should NOT flank unless squad is very small (< 3 alive)
            // They need to anchor the squad position
            if (controller.IsSquadLeader && controller.Squad != null)
            {
                int aliveCount = controller.Squad.GetAliveUnitCount();
                if (aliveCount >= 3)
                {
                    shouldFlank = false;
                }
            }

            if (shouldFlank && currentTarget != null)
            {
                // Try to find a flank position (exclude self from occupancy check)
                var flankResult = CombatUtils.FindFlankPosition(
                    controller.transform.position,
                    currentTarget.transform.position,
                    controller.WeaponRange,
                    CoverQuery.Instance,
                    controller.gameObject,
                    controller.Team
                );

                if (flankResult.Found)
                {
                    // Flank position available - go for it
                    var flankState = new FlankState(currentTarget);
                    stateMachine.ChangeState(flankState);
                    return;
                }
            }

            // Can't flank - decide between suppress and overwatch
            // Aggressive units prefer suppression, defensive prefer overwatch
            // But don't suppress if target is already being suppressed by a squadmate
            bool targetAlreadySuppressed = controller.Squad != null &&
                currentTarget != null &&
                controller.Squad.IsTargetBeingSuppressed(currentTarget);

            bool shouldSuppress = !targetAlreadySuppressed && ShouldSuppressInsteadOfOverwatch(bravery);

            if (shouldSuppress && currentTarget != null)
            {
                var suppressState = new SuppressState(currentTarget);
                stateMachine.ChangeState(suppressState);
            }
            else
            {
                var overwatchState = new OverwatchState(currentTarget);
                stateMachine.ChangeState(overwatchState);
            }
        }

        /// <summary>
        /// Decide if we should suppress instead of overwatch.
        /// Based on squad threat, aggression/bravery, and randomness.
        /// Note: Caller should already check if target is being suppressed.
        /// </summary>
        private bool ShouldSuppressInsteadOfOverwatch(int bravery)
        {
            // If squad is under heavy fire, prefer suppression (but caller checks if target is already suppressed)
            if (controller.Squad != null && controller.Squad.IsUnderHeavyFire)
            {
                return true;
            }

            // More aggressive posture = more likely to suppress
            float suppressChance = controller.Posture switch
            {
                Posture.Aggressive => 0.8f,
                Posture.Neutral => 0.4f + (bravery / 40f), // 0.4 to 0.9 based on bravery
                Posture.Defensive => 0.2f,
                _ => 0.3f
            };

            return Random.value < suppressChance;
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

        /// <summary>
        /// Attempt to move to cover. Returns true if movement started.
        /// </summary>
        private bool SeekCover()
        {
            // Build tactical search parameters from posture
            int bravery = controller.Character?.Bravery ?? 10;
            var leaderPos = controller.GetLeaderPosition();
            var rallyPoint = controller.GetRallyPoint();
            bool isLeader = controller.IsSquadLeader;
            var searchParams = CoverSearchParams.FromPosture(controller.WeaponRange, controller.Posture, bravery, controller.Team, leaderPos, rallyPoint, isLeader);

            // Limit cover search radius based on health
            // Healthy units stay close and fight, hurt units can retreat further
            float healthPercent = controller.Health != null ? controller.Health.HealthPercent : 1f;
            float maxCoverDistance;

            if (healthPercent > 0.7f)
            {
                // Healthy - only look for nearby cover (stay in the fight)
                maxCoverDistance = 5f;
            }
            else if (healthPercent > 0.4f)
            {
                // Wounded - expand search a bit
                maxCoverDistance = 8f;
            }
            else
            {
                // Critical - search far for safety
                maxCoverDistance = -1f; // No limit
            }

            Vector3 threatPos;

            if (ThreatManager != null)
            {
                var threatDir = ThreatManager.GetHighestThreatDirection();
                if (threatDir.HasValue)
                {
                    threatPos = controller.transform.position +
                        new Vector3(threatDir.Value.x, threatDir.Value.y, 0) * 10f;
                    return Movement.MoveToCover(threatPos, searchParams, maxCoverDistance);
                }
            }

            // No threat direction, use target position
            if (currentTarget != null)
            {
                return Movement.MoveToCover(currentTarget.transform.position, searchParams, maxCoverDistance);
            }

            return false;
        }

        private void FindTarget()
        {
            var (target, priority) = CombatUtils.FindBestTargetWithPriority(
                controller.transform.position,
                controller.WeaponRange,
                controller.Team,
                controller.transform,
                ThreatManager
            );

            currentTarget = target;
            currentTargetPriority = priority;
        }

        private bool IsTargetDead()
        {
            return CombatUtils.IsTargetDead(currentTarget);
        }

        private void FireAtTarget()
        {
            if (currentTarget == null) return;
            if (controller.ProjectilePrefab == null) return;

            int accuracy = controller.Character?.Accuracy ?? 10;

            var shootParams = new CombatUtils.ShootParams
            {
                FirePosition = controller.FirePosition,
                TargetPosition = currentTarget.transform.position,
                SpreadRadians = CombatUtils.CalculateAccuracySpread(accuracy),
                Team = controller.Team,
                SourceUnit = controller.gameObject,
                ProjectilePrefab = controller.ProjectilePrefab
            };

            CombatUtils.ShootProjectile(shootParams);
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
