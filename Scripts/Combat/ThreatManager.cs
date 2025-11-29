using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Starbelter.Core;

namespace Starbelter.Combat
{
    /// <summary>
    /// Tracks incoming threats from projectiles using 16 directional buckets,
    /// plus individual enemy threat scores (aggro list).
    /// Attach to a child object with a trigger Circle2D collider.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class ThreatManager : MonoBehaviour
    {
        private const int BUCKET_COUNT = 16;
        private const float DEGREES_PER_BUCKET = 360f / BUCKET_COUNT; // 22.5°

        [Header("Settings")]
        [Tooltip("Time in seconds for threat to decay from max to zero")]
        [SerializeField] private float decayTime = 10f;

        [Tooltip("This unit's team - ignores projectiles from same team")]
        [SerializeField] private Team myTeam = Team.Ally;

        [Header("Debug")]
        [SerializeField] private bool showDebugGizmos = true;

        private float[] threatLevels = new float[BUCKET_COUNT];
        private float maxThreatEver = 1f; // For normalization, tracks highest threat seen

        // Individual enemy threat tracking (aggro list)
        private Dictionary<GameObject, EnemyThreatData> enemyThreats = new Dictionary<GameObject, EnemyThreatData>();

        /// <summary>
        /// Threat data for a specific enemy.
        /// </summary>
        public class EnemyThreatData
        {
            public GameObject Enemy;
            public int ShotsFiredAtMe;      // How many times they shot at me
            public float LastShotTime;       // When they last shot at me
            public float TotalDamageDealt;   // Total damage they've done to me

            public float GetThreatScore(Vector3 myPosition)
            {
                if (Enemy == null) return 0f;

                float distance = Vector3.Distance(myPosition, Enemy.transform.position);
                float distanceScore = Mathf.Max(0, 20f - distance); // Closer = more threatening

                float shotScore = ShotsFiredAtMe * 5f;
                float damageScore = TotalDamageDealt * 0.5f;

                // Recency bonus - threats from last 3 seconds are more urgent
                float recency = Time.time - LastShotTime;
                float recencyMultiplier = recency < 3f ? 2f : 1f;

                return (distanceScore + shotScore + damageScore) * recencyMultiplier;
            }
        }

        public Team MyTeam
        {
            get => myTeam;
            set => myTeam = value;
        }

        private void Awake()
        {
            // Ensure collider is a trigger
            var collider = GetComponent<Collider2D>();
            if (!collider.isTrigger)
            {
                collider.isTrigger = true;
            }
        }

        private void Update()
        {
            DecayThreats();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            // Check if it's a projectile
            var projectile = other.GetComponent<Projectile>();
            if (projectile == null) return;

            // Ignore friendly fire
            if (projectile.SourceTeam == myTeam) return;

            // Ignore neutral projectiles (or handle differently if needed)
            if (projectile.SourceTeam == Team.Neutral) return;

            // Calculate direction FROM us TO the projectile (where the threat is coming FROM)
            Vector2 threatDirection = (other.transform.position - transform.position).normalized;

            RegisterThreat(threatDirection, projectile.Damage);

            // Track who shot at us
            if (projectile.SourceUnit != null)
            {
                RegisterEnemyShot(projectile.SourceUnit, 0f); // 0 damage since it hasn't hit yet
            }
        }

        /// <summary>
        /// Manually register a threat from a direction.
        /// </summary>
        /// <param name="direction">Direction the threat is coming FROM (toward us)</param>
        /// <param name="amount">Threat amount (typically damage)</param>
        public void RegisterThreat(Vector2 direction, float amount)
        {
            int bucket = DirectionToBucket(direction);
            threatLevels[bucket] += amount;

            // Track max for debug visualization
            if (threatLevels[bucket] > maxThreatEver)
            {
                maxThreatEver = threatLevels[bucket];
            }
        }

        /// <summary>
        /// Gets the direction with the highest threat level.
        /// </summary>
        /// <returns>Direction vector, or null if no active threats</returns>
        public Vector2? GetHighestThreatDirection()
        {
            int highestBucket = -1;
            float highestThreat = 0f;

            for (int i = 0; i < BUCKET_COUNT; i++)
            {
                if (threatLevels[i] > highestThreat)
                {
                    highestThreat = threatLevels[i];
                    highestBucket = i;
                }
            }

            if (highestBucket < 0 || highestThreat <= 0.01f)
            {
                return null;
            }

            return BucketToDirection(highestBucket);
        }

        /// <summary>
        /// Gets all active threat directions above a threshold.
        /// </summary>
        /// <param name="threshold">Minimum threat level to include</param>
        /// <returns>List of ThreatInfo sorted by threat level (highest first)</returns>
        public List<ThreatInfo> GetActiveThreats(float threshold = 0.1f)
        {
            var threats = new List<ThreatInfo>();

            for (int i = 0; i < BUCKET_COUNT; i++)
            {
                if (threatLevels[i] > threshold)
                {
                    threats.Add(new ThreatInfo
                    {
                        Direction = BucketToDirection(i),
                        ThreatLevel = threatLevels[i],
                        BucketIndex = i
                    });
                }
            }

            // Sort by threat level descending
            threats.Sort((a, b) => b.ThreatLevel.CompareTo(a.ThreatLevel));

            return threats;
        }

        /// <summary>
        /// Gets the threat level from a specific direction.
        /// </summary>
        public float GetThreatLevel(Vector2 direction)
        {
            int bucket = DirectionToBucket(direction);
            return threatLevels[bucket];
        }

        /// <summary>
        /// Gets the raw threat level for a specific bucket index.
        /// </summary>
        public float GetThreatLevelByBucket(int bucketIndex)
        {
            if (bucketIndex < 0 || bucketIndex >= BUCKET_COUNT)
                return 0f;
            return threatLevels[bucketIndex];
        }

        /// <summary>
        /// Returns true if any threat bucket is above the threshold.
        /// </summary>
        public bool IsUnderFire(float threshold = 0.1f)
        {
            for (int i = 0; i < BUCKET_COUNT; i++)
            {
                if (threatLevels[i] > threshold)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the total accumulated threat from all directions.
        /// </summary>
        public float GetTotalThreat()
        {
            float total = 0f;
            for (int i = 0; i < BUCKET_COUNT; i++)
            {
                total += threatLevels[i];
            }
            return total;
        }

        /// <summary>
        /// Clears all threat data.
        /// </summary>
        public void ClearThreats()
        {
            for (int i = 0; i < BUCKET_COUNT; i++)
            {
                threatLevels[i] = 0f;
            }
            enemyThreats.Clear();
        }

        /// <summary>
        /// Reset threat levels to a minimum value (not zero).
        /// Used when repositioning - old threats are stale but we want to stay "engaged".
        /// </summary>
        public void ResetThreats(float minimumValue = 0.01f)
        {
            for (int i = 0; i < BUCKET_COUNT; i++)
            {
                threatLevels[i] = minimumValue;
            }
            // Keep enemy tracking but reset their shot counts
            foreach (var data in enemyThreats.Values)
            {
                data.ShotsFiredAtMe = 0;
                data.TotalDamageDealt = 0f;
            }
        }

        /// <summary>
        /// Register threat from a visible enemy (even before they shoot).
        /// Call this when an enemy is spotted in weapon range.
        /// </summary>
        public void RegisterVisibleEnemy(Vector2 enemyPosition, float threatAmount = 1f)
        {
            Vector2 direction = (enemyPosition - (Vector2)transform.position).normalized;
            RegisterThreat(direction, threatAmount);
        }

        /// <summary>
        /// Register that a specific enemy shot at us.
        /// Called when a projectile from this enemy enters our threat zone or hits us.
        /// </summary>
        public void RegisterEnemyShot(GameObject enemy, float damage = 0f)
        {
            if (enemy == null) return;

            // Clean up dead entries
            CleanupDeadEnemies();

            if (!enemyThreats.TryGetValue(enemy, out var data))
            {
                data = new EnemyThreatData { Enemy = enemy };
                enemyThreats[enemy] = data;
            }

            data.ShotsFiredAtMe++;
            data.LastShotTime = Time.time;
            data.TotalDamageDealt += damage;
        }

        /// <summary>
        /// Get the most dangerous enemies sorted by threat score.
        /// </summary>
        public List<GameObject> GetMostDangerousEnemies(int maxCount = 3)
        {
            CleanupDeadEnemies();

            return enemyThreats.Values
                .OrderByDescending(d => d.GetThreatScore(transform.position))
                .Take(maxCount)
                .Select(d => d.Enemy)
                .Where(e => e != null)
                .ToList();
        }

        /// <summary>
        /// Get threat score for a specific enemy.
        /// </summary>
        public float GetEnemyThreatScore(GameObject enemy)
        {
            if (enemy == null) return 0f;
            if (enemyThreats.TryGetValue(enemy, out var data))
            {
                return data.GetThreatScore(transform.position);
            }
            return 0f;
        }

        /// <summary>
        /// Remove dead or destroyed enemies from tracking.
        /// </summary>
        private void CleanupDeadEnemies()
        {
            var deadEnemies = enemyThreats.Keys
                .Where(e => e == null || !e.activeInHierarchy)
                .ToList();

            foreach (var dead in deadEnemies)
            {
                enemyThreats.Remove(dead);
            }

            // Also check ITargetable.IsDead
            var deadTargetables = enemyThreats.Keys
                .Where(e => {
                    var t = e.GetComponent<ITargetable>();
                    return t != null && t.IsDead;
                })
                .ToList();

            foreach (var dead in deadTargetables)
            {
                enemyThreats.Remove(dead);
            }
        }

        private void DecayThreats()
        {
            // Decay rate: full threat decays to 0 over decayTime seconds
            float decayAmount = (maxThreatEver / decayTime) * Time.deltaTime;

            for (int i = 0; i < BUCKET_COUNT; i++)
            {
                if (threatLevels[i] > 0)
                {
                    threatLevels[i] = Mathf.Max(0f, threatLevels[i] - decayAmount);
                }
            }
        }

        private int DirectionToBucket(Vector2 direction)
        {
            // Convert direction to angle (0-360)
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            if (angle < 0) angle += 360f;

            // Convert to bucket index
            int bucket = Mathf.FloorToInt(angle / DEGREES_PER_BUCKET) % BUCKET_COUNT;
            return bucket;
        }

        private Vector2 BucketToDirection(int bucket)
        {
            // Get center angle of bucket
            float angle = (bucket * DEGREES_PER_BUCKET + DEGREES_PER_BUCKET / 2f) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showDebugGizmos || !Application.isPlaying) return;

            Vector3 center = transform.position;
            float maxDisplayRadius = 2f;

            for (int i = 0; i < BUCKET_COUNT; i++)
            {
                if (threatLevels[i] <= 0.01f) continue;

                Vector2 dir = BucketToDirection(i);
                float normalizedThreat = Mathf.Clamp01(threatLevels[i] / Mathf.Max(maxThreatEver, 1f));

                // Color: green (low) -> yellow -> red (high)
                Gizmos.color = Color.Lerp(Color.green, Color.red, normalizedThreat);

                // Draw line showing threat direction and intensity
                float lineLength = normalizedThreat * maxDisplayRadius;
                Vector3 endPoint = center + new Vector3(dir.x, dir.y, 0) * lineLength;
                Gizmos.DrawLine(center, endPoint);

                // Draw small sphere at end
                Gizmos.DrawWireSphere(endPoint, 0.1f);
            }
        }
#endif
    }

    public struct ThreatInfo
    {
        public Vector2 Direction;
        public float ThreatLevel;
        public int BucketIndex;
    }
}
