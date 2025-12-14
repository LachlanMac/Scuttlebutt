using UnityEngine;
using Starbelter.Core;
using Starbelter.Arena;
using Starbelter.Ship;

namespace Starbelter.Space
{
    /// <summary>
    /// Base class for all space-capable vessels.
    /// Fighters, capital ships, dropships, etc.
    /// Stats loaded from ShipData via DataLoader.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class SpaceVessel : MonoBehaviour
    {
        [Header("Ship Type")]
        [Tooltip("Ship type ID from Ships.json (e.g., 'starfighter_a', 'frigate')")]
        [SerializeField] private string shipTypeId = "starfighter_a";

        [Header("Vessel Identity")]
        [SerializeField] private string vesselId;
        [SerializeField] private Team team = Team.Federation;

        [Header("Interior")]
        [Tooltip("The arena representing this vessel's interior (if any)")]
        [SerializeField] private Arena.Arena interiorArena;

        // Loaded ship data
        private ShipData shipData;

        // Components
        private Rigidbody2D rb;

        // Runtime state
        private float targetHeading;
        private Vector2 targetVelocity;
        private bool isDestroyed;
        private float currentHull;
        private float currentShields;

        // Ship state (transferred between arena/space)
        private ShipState shipState;

        // Events
        public event System.Action<SpaceVessel> OnDestroyed;
        public event System.Action<float, float> OnHullChanged; // current, max
        public event System.Action<float, float> OnShieldsChanged; // current, max

        // Properties - from ShipData
        public string ShipTypeId => shipTypeId;
        public ShipData ShipData => shipData;
        public float MaxSpeed => shipData?.maxSpeed ?? 20f;
        public float Acceleration => shipData?.acceleration ?? 10f;
        public float TurnRate => shipData?.turnRate ?? 180f;
        public float MaxHull => shipData?.maxHull ?? 100f;
        public float MaxShields => shipData?.maxShields ?? 50f;
        public float ShieldRegenRate => shipData?.shieldRegenRate ?? 5f;
        public ShipCategory Category => shipData?.category ?? ShipCategory.Fighter;
        public bool CanDock => shipData?.canDock ?? true;
        public bool HasHangarBay => shipData?.hasHangar ?? false;
        public GameObject ParkedPrefab => shipData?.parkedPrefab;

        // Properties - instance state
        public string VesselId => vesselId;
        public Team Team => team;
        public Arena.Arena InteriorArena => interiorArena;
        public float Heading => transform.eulerAngles.z;
        public Vector2 Velocity => rb != null ? rb.linearVelocity : Vector2.zero;
        public float Speed => Velocity.magnitude;
        public float CurrentHull => currentHull;
        public float CurrentShields => currentShields;
        public float HullPercent => MaxHull > 0 ? currentHull / MaxHull : 0f;
        public float ShieldsPercent => MaxShields > 0 ? currentShields / MaxShields : 0f;
        public bool IsDestroyed => isDestroyed;
        public bool HasInterior => interiorArena != null || (shipData?.HasInterior ?? false);
        public ShipState State => shipState;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f; // No gravity in space

            // Load ship data
            LoadShipData();

            if (string.IsNullOrEmpty(vesselId))
            {
                vesselId = $"{shipTypeId}_{System.Guid.NewGuid().ToString().Substring(0, 8)}";
            }

            currentHull = MaxHull;
            currentShields = MaxShields;
        }

        /// <summary>
        /// Load ship data from DataLoader.
        /// </summary>
        private void LoadShipData()
        {
            if (string.IsNullOrEmpty(shipTypeId))
            {
                Debug.LogWarning($"[SpaceVessel] No shipTypeId set on {gameObject.name}");
                return;
            }

            shipData = DataLoader.GetShip(shipTypeId);
            if (shipData == null)
            {
                Debug.LogWarning($"[SpaceVessel] Unknown ship type: {shipTypeId}");
            }
            else
            {
                Debug.Log($"[SpaceVessel] Loaded ship data for {shipTypeId}: {shipData.displayName}");
            }
        }

        /// <summary>
        /// Initialize this vessel with state from a parked ship.
        /// </summary>
        public void Initialize(ShipState state)
        {
            if (state == null)
            {
                shipState = new ShipState(shipTypeId);
                return;
            }

            shipState = state;

            // Update ship type if state has a different one
            if (!string.IsNullOrEmpty(state.ShipTypeId) && state.ShipTypeId != shipTypeId)
            {
                shipTypeId = state.ShipTypeId;
                LoadShipData();
            }

            // Apply state to vessel
            currentHull = state.CurrentHull;
            currentShields = state.CurrentShields;

            if (!string.IsNullOrEmpty(state.ShipId))
            {
                vesselId = state.ShipId;
            }

            Debug.Log($"[SpaceVessel] Initialized with state - Pilot: {state.Pilot?.FullName ?? "None"}, Hull: {currentHull}/{MaxHull}");
        }

        /// <summary>
        /// Get current state for transfer to parked ship.
        /// </summary>
        public ShipState GetCurrentState()
        {
            if (shipState == null)
            {
                shipState = new ShipState(shipTypeId);
            }

            // Update state from vessel
            shipState.ShipId = vesselId;
            shipState.ShipTypeId = shipTypeId;
            shipState.CurrentHull = currentHull;
            shipState.CurrentShields = currentShields;

            return shipState;
        }

        private void Start()
        {
            // Register with SpaceManager
            if (SpaceManager.Instance != null)
            {
                SpaceManager.Instance.RegisterVessel(this);
            }
        }

        private void OnDestroy()
        {
            if (SpaceManager.Instance != null)
            {
                SpaceManager.Instance.UnregisterVessel(this);
            }
        }

        private void FixedUpdate()
        {
            if (isDestroyed) return;

            UpdateMovement();
        }

        #region Movement

        private void UpdateMovement()
        {
            float maxSpeed = MaxSpeed;
            float acceleration = Acceleration;
            float turnRate = TurnRate;

            // Turn toward target heading
            float currentHeading = transform.eulerAngles.z;
            float headingDiff = Mathf.DeltaAngle(currentHeading, targetHeading);
            float turnAmount = Mathf.Clamp(headingDiff, -turnRate * Time.fixedDeltaTime, turnRate * Time.fixedDeltaTime);
            transform.Rotate(0, 0, turnAmount);

            // Accelerate toward target velocity
            Vector2 currentVel = rb.linearVelocity;
            Vector2 velDiff = targetVelocity - currentVel;
            Vector2 accelVector = velDiff.normalized * acceleration * Time.fixedDeltaTime;

            if (accelVector.magnitude > velDiff.magnitude)
            {
                rb.linearVelocity = targetVelocity;
            }
            else
            {
                rb.linearVelocity = currentVel + accelVector;
            }

            // Clamp to max speed
            if (rb.linearVelocity.magnitude > maxSpeed)
            {
                rb.linearVelocity = rb.linearVelocity.normalized * maxSpeed;
            }
        }

        /// <summary>
        /// Set target heading in degrees.
        /// </summary>
        public void SetHeading(float heading)
        {
            targetHeading = heading;
        }

        /// <summary>
        /// Set target velocity.
        /// </summary>
        public void SetVelocity(Vector2 velocity)
        {
            targetVelocity = Vector2.ClampMagnitude(velocity, MaxSpeed);
        }

        /// <summary>
        /// Move toward a position.
        /// </summary>
        public void MoveToward(Vector2 targetPosition)
        {
            Vector2 direction = (targetPosition - (Vector2)transform.position).normalized;
            targetHeading = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            targetVelocity = direction * MaxSpeed;
        }

        /// <summary>
        /// Stop the vessel.
        /// </summary>
        public void Stop()
        {
            targetVelocity = Vector2.zero;
        }

        #endregion

        #region Combat

        /// <summary>
        /// Take damage from an attack.
        /// Shields absorb damage first, then hull.
        /// </summary>
        public void TakeDamage(float damage, DamageType damageType = DamageType.Physical)
        {
            if (isDestroyed) return;

            // Shields first
            if (currentShields > 0)
            {
                float shieldDamage = Mathf.Min(currentShields, damage);
                currentShields -= shieldDamage;
                damage -= shieldDamage;
                OnShieldsChanged?.Invoke(currentShields, MaxShields);
            }

            // Remaining damage to hull
            if (damage > 0)
            {
                currentHull -= damage;
                OnHullChanged?.Invoke(currentHull, MaxHull);

                // Notify interior arena of impact
                if (interiorArena != null)
                {
                    // TODO: Apply camera shake, potential hull breach
                }

                if (currentHull <= 0)
                {
                    Destroy();
                }
            }
        }

        /// <summary>
        /// Destroy this vessel.
        /// </summary>
        public void Destroy()
        {
            if (isDestroyed) return;

            isDestroyed = true;
            currentHull = 0;

            Debug.Log($"[SpaceVessel] Vessel '{vesselId}' destroyed!");
            OnDestroyed?.Invoke(this);

            // TODO: Spawn explosion effects
            // TODO: Handle crew if interior exists

            Destroy(gameObject);
        }

        /// <summary>
        /// Repair hull damage.
        /// </summary>
        public void RepairHull(float amount)
        {
            currentHull = Mathf.Min(currentHull + amount, MaxHull);
            OnHullChanged?.Invoke(currentHull, MaxHull);
        }

        /// <summary>
        /// Recharge shields.
        /// </summary>
        public void RechargeShields(float amount)
        {
            currentShields = Mathf.Min(currentShields + amount, MaxShields);
            OnShieldsChanged?.Invoke(currentShields, MaxShields);
        }

        #endregion

        #region Interior

        /// <summary>
        /// Set the interior arena for this vessel.
        /// </summary>
        public void SetInteriorArena(Arena.Arena arena)
        {
            interiorArena = arena;
        }

        /// <summary>
        /// Apply an impact effect to the interior arena.
        /// </summary>
        public void ApplyInteriorImpact(Vector2 impactPoint, float intensity)
        {
            if (interiorArena == null) return;

            // Camera shake
            if (CameraManager.Instance != null)
            {
                CameraManager.Instance.ShakeCamera(CameraManager.Instance.ArenaCamera, intensity * 0.1f, 0.3f);
            }

            // TODO: Hull breach at impact location
        }

        #endregion

        #region Hangar Operations

        // Cached hangar exits (found on first query)
        private HangarExit[] hangarExits;

        /// <summary>
        /// Get all hangar exits on this vessel.
        /// </summary>
        public HangarExit[] GetHangarExits()
        {
            if (hangarExits == null)
            {
                hangarExits = GetComponentsInChildren<HangarExit>();
            }
            return hangarExits;
        }

        /// <summary>
        /// Clear cached hangar exits (call after adding/removing HangarExit components at runtime).
        /// </summary>
        public void RefreshHangarExits()
        {
            hangarExits = null;
        }

        /// <summary>
        /// Get a hangar exit by its ID.
        /// </summary>
        public HangarExit GetHangarExit(string exitId)
        {
            foreach (var exit in GetHangarExits())
            {
                if (exit.ExitId == exitId)
                    return exit;
            }
            return null;
        }

        /// <summary>
        /// Check if this vessel has a hangar with the given exit ID.
        /// </summary>
        public bool HasHangar(string exitId)
        {
            return GetHangarExit(exitId) != null;
        }

        /// <summary>
        /// Request docking at a hangar. Returns the HangarExit if docking is allowed, null otherwise.
        /// </summary>
        public HangarExit RequestDocking(string exitId)
        {
            // Check if we have this hangar exit
            var exit = GetHangarExit(exitId);
            if (exit == null)
            {
                Debug.Log($"[SpaceVessel] Docking denied: No hangar exit '{exitId}'");
                return null;
            }

            // Check if interior arena has available slots
            if (interiorArena == null)
            {
                Debug.Log($"[SpaceVessel] Docking denied: No interior arena");
                return null;
            }

            if (!interiorArena.HasAvailableHangarSlot(exitId))
            {
                Debug.Log($"[SpaceVessel] Docking denied: No available landing slots for '{exitId}'");
                return null;
            }

            Debug.Log($"[SpaceVessel] Docking approved at '{exitId}'");
            return exit;
        }

        /// <summary>
        /// Get the HangarEntrance in the interior arena that matches an exit ID.
        /// </summary>
        public Arena.HangarEntrance GetHangarEntrance(string exitId)
        {
            if (interiorArena == null) return null;
            return interiorArena.GetHangarEntrance(exitId);
        }

        #endregion

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Draw heading
            Gizmos.color = Color.green;
            Vector2 forward = transform.up;
            Gizmos.DrawRay(transform.position, forward * 3f);

            // Draw velocity
            if (Application.isPlaying && rb != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawRay(transform.position, rb.linearVelocity * 0.5f);
            }
        }
#endif
    }
}
