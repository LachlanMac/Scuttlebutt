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
        [Tooltip("Maximum distance to detect enemies (should be > weapon range so units spot before they can shoot)")]
        [SerializeField] private float perceptionRange = 25f;

        [Tooltip("How often to run perception checks (seconds)")]
        [SerializeField] private float checkInterval = 1f;

        [Tooltip("How long to remember an enemy after losing sight")]
        [SerializeField] private float memoryDuration = 3f;

        [Tooltip("This unit's team")]
        [SerializeField] private Team myTeam = Team.Federation;

        [Header("Threat Settings")]
        [Tooltip("Time in seconds for aimed shot threat to decay by 1 point")]
        [SerializeField] private float aimedShotDecayTime = 10f;

        [Header("Debug")]
        [SerializeField] private bool showDebugGizmos = false;

        #endregion

        #region Private State

        // Character stats reference
        private Character character;


        // Perceived units dictionary
        private Dictionary<GameObject, PerceivedUnit> perceivedUnits = new Dictionary<GameObject, PerceivedUnit>();

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

        /// <summary>
        /// Fired when this unit detects incoming fire (projectile enters threat range).
        /// Passes the enemy who shot at us.
        /// </summary>
        public event System.Action<PerceivedUnit> OnUnderFire;

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

        /// <summary>
        /// Get perception modifier based on unit's current state.
        /// Ready: 1.5x, Pinned: 0.75x, Others: 1.0x
        /// </summary>
        private float GetStatePerceptionModifier()
        {
            var unitController = GetComponentInParent<UnitController>();
            if (unitController == null) return 1f;

            return unitController.CurrentStateType switch
            {
                UnitStateType.Ready => 1.5f,
                UnitStateType.Pinned => 0.75f,
                _ => 1f
            };
        }

        private void Awake()
        {
            // Ensure collider is a trigger (for projectile detection)
            var collider = GetComponent<Collider2D>();
            if (!collider.isTrigger)
            {
                collider.isTrigger = true;
            }

            // Set collider size to match perception range for projectile detection
            if (collider is CircleCollider2D circleCollider)
            {
                circleCollider.radius = perceptionRange;
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

            // Track who shot at us - instant Confirmed awareness
            if (projectile.SourceUnit != null)
            {
                // Precise shots (cover penetration < 1.0) count as aimed threats
                bool isPreciseShot = projectile.CoverPenetration < 1.0f;
                RegisterEnemyShot(projectile.SourceUnit, projectile.Damage, isPreciseShot);
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

            // Get enemy controller once for reuse
            var enemyController = enemy.GetComponent<UnitController>();

            // Check line of sight first
            var los = CombatUtils.CheckLineOfSight(transform.position, enemy.transform.position);

            if (los.IsBlocked)
            {
                // Can't see through full cover
                return;
            }

            // Half cover blocks perception if target is hiding (ducking or stealthed)
            if (los.IsPartialCover)
            {
                if (enemyController != null && enemyController.IsHiding)
                {
                    return; // Target is hiding behind half cover - can't see them
                }
                // Target is standing/exposed - half cover doesn't hide them, continue with roll
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

            // Get stats with state modifier
            int basePerception = character?.Perception ?? 10;
            float stateModifier = GetStatePerceptionModifier();
            int perception = Mathf.RoundToInt(basePerception * stateModifier);
            int stealth = 10;

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

        #region Aimed Shot Tracking

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

        #endregion

        #region Public API - Threat Queries

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
            }

            // Notify listeners that we're under fire
            OnUnderFire?.Invoke(contact);
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
        /// Clears all perception data.
        /// </summary>
        public void ClearPerceptionData()
        {
            perceivedUnits.Clear();
        }

        /// <summary>
        /// Reset threat tracking on all contacts.
        /// </summary>
        public void ResetContactThreats()
        {
            foreach (var contact in perceivedUnits.Values)
            {
                contact.ShotsFiredAtMe = 0;
                contact.TotalDamageDealt = 0f;
                contact.AimedShotThreat = 0f;
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

    #endregion
}
