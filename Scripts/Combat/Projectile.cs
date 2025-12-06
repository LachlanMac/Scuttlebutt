using UnityEngine;
using System.Text;
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
        private float coverPenetration = 1.0f;  // 1.0 = normal, <1.0 = penetrates cover, >1.0 = cover more effective

        // Tile threat tracking
        private Vector3 lastPosition;

        // Shot report tracking
        private ShotType shotType = ShotType.Snap;
        private float accuracy = 0.7f;
        private StringBuilder coverReport = new StringBuilder();
        private string finalResult = "EXPIRED";
        private string targetName = "";
        private bool reportLogged = false;

        public float Damage => damage;
        public DamageType DamageType => damageType;
        public Team SourceTeam => sourceTeam;
        public Vector2 Direction => direction;
        public Vector2 Origin => origin;
        public GameObject SourceUnit => sourceUnit;
        public float CoverPenetration => coverPenetration;

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
        /// Set the cover penetration for this projectile.
        /// Values less than 1.0 penetrate cover better (aimed shots).
        /// Values greater than 1.0 are worse against cover (suppression, burst).
        /// </summary>
        public void SetCoverPenetration(float penetration)
        {
            coverPenetration = penetration;
        }

        /// <summary>
        /// Set shot info for the consolidated report.
        /// </summary>
        public void SetShotInfo(ShotType type, float acc)
        {
            shotType = type;
            accuracy = acc;
        }

        /// <summary>
        /// Record a cover encounter for the report.
        /// </summary>
        public void RecordCoverEncounter(string coverName, bool blocked, float blockChance)
        {
            if (coverReport.Length > 0) coverReport.Append(", ");
            coverReport.Append($"{coverName}({blockChance:P0}->{(blocked ? "BLOCKED" : "passed")})");
        }

        /// <summary>
        /// Record the final result (hit, dodged, blocked, etc.)
        /// </summary>
        public void RecordResult(string result, string target = "")
        {
            finalResult = result;
            if (!string.IsNullOrEmpty(target)) targetName = target;
        }

        private void OnDestroy()
        {
            LogShotReport();
        }

        /// <summary>
        /// Log consolidated shot report.
        /// Format: [SHOT] Shooter -> Target | TYPE (acc%) | Cover: info | RESULT
        /// </summary>
        private void LogShotReport()
        {
            if (reportLogged) return;
            reportLogged = true;

            string shooterName = sourceUnit != null ? sourceUnit.name : "Unknown";
            string targetDisplay = string.IsNullOrEmpty(targetName) ? "?" : targetName;
            string coverInfo = coverReport.Length > 0 ? coverReport.ToString() : "none";

            Debug.Log($"[SHOT] {shooterName} -> {targetDisplay} | {shotType} ({accuracy:P0} acc, {coverPenetration:F2} pen) | Cover: {coverInfo} | {finalResult}");
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
                if (!IsEnemyNearCover(structure.transform.position))
                {
                    return; // No enemy using this cover - pass through silently
                }

                // Enemy is using this cover - let structure decide if it blocks
                if (structure.TryBlockProjectile(this))
                {
                    RecordResult("BLOCKED BY COVER");
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

                // Record target name
                string hitTargetName = other.transform.parent?.name ?? other.name;
                targetName = hitTargetName;

                bool wasHit = unitHealth.TryApplyDamage(damage, damageType, origin, direction, sourceUnit, coverPenetration);

                if (wasHit)
                {
                    RecordResult($"HIT ({damage:F0} dmg)", hitTargetName);
                    if (hitEffectPrefab != null)
                    {
                        Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
                    }
                    Destroy(gameObject);
                }
                else
                {
                    RecordResult("DODGED", hitTargetName);
                    // Projectile continues through
                }
                return;
            }

            // Handle hit logic for other objects
            if (!other.isTrigger)
            {
                RecordResult("HIT OBSTACLE");
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
