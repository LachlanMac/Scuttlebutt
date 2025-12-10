using UnityEngine;
using System.Collections.Generic;

namespace Starbelter.Core
{
    /// <summary>
    /// A physical location where work happens.
    /// Attached to GameObjects in rooms (consoles, workbenches, etc.)
    /// </summary>
    public class DutyStation : MonoBehaviour
    {
        [Header("Station Identity")]
        [Tooltip("Display name for this station")]
        [SerializeField] private string stationName = "Duty Station";

        [Tooltip("What room/area this station belongs to")]
        [SerializeField] private string roomId;

        [Header("Job Requirements")]
        [Tooltip("What job is required to use this station")]
        [SerializeField] private Job requiredJob;

        [Tooltip("Specific role required (None = any role in that job)")]
        [SerializeField] private Role requiredRole = Role.None;

        [Header("Capacity")]
        [Tooltip("How many crew can work here simultaneously")]
        [SerializeField] private int capacity = 1;

        [Header("Station Behavior")]
        [Tooltip("Must this station always be manned?")]
        [SerializeField] private bool requiresContinuousManning = false;

        [Tooltip("Position offset where crew stands/sits")]
        [SerializeField] private Vector3 workPositionOffset = Vector3.zero;

        [Tooltip("Direction crew faces while working")]
        [SerializeField] private Vector2 workFacingDirection = Vector2.down;

        // Runtime state
        private List<CrewMember> currentOccupants = new List<CrewMember>();

        // Public properties
        public string StationName => stationName;
        public string RoomId => roomId;
        public Job RequiredJob => requiredJob;
        public Role RequiredRole => requiredRole;
        public int Capacity => capacity;
        public bool RequiresContinuousManning => requiresContinuousManning;
        public Vector3 WorkPosition => transform.position + workPositionOffset;
        public Vector2 WorkFacingDirection => workFacingDirection;
        public int OccupantCount => currentOccupants.Count;
        public bool IsFull => currentOccupants.Count >= capacity;
        public bool IsEmpty => currentOccupants.Count == 0;
        public bool NeedsManning => requiresContinuousManning && IsEmpty;
        public IReadOnlyList<CrewMember> Occupants => currentOccupants;

        /// <summary>
        /// Check if a crew member can use this station.
        /// </summary>
        public bool CanUse(CrewMember crew)
        {
            if (crew == null) return false;
            if (IsFull) return false;

            // Check job requirement
            if (crew.AssignedJob != requiredJob) return false;

            // Check role requirement (if specified)
            if (requiredRole != Role.None)
            {
                if (!crew.HasRole(requiredRole)) return false;
            }

            return true;
        }

        /// <summary>
        /// Check if a crew member can use this station based on their Character data.
        /// Used before CrewMember assignment is finalized.
        /// </summary>
        public bool CanUse(Job job, Role[] roles)
        {
            if (IsFull) return false;
            if (job != requiredJob) return false;

            if (requiredRole != Role.None)
            {
                bool hasRole = false;
                foreach (var role in roles)
                {
                    if (role == requiredRole)
                    {
                        hasRole = true;
                        break;
                    }
                }
                if (!hasRole) return false;
            }

            return true;
        }

        /// <summary>
        /// Assign a crew member to this station.
        /// Returns true if successful.
        /// </summary>
        public bool Occupy(CrewMember crew)
        {
            if (!CanUse(crew))
            {
                Debug.LogWarning($"[DutyStation] {crew?.Name ?? "null"} cannot use {stationName}");
                return false;
            }

            if (currentOccupants.Contains(crew))
            {
                Debug.LogWarning($"[DutyStation] {crew.Name} is already at {stationName}");
                return false;
            }

            currentOccupants.Add(crew);
            Debug.Log($"[DutyStation] {crew.Name} now manning {stationName}");
            return true;
        }

        /// <summary>
        /// Remove a crew member from this station.
        /// </summary>
        public bool Vacate(CrewMember crew)
        {
            if (!currentOccupants.Contains(crew))
            {
                return false;
            }

            currentOccupants.Remove(crew);
            Debug.Log($"[DutyStation] {crew.Name} left {stationName}");

            if (requiresContinuousManning && IsEmpty)
            {
                Debug.LogWarning($"[DutyStation] {stationName} is now unmanned!");
            }

            return true;
        }

        /// <summary>
        /// Get the first occupant (for single-capacity stations).
        /// </summary>
        public CrewMember GetOccupant()
        {
            return currentOccupants.Count > 0 ? currentOccupants[0] : null;
        }

        /// <summary>
        /// Clear all occupants (use with caution).
        /// </summary>
        public void ClearOccupants()
        {
            currentOccupants.Clear();
        }

        #region Editor Helpers

        private void OnValidate()
        {
            // Auto-generate station name if empty
            if (string.IsNullOrEmpty(stationName))
            {
                stationName = $"{requiredJob} Station";
            }
        }

        private void OnDrawGizmos()
        {
            // Draw work position
            Gizmos.color = IsFull ? Color.red : (IsEmpty ? Color.green : Color.yellow);
            Gizmos.DrawWireSphere(WorkPosition, 0.25f);

            // Draw facing direction
            Gizmos.color = Color.blue;
            Vector3 facing = new Vector3(workFacingDirection.x, workFacingDirection.y, 0).normalized;
            Gizmos.DrawRay(WorkPosition, facing * 0.5f);
        }

        private void OnDrawGizmosSelected()
        {
            // Draw station info when selected
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(WorkPosition, Vector3.one * 0.5f);
        }

        #endregion
    }
}
