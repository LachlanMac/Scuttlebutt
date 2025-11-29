using UnityEngine;
using Starbelter.Core;
using Starbelter.Pathfinding;
using Starbelter.AI;

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

        [Header("Cover")]
        [Tooltip("Cover only blocks if an enemy is within this distance of it")]
        [SerializeField] private float coverEffectiveRadius = 2f;

        private Rigidbody2D rb;
        private Vector2 direction;
        private Vector2 origin;
        private GameObject sourceUnit;

        public float Damage => damage;
        public DamageType DamageType => damageType;
        public Team SourceTeam => sourceTeam;
        public Vector2 Direction => direction;
        public Vector2 Origin => origin;
        public GameObject SourceUnit => sourceUnit;

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
        /// <param name="source">The unit that fired this projectile (optional)</param>
        public void Fire(Vector2 fireDirection, Team team, GameObject source = null)
        {
            direction = fireDirection.normalized;
            sourceTeam = team;
            sourceUnit = source;
            origin = transform.position;
            rb.linearVelocity = direction * speed;

            // Rotate so sprite's "up" faces the fire direction
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            transform.rotation = Quaternion.Euler(0, 0, angle);
        }

        /// <summary>
        /// Initialize and fire the projectile with custom damage.
        /// </summary>
        public void Fire(Vector2 fireDirection, Team team, float customDamage, GameObject source = null)
        {
            damage = customDamage;
            Fire(fireDirection, team, source);
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
                // Cover only matters if an enemy is using it (standing near it)
                // This handles: shooter's own cover, no-man's-land cover, and defender's cover
                if (!IsEnemyNearCover(structure.transform.position))
                {
                    return; // No enemy using this cover - pass through
                }

                // Apply suppression to enemies near this cover
                ApplySuppressionNearCover(structure.transform.position);

                // Enemy is using this cover - let structure decide if it blocks
                if (structure.TryBlockProjectile(this))
                {
                    if (hitEffectPrefab != null)
                    {
                        Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
                    }
                    Destroy(gameObject);
                }
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

                bool wasHit = unitHealth.TryApplyDamage(damage, damageType, origin, direction, sourceUnit);

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

        /// <summary>
        /// Check if any enemy unit is near this cover position (actually using it).
        /// </summary>
        private bool IsEnemyNearCover(Vector2 coverPosition)
        {
            var colliders = Physics2D.OverlapCircleAll(coverPosition, coverEffectiveRadius);

            foreach (var col in colliders)
            {
                var targetable = col.GetComponentInParent<ITargetable>();
                if (targetable != null && targetable.Team != sourceTeam && !targetable.IsDead)
                {
                    return true; // Enemy is using this cover
                }
            }

            return false;
        }

        /// <summary>
        /// Apply suppression to any enemy units near this cover.
        /// </summary>
        private void ApplySuppressionNearCover(Vector2 coverPosition)
        {
            var colliders = Physics2D.OverlapCircleAll(coverPosition, coverEffectiveRadius);

            foreach (var col in colliders)
            {
                var unitController = col.GetComponentInParent<AI.UnitController>();
                if (unitController != null && unitController.Team != sourceTeam)
                {
                    unitController.ApplySuppression();
                }
            }
        }
    }
}
