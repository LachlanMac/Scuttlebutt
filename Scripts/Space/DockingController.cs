using UnityEngine;
using Starbelter.Ship;

namespace Starbelter.Space
{
    /// <summary>
    /// Handles docking behavior for a SpaceVessel.
    /// Manages the approach → parent → dock sequence.
    /// </summary>
    public class DockingController : MonoBehaviour
    {
        private enum DockingState
        {
            Idle,
            Approaching,    // Flying to ApproachVector position
            Docking,        // Parented to mothership, flying to HangarExit
            Complete        // Ready for arena transition
        }

        [Header("Settings")]
        [SerializeField] private float approachSpeed = 15f;
        [SerializeField] private float dockingSpeed = 5f;
        [SerializeField] private float rotationSpeed = 180f;
        [SerializeField] private float arrivalThreshold = 2f;

        // Runtime state
        private DockingState state = DockingState.Idle;
        private SpaceVessel targetVessel;
        private HangarExit targetExit;
        private string targetExitId;
        private Transform originalParent;

        // Components
        private SpaceVessel myVessel;
        private Rigidbody2D rb;

        // Events
        public event System.Action<DockingController, HangarExit> OnDockingComplete;

        // Properties
        public bool IsDocking => state != DockingState.Idle;
        public SpaceVessel TargetVessel => targetVessel;

        private void Awake()
        {
            myVessel = GetComponent<SpaceVessel>();
            rb = GetComponent<Rigidbody2D>();
        }

        private void Update()
        {
            switch (state)
            {
                case DockingState.Approaching:
                    UpdateApproaching();
                    break;
                case DockingState.Docking:
                    UpdateDocking();
                    break;
            }
        }

        /// <summary>
        /// Request docking at a target vessel's hangar.
        /// </summary>
        public bool RequestDocking(SpaceVessel mothership, string exitId = "hangar_main")
        {
            if (state != DockingState.Idle)
            {
                Debug.LogWarning("[DockingController] Already docking!");
                return false;
            }

            // Request docking permission
            var exit = mothership.RequestDocking(exitId);
            if (exit == null)
            {
                Debug.Log("[DockingController] Docking request denied");
                return false;
            }

            targetVessel = mothership;
            targetExit = exit;
            targetExitId = exitId;
            originalParent = transform.parent;

            // Disable physics for manual control
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.bodyType = RigidbodyType2D.Kinematic;
            }

            state = DockingState.Approaching;
            Debug.Log($"[DockingController] Docking approved, approaching {targetExit.name}");
            return true;
        }

        /// <summary>
        /// Cancel docking and return to idle.
        /// </summary>
        public void CancelDocking()
        {
            if (state == DockingState.Idle) return;

            // Unparent if we were docking
            if (state == DockingState.Docking)
            {
                transform.SetParent(originalParent);
            }

            // Re-enable physics
            if (rb != null)
            {
                rb.bodyType = RigidbodyType2D.Dynamic;
            }

            state = DockingState.Idle;
            targetVessel = null;
            targetExit = null;
            Debug.Log("[DockingController] Docking cancelled");
        }

        private void UpdateApproaching()
        {
            if (targetExit == null)
            {
                CancelDocking();
                return;
            }

            Vector2 targetPos = targetExit.ApproachPosition;
            Vector2 currentPos = transform.position;
            Vector2 direction = targetPos - currentPos;
            float distance = direction.magnitude;

            // Check if arrived at approach point
            if (distance < arrivalThreshold)
            {
                Debug.Log("[DockingController] Reached approach point, parenting to mothership");

                // Parent to mothership so we move with it
                transform.SetParent(targetVessel.transform);

                state = DockingState.Docking;
                return;
            }

            // Rotate toward target
            float targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            float currentAngle = transform.eulerAngles.z;
            float newAngle = Mathf.MoveTowardsAngle(currentAngle, targetAngle, rotationSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0, 0, newAngle);

            // Move toward target
            transform.position = Vector2.MoveTowards(currentPos, targetPos, approachSpeed * Time.deltaTime);
        }

        private void UpdateDocking()
        {
            if (targetExit == null)
            {
                CancelDocking();
                return;
            }

            Vector2 targetPos = targetExit.Position;
            Vector2 currentPos = transform.position;
            Vector2 direction = targetPos - currentPos;
            float distance = direction.magnitude;

            // Check if arrived at hangar exit
            if (distance < arrivalThreshold)
            {
                Debug.Log("[DockingController] Reached hangar exit, docking complete!");

                state = DockingState.Complete;
                OnDockingComplete?.Invoke(this, targetExit);

                // Transition to arena will be handled by whoever listens to OnDockingComplete
                return;
            }

            // Rotate toward hangar (use approach rotation to face the right way)
            float targetAngle = targetExit.ApproachRotation;
            float currentAngle = transform.eulerAngles.z;
            float newAngle = Mathf.MoveTowardsAngle(currentAngle, targetAngle, rotationSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0, 0, newAngle);

            // Move toward hangar exit (slower docking speed)
            transform.position = Vector2.MoveTowards(currentPos, targetPos, dockingSpeed * Time.deltaTime);
        }

        /// <summary>
        /// Complete the docking by transitioning to arena.
        /// Call this after OnDockingComplete to spawn the parked ship.
        /// </summary>
        public void CompleteDocking()
        {
            if (state != DockingState.Complete)
            {
                Debug.LogWarning("[DockingController] Cannot complete docking - not in Complete state");
                return;
            }

            // Get the hangar entrance
            var entrance = targetVessel.GetHangarEntrance(targetExitId);
            if (entrance == null)
            {
                Debug.LogError($"[DockingController] No HangarEntrance found for '{targetExitId}'");
                return;
            }

            // Get ship state for transfer
            ShipState shipState = myVessel?.GetCurrentState() ?? new ShipState();

            // Get parked prefab from our vessel
            GameObject parkedPrefab = myVessel?.ParkedPrefab;
            if (parkedPrefab == null)
            {
                Debug.LogWarning("[DockingController] No parked prefab on SpaceVessel, using HangarEntrance default");
            }

            // Spawn parked ship at hangar entrance
            var parkedShip = entrance.SpawnShipWithState(shipState, parkedPrefab);

            if (parkedShip != null)
            {
                Debug.Log("[DockingController] Spawned parked ship, destroying space vessel");
                Destroy(gameObject);
            }
            else
            {
                Debug.LogError("[DockingController] Failed to spawn parked ship!");
                CancelDocking();
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (targetExit != null && state != DockingState.Idle)
            {
                // Draw line to current target
                Gizmos.color = state == DockingState.Approaching ? Color.yellow : Color.green;
                Vector2 target = state == DockingState.Approaching
                    ? targetExit.ApproachPosition
                    : targetExit.Position;
                Gizmos.DrawLine(transform.position, target);
            }
        }
#endif
    }
}
