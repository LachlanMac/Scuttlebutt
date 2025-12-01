using UnityEngine;
using System.Collections.Generic;
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
            Aiming,         // Lining up shot (exposed, quick aim)
            ExtendedAiming, // Taking time to line up a careful shot (exposed, high accuracy)
            Shooting,       // Firing (exposed)
            Ducking         // Returning to cover
        }

        // Phase timings (could be moved to config or influenced by stats)
        private const float STAND_TIME = 0.4f;
        private const float AIM_TIME_BASE = 0.3f;
        private const float EXTENDED_AIM_TIME_BASE = 3f; // Base time for careful aim
        private const float SHOOT_TIME = 0.1f;
        private const float DUCK_TIME = 0.3f;
        private const float COVER_WAIT_TIME = 1.0f; // Time between shots

        // Extended aim accuracy bonus (added to effective accuracy)
        private const int EXTENDED_AIM_ACCURACY_BONUS = 8;

        // Base threat threshold for extended aim (modified by bravery)
        private const float LOW_THREAT_BASE = 15f;

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

        // If true, we just flanked to this position - take at least one shot before re-evaluating
        private bool justFlanked;

        // Track if we've started moving to cover (for retry logic)
        private bool startedMovingToCover;
        private float seekCoverRetryTimer;
        private const float SEEK_COVER_RETRY_INTERVAL = 0.5f;
        private float seekCoverGiveUpTimer;
        private const float SEEK_COVER_GIVE_UP_TIME = 2f;

        // Cooldown after arriving at cover - don't immediately re-evaluate and run away
        private float coverCommitTimer;
        private const float COVER_COMMIT_TIME = 2f;

        // Track if current shot is using extended aim (for accuracy bonus)
        private bool usingExtendedAim;

        // Track current cover type (only half cover uses ducking visual)
        private CoverType currentCoverType = CoverType.None;

        // Track consecutive cover-seeking failures per unit (persists across state changes)
        private static System.Collections.Generic.Dictionary<int, int> coverSeekFailures = new();
        private static System.Collections.Generic.Dictionary<int, float> lastCoverSeekTime = new();

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
        /// Constructor for Overwatch/Flank reaction.
        /// </summary>
        /// <param name="target">The target to engage</param>
        /// <param name="immediate">If true, skip extended aim (Overwatch reaction)</param>
        /// <param name="fromFlank">If true, we just flanked - take at least one shot before re-evaluating</param>
        public CombatState(GameObject target, bool immediate = true, bool fromFlank = false)
        {
            immediateShot = immediate;
            overwatchTarget = target;
            arrivedAtCover = false;
            justFlanked = fromFlank;
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
                // Determine cover type for ducking visual
                DetermineCoverType();

                StartPhase(CombatPhase.InCover);
                // If we arrived at cover (from SeekCover/Reposition), commit to it briefly
                coverCommitTimer = arrivedAtCover ? COVER_COMMIT_TIME : 0f;

                // Reset threat when arriving at new position - old threat is stale
                // Use small value (not 0) so squad.IsEngaged still works
                if (arrivedAtCover && PerceptionManager != null)
                {
                    PerceptionManager.ResetThreats(0.01f);
                }
            }
            else
            {
                currentCoverType = CoverType.None;
                StartPhase(CombatPhase.SeekingCover);
                startedMovingToCover = SeekCover();
                seekCoverRetryTimer = SEEK_COVER_RETRY_INTERVAL;
                seekCoverGiveUpTimer = SEEK_COVER_GIVE_UP_TIME;
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

            // Face toward current target (for vision cone)
            if (currentTarget != null)
            {
                Movement.FaceToward(currentTarget.transform.position);
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

                case CombatPhase.ExtendedAiming:
                    UpdateExtendedAiming();
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

            // Reset visual scale when leaving combat
            SetDuckingVisual(false);
        }

        #region Phase Updates

        private void UpdateSeekingCover()
        {
            // If we haven't started moving yet (path was throttled or no cover found), retry
            if (!startedMovingToCover && !Movement.IsMoving)
            {
                // Give up after trying for too long - fight in the open
                seekCoverGiveUpTimer -= Time.deltaTime;
                if (seekCoverGiveUpTimer <= 0f)
                {
                    Debug.Log($"[{controller.name}] CombatState: Gave up finding cover, fighting in open");
                    StartPhase(CombatPhase.Standing);
                    return;
                }

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
                var coverQuery = CoverQuery.Instance;
                CoverCheckResult coverCheck = default;

                if (coverQuery != null)
                {
                    // Try threat direction first
                    if (PerceptionManager != null)
                    {
                        var threatDir = PerceptionManager.GetHighestThreatDirection();
                        if (threatDir.HasValue)
                        {
                            Vector3 threatWorldPos = CombatUtils.ThreatDirectionToWorldPos(
                                controller.transform.position, threatDir.Value);
                            coverCheck = coverQuery.CheckCoverAt(controller.transform.position, threatWorldPos);
                        }
                    }

                    // Fall back to target direction
                    if (!coverCheck.HasCover && currentTarget != null)
                    {
                        coverCheck = coverQuery.CheckCoverAt(
                            controller.transform.position,
                            currentTarget.transform.position
                        );
                    }
                }

                if (coverCheck.HasCover)
                {
                    currentCoverType = coverCheck.Type;
                    StartPhase(CombatPhase.InCover);
                }
                else
                {
                    // Couldn't find cover, fight in the open
                    currentCoverType = CoverType.None;
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
            if (PerceptionManager != null && PerceptionManager.IsUnderFire())
            {
                var coverQuery = Pathfinding.CoverQuery.Instance;
                if (coverQuery != null)
                {
                    // Check cover against all active threats, not just the highest
                    // This prevents false negatives from threat bucket direction misalignment
                    var activeThreats = PerceptionManager.GetActiveThreats(1f);
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

                    // Only consider moving if we have NO cover from ANY direction AND high uncovered threat
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
                            // Decide: Advance (aggressive) or Retreat (defensive)?
                            // Aggressive posture + brave = advance with suppressing fire
                            // Defensive posture or cowardly = retreat to cover
                            bool shouldAdvance = ShouldAdvanceUnderFire(bravery, uncoveredThreat);

                            if (shouldAdvance)
                            {
                                Debug.Log($"[{controller.name}] CombatState: Exposed but advancing aggressively ({uncoveredThreat:F1} threat)");
                                ChangeState<AdvanceState>();
                            }
                            else
                            {
                                Debug.Log($"[{controller.name}] CombatState: No cover from any threat ({uncoveredThreat:F1} > {requiredThreat:F1}), seeking cover");
                                ChangeState<SeekCoverState>();
                            }
                            return;
                        }
                    }
                }
            }

            coverWaitTimer -= Time.deltaTime;

            if (coverWaitTimer <= 0)
            {
                // No target? Try to find one
                if (currentTarget == null)
                {
                    FindTarget();
                    if (currentTarget == null)
                    {
                        // Still no target - exit to idle
                        ChangeState<IdleState>();
                        return;
                    }
                }

                // If we just flanked, skip LOS re-check and take at least one shot
                // (FlankState already verified LOS before transitioning here)
                if (justFlanked)
                {
                    justFlanked = false; // Clear flag after one use
                    StartPhase(CombatPhase.Standing);
                    return;
                }

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
            bool shouldFlank = CombatUtils.ShouldAttemptFlank(PerceptionManager, bravery);

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
        /// Decide if we should advance aggressively under fire vs retreat to cover.
        /// Factors: posture, bravery, health, threat level, and cover-seeking failure history.
        /// </summary>
        private bool ShouldAdvanceUnderFire(int bravery, float threatLevel)
        {
            int unitId = controller.gameObject.GetInstanceID();
            int failures = GetCoverSeekFailures(unitId);
            float healthPercent = controller.Character?.HealthPercent ?? 1f;
            // Use BasePosture (the setting) not Posture (which becomes Defensive under fire)
            // The whole point of advancing is to push through when under fire
            var posture = controller.BasePosture;
            bool isLeader = controller.IsSquadLeader;

            // Build decision log
            string logPrefix = $"[{controller.name}] AdvanceDecision:";

            // Defensive posture setting never advances
            if (posture == Posture.Defensive)
            {
                Debug.Log($"{logPrefix} NO - Defensive posture setting");
                return false;
            }

            // Leaders don't advance recklessly - they anchor the squad
            if (isLeader)
            {
                Debug.Log($"{logPrefix} NO - Squad leader (must anchor)");
                return false;
            }

            // Low health = retreat
            if (healthPercent < 0.4f)
            {
                Debug.Log($"{logPrefix} NO - Low health ({healthPercent:P0})");
                return false;
            }

            // Very high threat = retreat regardless of bravery (unless desperate)
            if (threatLevel > 50f && failures < 3)
            {
                Debug.Log($"{logPrefix} NO - High threat ({threatLevel:F1}) and not desperate (failures={failures})");
                return false;
            }

            // Base chance modified by posture and bravery
            float baseChance = posture switch
            {
                Posture.Aggressive => 0.5f + (bravery / 40f), // 0.5-1.0
                Posture.Neutral => 0.15f + (bravery / 50f),   // 0.15-0.55
                _ => 0f
            };

            // Increase chance significantly if we've failed to find cover multiple times
            // Each failure adds 15% chance, capped at +60%
            float failureBonus = Mathf.Min(failures * 0.15f, 0.6f);

            // Random factor for variety (-10% to +10%)
            float randomFactor = Random.Range(-0.1f, 0.1f);

            float finalChance = Mathf.Clamp01(baseChance + failureBonus + randomFactor);
            float roll = Random.value;
            bool shouldAdvance = roll < finalChance;

            Debug.Log($"{logPrefix} posture={posture}, bravery={bravery}, health={healthPercent:P0}, " +
                      $"threat={threatLevel:F1}, failures={failures}, " +
                      $"chance={baseChance:F2}+{failureBonus:F2}+{randomFactor:F2}={finalChance:F2}, " +
                      $"roll={roll:F2} -> {(shouldAdvance ? "ADVANCE" : "RETREAT")}");

            return shouldAdvance;
        }

        /// <summary>
        /// Get cover-seek failure count for a unit, resetting if too much time has passed.
        /// </summary>
        private static int GetCoverSeekFailures(int unitId)
        {
            if (!coverSeekFailures.TryGetValue(unitId, out int failures))
                return 0;

            // Reset if last failure was more than 10 seconds ago
            if (lastCoverSeekTime.TryGetValue(unitId, out float lastTime))
            {
                if (Time.time - lastTime > 10f)
                {
                    coverSeekFailures[unitId] = 0;
                    return 0;
                }
            }

            return failures;
        }

        /// <summary>
        /// Record a cover-seeking failure for a unit.
        /// </summary>
        public static void RecordCoverSeekFailure(int unitId)
        {
            if (!coverSeekFailures.ContainsKey(unitId))
                coverSeekFailures[unitId] = 0;

            coverSeekFailures[unitId]++;
            lastCoverSeekTime[unitId] = Time.time;

            Debug.Log($"[Unit {unitId}] Cover seek failure #{coverSeekFailures[unitId]}");
        }

        /// <summary>
        /// Clear cover-seeking failures for a unit (called when successfully reaching cover).
        /// </summary>
        public static void ClearCoverSeekFailures(int unitId)
        {
            if (coverSeekFailures.ContainsKey(unitId) && coverSeekFailures[unitId] > 0)
            {
                Debug.Log($"[Unit {unitId}] Cleared {coverSeekFailures[unitId]} cover seek failures (found cover)");
                coverSeekFailures[unitId] = 0;
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
                // Decide whether to do extended aim based on threat level
                // Low threat = take time to aim carefully for better accuracy
                // Braver units will aim carefully even under more fire
                float threat = controller.GetEffectiveThreat();
                int bravery = controller.Character?.Bravery ?? 10;
                float threshold = LOW_THREAT_BASE + (bravery / 2f); // Bravery 10 = threshold 10, Bravery 20 = threshold 15
                bool lowThreat = threat < threshold;

                Debug.Log($"[{controller.name}] Aim decision: threat={threat:F2}, bravery={bravery}, threshold={threshold:F1}, lowThreat={lowThreat}, immediateShot={immediateShot}");

                if (lowThreat && !immediateShot)
                {
                    Debug.Log($"[{controller.name}] -> EXTENDED AIM");
                    usingExtendedAim = true;
                    StartPhase(CombatPhase.ExtendedAiming);
                }
                else
                {
                    Debug.Log($"[{controller.name}] -> Quick aim");
                    usingExtendedAim = false;
                    StartPhase(CombatPhase.Aiming);
                }
            }
        }

        private void UpdateAiming()
        {
            if (phaseTimer <= 0)
            {
                StartPhase(CombatPhase.Shooting);
            }
        }

        private void UpdateExtendedAiming()
        {
            // Check if threat has increased - abort extended aim and shoot quickly
            int bravery = controller.Character?.Bravery ?? 10;
            float threshold = LOW_THREAT_BASE + (bravery / 2f);

            if (controller.GetEffectiveThreat() >= threshold)
            {
                // Threat increased! Abort careful aim and shoot now
                Debug.Log($"[{controller.name}] Extended aim ABORTED - threat increased!");
                usingExtendedAim = false;
                StartPhase(CombatPhase.Shooting);
                return;
            }

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

            // Visual squish when ducking vs standing
            bool isDucking = newPhase == CombatPhase.Ducking || newPhase == CombatPhase.InCover;
            SetDuckingVisual(isDucking);

            // Set phase timer
            switch (newPhase)
            {
                case CombatPhase.Standing:
                    phaseTimer = STAND_TIME;
                    break;

                case CombatPhase.Aiming:
                    phaseTimer = CalculateAimTime();
                    break;

                case CombatPhase.ExtendedAiming:
                    phaseTimer = CalculateExtendedAimTime();
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
                   phase == CombatPhase.ExtendedAiming ||
                   phase == CombatPhase.Shooting;
        }

        private void SetDuckingVisual(bool isDucking)
        {
            // Only use ducking visual for half cover - full cover doesn't need it
            bool shouldDuck = isDucking && currentCoverType == CoverType.Half;

            var t = controller.transform;
            var scale = t.localScale;
            scale.y = shouldDuck ? 0.5f : 1f;
            t.localScale = scale;
        }

        /// <summary>
        /// Determine current cover type based on threat or target direction.
        /// </summary>
        private void DetermineCoverType()
        {
            var coverQuery = CoverQuery.Instance;
            if (coverQuery == null)
            {
                currentCoverType = CoverType.None;
                return;
            }

            // Try threat direction first
            if (PerceptionManager != null)
            {
                var threatDir = PerceptionManager.GetHighestThreatDirection();
                if (threatDir.HasValue)
                {
                    Vector3 threatWorldPos = CombatUtils.ThreatDirectionToWorldPos(
                        controller.transform.position, threatDir.Value);
                    var coverCheck = coverQuery.CheckCoverAt(controller.transform.position, threatWorldPos);
                    if (coverCheck.HasCover)
                    {
                        currentCoverType = coverCheck.Type;
                        return;
                    }
                }
            }

            // Fall back to target direction
            if (currentTarget != null)
            {
                var coverCheck = coverQuery.CheckCoverAt(
                    controller.transform.position,
                    currentTarget.transform.position
                );
                currentCoverType = coverCheck.HasCover ? coverCheck.Type : CoverType.None;
                return;
            }

            currentCoverType = CoverType.None;
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

        /// <summary>
        /// Calculate extended aim time for careful shots.
        /// Formula: 3 seconds - (Accuracy / 10) + random(0, 1)
        /// High accuracy units aim faster even when being careful.
        /// </summary>
        private float CalculateExtendedAimTime()
        {
            int accuracy = controller.Character?.Accuracy ?? 10;

            // Base 3 seconds, reduced by accuracy, with some randomness
            float aimTime = EXTENDED_AIM_TIME_BASE - (accuracy / 10f) + Random.Range(0f, 1f);

            // Clamp to reasonable range (1-4 seconds)
            return Mathf.Clamp(aimTime, 1f, 4f);
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
            CoverMode coverMode;

            if (healthPercent > 0.7f)
            {
                // Healthy - only look for nearby cover (stay in the fight)
                maxCoverDistance = 8f;
                coverMode = CoverMode.Fighting; // Must maintain LOS to enemies
            }
            else if (healthPercent > 0.4f)
            {
                // Wounded - expand search a bit
                maxCoverDistance = 10f;
                coverMode = CoverMode.Fighting; // Still fighting
            }
            else
            {
                // Critical - search far for safety, allow retreat behind walls
                maxCoverDistance = -1f; // No limit
                coverMode = CoverMode.Defensive; // Allow retreat behind walls
            }

            // Get known enemies for LOS checking
            var knownEnemies = GetKnownEnemies();
            searchParams = searchParams.WithMode(coverMode, knownEnemies);

            Vector3 threatPos;

            if (PerceptionManager != null)
            {
                var threatDir = PerceptionManager.GetHighestThreatDirection();
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
                PerceptionManager
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

            // Apply extended aim bonus - greatly increased accuracy
            bool wasExtendedAim = usingExtendedAim;
            if (usingExtendedAim)
            {
                accuracy = Mathf.Min(20, accuracy + EXTENDED_AIM_ACCURACY_BONUS);
                usingExtendedAim = false; // Reset for next shot
            }

            var shootParams = new CombatUtils.ShootParams
            {
                FirePosition = controller.FirePosition,
                TargetPosition = currentTarget.transform.position,
                SpreadRadians = CombatUtils.CalculateAccuracySpread(accuracy),
                Team = controller.Team,
                SourceUnit = controller.gameObject,
                ProjectilePrefab = controller.ProjectilePrefab
            };

            var projectile = CombatUtils.ShootProjectile(shootParams);

            // Mark as aimed shot - halves target's cover dodge bonus
            if (wasExtendedAim && projectile != null)
            {
                projectile.SetAimedShot(true);

                // DEBUG: Red projectile for extended aim shots
                var sr = projectile.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = Color.red;
            }
        }

        #endregion

        #region Interrupts

        /// <summary>
        /// Called when unit takes damage.
        /// Only interrupt exposed phases (Standing, Aiming, ExtendedAiming, Shooting).
        /// Don't duck if already safe in cover or seeking cover.
        /// </summary>
        public void OnDamageTaken()
        {
            // Only interrupt if currently exposed (peeking phases)
            if (IsPeekingPhase(currentPhase))
            {
                StartPhase(CombatPhase.Ducking);
            }
            // If in cover or seeking cover, stay put - no visual change needed
        }

        /// <summary>
        /// Called when unit needs to move - abort shooting sequence.
        /// </summary>
        public void OnForceMove()
        {
            StartPhase(CombatPhase.SeekingCover);
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Get list of known enemy GameObjects within weapon range.
        /// Used for LOS checking in cover scoring.
        /// </summary>
        private List<GameObject> GetKnownEnemies()
        {
            var enemies = new List<GameObject>();
            var allTargets = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

            foreach (var mb in allTargets)
            {
                var targetable = mb as ITargetable;
                if (targetable == null) continue;
                if (targetable.Team == controller.Team) continue;
                if (targetable.Team == Team.Neutral) continue;
                if (targetable.IsDead) continue;

                float dist = Vector2.Distance(controller.transform.position, targetable.Transform.position);
                if (dist <= controller.WeaponRange)
                {
                    enemies.Add(targetable.Transform.gameObject);
                }
            }

            return enemies;
        }

        #endregion
    }
}
