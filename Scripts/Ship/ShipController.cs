using UnityEngine;
using Starbelter.Arena;
using Starbelter.Ship;

namespace Starbelter.Core
{
    /// <summary>
    /// Controls a ship in the space layer.
    /// Handles movement, turning, and waypoint navigation.
    /// Subsystems (shields, weapons, etc.) are separate components on this GameObject.
    /// </summary>
    public class ShipController : MonoBehaviour
    {
        [Header("Ship Identity")]
        [SerializeField] private string shipName = "Unknown Vessel";

        [Header("Movement")]
        [SerializeField] private float maxSpeed = 10f;
        [SerializeField] private float acceleration = 5f;
        [SerializeField] private float turnSpeed = 90f; // Degrees per second
        [SerializeField] private float warpSpeed = 100f;

        [Header("State")]
        [SerializeField] private bool isWarping = false;

        // Runtime
        private Vector3? targetWaypoint = null;
        private float currentSpeed = 0f;
        private float thrustInput = 0f;
        private float turnInput = 0f;
        private Starbelter.Arena.Arena arena; // Linked arena (interior)
        private WeaponSystem weaponSystem;

        // Properties
        public string ShipName => shipName;
        public float CurrentSpeed => currentSpeed;
        public float MaxSpeed => maxSpeed;
        public bool IsWarping => isWarping;
        public bool HasWaypoint => targetWaypoint.HasValue;
        public Starbelter.Arena.Arena LinkedArena => arena;
        public WeaponSystem Weapons => weaponSystem;

        void Awake()
        {
            weaponSystem = GetComponent<WeaponSystem>();
        }

        /// <summary>
        /// Initialize with a linked arena (called by WorldManager on spawn).
        /// </summary>
        public void Initialize(Starbelter.Arena.Arena linkedArena)
        {
            arena = linkedArena;
            Debug.Log($"[ShipController] {shipName} linked to arena {linkedArena?.ArenaId ?? "none"}");
        }

        void Update()
        {
            if (targetWaypoint.HasValue)
            {
                MoveTowardsWaypoint();
            }
            else if (thrustInput != 0f || turnInput != 0f)
            {
                HandleDirectInput();
            }
        }

        #region Direct Input (Player/AI Control)

        /// <summary>
        /// Set direct control input. Used by PlayerPilot or AIPilot.
        /// </summary>
        /// <param name="thrust">-1 to 1 (back to forward)</param>
        /// <param name="turn">-1 to 1 (right to left)</param>
        public void SetInput(float thrust, float turn)
        {
            thrustInput = Mathf.Clamp(thrust, -1f, 1f);
            turnInput = Mathf.Clamp(turn, -1f, 1f);
        }

        private void HandleDirectInput()
        {
            // Turn
            transform.Rotate(0, 0, turnInput * turnSpeed * Time.deltaTime);

            // Thrust
            float effectiveMaxSpeed = isWarping ? warpSpeed : maxSpeed;
            float targetSpeed = thrustInput > 0 ? effectiveMaxSpeed * thrustInput : 0f;

            if (thrustInput < 0)
            {
                // Braking
                currentSpeed = Mathf.MoveTowards(currentSpeed, 0, acceleration * 2f * Time.deltaTime);
            }
            else
            {
                currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, acceleration * Time.deltaTime);
            }

            // Move forward (up is forward in 2D top-down)
            transform.position += transform.up * currentSpeed * Time.deltaTime;
        }

        #endregion

        #region Weapons

        /// <summary>
        /// Fire pilot weapon group.
        /// </summary>
        public void FirePilotGroup(int group = 1)
        {
            if (weaponSystem == null) return;
            weaponSystem.FirePilotGroup(group);
        }

        /// <summary>
        /// Fire all pilot weapons.
        /// </summary>
        public void FireAllPilotWeapons()
        {
            if (weaponSystem == null) return;
            weaponSystem.FireAllPilotWeapons();
        }

        #endregion

        #region Navigation

        /// <summary>
        /// Set a waypoint to navigate to.
        /// </summary>
        public void SetWaypoint(Vector3 position)
        {
            targetWaypoint = position;
            Debug.Log($"[ShipController] {shipName} waypoint set to {position}");
        }

        /// <summary>
        /// Clear the current waypoint.
        /// </summary>
        public void ClearWaypoint()
        {
            targetWaypoint = null;
            currentSpeed = 0f;
        }

        private void MoveTowardsWaypoint()
        {
            Vector3 target = targetWaypoint.Value;
            Vector3 direction = target - transform.position;
            float distance = direction.magnitude;

            // Arrived?
            if (distance < 0.5f)
            {
                ClearWaypoint();
                Debug.Log($"[ShipController] {shipName} arrived at waypoint");
                return;
            }

            // Turn towards target (up is forward, so subtract 90 degrees)
            float targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            float currentAngle = transform.eulerAngles.z;
            float newAngle = Mathf.MoveTowardsAngle(currentAngle, targetAngle, turnSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0, 0, newAngle);

            // Accelerate
            float effectiveMaxSpeed = isWarping ? warpSpeed : maxSpeed;
            currentSpeed = Mathf.MoveTowards(currentSpeed, effectiveMaxSpeed, acceleration * Time.deltaTime);

            // Move forward (up is forward in 2D top-down)
            transform.position += transform.up * currentSpeed * Time.deltaTime;
        }

        #endregion

        #region Warp

        /// <summary>
        /// Engage warp drive.
        /// </summary>
        public void EngageWarp()
        {
            isWarping = true;
            Debug.Log($"[ShipController] {shipName} warp engaged");
        }

        /// <summary>
        /// Disengage warp drive.
        /// </summary>
        public void DisengageWarp()
        {
            isWarping = false;
            Debug.Log($"[ShipController] {shipName} warp disengaged");
        }

        #endregion
    }
}
