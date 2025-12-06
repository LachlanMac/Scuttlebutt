using UnityEngine;
using Starbelter.Core;
using Starbelter.Arena;

namespace Starbelter.Space
{
    /// <summary>
    /// Base class for all space-capable vessels.
    /// Fighters, capital ships, dropships, etc.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class SpaceVessel : MonoBehaviour
    {
        [Header("Vessel Identity")]
        [SerializeField] private string vesselId;
        [SerializeField] private VesselType vesselType = VesselType.Fighter;
        [SerializeField] private Team team = Team.Federation;

        [Header("Movement")]
        [SerializeField] private float maxSpeed = 20f;
        [SerializeField] private float acceleration = 10f;
        [SerializeField] private float turnRate = 180f;

        [Header("Combat")]
        [SerializeField] private float maxHull = 100f;
        [SerializeField] private float maxShields = 50f;
        [SerializeField] private float currentHull;
        [SerializeField] private float currentShields;

        [Header("Interior")]
        [Tooltip("The arena representing this vessel's interior (if any)")]
        [SerializeField] private Arena.Arena interiorArena;

        // Components
        private Rigidbody2D rb;

        // Runtime state
        private float targetHeading;
        private Vector2 targetVelocity;
        private bool isDestroyed;

        // Events
        public event System.Action<SpaceVessel> OnDestroyed;
        public event System.Action<float, float> OnHullChanged; // current, max
        public event System.Action<float, float> OnShieldsChanged; // current, max

        // Properties
        public string VesselId => vesselId;
        public VesselType Type => vesselType;
        public Team Team => team;
        public Arena.Arena InteriorArena => interiorArena;
        public float Heading => transform.eulerAngles.z;
        public Vector2 Velocity => rb != null ? rb.linearVelocity : Vector2.zero;
        public float Speed => Velocity.magnitude;
        public float HullPercent => maxHull > 0 ? currentHull / maxHull : 0f;
        public float ShieldsPercent => maxShields > 0 ? currentShields / maxShields : 0f;
        public bool IsDestroyed => isDestroyed;
        public bool HasInterior => interiorArena != null;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f; // No gravity in space

            if (string.IsNullOrEmpty(vesselId))
            {
                vesselId = $"{vesselType}_{System.Guid.NewGuid().ToString().Substring(0, 8)}";
            }

            currentHull = maxHull;
            currentShields = maxShields;
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
            targetVelocity = Vector2.ClampMagnitude(velocity, maxSpeed);
        }

        /// <summary>
        /// Move toward a position.
        /// </summary>
        public void MoveToward(Vector2 targetPosition)
        {
            Vector2 direction = (targetPosition - (Vector2)transform.position).normalized;
            targetHeading = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            targetVelocity = direction * maxSpeed;
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
                OnShieldsChanged?.Invoke(currentShields, maxShields);
            }

            // Remaining damage to hull
            if (damage > 0)
            {
                currentHull -= damage;
                OnHullChanged?.Invoke(currentHull, maxHull);

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
            currentHull = Mathf.Min(currentHull + amount, maxHull);
            OnHullChanged?.Invoke(currentHull, maxHull);
        }

        /// <summary>
        /// Recharge shields.
        /// </summary>
        public void RechargeShields(float amount)
        {
            currentShields = Mathf.Min(currentShields + amount, maxShields);
            OnShieldsChanged?.Invoke(currentShields, maxShields);
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

    public enum VesselType
    {
        Fighter,
        Bomber,
        Dropship,
        Frigate,
        Cruiser,
        Carrier,
        Station
    }
}
