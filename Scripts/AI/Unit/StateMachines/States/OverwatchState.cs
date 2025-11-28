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

        public OverwatchState(GameObject target)
        {
            watchTarget = target;
        }

        public override void Enter()
        {
            if (watchTarget != null)
            {
                lastKnownPosition = watchTarget.transform.position;
            }

            targetScanTimer = TARGET_SCAN_INTERVAL;
            reactionTimer = 0f;
            isReacting = false;

            Debug.Log($"[OverwatchState] {controller.name} overwatching {watchTarget?.name ?? "position"}");
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
            if (watchTarget == null || !watchTarget.activeInHierarchy)
            {
                // Target gone - look for new targets
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
            Debug.Log($"[OverwatchState] {controller.name} reacting! Firing in {reactionTimer:F2}s");
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
