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
                Debug.LogWarning($"[FlankState] {controller.name} has no flank target!");
                ChangeState<IdleState>();
                return;
            }

            // Find a flank position
            var flankResult = CombatUtils.FindFlankPosition(
                controller.transform.position,
                flankTarget.transform.position,
                controller.WeaponRange,
                CoverQuery.Instance
            );

            if (flankResult.Found)
            {
                flankPosition = flankResult.Position;
                hasFlankPosition = true;

                // Move to flank position
                Movement.MoveTo(flankPosition);
                Debug.Log($"[FlankState] {controller.name} flanking to {flankPosition}");
            }
            else
            {
                // No flank position found - fall back to overwatch
                Debug.Log($"[FlankState] {controller.name} couldn't find flank position, going to overwatch");
                var overwatchState = new OverwatchState(flankTarget);
                stateMachine.ChangeState(overwatchState);
            }
        }

        public override void Update()
        {
            if (!hasFlankPosition) return;

            // Check if target is still valid
            if (flankTarget == null || !flankTarget.activeInHierarchy)
            {
                ChangeState<IdleState>();
                return;
            }

            // Periodically check threat level - abort flank if too dangerous
            threatCheckTimer -= Time.deltaTime;
            if (threatCheckTimer <= 0f)
            {
                threatCheckTimer = THREAT_CHECK_INTERVAL;

                if (!ShouldContinueFlanking())
                {
                    Debug.Log($"[FlankState] {controller.name} aborting flank - threat too high");
                    ChangeState<SeekCoverState>();
                    return;
                }
            }

            // Check if we've arrived at flank position
            if (!Movement.IsMoving)
            {
                // Verify we actually have LOS from the FIRE POSITION (not unit center)
                // This must match where CombatState.FireAtTarget() spawns projectiles
                Vector2 firePos = controller.FirePosition;
                Vector2 targetPos = flankTarget.transform.position;

                var los = CombatUtils.CheckLineOfSight(firePos, targetPos);

                Debug.Log($"[FlankState] {controller.name} arrived. Unit at {(Vector2)controller.transform.position}, fire pos at {firePos}, target at {targetPos}");
                Debug.Log($"[FlankState] LOS check: blocked={los.IsBlocked}, blocker={los.BlockingCover?.name}, distance={los.Distance:F1}");

                if (los.IsBlocked)
                {
                    // Flank failed - still no LOS, try again or give up
                    Debug.Log($"[FlankState] {controller.name} arrived but still no LOS (blocked by {los.BlockingCover?.name}), going to overwatch");
                    var overwatchState = new OverwatchState(flankTarget);
                    stateMachine.ChangeState(overwatchState);
                    return;
                }

                // Arrived with valid LOS - engage target from THIS position
                // Pass the target so CombatState skips seeking cover and shoots immediately
                Debug.Log($"[FlankState] {controller.name} arrived at flank position with LOS, engaging");
                var combatState = new CombatState(flankTarget);
                stateMachine.ChangeState(combatState);
            }
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
            float threshold = 3f + (bravery * 0.3f);

            if (totalThreat > threshold) return false;

            return true;
        }
    }
}
