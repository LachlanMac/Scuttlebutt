using UnityEngine;

namespace Starbelter.Core
{
    /// <summary>
    /// Type of quarters this bed is in. Filters which positions can be assigned.
    /// </summary>
    public enum QuartersType
    {
        Any,                // No restriction
        Officer,            // Junior officers (O-1 to O-3)
        SeniorOfficer,      // Senior officers (O-4+)
        Enlisted,           // Junior enlisted Navy (E-1 to E-6)
        SeniorEnlisted,     // Senior enlisted / Chiefs (E-7+)
        Marine              // Marines (any rank)
    }

    /// <summary>
    /// A bed/bunk in the ship that generates and houses a crew member.
    /// Place in rooms and assign a position ID to auto-generate crew on start.
    /// </summary>
    public class Bed : MonoBehaviour
    {
        [Header("Position Assignment")]
        [Tooltip("Type of quarters - filters which positions can be assigned")]
        [SerializeField] private QuartersType quartersType = QuartersType.Enlisted;

        [Tooltip("Position ID from Positions.json")]
        [SerializeField, PositionId] private string positionId;

        [Tooltip("Which shift this crew member works")]
        [SerializeField] private Shift shift = Shift.Main;

        [Header("Generation Settings")]
        [Tooltip("Optional seed for deterministic generation. -1 = random.")]
        [SerializeField] private int seed = -1;

        [Header("Runtime Info (Read Only)")]
        [SerializeField] private string assignedCrewName;
        [SerializeField] private string assignedCrewRoles;

        // Runtime state
        private CrewMember assignedCrew;
        private Position position;

        // Public properties
        public CrewMember AssignedCrew => assignedCrew;
        public Position Position => position;
        public string PositionId => positionId;
        public Shift Shift => shift;
        public QuartersType QuartersType => quartersType;
        public bool IsOccupied => assignedCrew != null;

        /// <summary>
        /// Generate a crew member for this bed based on the assigned position.
        /// Called by CrewManager.
        /// </summary>
        public CrewMember GenerateCrew()
        {
            if (string.IsNullOrEmpty(positionId))
            {
                Debug.LogWarning($"[Bed] {gameObject.name}: No position ID assigned");
                return null;
            }

            // Load position definition
            position = PositionRegistry.Get(positionId);
            if (position == null)
            {
                Debug.LogError($"[Bed] {gameObject.name}: Position '{positionId}' not found in Positions.json");
                return null;
            }

            // Generate crew member
            int? seedValue = seed >= 0 ? seed : null;
            assignedCrew = CharacterFactory.GenerateForPosition(position, shift, seedValue);

            if (assignedCrew != null)
            {
                // Update editor display
                assignedCrewName = assignedCrew.RankAndName;
                assignedCrewRoles = assignedCrew.GetRolesString();
            }

            return assignedCrew;
        }

        /// <summary>
        /// Assign an existing crew member to this bed.
        /// </summary>
        public void AssignCrew(CrewMember crew)
        {
            assignedCrew = crew;
            if (crew != null)
            {
                assignedCrewName = crew.RankAndName;
                assignedCrewRoles = crew.GetRolesString();
            }
            else
            {
                assignedCrewName = "";
                assignedCrewRoles = "";
            }
        }

        /// <summary>
        /// Clear the assigned crew member.
        /// </summary>
        public void ClearCrew()
        {
            assignedCrew = null;
            assignedCrewName = "";
            assignedCrewRoles = "";
        }

        /// <summary>
        /// Regenerate with a new random seed.
        /// </summary>
        public void Regenerate()
        {
            seed = -1;
            GenerateCrew();
        }

        #region Editor Helpers

        private void OnValidate()
        {
            // Update display when position changes in editor
            if (!Application.isPlaying && !string.IsNullOrEmpty(positionId))
            {
#if UNITY_EDITOR
                PositionRegistry.Reload();
#endif
                var pos = PositionRegistry.Get(positionId);
                if (pos != null)
                {
                    gameObject.name = $"Bed_{pos.DisplayName}_{shift}";
                }
            }
        }

        private void OnDrawGizmos()
        {
            // Draw bed indicator
            Gizmos.color = IsOccupied ? Color.green : Color.yellow;
            Gizmos.DrawWireCube(transform.position, new Vector3(0.8f, 0.3f, 1.5f));
        }

        private void OnDrawGizmosSelected()
        {
            // Show more info when selected
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(transform.position, new Vector3(0.9f, 0.35f, 1.6f));

#if UNITY_EDITOR
            // Draw label with position info
            string label = string.IsNullOrEmpty(positionId) ? "No Position" : positionId;
            if (IsOccupied)
            {
                label = $"{assignedCrewName}\n{assignedCrewRoles}";
            }
            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.5f, label);
#endif
        }

        #endregion
    }
}
