using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Starbelter.Core;
using Starbelter.AI;
using Starbelter.Pathfinding;

namespace Starbelter.Combat
{
    /// <summary>
    /// Unified perception and threat tracking system.
    /// Handles vision checks, threat awareness, and maintains knowledge of enemies.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class PerceptionManager : MonoBehaviour
    {
        #region Settings

        [Header("Perception Settings")]
        [Tooltip("Maximum distance to detect enemies")]
        [SerializeField] private float perceptionRange = 15f;

        [Tooltip("How often to run perception checks (seconds)")]
        [SerializeField] private float checkInterval = 1f;

        [Tooltip("How long to remember an enemy after losing sight")]
        [SerializeField] private float memoryDuration = 3f;

        [Tooltip("This unit's team")]
        [SerializeField] private Team myTeam = Team.Federation;

        [Header("Threat Settings")]
        [Tooltip("Time in seconds for threat to decay from max to zero")]
        [SerializeField] private float decayTime = 20f;

        [Tooltip("Time in seconds for aimed shot threat to decay by 1 point")]
        [SerializeField] private float aimedShotDecayTime = 10f;

        [Header("Debug")]
        [SerializeField] private bool showDebugGizmos = true;

        #endregion

        #region Private State

        // Character stats reference
        private Character character;


        // Perceived units dictionary
        private Dictionary<GameObject, PerceivedUnit> perceivedUnits = new Dictionary<GameObject, PerceivedUnit>();

        // Directional threat buckets (for quick threat direction queries)
        private const int BUCKET_COUNT = 16;
        private const float DEGREES_PER_BUCKET = 360f / BUCKET_COUNT;
        private float[] threatBuckets = new float[BUCKET_COUNT];
        private float maxThreatEver = 1f;

        private float checkTimer;

        #endregion

        #region Events

        /// <summary>
        /// Fired when a new enemy is perceived for the first time.
        /// </summary>
        public event System.Action<PerceivedUnit> OnNewContact;

        /// <summary>
        /// Fired when an enemy is no longer perceived (forgotten or dead).
        /// </summary>
        public event System.Action<PerceivedUnit> OnContactLost;

        /// <summary>
        /// Fired when a perceived enemy's awareness level changes.
        /// </summary>
        public event System.Action<PerceivedUnit> OnContactUpdated;

        #endregion

        #region Public Properties

        public Team MyTeam
        {
            get => myTeam;
            set => myTeam = value;
        }

        public float PerceptionRange => perceptionRange;

        #endregion

        #region Initialization

        public void SetCharacter(Character c) => character = c;

        private void Awake()
        {
            // Ensure collider is a trigger (for projectile detection)
            var collider = GetComponent<Collider2D>();
            if (!collider.isTrigger)
            {
                collider.isTrigger = true;
            }
        }

        #endregion

        #region Update Loop

        private void Update()
        {
            // Periodic perception checks
            checkTimer -= Time.deltaTime;
            if (checkTimer <= 0f)
            {
                checkTimer = checkInterval;
                PerformPerceptionCheck();
            }

            // Decay threat buckets
            DecayThreats();

            // Decay aimed shot threat on all contacts
            DecayAimedShotThreats();

            // Clean up stale/dead contacts
            CleanupStaleContacts();
        }

        /// <summary>
        /// Detect projectiles entering threat range.
        /// </summary>
        private void OnTriggerEnter2D(Collider2D other)
        {
            var projectile = other.GetComponent<Projectile>();
            if (projectile == null) return;

            // Ignore friendly fire
            if (projectile.SourceTeam == myTeam) return;

            // Ignore neutral projectiles
            if (projectile.SourceTeam == Team.Neutral) return;

            // Register directional threat from projectile origin
            Vector2 threatDirection = (projectile.Origin - (Vector2)transform.position).normalized;
            RegisterDirectionalThreat(threatDirection, projectile.Damage);
            Debug.Log($"[{transform.root.name}] Threat registered: {projectile.Damage} from {threatDirection}{(projectile.IsAimedShot ? " [AIMED]" : "")}");

            // Track who shot at us - instant Confirmed awareness
            if (projectile.SourceUnit != null)
            {
                RegisterEnemyShot(projectile.SourceUnit, 0f, projectile.IsAimedShot);
            }
        }

        #endregion

        #region Perception

        private void PerformPerceptionCheck()
        {
            var colliders = Physics2D.OverlapCircleAll(transform.position, perceptionRange);

            foreach (var col in colliders)
            {
                // Skip non-units
                var targetable = col.GetComponent<ITargetable>();
                if (targetable == null) continue;

                // Skip same team
                if (targetable.Team == myTeam) continue;

                // Skip dead
                if (targetable.IsDead) continue;

                // Skip PerceptionManager colliders (they're detection zones, not units)
                if (col.GetComponent<PerceptionManager>() != null) continue;

                GameObject enemy = col.gameObject;
                TryPerceiveEnemy(enemy, targetable);
            }
        }

        private void TryPerceiveEnemy(GameObject enemy, ITargetable targetable)
        {
            Vector2 toEnemy = (enemy.transform.position - transform.position);
            float distance = toEnemy.magnitude;

            // Check line of sight first
            var los = CombatUtils.CheckLineOfSight(transform.position, enemy.transform.position);

            if (los.IsBlocked)
            {
                // Can't see through full cover
                return;
            }

            // Distance penalty (further = harder to spot)
            int distanceModifier = 0;
            float distanceRatio = distance / perceptionRange;
            if (distanceRatio > 0.5f)
            {
                distanceModifier = -Mathf.RoundToInt((distanceRatio - 0.5f) * 10f);
            }

            // Cover modifier (enemies in half cover are harder to spot)
            int coverModifier = 0;
            if (los.CoverType == CoverType.Half)
            {
                coverModifier = -3;
            }

            // Movement modifier (moving targets are easier to spot)
            int movementModifier = 0;
            var enemyMovement = enemy.GetComponentInChildren<UnitMovement>();
            if (enemyMovement != null && enemyMovement.IsMoving)
            {
                movementModifier = 2;
            }

            // Get stats
            int perception = character?.Perception ?? 10;
            int stealth = 10;

            var enemyController = enemy.GetComponent<UnitController>();
            if (enemyController?.Character != null)
            {
                stealth = enemyController.Character.Stealth;
            }

            int totalModifier = distanceModifier + coverModifier + movementModifier;

            // Contested roll: Perception vs Stealth
            bool detected = Character.ContestedRoll(perception, stealth, totalModifier);

            if (detected)
            {
                AddOrUpdateContact(enemy, AwarenessLevel.Confirmed, isVisible: true);
            }
        }

        #endregion

        #region Contact Management

        private void AddOrUpdateContact(GameObject enemy, AwarenessLevel awareness, bool isVisible, float threatAmount = 0f)
        {
            bool isNew = false;

            if (!perceivedUnits.TryGetValue(enemy, out var contact))
            {
                contact = new PerceivedUnit { Unit = enemy };
                perceivedUnits[enemy] = contact;
                isNew = true;
            }

            // Update contact data
            contact.LastKnownPosition = enemy.transform.position;
            contact.DirectionFromMe = ((Vector2)(enemy.transform.position - transform.position)).normalized;
            contact.CurrentlyVisible = isVisible;

            if (isVisible)
            {
                contact.LastSeenTime = Time.time;
            }

            // Awareness can only escalate, not degrade from vision
            if (awareness > contact.Awareness)
            {
                contact.Awareness = awareness;
            }

            // Fire events
            if (isNew)
            {
                OnNewContact?.Invoke(contact);
            }
            else
            {
                OnContactUpdated?.Invoke(contact);
            }
        }

        private void CleanupStaleContacts()
        {
            var toRemove = new List<GameObject>();

            foreach (var kvp in perceivedUnits)
            {
                var contact = kvp.Value;

                // Check if enemy is destroyed or dead
                if (contact.Unit == null || !contact.Unit.activeInHierarchy)
                {
                    toRemove.Add(kvp.Key);
                    continue;
                }

                var targetable = contact.Unit.GetComponent<ITargetable>();
                if (targetable != null && targetable.IsDead)
                {
                    toRemove.Add(kvp.Key);
                    continue;
                }

                // Check if we've lost sight for too long
                float timeSinceSeen = Time.time - contact.LastSeenTime;
                if (timeSinceSeen > memoryDuration)
                {
                    float distance = Vector3.Distance(transform.position, contact.Unit.transform.position);
                    if (distance > perceptionRange)
                    {
                        toRemove.Add(kvp.Key);
                        continue;
                    }

                    contact.CurrentlyVisible = false;

                    // Degrade awareness over time
                    if (timeSinceSeen > memoryDuration * 1.5f && contact.Awareness == AwarenessLevel.Confirmed)
                    {
                        contact.Awareness = AwarenessLevel.Suspected;
                    }

                    // If way too long, forget them
                    if (timeSinceSeen > memoryDuration * 2f)
                    {
                        toRemove.Add(kvp.Key);
                    }
                }
            }

            foreach (var key in toRemove)
            {
                var contact = perceivedUnits[key];
                perceivedUnits.Remove(key);
                OnContactLost?.Invoke(contact);
            }
        }

        #endregion

        #region Threat Tracking (from ThreatManager)

        private void RegisterDirectionalThreat(Vector2 direction, float amount)
        {
            int bucket = DirectionToBucket(direction);
            threatBuckets[bucket] += amount;

            if (threatBuckets[bucket] > maxThreatEver)
            {
                maxThreatEver = threatBuckets[bucket];
            }
        }

        private void DecayThreats()
        {
            float decayAmount = (maxThreatEver / decayTime) * Time.deltaTime;

            for (int i = 0; i < BUCKET_COUNT; i++)
            {
                if (threatBuckets[i] > 0)
                {
                    threatBuckets[i] = Mathf.Max(0f, threatBuckets[i] - decayAmount);
                }
            }
        }

        private void DecayAimedShotThreats()
        {
            // Decay rate: 1 point per aimedShotDecayTime seconds
            float decayAmount = (1f / aimedShotDecayTime) * Time.deltaTime;

            foreach (var contact in perceivedUnits.Values)
            {
                if (contact.AimedShotThreat > 0f)
                {
                    contact.AimedShotThreat = Mathf.Max(0f, contact.AimedShotThreat - decayAmount);
                }
            }
        }

        private int DirectionToBucket(Vector2 direction)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            if (angle < 0) angle += 360f;
            return Mathf.FloorToInt(angle / DEGREES_PER_BUCKET) % BUCKET_COUNT;
        }

        private Vector2 BucketToDirection(int bucket)
        {
            float angle = (bucket * DEGREES_PER_BUCKET + DEGREES_PER_BUCKET / 2f) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        #endregion

        #region Public API - Threat Queries (ThreatManager compatibility)

        /// <summary>
        /// Register that a specific enemy shot at us.
        /// </summary>
        public void RegisterEnemyShot(GameObject enemy, float damage = 0f, bool isAimedShot = false)
        {
            if (enemy == null) return;

            // Add or update contact with Confirmed awareness
            if (!perceivedUnits.TryGetValue(enemy, out var contact))
            {
                contact = new PerceivedUnit { Unit = enemy };
                perceivedUnits[enemy] = contact;
                OnNewContact?.Invoke(contact);
            }

            contact.LastKnownPosition = enemy.transform.position;
            contact.DirectionFromMe = ((Vector2)(enemy.transform.position - transform.position)).normalized;
            contact.LastThreatTime = Time.time;
            contact.ShotsFiredAtMe++;
            contact.TotalDamageDealt += damage;
            contact.Awareness = AwarenessLevel.Confirmed;

            if (isAimedShot)
            {
                contact.AimedShotThreat += 1f;
                Debug.Log($"[{transform.root.name}] AIMED SHOT from {enemy.name} - threat now {contact.AimedShotThreat:F1} (IsSniper: {contact.IsSniper})");
            }

            // Also register directional threat (aimed shots register more threat)
            float threatAmount = damage > 0 ? damage : 5f;
            if (isAimedShot) threatAmount *= 1.5f;
            RegisterDirectionalThreat(contact.DirectionFromMe, threatAmount);
        }

        /// <summary>
        /// Manually register a threat from a direction.
        /// </summary>
        public void RegisterThreat(Vector2 direction, float amount)
        {
            RegisterDirectionalThreat(direction, amount);
        }

        /// <summary>
        /// Register threat from a visible enemy position.
        /// </summary>
        public void RegisterVisibleEnemy(Vector2 enemyPosition, float threatAmount = 1f)
        {
            Vector2 direction = (enemyPosition - (Vector2)transform.position).normalized;
            RegisterDirectionalThreat(direction, threatAmount);
        }

        /// <summary>
        /// Returns true if any threat bucket is above the threshold.
        /// </summary>
        public bool IsUnderFire(float threshold = 0.1f)
        {
            for (int i = 0; i < BUCKET_COUNT; i++)
            {
                if (threatBuckets[i] > threshold)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the direction with the highest threat level.
        /// </summary>
        public Vector2? GetHighestThreatDirection()
        {
            int highestBucket = -1;
            float highestThreat = 0f;

            for (int i = 0; i < BUCKET_COUNT; i++)
            {
                if (threatBuckets[i] > highestThreat)
                {
                    highestThreat = threatBuckets[i];
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
        /// Gets the total accumulated threat from all directions.
        /// </summary>
        public float GetTotalThreat()
        {
            float total = 0f;
            for (int i = 0; i < BUCKET_COUNT; i++)
            {
                total += threatBuckets[i];
            }
            return total;
        }

        /// <summary>
        /// Gets all active threat directions above a threshold.
        /// </summary>
        public List<ThreatInfo> GetActiveThreats(float threshold = 0.1f)
        {
            var threats = new List<ThreatInfo>();

            for (int i = 0; i < BUCKET_COUNT; i++)
            {
                if (threatBuckets[i] > threshold)
                {
                    threats.Add(new ThreatInfo
                    {
                        Direction = BucketToDirection(i),
                        ThreatLevel = threatBuckets[i],
                        BucketIndex = i
                    });
                }
            }

            threats.Sort((a, b) => b.ThreatLevel.CompareTo(a.ThreatLevel));
            return threats;
        }

        /// <summary>
        /// Gets the threat level from a specific direction.
        /// </summary>
        public float GetThreatLevel(Vector2 direction)
        {
            int bucket = DirectionToBucket(direction);
            return threatBuckets[bucket];
        }

        /// <summary>
        /// Gets the raw threat level for a specific bucket index.
        /// </summary>
        public float GetThreatLevelByBucket(int bucketIndex)
        {
            if (bucketIndex < 0 || bucketIndex >= BUCKET_COUNT)
                return 0f;
            return threatBuckets[bucketIndex];
        }

        /// <summary>
        /// Get the most dangerous enemies sorted by threat score.
        /// </summary>
        public List<GameObject> GetMostDangerousEnemies(int maxCount = 3)
        {
            return perceivedUnits.Values
                .Where(p => p.Unit != null)
                .OrderByDescending(p => p.GetThreatScore(transform.position))
                .Take(maxCount)
                .Select(p => p.Unit)
                .ToList();
        }

        /// <summary>
        /// Get enemies acting as snipers (aimed shot threat >= 2).
        /// </summary>
        public List<GameObject> GetSnipers()
        {
            return perceivedUnits.Values
                .Where(p => p.Unit != null && p.IsSniper)
                .OrderByDescending(p => p.AimedShotThreat)
                .Select(p => p.Unit)
                .ToList();
        }

        /// <summary>
        /// Get the aimed shot threat level from a specific enemy.
        /// </summary>
        public float GetAimedShotThreat(GameObject enemy)
        {
            if (enemy == null) return 0f;
            if (perceivedUnits.TryGetValue(enemy, out var contact))
            {
                return contact.AimedShotThreat;
            }
            return 0f;
        }

        /// <summary>
        /// Get threat score for a specific enemy.
        /// </summary>
        public float GetEnemyThreatScore(GameObject enemy)
        {
            if (enemy == null) return 0f;
            if (perceivedUnits.TryGetValue(enemy, out var contact))
            {
                return contact.GetThreatScore(transform.position);
            }
            return 0f;
        }

        /// <summary>
        /// Clears all threat and perception data.
        /// </summary>
        public void ClearThreats()
        {
            for (int i = 0; i < BUCKET_COUNT; i++)
            {
                threatBuckets[i] = 0f;
            }
            perceivedUnits.Clear();
        }

        /// <summary>
        /// Reset threat levels to a minimum value (not zero).
        /// </summary>
        public void ResetThreats(float minimumValue = 0.01f)
        {
            for (int i = 0; i < BUCKET_COUNT; i++)
            {
                threatBuckets[i] = minimumValue;
            }
            foreach (var contact in perceivedUnits.Values)
            {
                contact.ShotsFiredAtMe = 0;
                contact.TotalDamageDealt = 0f;
            }
        }

        #endregion

        #region Public API - Perception Queries

        /// <summary>
        /// Returns true if any enemies are currently perceived.
        /// </summary>
        public bool HasPerceivedEnemies()
        {
            return perceivedUnits.Count > 0;
        }

        /// <summary>
        /// Returns true if we currently have eyes on any enemy.
        /// </summary>
        public bool HasVisibleEnemies()
        {
            foreach (var contact in perceivedUnits.Values)
            {
                if (contact.CurrentlyVisible) return true;
            }
            return false;
        }

        /// <summary>
        /// Get all perceived enemies (visible or remembered).
        /// </summary>
        public List<PerceivedUnit> GetPerceivedEnemies()
        {
            return new List<PerceivedUnit>(perceivedUnits.Values);
        }

        /// <summary>
        /// Get the closest currently visible enemy.
        /// </summary>
        public GameObject GetClosestVisibleEnemy()
        {
            GameObject closest = null;
            float closestDist = float.MaxValue;

            foreach (var contact in perceivedUnits.Values)
            {
                if (!contact.CurrentlyVisible) continue;
                if (contact.Unit == null) continue;

                float dist = Vector3.Distance(transform.position, contact.Unit.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = contact.Unit;
                }
            }

            return closest;
        }

        /// <summary>
        /// Check if a specific enemy is perceived.
        /// </summary>
        public bool IsEnemyPerceived(GameObject enemy)
        {
            return enemy != null && perceivedUnits.ContainsKey(enemy);
        }

        /// <summary>
        /// Immediately perceive an enemy (e.g., they shot at us).
        /// </summary>
        public void ForcePerceive(GameObject enemy)
        {
            if (enemy == null) return;

            AddOrUpdateContact(enemy, AwarenessLevel.Confirmed, isVisible: true);
        }

        /// <summary>
        /// Receive intel from a squadmate (suspected awareness).
        /// </summary>
        public void ReceiveSquadIntel(PerceivedUnit intel)
        {
            if (intel?.Unit == null) return;

            // Don't downgrade our own confirmed sightings
            if (perceivedUnits.TryGetValue(intel.Unit, out var existing))
            {
                if (existing.Awareness >= intel.Awareness)
                {
                    // Just update position if intel is fresher
                    if (intel.LastSeenTime > existing.LastSeenTime)
                    {
                        existing.LastKnownPosition = intel.LastKnownPosition;
                    }
                    return;
                }
            }

            // Add as suspected contact
            AddOrUpdateContact(intel.Unit, AwarenessLevel.Suspected, isVisible: false);

            // Update with intel data
            var contact = perceivedUnits[intel.Unit];
            contact.LastKnownPosition = intel.LastKnownPosition;
        }

        /// <summary>
        /// Clear all perceptions (e.g., on respawn or teleport).
        /// </summary>
        public void ClearPerceptions()
        {
            perceivedUnits.Clear();
        }

        #endregion

        #region Debug Visualization

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showDebugGizmos) return;

            Vector3 pos = transform.position;

            // Draw perception range (360° circle)
            UnityEditor.Handles.color = new Color(1f, 1f, 0f, 0.1f);
            UnityEditor.Handles.DrawSolidDisc(pos, Vector3.forward, perceptionRange);

            if (!Application.isPlaying) return;

            // Draw threat buckets
            float maxDisplayRadius = 2f;
            for (int i = 0; i < BUCKET_COUNT; i++)
            {
                if (threatBuckets[i] <= 0.01f) continue;

                Vector2 dir = BucketToDirection(i);
                float normalizedThreat = Mathf.Clamp01(threatBuckets[i] / Mathf.Max(maxThreatEver, 1f));

                Gizmos.color = Color.Lerp(Color.green, Color.red, normalizedThreat);
                float lineLength = normalizedThreat * maxDisplayRadius;
                Vector3 endPoint = pos + new Vector3(dir.x, dir.y, 0) * lineLength;
                Gizmos.DrawLine(pos, endPoint);
                Gizmos.DrawWireSphere(endPoint, 0.1f);
            }

            // Draw perceived enemies
            foreach (var contact in perceivedUnits.Values)
            {
                if (contact.Unit == null) continue;

                // Color based on awareness level
                switch (contact.Awareness)
                {
                    case AwarenessLevel.Confirmed:
                        Gizmos.color = contact.CurrentlyVisible ? Color.red : new Color(1f, 0.5f, 0f);
                        break;
                    case AwarenessLevel.Suspected:
                        Gizmos.color = Color.yellow;
                        break;
                    default:
                        Gizmos.color = Color.gray;
                        break;
                }

                Gizmos.DrawLine(pos, contact.LastKnownPosition);
                Gizmos.DrawWireSphere(contact.LastKnownPosition, 0.3f);
            }
        }
#endif

        #endregion
    }

    #region Data Classes

    /// <summary>
    /// How aware we are of an enemy's presence.
    /// </summary>
    public enum AwarenessLevel
    {
        Unaware = 0,    // Don't know they exist
        Suspected = 1,  // Heard/saw something, investigating
        Confirmed = 2   // Full knowledge of position
    }

    /// <summary>
    /// Data about a perceived enemy unit.
    /// </summary>
    public class PerceivedUnit
    {
        public GameObject Unit;
        public Vector3 LastKnownPosition;
        public Vector2 DirectionFromMe;
        public float LastSeenTime;
        public float LastThreatTime;
        public int ShotsFiredAtMe;
        public float AimedShotThreat; // Decaying threat from aimed shots (sniper behavior)
        public float TotalDamageDealt;
        public bool CurrentlyVisible;
        public AwarenessLevel Awareness;

        /// <summary>
        /// Returns true if this enemy is acting like a sniper (aimed shot threat >= 2).
        /// </summary>
        public bool IsSniper => AimedShotThreat >= 2f;

        /// <summary>
        /// Calculate threat score for prioritization.
        /// </summary>
        public float GetThreatScore(Vector3 myPosition)
        {
            if (Unit == null) return 0f;

            float distance = Vector3.Distance(myPosition, Unit.transform.position);
            float distanceScore = Mathf.Max(0, 20f - distance);

            float shotScore = ShotsFiredAtMe * 5f;
            float aimedShotScore = AimedShotThreat * 15f; // Aimed shots are much more threatening
            float damageScore = TotalDamageDealt * 0.5f;

            // Recency bonus
            float recency = Time.time - LastThreatTime;
            float recencyMultiplier = recency < 3f ? 2f : 1f;

            // Awareness multiplier
            float awarenessMultiplier = Awareness == AwarenessLevel.Confirmed ? 1f : 0.5f;

            return (distanceScore + shotScore + aimedShotScore + damageScore) * recencyMultiplier * awarenessMultiplier;
        }
    }

    /// <summary>
    /// Directional threat information (kept for AI state compatibility).
    /// </summary>
    public struct ThreatInfo
    {
        public Vector2 Direction;
        public float ThreatLevel;
        public int BucketIndex;
    }

    #endregion
}
