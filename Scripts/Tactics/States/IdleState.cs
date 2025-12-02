using UnityEngine;
using Starbelter.Core;

namespace Starbelter.Tactics.States
{
    /// <summary>
    /// Idle state - no immediate threats, holding position.
    /// Scans for enemies and transitions to Combat when found.
    /// </summary>
    public class IdleState : TacticalState
    {
        private float lastScanTime;
        private const float ScanInterval = 0.5f;

        public override void Enter(TacticalUnit unit)
        {
            base.Enter(unit);
            lastScanTime = 0f;
        }

        public override void Update()
        {
            // Periodic scan for enemies
            if (Time.time - lastScanTime >= ScanInterval)
            {
                lastScanTime = Time.time;
                ScanForThreats();
            }

            // Check if we're in a dangerous tile
            if (TacticalQueries.IsInDanger(unit.Position, unit.Team))
            {
                // Find cover!
                unit.RequestCoverPosition();
                unit.ChangeState(TacticalStateType.Moving);
                return;
            }
        }

        private void ScanForThreats()
        {
            var enemy = TacticalQueries.FindClosestVisibleEnemy(
                unit.Position,
                unit.Team,
                TacticalConstants.MaxEngageRange);

            if (enemy != null)
            {
                unit.SetTarget(enemy);
                unit.ChangeState(TacticalStateType.Combat);
            }
        }
    }
}
