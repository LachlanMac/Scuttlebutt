using UnityEngine;
using System.Collections.Generic;
using Pathfinding;
using Starbelter.Core;
using Starbelter.Combat;
using Starbelter.Pathfinding;

namespace Starbelter.AI
{
    /// <summary>
    /// The 5 core states a unit can be in.
    /// </summary>
    public enum UnitStateType
    {
        Ready,      // No threats, holding position, high alertness
        Combat,     // Engaged with enemy, shooting
        Moving,     // Relocating to new position
        Pinned,     // Suppressed, can't act (ducking)
        Reloading   // Reloading weapon, vulnerable
    }

    /// <summary>
    /// Main AI controller for a unit. Simple 5-state tactical brain.
    /// Uses TileThreatMap for threat-aware decision making.
    /// </summary>
    [RequireComponent(typeof(UnitMovement))]
    public class UnitController : MonoBehaviour, ITargetable
    {
        // === CONSTANTS ===
        private const float THREAT_DANGEROUS = 10f;
        private const float THREAT_DEADLY = 20f;
        private const float SUPPRESSION_PIN_THRESHOLD = 80f;

        // Suppression decay rates based on threat level
        private const float SUPPRESSION_DECAY_HIGH_THREAT = 2f;    // Under heavy fire - slow recovery
        private const float SUPPRESSION_DECAY_MED_THREAT = 8f;     // Some fire - moderate recovery
        private const float SUPPRESSION_DECAY_LOW_THREAT = 20f;    // Light/no fire - fast recovery
        private const float EVALUATION_INTERVAL = 0.5f;
        private const float MIN_STATE_TIME = 1f;
        private const float COVER_SEARCH_RADIUS = 15f;
        private const float ARRIVAL_THRESHOLD = 0.5f;

        // === SERIALIZED FIELDS ===
        [Header("Team")]
        [SerializeField] private Team team = Team.Federation;

        [Header("Character")]
        [SerializeField] private Character character;

        [Header("Combat")]
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private Transform firePoint;
        [SerializeField] private float weaponRange = 15f;
        [SerializeField] private float fireRate = 1f;

        [Header("Tactics")]
        [SerializeField] private Posture posture = Posture.Neutral;

        [Header("Radio UI")]
        [SerializeField] private GameObject radioMessageObject;
        [SerializeField] private TMPro.TextMeshProUGUI radioText;

        // === COMPONENTS ===
        private UnitMovement movement;
        private UnitHealth unitHealth;
        private PerceptionManager perceptionManager;

        // === STATE MACHINE ===
        private Dictionary<UnitStateType, UnitState> states;
        private UnitState currentState;
        private UnitStateType currentStateType;
        private float stateEnterTime;

        // === SQUAD ===
        private SquadController squad;
        private bool isSquadLeader;

        // === TARGETING ===
        private ITargetable currentTarget;

        // === MOVEMENT ===
        private Vector3 pendingDestination;
        private bool hasPendingDestination;
        private bool useThreatAwarePath;
        private bool hasPendingFightingPositionRequest;  // Prevents overlapping async requests
        private float lastRepositionTime = -10f;  // When we last requested a new position (for cooldown)
        private const float REPOSITION_COOLDOWN = 3f;

        public void ClearPendingFightingPositionRequest() => hasPendingFightingPositionRequest = false;
        public float LastRepositionTime => lastRepositionTime;
        public float RepositionCooldown => REPOSITION_COOLDOWN;
        public bool IsRepositionOnCooldown => Time.time - lastRepositionTime < REPOSITION_COOLDOWN;
        public void ResetRepositionCooldown() => lastRepositionTime = Time.time;

        // === COMBAT ===
        private float lastFireTime;
        private float suppression;

        // === LIFECYCLE ===
        private bool isDestroyed;

        // === STEALTH ===
        private bool isStealthed;

        // === DUCKED STATE ===
        private bool isDucked;
        private Vector3 normalScale;

        // === ITargetable ===
        public Vector3 Position => transform.position;
        Team ITargetable.Team => team;
        public Transform Transform => transform;
        public bool IsDead => isDestroyed || (unitHealth != null && unitHealth.IsDead);
        public bool IsDucked => isDucked;

        // === PUBLIC ACCESSORS ===
        public Team Team => team;
        public Character Character => character;
        public SquadController Squad => squad;
        public bool IsSquadLeader => isSquadLeader;
        public UnitMovement Movement => movement;
        public UnitHealth Health => unitHealth;
        public PerceptionManager PerceptionManager => perceptionManager;
        public GameObject ProjectilePrefab => projectilePrefab;
        public Transform FirePoint => firePoint;
        public float WeaponRange => weaponRange;
        public float PerceptionRange => perceptionManager != null ? perceptionManager.PerceptionRange : weaponRange * 1.5f;
        public Posture Posture => posture;
        public Posture BasePosture => posture;

        public Vector3 FirePosition => firePoint != null ? firePoint.position : transform.position;
        public ITargetable CurrentTarget => currentTarget;
        public float Suppression => suppression;
        public bool IsSuppressed => suppression >= SUPPRESSION_PIN_THRESHOLD * 0.5f;
        public bool HasPendingDestination => hasPendingDestination;
        public bool ShouldUseThreatAwarePath => useThreatAwarePath;
        public bool CanShoot => Time.time - lastFireTime >= 1f / fireRate && !NeedsReload;
        public UnitStateType CurrentStateType => currentStateType;
        public bool IsStealthed => isStealthed;

        /// <summary>
        /// Returns true if unit is in stealth mode (not yet spotted).
        /// Hidden units behind half cover cannot be perceived.
        /// Once engaged, a unit cannot hide again - suppression doesn't make you invisible.
        /// </summary>
        public bool IsHiding => isStealthed;

        public bool IsInCover
        {
            get
            {
                var coverQuery = CoverQuery.Instance;
                if (coverQuery == null) return false;

                // Need a threat position to check cover against
                Vector3 threatPos = transform.position + Vector3.right * 10f; // Default
                if (currentTarget != null)
                {
                    threatPos = currentTarget.Position;
                }
                else if (perceptionManager != null)
                {
                    // Use closest visible enemy from perception
                    var closestEnemy = perceptionManager.GetClosestVisibleEnemy();
                    if (closestEnemy != null)
                    {
                        threatPos = closestEnemy.transform.position;
                    }
                }

                var coverCheck = coverQuery.CheckCoverAt(transform.position, threatPos);
                return coverCheck.HasCover;
            }
        }

        // Compatibility with old code
        public bool IsPeeking => currentStateType == UnitStateType.Combat;

        /// <summary>
        /// Returns true if unit is specifically in half cover (not full cover).
        /// Used for ducking during reload.
        /// </summary>
        public bool IsInHalfCover
        {
            get
            {
                var coverQuery = CoverQuery.Instance;
                if (coverQuery == null) return false;

                Vector3 threatPos = transform.position + Vector3.right * 10f;
                if (currentTarget != null)
                {
                    threatPos = currentTarget.Position;
                }
                else if (perceptionManager != null)
                {
                    var closestEnemy = perceptionManager.GetClosestVisibleEnemy();
                    if (closestEnemy != null)
                    {
                        threatPos = closestEnemy.transform.position;
                    }
                }

                var coverCheck = coverQuery.CheckCoverAt(transform.position, threatPos);
                return coverCheck.HasCover && coverCheck.Type == CoverType.Half;
            }
        }

        // === DUCKED STATE ===

        public void SetDucked(bool ducked)
        {
            isDucked = ducked;

            // Visual indicator - shrink Y axis when ducked
            transform.localScale = ducked
                ? new Vector3(normalScale.x, normalScale.y * 0.7f, normalScale.z)
                : normalScale;
        }

        // === RELOAD ===

        /// <summary>
        /// Returns true if weapon is empty and needs reload.
        /// </summary>
        public bool NeedsReload
        {
            get
            {
                if (character?.MainWeapon == null) return false;
                return character.MainWeapon.NeedsReload;
            }
        }

        /// <summary>
        /// Returns true if should tactically reload (low ammo and in a safe position).
        /// </summary>
        public bool ShouldTacticalReload
        {
            get
            {
                if (character?.MainWeapon == null) return false;
                var weapon = character.MainWeapon;

                // Don't tactical reload if more than 30% ammo
                float ammoPercent = (float)weapon.CurrentAmmo / weapon.MagazineSize;
                if (ammoPercent > 0.3f) return false;

                // Only tactical reload if in cover and not in immediate danger
                if (!IsInCover) return false;
                if (IsInDeadlyDanger()) return false;

                return true;
            }
        }

        /// <summary>
        /// Get the reload time for current weapon.
        /// </summary>
        public float GetReloadTime()
        {
            if (character?.MainWeapon == null) return 2f; // Default
            return character.MainWeapon.ReloadTime;
        }

        /// <summary>
        /// Complete the reload, refilling magazine.
        /// </summary>
        public void FinishReload()
        {
            if (character?.MainWeapon != null)
            {
                character.MainWeapon.Reload();
            }
        }

        private void Awake()
        {
            movement = GetComponent<UnitMovement>();
            unitHealth = GetComponentInChildren<UnitHealth>();
            perceptionManager = GetComponentInChildren<PerceptionManager>();
            normalScale = transform.localScale;

            if (perceptionManager != null)
            {
                perceptionManager.MyTeam = team;
                perceptionManager.SetCharacter(character);
            }

            if (character == null)
            {
                character = new Character();
            }

            InitializeStates();
        }

        private void Start()
        {
            if (unitHealth != null)
            {
                unitHealth.OnDeath += OnDeath;
            }

            if (perceptionManager != null)
            {
                perceptionManager.OnUnderFire += OnUnderFire;
            }

            // Hide radio bubble initially
            if (radioMessageObject != null)
            {
                radioMessageObject.SetActive(false);
            }

            UpdateTeamColor();
            ChangeState(UnitStateType.Ready);
        }

        private void OnDestroy()
        {
            isDestroyed = true;

            if (unitHealth != null)
            {
                unitHealth.OnDeath -= OnDeath;
            }

            if (perceptionManager != null)
            {
                perceptionManager.OnUnderFire -= OnUnderFire;
            }
        }

        private void InitializeStates()
        {
            states = new Dictionary<UnitStateType, UnitState>
            {
                { UnitStateType.Ready, new ReadyState() },
                { UnitStateType.Combat, new CombatState() },
                { UnitStateType.Moving, new MovingState() },
                { UnitStateType.Pinned, new PinnedState() },
                { UnitStateType.Reloading, new ReloadState() }
            };
        }

        private void Update()
        {
            // Check if we've been destroyed
            if (isDestroyed) return;
            if (IsDead) return;

            // Threat-based suppression: standing in dangerous areas builds suppression
            float threat = GetThreatAtPosition(transform.position);
            float oldSuppression = suppression;

            // Suppression GAIN only from dangerous+ threat
            if (threat >= THREAT_DANGEROUS)
            {
                // At threat 10 (dangerous), gain ~10 suppression/sec
                // At threat 20 (deadly), gain ~20 suppression/sec
                float suppressionGain = threat * Time.deltaTime;
                suppression += suppressionGain;
            }

            // Suppression DECAY - always happens, but rate depends on threat level
            // High threat = slow decay, Low/no threat = fast decay
            float decayRate;
            if (threat >= THREAT_DEADLY)
            {
                decayRate = SUPPRESSION_DECAY_HIGH_THREAT;  // 2/sec - barely recovering
            }
            else if (threat >= THREAT_DANGEROUS)
            {
                decayRate = SUPPRESSION_DECAY_MED_THREAT;   // 8/sec - moderate recovery
            }
            else
            {
                decayRate = SUPPRESSION_DECAY_LOW_THREAT;   // 20/sec - fast recovery
            }

            suppression -= decayRate * Time.deltaTime;
            suppression = Mathf.Clamp(suppression, 0f, 100f);

            // Log when crossing thresholds
            if (oldSuppression < 40f && suppression >= 40f)
                Debug.Log($"[{gameObject.name}] Suppression rising: {suppression:F0} (threat={threat:F1})");
            if (oldSuppression < SUPPRESSION_PIN_THRESHOLD && suppression >= SUPPRESSION_PIN_THRESHOLD)
                Debug.Log($"[{gameObject.name}] PINNED! Suppression={suppression:F0} (threat={threat:F1})");

            // Update current state
            currentState?.Update();
        }

        // === STATE MANAGEMENT ===

        public void ChangeState(UnitStateType newState)
        {
            if (isDestroyed || IsDead) return;

            string oldState = currentStateType.ToString();

            currentState?.Exit();

            currentStateType = newState;
            currentState = states[newState];
            currentState.Initialize(this);
            currentState.Enter();
            stateEnterTime = Time.time;

            Debug.Log($"[{name}] {oldState} -> {newState}");
        }

        public string GetCurrentStateName() => currentStateType.ToString();

        protected float TimeInState => Time.time - stateEnterTime;
        public bool CanTransition => TimeInState >= MIN_STATE_TIME;

        // === TARGETING ===

        public void SetTarget(ITargetable target)
        {
            currentTarget = target;
        }

        public void ClearTarget()
        {
            currentTarget = null;
        }

        /// <summary>
        /// Check if current target is valid (not null, not destroyed, not dead).
        /// Handles Unity's "fake null" for destroyed objects.
        /// </summary>
        public bool IsTargetValid()
        {
            if (currentTarget == null) return false;

            // Check if the Unity object has been destroyed
            var mb = currentTarget as MonoBehaviour;
            if (mb == null) return false;

            return !currentTarget.IsDead;
        }

        // === THREAT QUERIES ===

        public float GetThreatAtPosition(Vector3 pos)
        {
            if (TileThreatMap.Instance == null) return 0f;
            return TileThreatMap.Instance.GetThreatAtWorld(pos, team);
        }

        public bool IsInDanger() => GetThreatAtPosition(transform.position) >= THREAT_DANGEROUS;
        public bool IsInDeadlyDanger() => GetThreatAtPosition(transform.position) >= THREAT_DEADLY;

        // === MOVEMENT REQUESTS ===

        public void RequestCoverPosition()
        {
            // Need a threat position to find cover from
            Vector3 threatPos = transform.position + Vector3.right * 10f; // Default
            if (currentTarget != null)
            {
                threatPos = currentTarget.Position;
            }

            var coverPositions = FindCoverPositions(transform.position, COVER_SEARCH_RADIUS, threatPos);
            if (coverPositions.Count == 0)
            {
                hasPendingDestination = false;
                return;
            }

            // Score and pick best cover
            float bestScore = float.MinValue;
            Vector3 bestPos = transform.position;

            foreach (var pos in coverPositions)
            {
                float score = ScorePosition(pos);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPos = pos;
                }
            }

            pendingDestination = bestPos;
            hasPendingDestination = true;
        }

        /// <summary>
        /// Find a fighting position - cover with LOS to at least one enemy.
        /// Works even without a current target. Use when squad is engaged but unit can't see anyone.
        /// Uses async A* path queries for accurate scoring.
        /// NOTE: This is ASYNC - hasPendingDestination will only be true AFTER the callback fires.
        /// Callers should NOT check HasPendingDestination immediately after calling this.
        /// </summary>
        public void RequestFightingPosition()
        {
            // Prevent overlapping async requests
            if (hasPendingFightingPositionRequest)
            {
                return;
            }

            // Clear immediately - destination won't be valid until async callback completes
            hasPendingDestination = false;
            useThreatAwarePath = false;

            var coverQuery = CoverQuery.Instance;
            if (coverQuery == null)
            {
                return;
            }

            hasPendingFightingPositionRequest = true;

            // Get known enemies from perception for defensive cover scoring
            List<GameObject> knownEnemies = null;
            if (perceptionManager != null)
            {
                var perceived = perceptionManager.GetPerceivedEnemies();
                knownEnemies = new List<GameObject>(perceived.Count);
                foreach (var p in perceived)
                {
                    if (p.Unit != null)
                        knownEnemies.Add(p.Unit);
                }
            }

            CombatUtils.FindFightingPositionAsync(
                transform.position,
                weaponRange,
                coverQuery,
                gameObject,
                team,
                squad?.RallyPointPosition,
                isSquadLeader,
                character?.Tactics ?? 10,
                knownEnemies,
                OnFightingPositionFound
            );
        }

        private void OnFightingPositionFound(FightingPositionResult result)
        {
            if (IsDead || isDestroyed)
            {
                hasPendingFightingPositionRequest = false;
                return;
            }

            // If we're already moving, ignore this callback (stale request)
            if (movement.IsMoving || currentStateType == UnitStateType.Moving)
            {
                hasPendingFightingPositionRequest = false;
                return;
            }

            if (!result.Found)
            {
                hasPendingFightingPositionRequest = false;
                return;
            }

            // Only set destination if we actually need to move
            float distToPosition = Vector2.Distance(transform.position, result.Position);
            if (distToPosition <= 1f)
            {
                // Already at best position - clear flag and stay put
                hasPendingFightingPositionRequest = false;
                hasPendingDestination = false;
                useThreatAwarePath = false;

                // If we found a target, set it
                if (result.BestTarget != null)
                {
                    var targetable = result.BestTarget.GetComponent<ITargetable>();
                    if (targetable != null && !targetable.IsDead)
                    {
                        SetTarget(targetable);
                    }
                }
                return;
            }

            // SANITY CHECK: Don't go to origin unless we're actually near it
            if (result.Position.sqrMagnitude < 0.01f && transform.position.sqrMagnitude > 1f)
            {
                Debug.LogError($"[{name}] REJECTING (0,0,0) destination! Current pos={transform.position}");
                hasPendingFightingPositionRequest = false;
                hasPendingDestination = false;
                return;
            }

            // Set target FIRST so score comparison includes cover/LOS bonuses
            if (result.BestTarget != null)
            {
                var targetable = result.BestTarget.GetComponent<ITargetable>();
                if (targetable != null && !targetable.IsDead)
                {
                    SetTarget(targetable);
                }
            }

            // Compare position scores - only move if new position is significantly better
            float currentScore = ScorePosition(transform.position);
            float newScore = ScorePosition(result.Position);

            // Use the async result's score if our local scoring doesn't show improvement
            // The async scoring is more comprehensive (considers all enemies, path costs, etc.)
            float asyncScore = result.Score;

            Debug.Log($"[{name}] Position comparison: current={currentScore:F1}, new={newScore:F1}, asyncScore={asyncScore:F1}");

            // Move if EITHER our local scoring shows improvement OR the async score is good (>50)
            // This prevents the "staying put" bug when we can't see the target from current position
            if (newScore <= currentScore && asyncScore < 50f)
            {
                Debug.Log($"[{name}] New position not better - staying put");
                hasPendingFightingPositionRequest = false;
                hasPendingDestination = false;
                return;
            }

            pendingDestination = result.Position;
            hasPendingDestination = true;
            useThreatAwarePath = true;

            // Keep hasPendingFightingPositionRequest = true until we start moving
            // This prevents new requests from being made while we're about to move

            // Trigger state transition - only if in Ready or Combat
            if (currentStateType == UnitStateType.Ready || currentStateType == UnitStateType.Combat)
            {
                ChangeState(UnitStateType.Moving);
                // Flag will be cleared when MovingState.Enter() starts the move
            }
            else
            {
                // Can't transition, clear the flag
                hasPendingFightingPositionRequest = false;
            }
        }

        private float ScorePosition(Vector3 pos)
        {
            float score = 0f;

            // Distance penalty
            float dist = Vector3.Distance(transform.position, pos);
            score -= dist * 1f;

            // Threat penalty
            float threat = GetThreatAtPosition(pos);
            score -= threat * 2f;

            // Cover bonus
            if (currentTarget != null)
            {
                var coverCheck = CoverQuery.Instance?.CheckCoverAt(pos, currentTarget.Position);
                if (coverCheck.HasValue && coverCheck.Value.HasCover)
                {
                    score += coverCheck.Value.Type == CoverType.Full ? 10f : 5f;
                }

                // LOS bonus
                if (HasLineOfSight(pos, currentTarget.Position))
                {
                    score += 3f;
                }
            }

            return score;
        }

        private List<Vector3> FindCoverPositions(Vector3 center, float radius, Vector3 threatPos)
        {
            var results = new List<Vector3>();
            if (CoverQuery.Instance == null) return results;

            float step = 1f;
            for (float x = -radius; x <= radius; x += step)
            {
                for (float y = -radius; y <= radius; y += step)
                {
                    Vector3 samplePos = center + new Vector3(x, y, 0);
                    if (Vector3.Distance(center, samplePos) > radius) continue;

                    var coverCheck = CoverQuery.Instance.CheckCoverAt(samplePos, threatPos);
                    if (coverCheck.HasCover)
                    {
                        results.Add(samplePos);
                    }
                }
            }

            return results;
        }

        // === MOVEMENT EXECUTION ===

        public void StartMoving()
        {
            if (!hasPendingDestination) return;
            movement.MoveTo(pendingDestination);
            hasPendingDestination = false;
            useThreatAwarePath = false;
        }

        /// <summary>
        /// Start moving using threat-aware pathfinding.
        /// Routes will avoid high-threat tiles where possible.
        /// </summary>
        public void StartThreatAwareMove()
        {
            if (!hasPendingDestination) return;
            movement.RequestThreatAwarePath(pendingDestination, team);
            hasPendingDestination = false;
            useThreatAwarePath = false;
        }

        public void StopMoving()
        {
            movement.Stop();
        }

        /// <summary>
        /// Interrupts movement but redirects to nearest tile center first.
        /// Use this when interrupting movement (suppression, state change) to avoid stopping mid-tile.
        /// </summary>
        public void InterruptMovement()
        {
            movement.StopAtNearestTile();
        }

        public bool HasArrivedAtDestination => !movement.IsMoving;

        // === COMBAT ===

        public void FireAtTarget()
        {
            if (currentTarget == null || projectilePrefab == null) return;

            // Check ammo - don't fire if empty
            if (character?.MainWeapon != null)
            {
                if (!character.MainWeapon.ConsumeAmmo())
                {
                    // Out of ammo - can't fire
                    return;
                }
            }

            Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position;
            Vector2 direction = (currentTarget.Position - spawnPos).normalized;

            GameObject projObj = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
            var projectile = projObj.GetComponent<Projectile>();
            if (projectile != null)
            {
                projectile.Fire(direction, team, gameObject);
            }

            lastFireTime = Time.time;
        }

        public void ApplySuppression(float amount = 20f)
        {
            suppression += amount;
            suppression = Mathf.Min(suppression, 100f);
        }

        // === LINE OF SIGHT ===

        public bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            int obstacleMask = LayerMask.GetMask("Obstacles", "Cover");
            Vector2 direction = (to - from);
            float distance = direction.magnitude;
            return !Physics2D.Raycast(from, direction.normalized, distance, obstacleMask);
        }

        // === TARGET FINDING ===

        public ITargetable FindClosestVisibleEnemy(float maxRange = float.MaxValue)
        {
            ITargetable closest = null;
            float closestDist = maxRange;
            var coverQuery = CoverQuery.Instance;

            var allTargets = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var mb in allTargets)
            {
                var targetable = mb as ITargetable;
                if (targetable == null) continue;
                if (targetable.Team == team || targetable.Team == Team.Neutral) continue;
                if (targetable.IsDead) continue;

                float dist = Vector3.Distance(transform.position, targetable.Position);
                bool hasLOS = HasLineOfSight(transform.position, targetable.Position);

                // Ducked targets behind half cover are treated as having full cover (not visible)
                if (targetable.IsDucked && coverQuery != null)
                {
                    var coverCheck = coverQuery.CheckCoverAt(targetable.Position, transform.position);
                    if (coverCheck.HasCover && coverCheck.Type == CoverType.Half)
                    {
                        Debug.Log($"[{name}] Can't see {mb.name} - DUCKED behind half cover (dist={dist:F1}, range={maxRange:F1})");
                        continue; // Target is ducked behind half cover - can't see them
                    }
                }

                if (dist >= closestDist)
                {
                    // Not closer than current best
                    continue;
                }

                if (!hasLOS)
                {
                    Debug.Log($"[{name}] Can't see {mb.name} - NO LOS (dist={dist:F1}, range={maxRange:F1})");
                    continue;
                }

                closestDist = dist;
                closest = targetable;
            }

            return closest;
        }

        // === TEAM/SQUAD SETUP ===

        public void SetTeam(Team newTeam)
        {
            team = newTeam;
            if (perceptionManager != null)
            {
                perceptionManager.MyTeam = newTeam;
            }
            UpdateTeamColor();
        }

        public void SetSquad(SquadController newSquad, bool isLeader = false)
        {
            squad = newSquad;
            isSquadLeader = isLeader;
        }

        public void SetCharacter(Character newCharacter)
        {
            character = newCharacter;

            if (character != null)
            {
                string teamPrefix = team == Team.Federation ? "FED" : (team == Team.Empire ? "IMP" : "NEUTRAL");
                string leaderSuffix = isSquadLeader ? "_LEADER" : "";
                gameObject.name = $"{teamPrefix}_{character.RankAndName}{leaderSuffix}";
            }
        }

        public void SetPosture(Posture newPosture)
        {
            posture = newPosture;
        }

        public void SetStealthed(bool stealthed)
        {
            isStealthed = stealthed;
        }

        private void UpdateTeamColor()
        {
            var spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            if (spriteRenderer == null) return;

            spriteRenderer.color = team switch
            {
                Team.Federation => new Color(0.5f, 0.7f, 1f),
                Team.Empire => new Color(1f, 0.5f, 0.5f),
                Team.Neutral => Color.white,
                _ => Color.white
            };
        }

        private void OnDeath()
        {
            // Notify squad about our death
            if (squad != null)
            {
                squad.NotifyAllyDown(this);

                if (isSquadLeader)
                {
                    squad.OnLeaderDied();
                }
            }
        }

        private void OnUnderFire(PerceivedUnit shooter)
        {
            string shooterName = shooter.Unit != null ? shooter.Unit.name : "unknown";

            // ALWAYS alert squad when under fire - even if we personally can't react
            if (squad != null && shooter.Unit != null)
            {
                squad.AlertSquadContact(this, shooter.Unit.transform.position);
            }

            // Don't react if we're already moving or have a pending destination
            if (movement.IsMoving || hasPendingDestination || hasPendingFightingPositionRequest)
            {
                Debug.Log($"[{gameObject.name}] OnUnderFire from {shooterName} - SKIPPED (already moving/pending)");
                return;
            }

            // Don't react if pinned - can't move anyway
            if (currentStateType == UnitStateType.Pinned)
            {
                Debug.Log($"[{gameObject.name}] OnUnderFire from {shooterName} - SKIPPED (pinned)");
                return;
            }

            // Don't react if already in cover from the shooter
            if (shooter.Unit != null)
            {
                var coverQuery = CoverQuery.Instance;
                if (coverQuery != null)
                {
                    var coverCheck = coverQuery.CheckCoverAt(transform.position, shooter.Unit.transform.position);
                    if (coverCheck.HasCover)
                    {
                        Debug.Log($"[{gameObject.name}] OnUnderFire from {shooterName} - SKIPPED (already in cover)");
                        return;
                    }
                }
            }

            Debug.Log($"[{gameObject.name}] OnUnderFire from {shooterName} - requesting new position");

            // Set the shooter as our target if we don't have one
            if (currentTarget == null && shooter.Unit != null)
            {
                var targetable = shooter.Unit.GetComponent<ITargetable>();
                if (targetable != null && !targetable.IsDead)
                {
                    SetTarget(targetable);
                }
            }

            // Force tactical reevaluation - find better position
            ResetRepositionCooldown();
            RequestFightingPosition();
        }

        // === SQUAD HELPERS ===

        public Vector3? GetLeaderPosition()
        {
            if (squad == null) return null;
            if (isSquadLeader) return null;
            return squad.GetLeaderPosition();
        }

        public Vector3? GetRallyPoint()
        {
            if (squad == null) return null;
            return squad.RallyPointPosition;
        }

        /// <summary>
        /// Alert squad that this unit spotted an enemy. Triggers FIRST_CONTACT if squad not yet engaged.
        /// </summary>
        public void AlertSquadFirstContact(Vector3 enemyPosition)
        {
            if (squad != null)
            {
                squad.AlertSquadContact(this, enemyPosition);
            }
        }

        /// <summary>
        /// Notify squad that this unit killed an enemy.
        /// </summary>
        public void NotifySquadEnemyKilled()
        {
            if (squad != null)
            {
                squad.NotifyEnemyKilled(this);
            }
        }

        // === COMPATIBILITY METHODS ===
        // These exist so SquadController doesn't need major changes

        public void CommandSuppress(GameObject target)
        {
            // Simplified: just target and enter combat
            var targetable = target?.GetComponent<ITargetable>();
            if (targetable != null)
            {
                SetTarget(targetable);
                ChangeState(UnitStateType.Combat);
            }
        }

        public float GetEffectiveThreat()
        {
            return GetThreatAtPosition(transform.position);
        }

        // === RADIO UI ===

        /// <summary>
        /// Show a radio message using an event name from RadioLines.json.
        /// Automatically picks a random variant for that event.
        /// </summary>
        /// <param name="eventName">Event name like "FIRST_CONTACT", "ENEMY_DOWN", etc.</param>
        /// <param name="duration">How long to show the message</param>
        public void ShowRadioMessage(string eventName, float duration = 2f)
        {
            if (radioMessageObject == null || radioText == null) return;

            string message = DataLoader.GetRadioLine(eventName);
            radioText.text = message;
            radioMessageObject.SetActive(true);

            CancelInvoke(nameof(HideRadioMessage));
            Invoke(nameof(HideRadioMessage), duration);
        }

        /// <summary>
        /// Show a radio message after a random delay (0.5-2.5 seconds) for realism.
        /// </summary>
        public void ShowRadioMessageDelayed(string eventName, float minDelay = 0.5f, float maxDelay = 2.5f)
        {
            if (radioMessageObject == null || radioText == null) return;

            float delay = Random.Range(minDelay, maxDelay);
            StartCoroutine(ShowRadioMessageAfterDelay(eventName, delay));
        }

        private System.Collections.IEnumerator ShowRadioMessageAfterDelay(string eventName, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (!IsDead)
            {
                ShowRadioMessage(eventName);
            }
        }

        private void HideRadioMessage()
        {
            if (radioMessageObject != null)
            {
                radioMessageObject.SetActive(false);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Draw current state
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 1.5f,
                $"State: {currentStateType}"
            );

            // Draw target line
            if (currentTarget != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position, currentTarget.Position);
            }

            // Draw weapon range
            Gizmos.color = new Color(1f, 1f, 0f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, weaponRange);

            // Draw pending destination
            if (hasPendingDestination)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(pendingDestination, 0.5f);
            }
        }
#endif
    }
}
