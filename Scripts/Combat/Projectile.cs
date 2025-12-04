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
        [SerializeField] private float coverEffectiveRadius = 2.5f;

        private Rigidbody2D rb;
        private Vector2 direction;
        private Vector2 origin;
        private GameObject sourceUnit;
        private bool isAimedShot;

        // Tile threat tracking
        private Vector3 lastPosition;

        public float Damage => damage;
        public DamageType DamageType => damageType;
        public Team SourceTeam => sourceTeam;
        public Vector2 Direction => direction;
        public Vector2 Origin => origin;
        public GameObject SourceUnit => sourceUnit;
        public bool IsAimedShot => isAimedShot;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            // Set origin immediately in case collision happens before Fire() is called
            origin = transform.position;
            lastPosition = transform.position;
        }

        private void Start()
        {
            Destroy(gameObject, lifetime);
            SyncTrailColor();
        }

        /// <summary>
        /// Sync TrailRenderer color with SpriteRenderer color.
        /// </summary>
        private void SyncTrailColor()
        {
            var sr = GetComponent<SpriteRenderer>();
            var trail = GetComponent<TrailRenderer>();
            if (sr != null && trail != null)
            {
                trail.startColor = sr.color;
                trail.endColor = sr.color;
            }
        }

        private void Update()
        {
            // Report tile crossings to threat map
            if (TileThreatMap.Instance != null)
            {
                Vector3 currentPosition = transform.position;
                if (currentPosition != lastPosition)
                {
                    TileThreatMap.Instance.AddThreatAlongPath(lastPosition, currentPosition, damage, sourceTeam);
                    lastPosition = currentPosition;
                }
            }
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
            lastPosition = transform.position; // Reset for threat tracking
            rb.linearVelocity = direction * speed;

            // Rotate so sprite's "up" faces the fire direction
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            transform.rotation = Quaternion.Euler(0, 0, angle);

            var collider = GetComponent<Collider2D>();
            Debug.Log($"[Projectile] Fired - Layer: {LayerMask.LayerToName(gameObject.layer)}, Collider: {collider?.GetType().Name ?? "null"}, IsTrigger: {collider?.isTrigger}, Team: {team}, Source: {source?.name ?? "null"}");
        }

        /// <summary>
        /// Initialize and fire the projectile with custom damage.
        /// </summary>
        public void Fire(Vector2 fireDirection, Team team, float customDamage, GameObject source = null)
        {
            damage = customDamage;
            Fire(fireDirection, team, source);
        }

        /// <summary>
        /// Mark this projectile as an aimed shot (from extended aim).
        /// Aimed shots halve the target's cover dodge bonus.
        /// </summary>
        public void SetAimedShot(bool aimed)
        {
            isAimedShot = aimed;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            // Don't hit perception detection colliders
            if (other.GetComponent<PerceptionManager>() != null)
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

                bool wasHit = unitHealth.TryApplyDamage(damage, damageType, origin, direction, sourceUnit, isAimedShot);

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
                // Skip PerceptionManager colliders - they're huge detection zones, not units
                if (col.GetComponent<PerceptionManager>() != null) continue;

                var targetable = col.GetComponentInParent<ITargetable>();
                if (targetable != null && targetable.Team != sourceTeam && !targetable.IsDead)
                {
                    return true; // Enemy is using this cover
                }
            }

            return false;
        }

    }
}
