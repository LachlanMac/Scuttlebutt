using UnityEngine;

namespace Starbelter.AI
{
    /// <summary>
    /// OnDuty Guard - Unit stands guard at a fixed position.
    /// Watches the area but doesn't move unless responding to threats.
    /// </summary>
    public class OnDutyGuardState : UnitState
    {
        private Vector3 guardPosition;
        private float lookAroundTimer;
        private const float LOOK_INTERVAL = 3f;

        public override void Enter()
        {
            base.Enter();
            guardPosition = controller.transform.position;
            lookAroundTimer = Time.time + LOOK_INTERVAL;
        }

        public override void Update()
        {
            if (!IsValid) return;

            // TODO: Check for threats -> switch to Alert mode
            // if (controller.PerceivesThreat()) { controller.ChangeBehaviorMode(BehaviorMode.Alert); return; }

            // Occasionally look around
            if (Time.time >= lookAroundTimer)
            {
                LookAround();
                lookAroundTimer = Time.time + UnitActions.RandomWaitTime(2f, 5f);
            }

            // If somehow moved from guard position, return to it
            float distFromPost = Vector3.Distance(controller.transform.position, guardPosition);
            if (distFromPost > 1f && !Movement.IsMoving)
            {
                UnitActions.MoveToPosition(controller, guardPosition, useThreatAwarePath: false);
            }
        }

        private void LookAround()
        {
            // Random look direction
            Vector2 randomDir = Random.insideUnitCircle.normalized;
            controller.SetFacingDirection(randomDir);
        }
    }
}
