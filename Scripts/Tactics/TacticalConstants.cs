namespace Starbelter.Tactics
{
    /// <summary>
    /// Tuning constants for the tactical AI system.
    /// All values in one place for easy balancing.
    /// </summary>
    public static class TacticalConstants
    {
        // === THREAT ===
        /// <summary>Threat level considered "dangerous" - unit should avoid or take cover.</summary>
        public const float ThreatDangerous = 10f;

        /// <summary>Threat level considered "deadly" - unit must retreat or is pinned.</summary>
        public const float ThreatDeadly = 20f;

        /// <summary>Weight multiplier for threat when scoring paths (higher = more threat avoidance).</summary>
        public const float ThreatPathWeight = 2f;

        // === ENGAGEMENT ===
        /// <summary>Preferred engagement range for rifles.</summary>
        public const float EngageRangeRifle = 12f;

        /// <summary>Preferred engagement range for SMGs/shotguns.</summary>
        public const float EngageRangeCQB = 6f;

        /// <summary>Preferred engagement range for snipers.</summary>
        public const float EngageRangeSniper = 20f;

        /// <summary>Maximum range to consider a target valid.</summary>
        public const float MaxEngageRange = 30f;

        /// <summary>Minimum range before unit wants to back off.</summary>
        public const float MinEngageRange = 3f;

        // === COVER ===
        /// <summary>Max distance to search for cover positions.</summary>
        public const float CoverSearchRadius = 15f;

        /// <summary>How close unit must be to cover to be "in cover".</summary>
        public const float CoverProximity = 1.5f;

        /// <summary>Bonus score for full cover vs half cover.</summary>
        public const float FullCoverBonus = 10f;

        // === SUPPRESSION ===
        /// <summary>Suppression threshold to enter Pinned state.</summary>
        public const float SuppressionPinThreshold = 80f;

        /// <summary>Suppression decay per second.</summary>
        public const float SuppressionDecay = 5f;

        // === TIMING ===
        /// <summary>How often units re-evaluate their state (seconds).</summary>
        public const float EvaluationInterval = 0.5f;

        /// <summary>Minimum time in a state before switching (prevents flicker).</summary>
        public const float MinStateTime = 1f;

        /// <summary>Max paths to evaluate per frame when scoring destinations.</summary>
        public const int MaxPathsPerFrame = 10;

        // === MOVEMENT ===
        /// <summary>Distance threshold to consider "arrived" at destination.</summary>
        public const float ArrivalThreshold = 0.5f;

        /// <summary>How long to wait before re-pathing when stuck.</summary>
        public const float StuckTimeout = 2f;

        // === SCORING WEIGHTS ===
        /// <summary>Weight for distance in tile scoring (lower = prefers closer).</summary>
        public const float ScoreDistanceWeight = 1f;

        /// <summary>Weight for cover quality in tile scoring.</summary>
        public const float ScoreCoverWeight = 5f;

        /// <summary>Weight for line-of-sight to target in tile scoring.</summary>
        public const float ScoreLOSWeight = 3f;

        /// <summary>Weight for threat level in tile scoring (negative = avoids threat).</summary>
        public const float ScoreThreatWeight = -2f;
    }
}
