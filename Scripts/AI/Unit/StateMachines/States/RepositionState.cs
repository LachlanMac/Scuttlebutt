using UnityEngine;
using Starbelter.Core;
using Starbelter.Combat;

namespace Starbelter.AI
{
    /// <summary>
    /// Unit is moving to a new fighting position.
    /// Waits for movement to complete, then transitions to CombatState.
    /// </summary>
    public class RepositionState : UnitState
    {
        private GameObject targetAfterArrival;

        // Abort if taking too much fire
        private float threatCheckTimer;
        private const float THREAT_CHECK_INTERVAL = 0.5f;

        // Give movement time to start (async path request)
        private float waitForMovementTimer;
        private const float WAIT_FOR_MOVEMENT_TIME = 0.2f;

        public RepositionState(GameObject target)
        {
            targetAfterArrival = target;
        }

        public override void Enter()
        {
            threatCheckTimer = THREAT_CHECK_INTERVAL;
            waitForMovementTimer = WAIT_FOR_MOVEMENT_TIME;
        }

        public override void Update()
        {
            // Give movement time to start (path request is async)
            if (waitForMovementTimer > 0f)
            {
                waitForMovementTimer -= Time.deltaTime;
                return;
            }

            // Check if target is still valid
            if (targetAfterArrival != null && (!targetAfterArrival.activeInHierarchy || IsTargetDead()))
            {
                targetAfterArrival = null; // Clear invalid target, will find new one in combat
            }

            // Periodically check threat - abort reposition if too dangerous
            // But use high threshold - we committed to this move, don't bail easily
            threatCheckTimer -= Time.deltaTime;
            if (threatCheckTimer <= 0f)
            {
                threatCheckTimer = THREAT_CHECK_INTERVAL;

                if (ThreatManager != null)
                {
                    int bravery = controller.Character?.Bravery ?? 10;
                    float abortThreshold = CombatUtils.CalculateThreatThreshold(
                        CombatUtils.REPOSITION_ABORT_THREAT_BASE, CombatUtils.REPOSITION_ABORT_BRAVERY_MULT, bravery);

                    if (ThreatManager.GetTotalThreat() > abortThreshold)
                    {
                        Debug.Log($"[{controller.name}] RepositionState: Aborting, threat too high");
                        Movement.Stop();
                        ChangeState<SeekCoverState>();
                        return;
                    }
                }
            }

            // Wait for movement to complete
            if (!Movement.IsMoving)
            {
                // Arrived at fighting position - engage normally (don't skip cover phase)
                Debug.Log($"[{controller.name}] RepositionState: Arrived at fighting position");

                // Use alreadyAtCover: true to prevent immediate re-seeking
                // The fighting position was chosen specifically for cover + shooting angle
                var combatState = new CombatState(alreadyAtCover: true);
                stateMachine.ChangeState(combatState);
            }
        }

        private bool IsTargetDead()
        {
            return CombatUtils.IsTargetDead(targetAfterArrival);
        }
    }
}
