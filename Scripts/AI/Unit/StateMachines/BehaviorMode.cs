namespace Starbelter.AI
{
    /// <summary>
    /// High-level behavior mode that determines which state set is active.
    /// Mode transitions are triggered by perception and threat detection.
    /// </summary>
    public enum BehaviorMode
    {
        /// <summary>
        /// Personal time - relaxed, casual behavior.
        /// States: Idle, Wander, Socialize, Rest
        /// </summary>
        OffDuty,

        /// <summary>
        /// Working assigned role - professional, routine behavior.
        /// States: Work, Patrol, Guard, StandWatch
        /// </summary>
        OnDuty,

        /// <summary>
        /// Something suspicious detected - investigating.
        /// States: Investigate, Search, Regroup, Report
        /// </summary>
        Alert,

        /// <summary>
        /// Confirmed threat - full combat.
        /// States: Ready, Combat, Moving, Pinned, Reload, SeekCover, Flank, Suppress
        /// </summary>
        Combat
    }
}
