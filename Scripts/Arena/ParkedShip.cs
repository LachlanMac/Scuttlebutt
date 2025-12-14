using UnityEngine;
using Starbelter.Core;
using Starbelter.Ship;

namespace Starbelter.Arena
{
    /// <summary>
    /// A ship that has entered the hangar and is navigating to its landing zone.
    /// Handles approach movement, parking, and launch sequences.
    /// Stats loaded from ShipData via DataLoader.
    /// </summary>
    public class ParkedShip : MonoBehaviour
    {
        private enum State
        {
            Approaching,    // Moving toward landing zone
            Rotating,       // Rotating to final facing
            Landing,        // Scaling down
            Parked,         // Done, waiting
            Launching,      // Scaling up (reverse of landing)
            Departing       // Moving toward hangar exit
        }

        [Header("Ship Type")]
        [Tooltip("Ship type ID from Ships.json (e.g., 'starfighter_a')")]
        [SerializeField] private string shipTypeId = "starfighter_a";

        [Header("References")]
        [Tooltip("Particle system to play during landing/launch (e.g., thrusters)")]
        [SerializeField] private ParticleSystem landingParticles;

        [Header("Runtime (Set by HangarEntrance)")]
        [SerializeField] private LandingZone targetZone;

        // Loaded ship data
        private ShipData shipData;

        private State currentState = State.Approaching;
        private const float ARRIVAL_THRESHOLD = 0.1f;
        private const float ROTATION_THRESHOLD = 1f;
        private const float LANDING_SCALE_DECREASE = 0.1f; // 10% shrink

        // Properties from ShipData
        private float MoveSpeed => shipData?.approachSpeed ?? 3f;
        private float RotateSpeed => (shipData?.turnRate ?? 90f) * 0.5f; // Half turn rate for docking
        private float LandingDuration => shipData?.landingDuration ?? 4f;
        private float DockingSpeed => shipData?.dockingSpeed ?? 5f;

        private Vector3 initialScale;
        private Vector3 targetScale;
        private float landingTimer = 0f;

        // Ship state data (transferred between arena/space)
        private ShipState shipState;
        private HangarEntrance exitPoint;

        // Events
        public event System.Action<ParkedShip> OnParked;
        public event System.Action<ParkedShip, ShipState> OnLaunched;

        // Properties
        public bool IsParked => currentState == State.Parked;
        public LandingZone OccupiedZone => targetZone;
        public ShipState ShipState => shipState;
        public string ShipTypeId => shipTypeId;
        public ShipData ShipData => shipData;
        public GameObject SpacePrefab => shipData?.spacePrefab;

        private void Awake()
        {
            LoadShipData();
        }

        /// <summary>
        /// Load ship data from DataLoader.
        /// </summary>
        private void LoadShipData()
        {
            if (string.IsNullOrEmpty(shipTypeId))
            {
                Debug.LogWarning($"[ParkedShip] No shipTypeId set on {gameObject.name}");
                return;
            }

            shipData = DataLoader.GetShip(shipTypeId);
            if (shipData == null)
            {
                Debug.LogWarning($"[ParkedShip] Unknown ship type: {shipTypeId}");
            }
        }

        /// <summary>
        /// Initialize the ship with its target and movement settings.
        /// </summary>
        public void Initialize(LandingZone zone, HangarEntrance entrance, float speed, float rotSpeed, ShipState state = null)
        {
            targetZone = zone;
            exitPoint = entrance;
            shipState = state ?? new ShipState(shipTypeId);

            // Update ship type from state if provided
            if (state != null && !string.IsNullOrEmpty(state.ShipTypeId) && state.ShipTypeId != shipTypeId)
            {
                shipTypeId = state.ShipTypeId;
                LoadShipData();
            }

            // Immediately face toward the landing zone
            FaceToward(zone.Position);

            // Mark zone as occupied
            zone.SetOccupied(true);

            // Store scale for landing effect (scale DOWN to simulate lowering)
            initialScale = transform.localScale;
            targetScale = initialScale * (1f - LANDING_SCALE_DECREASE);

            // Ensure particles are off
            if (landingParticles != null)
                landingParticles.Stop();

            currentState = State.Approaching;
        }

        private void Update()
        {
            switch (currentState)
            {
                case State.Approaching:
                    UpdateApproach();
                    break;
                case State.Rotating:
                    UpdateRotating();
                    break;
                case State.Landing:
                    UpdateLanding();
                    break;
                case State.Parked:
                    // Do nothing, waiting for Launch()
                    break;
                case State.Launching:
                    UpdateLaunching();
                    break;
                case State.Departing:
                    UpdateDeparting();
                    break;
            }
        }

        private void UpdateApproach()
        {
            if (targetZone == null) return;

            Vector3 targetPos = targetZone.Position;
            Vector3 direction = targetPos - transform.position;
            float distance = direction.magnitude;

            // Check if arrived
            if (distance < ARRIVAL_THRESHOLD)
            {
                transform.position = targetPos;
                currentState = State.Rotating;

                // Start particles during rotation
                if (landingParticles != null)
                    landingParticles.Play();

                Debug.Log($"[ParkedShip] Arrived at landing zone, rotating...");
                return;
            }

            // Move toward target
            transform.position += direction.normalized * MoveSpeed * Time.deltaTime;
        }

        private void UpdateRotating()
        {
            if (targetZone == null) return;

            float targetRotation = targetZone.ParkedRotation;
            float currentRotation = transform.eulerAngles.z;

            // Use shortest rotation direction
            float remainingRotation = Mathf.Abs(Mathf.DeltaAngle(currentRotation, targetRotation));

            // Check if rotation complete
            if (remainingRotation < ROTATION_THRESHOLD)
            {
                transform.rotation = Quaternion.Euler(0, 0, targetRotation);
                landingTimer = 0f;
                currentState = State.Landing;
                Debug.Log($"[ParkedShip] Rotation complete, landing...");
                return;
            }

            // Rotate toward target using shortest path
            float newAngle = Mathf.MoveTowardsAngle(currentRotation, targetRotation, RotateSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0, 0, newAngle);
        }

        private void UpdateLanding()
        {
            float landingDuration = LandingDuration;
            landingTimer += Time.deltaTime;
            float progress = Mathf.Clamp01(landingTimer / landingDuration);

            // Lerp scale down to final size
            transform.localScale = Vector3.Lerp(initialScale, targetScale, progress);

            // Check if landing complete
            if (progress >= 1f)
            {
                transform.localScale = targetScale;
                currentState = State.Parked;

                // Stop landing particles
                if (landingParticles != null)
                    landingParticles.Stop();

                Debug.Log($"[ParkedShip] Parked and ready");
                OnParked?.Invoke(this);
            }
        }

        /// <summary>
        /// Immediately face toward a position.
        /// </summary>
        private void FaceToward(Vector3 targetPos)
        {
            Vector3 direction = targetPos - transform.position;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            transform.rotation = Quaternion.Euler(0, 0, angle);
        }

        /// <summary>
        /// Called when the ship is being launched (leaves the hangar).
        /// </summary>
        public void Launch()
        {
            if (currentState != State.Parked)
            {
                Debug.LogWarning("[ParkedShip] Cannot launch - ship is not parked!");
                return;
            }

            if (exitPoint == null)
            {
                Debug.LogError("[ParkedShip] Cannot launch - no exit point assigned!");
                return;
            }

            // Free up the landing zone
            if (targetZone != null)
            {
                targetZone.SetOccupied(false);
            }

            // Prepare for launch sequence (reverse of landing)
            landingTimer = 0f;
            initialScale = transform.localScale; // Current (parked) scale
            targetScale = initialScale * (1f + LANDING_SCALE_DECREASE / (1f - LANDING_SCALE_DECREASE)); // Back to original

            // Start particles
            if (landingParticles != null)
                landingParticles.Play();

            currentState = State.Launching;
            Debug.Log($"[ParkedShip] Launching...");
        }

        private void UpdateLaunching()
        {
            float landingDuration = LandingDuration;
            landingTimer += Time.deltaTime;
            float progress = Mathf.Clamp01(landingTimer / landingDuration);

            // Lerp scale UP (reverse of landing)
            transform.localScale = Vector3.Lerp(initialScale, targetScale, progress);

            // Check if launch lift complete
            if (progress >= 1f)
            {
                transform.localScale = targetScale;

                // Face toward exit
                if (exitPoint != null)
                {
                    FaceToward(exitPoint.transform.position);
                }

                currentState = State.Departing;
                Debug.Log($"[ParkedShip] Departing toward exit...");
            }
        }

        private void UpdateDeparting()
        {
            if (exitPoint == null) return;

            Vector3 targetPos = exitPoint.transform.position;
            Vector3 direction = targetPos - transform.position;
            float distance = direction.magnitude;

            // Check if reached exit
            if (distance < ARRIVAL_THRESHOLD)
            {
                // Stop particles
                if (landingParticles != null)
                    landingParticles.Stop();

                Debug.Log($"[ParkedShip] Reached exit, spawning space vessel...");

                // Notify listeners (HangarEntrance will handle spawning)
                OnLaunched?.Invoke(this, shipState);

                // Destroy this parked representation
                Destroy(gameObject);
                return;
            }

            // Move toward exit at docking speed
            transform.position += direction.normalized * DockingSpeed * Time.deltaTime;
        }

        private void OnDestroy()
        {
            // Free up the landing zone if destroyed
            if (targetZone != null)
            {
                targetZone.SetOccupied(false);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (targetZone != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(transform.position, targetZone.Position);
            }
        }
#endif
    }
}
