using UnityEngine;
using Starbelter.Arena;

namespace Starbelter.AI
{
    /// <summary>
    /// OffDuty Wander - Unit casually walks to random nearby locations.
    /// No urgency, no threat awareness.
    /// </summary>
    public class OffDutyWanderState : UnitState
    {
        private bool hasDestination;
        private float wanderTimeout;

        public override void Enter()
        {
            base.Enter();
            hasDestination = false;
            wanderTimeout = Time.time + 15f; // Max time to wander before giving up
            PickDestination();
        }

        public override void Update()
        {
            if (!IsValid) return;

            // Check if we've arrived or timed out
            if (!Movement.IsMoving || Time.time >= wanderTimeout)
            {
                // Done wandering, go back to idle
                controller.ChangeState(UnitStateType.OffDuty_Idle);
                return;
            }
        }

        public override void Exit()
        {
            base.Exit();
            UnitActions.StopMovement(controller);
        }

        private void PickDestination()
        {
            // Try to find a random room to wander to
            Room targetRoom = UnitActions.FindRandomRoom(controller);

            if (targetRoom != null)
            {
                hasDestination = UnitActions.MoveToRoom(controller, targetRoom, useThreatAwarePath: false);
            }
            else
            {
                // No room system, just pick random nearby position
                hasDestination = UnitActions.MoveToRandomPosition(controller, maxDistance: 5f, useThreatAwarePath: false);
            }

            if (!hasDestination)
            {
                // Couldn't find destination, go back to idle
                controller.ChangeState(UnitStateType.OffDuty_Idle);
            }
        }
    }
}
