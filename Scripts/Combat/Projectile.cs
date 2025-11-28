using UnityEngine;
using Starbelter.Core;
using Starbelter.Pathfinding;

namespace Starbelter.Combat
{
    /// <summary>
    /// Base projectile class. Attach to any projectile prefab.
    /// Must be tagged as "Projectile" for threat detection.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    public class Projectile : MonoBehaviour
    {
        [Header("Projectile Settings")]
        [SerializeField] private float damage = 10f;
        [SerializeField] private DamageType damageType = DamageType.Physical;
        [SerializeField] private float speed = 20f;
        [SerializeField] private float lifetime = 5f;

        [Header("Effects")]
        [Tooltip("Spawned on hit (particle system, sound, etc.)")]
        [SerializeField] private GameObject hitEffectPrefab;

        [Header("Team")]
        [SerializeField] private Team sourceTeam = Team.Neutral;

        [Header("Cover Ignore")]
        [Tooltip("Projectile ignores structures within this distance of spawn point")]
        [SerializeField] private float ignoreStructureRadius = 1.5f;

        private Rigidbody2D rb;
        private Vector2 direction;
        private Vector2 origin;

        public float Damage => damage;
        public DamageType DamageType => damageType;
        public Team SourceTeam => sourceTeam;
        public Vector2 Direction => direction;
        public Vector2 Origin => origin;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            // Set origin immediately in case collision happens before Fire() is called
            origin = transform.position;
        }

        private void Start()
        {
            Destroy(gameObject, lifetime);
        }

        /// <summary>
        /// Initialize and fire the projectile.
        /// </summary>
        /// <param name="fireDirection">Normalized direction to travel</param>
        /// <param name="team">Team that fired this projectile</param>
        public void Fire(Vector2 fireDirection, Team team)
        {
            direction = fireDirection.normalized;
            sourceTeam = team;
            origin = transform.position;
            rb.linearVelocity = direction * speed;

            // Rotate so sprite's "up" faces the fire direction
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            transform.rotation = Quaternion.Euler(0, 0, angle);
        }

        /// <summary>
        /// Initialize and fire the projectile with custom damage.
        /// </summary>
        public void Fire(Vector2 fireDirection, Team team, float customDamage)
        {
            damage = customDamage;
            Fire(fireDirection, team);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            // Don't hit threat detection colliders
            if (other.GetComponent<ThreatManager>() != null)
            {
                return;
            }

            // Check if we hit a Structure (cover)
            var structure = other.GetComponent<Structure>();
            if (structure != null)
            {
                // Ignore HALF cover close to spawn point (shooter peeking over their cover)
                // Full cover always blocks, even at close range
                float distFromOrigin = Vector2.Distance(origin, transform.position);
                if (distFromOrigin < ignoreStructureRadius && structure.CoverType == CoverType.Half)
                {
                    return; // Pass through half cover - too close to shooter
                }

                // Ask structure if we're blocked
                if (structure.TryBlockProjectile(this))
                {
                    // Blocked - spawn hit effect and destroy projectile
                    Debug.Log($"[Projectile] Blocked by structure {structure.name} ({structure.CoverType}) at {transform.position}, origin was {origin}");
                    if (hitEffectPrefab != null)
                    {
                        Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
                    }
                    Destroy(gameObject);
                }
                // Not blocked - continue through (half cover has % chance)
                return;
            }

            // Check if we hit a unit with health
            var unitHealth = other.GetComponent<UnitHealth>();
            if (unitHealth != null)
            {
                // Check for friendly fire - get team from ITargetable on parent
                var targetable = other.GetComponentInParent<ITargetable>();
                if (targetable != null && targetable.Team == sourceTeam)
                {
                    return; // Ignore same-team hits (no friendly fire)
                }

                bool wasHit = unitHealth.TryApplyDamage(damage, damageType, origin, direction);

                if (wasHit)
                {
                    // Hit confirmed - spawn effect and destroy projectile
                    if (hitEffectPrefab != null)
                    {
                        Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
                    }
                    Destroy(gameObject);
                }
                // If dodged, projectile continues through (UnitHealth spawns dodge effect)
                return;
            }

            // Handle hit logic for other objects
            if (!other.isTrigger)
            {
                OnHit(other);
            }
        }

        protected virtual void OnHit(Collider2D hitCollider)
        {
            // Spawn hit effect
            if (hitEffectPrefab != null)
            {
                Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
            }

            // Override in subclasses for specific hit behavior
            Destroy(gameObject);
        }
    }
}
