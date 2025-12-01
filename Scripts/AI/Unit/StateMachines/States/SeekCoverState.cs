using UnityEngine;
using System.Collections.Generic;
using Starbelter.Combat;
using Starbelter.Core;
using Starbelter.Pathfinding;

namespace Starbelter.AI
{
    /// <summary>
    /// Unit seeks cover from the highest threat direction or a specific flank direction.
    /// </summary>
    /// <summary>
    /// How urgently we need to find new cover.
    /// Affects the threshold for deciding if new cover is "better enough" to move.
    /// </summary>
    public enum CoverUrgency
    {
        Low,      // Just checking - need significant improvement to move (10+ points)
        Medium,   // Dodged a shot - moderate improvement needed (5+ points)
        High,     // Got hit - low bar to move (2+ points)
        Flanked   // Cover compromised - always move
    }

    public class SeekCoverState : UnitState
    {
        private bool hasCoverTarget;
        private float searchCooldown;
        private float giveUpTimer;
        private const float SEARCH_INTERVAL = 0.5f;
        private const float GIVE_UP_TIME = 2f;

        // Base improvement thresholds by urgency
        private const float THRESHOLD_LOW = 10f;
        private const float THRESHOLD_MEDIUM = 5f;
        private const float THRESHOLD_HIGH = 2f;

        // Optional: specific direction to seek cover from (used when flanked)
        private Vector2? overrideThreatDirection;

        // Urgency level affects how readily we'll move
        private CoverUrgency urgency;

        /// <summary>
        /// Default constructor - low urgency, needs significant improvement to move.
        /// </summary>
        public SeekCoverState()
        {
            overrideThreatDirection = null;
            urgency = CoverUrgency.Low;
        }

        /// <summary>
        /// Constructor with urgency level.
        /// </summary>
        public SeekCoverState(CoverUrgency urgency)
        {
            overrideThreatDirection = null;
            this.urgency = urgency;
        }

        /// <summary>
        /// Constructor with specific threat direction (used when flanked).
        /// </summary>
        public SeekCoverState(Vector2 flankDirection)
        {
            overrideThreatDirection = flankDirection;
            urgency = CoverUrgency.Flanked;
        }

        private float GetImprovementThreshold()
        {
            return urgency switch
            {
                CoverUrgency.Low => THRESHOLD_LOW,
                CoverUrgency.Medium => THRESHOLD_MEDIUM,
                CoverUrgency.High => THRESHOLD_HIGH,
                CoverUrgency.Flanked => 0f, // Always move
                _ => THRESHOLD_LOW
            };
        }

        public override void Enter()
        {
            hasCoverTarget = false;
            searchCooldown = 0f;
            giveUpTimer = GIVE_UP_TIME;

            // If already moving to cover, just wait for arrival
            if (Movement.IsMoving)
            {
                hasCoverTarget = true;
                return;
            }

            // Try to find and move to cover
            bool startedMoving = FindAndMoveToCover();

            // If we didn't start moving, we're probably already at cover
            if (!startedMoving && controller.IsInCover)
            {
                var combatState = new CombatState(alreadyAtCover: true);
                stateMachine.ChangeState(combatState);
            }
        }

        public override void Update()
        {
            // If we've arrived at cover, transition to combat
            if (hasCoverTarget && !Movement.IsMoving)
            {
                // Successfully found cover - clear failure counter
                int unitId = controller.gameObject.GetInstanceID();
                CombatState.ClearCoverSeekFailures(unitId);

                var combatState = new CombatState(alreadyAtCover: true);
                stateMachine.ChangeState(combatState);
                return;
            }

            // Periodically re-evaluate if we should find new cover
            searchCooldown -= Time.deltaTime;
            if (searchCooldown <= 0f)
            {
                searchCooldown = SEARCH_INTERVAL;

                // Try to find cover if we don't have a target yet
                if (!hasCoverTarget)
                {
                    FindAndMoveToCover();
                }
                // Or if threat direction changed significantly
                else if (ShouldReevaluateCover())
                {
                    FindAndMoveToCover();
                }
            }

            // Give up timer - don't immediately bail, wait for path throttle to clear
            if (!hasCoverTarget)
            {
                giveUpTimer -= Time.deltaTime;
                if (giveUpTimer <= 0f)
                {
                    // Couldn't find cover after waiting - record failure
                    int unitId = controller.gameObject.GetInstanceID();
                    CombatState.RecordCoverSeekFailure(unitId);

                    Debug.Log($"[{controller.name}] SeekCoverState: Gave up finding cover, fighting in open");
                    var combatState = new CombatState(alreadyAtCover: true); // Prevent re-seeking
                    stateMachine.ChangeState(combatState);
                }
            }
        }

        /// <summary>
        /// Find and move to cover. Returns true if movement was started.
        /// </summary>
        private bool FindAndMoveToCover()
        {
            // Use override direction if set (flanking), otherwise get from PerceptionManager
            Vector2? threatDir = overrideThreatDirection;

            if (!threatDir.HasValue)
            {
                if (PerceptionManager == null)
                {
                    hasCoverTarget = false;
                    return false;
                }

                threatDir = PerceptionManager.GetHighestThreatDirection();
                if (!threatDir.HasValue)
                {
                    hasCoverTarget = false;
                    return false;
                }
            }

            Vector3 unitPos = controller.transform.position;
            Vector3 threatWorldPos = CombatUtils.ThreatDirectionToWorldPos(unitPos, threatDir.Value);

            // Find cover that protects from this threat
            var coverQuery = CoverQuery.Instance;
            if (coverQuery == null)
            {
                Debug.LogWarning("[SeekCoverState] CoverQuery not available");
                hasCoverTarget = false;
                return false;
            }

            // Use tactical search with posture
            int bravery = controller.Character?.Bravery ?? 10;
            var leaderPos = controller.GetLeaderPosition();
            var rallyPoint = controller.GetRallyPoint();
            bool isLeader = controller.IsSquadLeader;
            var searchParams = CoverSearchParams.FromPosture(controller.WeaponRange, controller.Posture, bravery, controller.Team, leaderPos, rallyPoint, isLeader);

            // Determine cover mode based on urgency:
            // - Flanked or High urgency (got hit) = Defensive mode (allow retreat behind walls)
            // - Low/Medium urgency = Fighting mode (must have LOS to enemies)
            CoverMode coverMode = (urgency == CoverUrgency.Flanked || urgency == CoverUrgency.High)
                ? CoverMode.Defensive
                : CoverMode.Fighting;

            // Get list of known enemies for LOS checking
            var knownEnemies = GetKnownEnemies();

            // Apply mode and enemies to search params
            searchParams = searchParams.WithMode(coverMode, knownEnemies);

            // Find best cover WITH score so we can compare
            var (coverResult, bestScore) = coverQuery.FindBestCoverWithScore(unitPos, threatWorldPos, searchParams, -1f, controller.gameObject);

            if (!coverResult.HasValue)
            {
                hasCoverTarget = false;
                return false;
            }

            // Check if best cover is our current position (distance < 0.5)
            float distToBestCover = Vector3.Distance(unitPos, coverResult.Value.WorldPosition);
            if (distToBestCover < 0.5f)
            {
                // Best cover is where we already are - stay put!
                Debug.Log($"[{controller.name}] SeekCoverState: Current position is best cover (score={bestScore:F1}), staying put");
                hasCoverTarget = false;
                return false;
            }

            // Score our current position to see if moving is worth it
            float currentScore = coverQuery.ScorePositionForCover(unitPos, threatWorldPos, searchParams);
            float improvement = bestScore - currentScore;

            // Only move if the new position is better by at least our threshold
            // Threshold depends on urgency (hit = low threshold, just checking = high threshold)
            float threshold = GetImprovementThreshold();

            if (improvement > threshold)
            {
                // Determine cover type at chosen position
                string coverTypeStr = "None";
                Vector2 coverThreatDir = ((Vector2)(threatWorldPos - coverResult.Value.WorldPosition)).normalized;
                foreach (var source in coverResult.Value.CoverSources)
                {
                    float alignment = Vector2.Dot(source.DirectionToCover, coverThreatDir);
                    float alignThreshold = (source.Type == CoverType.Full) ? 0.7f : 0.3f;
                    if (alignment > alignThreshold)
                    {
                        coverTypeStr = source.Type.ToString();
                        break;
                    }
                }

                Debug.Log($"[{controller.name}] SeekCoverState: Moving to {coverTypeStr} cover at {coverResult.Value.TilePosition} (current={currentScore:F1}, best={bestScore:F1}, improvement={improvement:F1} > {threshold}, urgency={urgency})");
                hasCoverTarget = Movement.MoveToTile(coverResult.Value.TilePosition);
                return hasCoverTarget;
            }
            else
            {
                Debug.Log($"[{controller.name}] SeekCoverState: Current cover is good enough (current={currentScore:F1}, best={bestScore:F1}, improvement={improvement:F1} <= {threshold}, urgency={urgency})");
                hasCoverTarget = false;
                return false;
            }
        }

        private bool ShouldReevaluateCover()
        {
            // Could add logic here to check if threat direction changed significantly
            // For now, just re-evaluate if we're not already moving to cover
            return !Movement.IsMoving && PerceptionManager != null && PerceptionManager.IsUnderFire();
        }

        /// <summary>
        /// Get list of known enemy GameObjects within weapon range.
        /// Used for LOS checking in cover scoring.
        /// </summary>
        private List<GameObject> GetKnownEnemies()
        {
            var enemies = new List<GameObject>();
            var allTargets = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

            foreach (var mb in allTargets)
            {
                var targetable = mb as ITargetable;
                if (targetable == null) continue;
                if (targetable.Team == controller.Team) continue;
                if (targetable.Team == Team.Neutral) continue;
                if (targetable.IsDead) continue;

                float dist = Vector2.Distance(controller.transform.position, targetable.Transform.position);
                if (dist <= controller.WeaponRange)
                {
                    enemies.Add(targetable.Transform.gameObject);
                }
            }

            return enemies;
        }
    }
}
