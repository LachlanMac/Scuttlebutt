using UnityEngine;
using Starbelter.Core;
using Starbelter.Combat;
using Starbelter.Pathfinding;

namespace Starbelter.AI
{
    /// <summary>
    /// Unit moves to a flanking position to get line-of-sight on a target in cover.
    /// Finds a position that has both LOS to target AND cover from target.
    /// </summary>
    public class FlankState : UnitState
    {
        private GameObject flankTarget;
        private Vector2 flankPosition;
        private bool hasFlankPosition;

        // Re-evaluate if we're taking fire
        private float threatCheckTimer;
        private const float THREAT_CHECK_INTERVAL = 0.5f;

        public FlankState(GameObject target)
        {
            flankTarget = target;
        }

        public override void Enter()
        {
            hasFlankPosition = false;
            threatCheckTimer = THREAT_CHECK_INTERVAL;

            if (flankTarget == null)
            {
                // Target gone, find new enemies
                ChangeState<CombatState>();
                return;
            }

            // Find a flank position (exclude self from occupancy check)
            var flankResult = CombatUtils.FindFlankPosition(
                controller.transform.position,
                flankTarget.transform.position,
                controller.WeaponRange,
                CoverQuery.Instance,
                controller.gameObject,
                controller.Team
            );

            if (flankResult.Found)
            {
                flankPosition = flankResult.Position;
                // Only set hasFlankPosition if movement actually started
                hasFlankPosition = Movement.MoveTo(flankPosition);

                if (!hasFlankPosition)
                {
                    // Movement throttled, fall back to overwatch
                    var overwatchState = new OverwatchState(flankTarget);
                    stateMachine.ChangeState(overwatchState);
                }
            }
            else
            {
                // No flank position found - fall back to overwatch
                var overwatchState = new OverwatchState(flankTarget);
                stateMachine.ChangeState(overwatchState);
            }
        }

        public override void Update()
        {
            if (!hasFlankPosition) return;

            // Check if target is still valid
            if (flankTarget == null || !flankTarget.activeInHierarchy || IsFlankTargetDead())
            {
                // Target gone or dead, find new enemies
                ChangeState<CombatState>();
                return;
            }

            // Periodically check threat level - abort flank if too dangerous
            threatCheckTimer -= Time.deltaTime;
            if (threatCheckTimer <= 0f)
            {
                threatCheckTimer = THREAT_CHECK_INTERVAL;

                if (!ShouldContinueFlanking())
                {
                    ChangeState<SeekCoverState>();
                    return;
                }
            }

            // Check if we've arrived at flank position
            if (!Movement.IsMoving)
            {
                // Verify we actually have LOS from the FIRE POSITION (not unit center)
                Vector2 firePos = controller.FirePosition;
                Vector2 targetPos = flankTarget.transform.position;

                var los = CombatUtils.CheckLineOfSight(firePos, targetPos);

                if (los.IsBlocked)
                {
                    // Flank failed - still no LOS, fall back to overwatch
                    var overwatchState = new OverwatchState(flankTarget);
                    stateMachine.ChangeState(overwatchState);
                    return;
                }

                // Arrived with valid LOS - engage target from THIS position
                var combatState = new CombatState(flankTarget);
                stateMachine.ChangeState(combatState);
            }
        }

        private bool IsFlankTargetDead()
        {
            return CombatUtils.IsTargetDead(flankTarget);
        }

        private bool ShouldContinueFlanking()
        {
            if (ThreatManager == null) return true;

            // If threat has increased significantly (multiple directions), abort
            var threats = ThreatManager.GetActiveThreats(0.5f);
            if (threats.Count > 1) return false;

            // If total threat is very high, abort
            float totalThreat = ThreatManager.GetTotalThreat();
            int bravery = controller.Character?.Bravery ?? 10;
            float threshold = CombatUtils.CalculateThreatThreshold(
                CombatUtils.FLANK_ABORT_THREAT_BASE, CombatUtils.FLANK_ABORT_BRAVERY_MULT, bravery);

            if (totalThreat > threshold) return false;

            return true;
        }
    }
}
