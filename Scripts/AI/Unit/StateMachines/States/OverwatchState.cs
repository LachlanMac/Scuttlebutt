using UnityEngine;
using System.Linq;
using Starbelter.Core;
using Starbelter.Combat;

namespace Starbelter.AI
{
    /// <summary>
    /// Unit watches a target position and shoots immediately when target peeks.
    /// Entered when target is in full cover and no better targets available.
    /// </summary>
    public class OverwatchState : UnitState
    {
        // The target we're watching (may be in full cover)
        private GameObject watchTarget;
        private Vector3 lastKnownPosition;

        // Scan for better targets periodically
        private float targetScanTimer;
        private const float TARGET_SCAN_INTERVAL = 1.0f;

        // Reaction time when target peeks
        private const float BASE_REACTION_TIME = 0.15f;
        private float reactionTimer;
        private bool isReacting;

        // If true, don't try to switch to suppress (came from failed suppress)
        private bool suppressionFailed;

        public OverwatchState(GameObject target, bool fromFailedSuppression = false)
        {
            watchTarget = target;
            suppressionFailed = fromFailedSuppression;
        }

        // Check squad threat periodically
        private float squadThreatCheckTimer;
        private const float SQUAD_THREAT_CHECK_INTERVAL = 0.5f;

        // When squad is engaged, can't sit in overwatch forever
        private float engagedTimer;
        private const float MAX_OVERWATCH_TIME_WHEN_ENGAGED = 3f;

        public override void Enter()
        {
            if (watchTarget != null)
            {
                lastKnownPosition = watchTarget.transform.position;
            }

            targetScanTimer = TARGET_SCAN_INTERVAL;
            squadThreatCheckTimer = SQUAD_THREAT_CHECK_INTERVAL;
            engagedTimer = 0f;
            reactionTimer = 0f;
            isReacting = false;
        }

        public override void Update()
        {
            // If we're reacting to a peek, count down and shoot
            if (isReacting)
            {
                reactionTimer -= Time.deltaTime;
                if (reactionTimer <= 0f)
                {
                    // Reaction complete - enter combat and shoot immediately
                    var combatState = new CombatState(watchTarget);
                    stateMachine.ChangeState(combatState);
                    return;
                }
                return;
            }

            // Check if watch target is still valid
            if (watchTarget == null || !watchTarget.activeInHierarchy || IsWatchTargetDead())
            {
                // Target gone or dead - look for new targets
                GameObject newTarget = FindBestTarget();
                if (newTarget != null)
                {
                    ChangeState<CombatState>();
                }
                else
                {
                    ChangeState<IdleState>();
                }
                return;
            }

            // Check if watch target is now exposed (peeking or moved out of cover)
            var los = CombatUtils.CheckLineOfSight(
                controller.transform.position,
                watchTarget.transform.position
            );

            if (!los.IsBlocked)
            {
                // Target exposed! Start reaction
                StartReaction();
                return;
            }

            // Periodically scan for better targets
            targetScanTimer -= Time.deltaTime;
            if (targetScanTimer <= 0f)
            {
                targetScanTimer = TARGET_SCAN_INTERVAL;

                GameObject betterTarget = FindExposedTarget();
                if (betterTarget != null)
                {
                    // Found an exposed target - engage!
                    ChangeState<CombatState>();
                    return;
                }
            }

            // React to incoming fire - might need to reposition
            if (ThreatManager != null && ThreatManager.IsUnderFire())
            {
                if (!controller.IsInCover)
                {
                    ChangeState<SeekCoverState>();
                    return;
                }
            }

            // Check if squad is under heavy fire - switch to suppression to help
            // But not if we just came from a failed suppression attempt
            // And not if target is already being suppressed by another squadmate
            if (!suppressionFailed)
            {
                squadThreatCheckTimer -= Time.deltaTime;
                if (squadThreatCheckTimer <= 0f)
                {
                    squadThreatCheckTimer = SQUAD_THREAT_CHECK_INTERVAL;

                    if (controller.Squad != null && controller.Squad.IsUnderHeavyFire && watchTarget != null)
                    {
                        // Check if target is already being suppressed
                        if (!controller.Squad.IsTargetBeingSuppressed(watchTarget))
                        {
                            // Squad needs help - switch to suppression
                            var suppressState = new SuppressState(watchTarget);
                            stateMachine.ChangeState(suppressState);
                            return;
                        }
                    }
                }
            }

            // If squad is engaged in combat, can't camp in overwatch forever
            // Must find a fighting position where we can actually contribute
            if (controller.Squad != null && controller.Squad.IsEngaged)
            {
                engagedTimer += Time.deltaTime;
                if (engagedTimer >= MAX_OVERWATCH_TIME_WHEN_ENGAGED)
                {
                    Debug.Log($"[{controller.name}] OverwatchState: Squad engaged for {engagedTimer:F1}s, finding fighting position");
                    FindFightingPosition();
                    return;
                }
            }
            else
            {
                // Reset timer if squad not engaged
                engagedTimer = 0f;
            }
        }

        /// <summary>
        /// Find a cover position where we can actually shoot enemies.
        /// Called when we've been sitting in overwatch too long during an engagement.
        /// </summary>
        private void FindFightingPosition()
        {
            // Get threat direction
            Vector2 threatDir = Vector2.up; // Default
            if (ThreatManager != null)
            {
                var dir = ThreatManager.GetHighestThreatDirection();
                if (dir.HasValue)
                {
                    threatDir = dir.Value;
                }
            }
            else if (watchTarget != null)
            {
                threatDir = ((Vector2)(watchTarget.transform.position - controller.transform.position)).normalized;
            }

            var fightingResult = CombatUtils.FindFightingPosition(
                controller.transform.position,
                threatDir,
                controller.WeaponRange,
                Pathfinding.CoverQuery.Instance,
                controller.gameObject,
                controller.Team
            );

            if (fightingResult.Found)
            {
                Debug.Log($"[{controller.name}] Found fighting position with target in {fightingResult.TargetCoverType} cover");

                // Move to the fighting position
                if (Movement.MoveTo(fightingResult.Position))
                {
                    // Go to RepositionState which will wait for arrival then engage
                    var repositionState = new RepositionState(fightingResult.BestTarget);
                    stateMachine.ChangeState(repositionState);
                }
                else
                {
                    // Movement throttled - stay in overwatch but reset timer
                    engagedTimer = 0f;
                }
            }
            else
            {
                // No fighting position found - try aggressive flank as last resort
                Debug.Log($"[{controller.name}] No fighting position found, trying flank");
                var flankState = new FlankState(watchTarget);
                stateMachine.ChangeState(flankState);
            }
        }

        private void StartReaction()
        {
            isReacting = true;

            // Reaction time modified by reflex stat
            float reactionTime = BASE_REACTION_TIME;
            if (controller.Character != null)
            {
                float reflexMod = Character.StatToModifier(controller.Character.Reflex);
                reactionTime -= reflexMod * 0.1f; // Good reflexes = faster reaction
            }

            reactionTimer = Mathf.Max(0.05f, reactionTime);
        }

        private bool IsWatchTargetDead()
        {
            if (watchTarget == null) return true;
            var targetable = watchTarget.GetComponent<ITargetable>();
            return targetable != null && targetable.IsDead;
        }

        /// <summary>
        /// Find any exposed enemy target (not in full cover).
        /// </summary>
        private GameObject FindExposedTarget()
        {
            ITargetable[] allTargets = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .OfType<ITargetable>()
                .ToArray();

            float weaponRange = controller.WeaponRange;

            foreach (var target in allTargets)
            {
                if (target.Transform == controller.transform) continue;
                if (target.Team == controller.Team) continue;
                if (controller.Team == Team.Neutral) continue;
                if (target.IsDead) continue;

                float dist = Vector3.Distance(controller.transform.position, target.Transform.position);
                if (dist > weaponRange) continue;

                // Check if target is exposed
                var los = CombatUtils.CheckLineOfSight(
                    controller.transform.position,
                    target.Transform.position
                );

                if (!los.IsBlocked)
                {
                    return target.Transform.gameObject;
                }
            }

            return null;
        }

        /// <summary>
        /// Find the best target overall (for when watch target is gone).
        /// </summary>
        private GameObject FindBestTarget()
        {
            ITargetable[] allTargets = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .OfType<ITargetable>()
                .ToArray();

            float weaponRange = controller.WeaponRange;
            float bestPriority = 0f;
            GameObject bestTarget = null;

            foreach (var target in allTargets)
            {
                if (target.Transform == controller.transform) continue;
                if (target.Team == controller.Team) continue;
                if (controller.Team == Team.Neutral) continue;
                if (target.IsDead) continue;

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

            return bestTarget;
        }
    }
}
