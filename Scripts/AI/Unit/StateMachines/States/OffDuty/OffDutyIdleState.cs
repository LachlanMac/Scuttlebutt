using UnityEngine;
using Starbelter.Arena;

namespace Starbelter.AI
{
    /// <summary>
    /// OffDuty Idle - Unit is relaxed, standing around, not doing much.
    /// May occasionally look around or shift position.
    /// </summary>
    public class OffDutyIdleState : UnitState
    {
        private float nextActionTime;
        private float idleDuration;

        public override void Enter()
        {
            base.Enter();
            // Decide how long to idle before doing something else
            idleDuration = UnitActions.RandomWaitTime(3f, 10f);
            nextActionTime = Time.time + idleDuration;
        }

        public override void Update()
        {
            if (!IsValid) return;

            // Check if it's time to do something else
            if (Time.time >= nextActionTime)
            {
                DecideNextAction();
            }
        }

        private void DecideNextAction()
        {
            // Random chance to wander
            if (UnitActions.RandomChance(0.3f))
            {
                controller.ChangeState(UnitStateType.OffDuty_Wander);
                return;
            }

            // Otherwise, reset idle timer
            idleDuration = UnitActions.RandomWaitTime(3f, 10f);
            nextActionTime = Time.time + idleDuration;
        }
    }
}
