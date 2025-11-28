using UnityEngine;
using Starbelter.Core;

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
                // Ask structure if we're blocked
                if (structure.TryBlockProjectile(this))
                {
                    // Blocked - spawn hit effect and destroy projectile
                    if (hitEffectPrefab != null)
                    {
                        Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
                    }
                    Destroy(gameObject);
                }
                // Not blocked - continue through
                return;
            }

            // Check if we hit a unit with health
            var unitHealth = other.GetComponent<UnitHealth>();
            if (unitHealth != null)
            {
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
