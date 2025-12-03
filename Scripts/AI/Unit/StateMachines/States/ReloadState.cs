using UnityEngine;

namespace Starbelter.AI
{
    /// <summary>
    /// Reload state - unit is reloading weapon.
    /// Ducks behind cover if available. Vulnerable during reload.
    /// </summary>
    public class ReloadState : UnitState
    {
        private const float SUPPRESSION_PIN_THRESHOLD = 80f;
        private float reloadTimer;
        private float reloadDuration;
        private bool wasDucked;

        public override void Enter()
        {
            base.Enter();

            // Stop any movement
            controller.InterruptMovement();

            // Get reload time from weapon
            reloadDuration = controller.GetReloadTime();
            reloadTimer = 0f;

            // Duck if we're in half cover (more protected while reloading)
            wasDucked = controller.IsDucked;
            if (controller.IsInHalfCover && !wasDucked)
            {
                controller.SetDucked(true);
            }

            // Radio callout
            controller.ShowRadioMessage("RELOADING");

            Debug.Log($"[{controller.name}] Reloading... ({reloadDuration:F1}s)");
        }

        public override void Update()
        {
            if (!IsValid) return;

            // Check suppression - get pinned if too suppressed
            if (controller.Suppression >= SUPPRESSION_PIN_THRESHOLD)
            {
                controller.ChangeState(UnitStateType.Pinned);
                return;
            }

            // Progress reload
            reloadTimer += Time.deltaTime;

            if (reloadTimer >= reloadDuration)
            {
                CompleteReload();
            }
        }

        public override void Exit()
        {
            base.Exit();

            // Restore duck state if we changed it
            if (!wasDucked && controller.IsDucked)
            {
                controller.SetDucked(false);
            }
        }

        private void CompleteReload()
        {
            // Actually reload the weapon
            controller.FinishReload();

            Debug.Log($"[{controller.name}] Reload complete!");

            // Check for threats and transition
            var enemy = controller.FindClosestVisibleEnemy(controller.WeaponRange);

            if (enemy != null)
            {
                controller.SetTarget(enemy);
                controller.ChangeState(UnitStateType.Combat);
            }
            else if (controller.Squad != null && controller.Squad.HasBeenEngaged)
            {
                // Squad is engaged but we can't see anyone - go to combat anyway
                controller.ChangeState(UnitStateType.Combat);
            }
            else
            {
                controller.ChangeState(UnitStateType.Ready);
            }
        }
    }
}
