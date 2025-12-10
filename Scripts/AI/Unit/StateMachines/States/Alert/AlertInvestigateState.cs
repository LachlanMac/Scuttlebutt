using UnityEngine;

namespace Starbelter.AI
{
    /// <summary>
    /// Alert Investigate - Unit moves to investigate a suspicious location.
    /// Cautious movement, weapon ready.
    /// </summary>
    public class AlertInvestigateState : UnitState
    {
        private Vector3 investigatePosition;
        private float investigateTimeout;
        private bool hasReachedPosition;

        public override void Enter()
        {
            base.Enter();
            hasReachedPosition = false;
            investigateTimeout = Time.time + 20f; // Max time to investigate

            // TODO: Get the actual suspicious location from perception system
            // For now, investigate nearby random position
            investigatePosition = controller.transform.position +
                new Vector3(Random.Range(-5f, 5f), Random.Range(-5f, 5f), 0);

            UnitActions.MoveToPosition(controller, investigatePosition, useThreatAwarePath: false);
        }

        public override void Update()
        {
            if (!IsValid) return;

            // Check for confirmed threat -> Combat mode
            // TODO: if (controller.HasConfirmedThreat()) { controller.ChangeBehaviorMode(BehaviorMode.Combat); return; }

            // Timeout - nothing found, return to previous duty
            if (Time.time >= investigateTimeout)
            {
                ReturnToDuty();
                return;
            }

            if (!Movement.IsMoving && !hasReachedPosition)
            {
                hasReachedPosition = true;
                // Start searching the area
                controller.ChangeState(UnitStateType.Alert_Search);
            }
        }

        public override void Exit()
        {
            base.Exit();
            UnitActions.StopMovement(controller);
        }

        private void ReturnToDuty()
        {
            // Nothing found, go back to OnDuty mode
            controller.ChangeBehaviorMode(BehaviorMode.OnDuty);
        }
    }
}
