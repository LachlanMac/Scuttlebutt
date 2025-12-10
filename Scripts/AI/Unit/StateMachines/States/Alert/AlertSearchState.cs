using UnityEngine;

namespace Starbelter.AI
{
    /// <summary>
    /// Alert Search - Unit actively searches an area for threats.
    /// Moves cautiously, checking corners and hiding spots.
    /// </summary>
    public class AlertSearchState : UnitState
    {
        private float searchStartTime;
        private float maxSearchTime = 15f;
        private int searchPoints;
        private const int MAX_SEARCH_POINTS = 3;

        public override void Enter()
        {
            base.Enter();
            searchStartTime = Time.time;
            searchPoints = 0;
            MoveToNextSearchPoint();
        }

        public override void Update()
        {
            if (!IsValid) return;

            // Check for confirmed threat -> Combat mode
            // TODO: if (controller.HasConfirmedThreat()) { controller.ChangeBehaviorMode(BehaviorMode.Combat); return; }

            // Search timeout
            if (Time.time - searchStartTime >= maxSearchTime)
            {
                ConcludeSearch();
                return;
            }

            // If arrived at search point, check the area then move to next
            if (!Movement.IsMoving)
            {
                searchPoints++;

                if (searchPoints >= MAX_SEARCH_POINTS)
                {
                    ConcludeSearch();
                    return;
                }

                // Brief pause to "check" the area
                if (TimeInState > 1f)
                {
                    MoveToNextSearchPoint();
                }
            }
        }

        public override void Exit()
        {
            base.Exit();
            UnitActions.StopMovement(controller);
        }

        private void MoveToNextSearchPoint()
        {
            // Search nearby positions
            Vector3 searchPos = controller.transform.position +
                new Vector3(Random.Range(-3f, 3f), Random.Range(-3f, 3f), 0);

            UnitActions.MoveToPosition(controller, searchPos, useThreatAwarePath: false);
        }

        private void ConcludeSearch()
        {
            // Nothing found, return to duty
            Debug.Log($"[{controller.name}] Search complete - all clear");
            controller.ChangeBehaviorMode(BehaviorMode.OnDuty);
        }
    }
}
