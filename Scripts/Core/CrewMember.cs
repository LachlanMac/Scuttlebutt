using UnityEngine;
using System;

namespace Starbelter.Core
{
    /// <summary>
    /// Represents a crew member on the ship.
    /// Wraps a Character and adds job, shift, and role assignments.
    /// </summary>
    [Serializable]
    public class CrewMember
    {
        [Header("Character Data")]
        [SerializeField] private Character character;

        [Header("Assignment")]
        [SerializeField] private Job assignedJob;
        [SerializeField] private Shift assignedShift;
        [SerializeField] private Role[] roles = new Role[] { Role.None };

        [Header("Duty Station")]
        [SerializeField] private string homeDutyStationId;  // Where they report to by default

        // Runtime state (not serialized)
        [NonSerialized] private DutyStation currentStation;
        [NonSerialized] private bool isOnDuty;

        #region Properties

        public Character Character => character;
        public string Name => character?.FullName ?? "Unknown";
        public string RankAndName => character?.RankAndName ?? "Unknown";
        public Job AssignedJob => assignedJob;
        public Shift AssignedShift => assignedShift;
        public Role[] Roles => roles;
        public Role PrimaryRole => roles != null && roles.Length > 0 ? roles[0] : Role.None;
        public string HomeDutyStationId => homeDutyStationId;
        public DutyStation CurrentStation => currentStation;
        public bool IsOnDuty => isOnDuty;
        public bool IsAtStation => currentStation != null;

        // Pass-through to Character
        public ServiceBranch Branch => character?.Branch ?? ServiceBranch.Navy;
        public bool IsOfficer => character?.IsOfficer ?? false;
        public int Rank => character?.Rank ?? 1;
        public bool IsDead => character?.IsDead ?? false;

        #endregion

        #region Constructors

        public CrewMember()
        {
            character = new Character();
            roles = new Role[] { Role.None };
        }

        public CrewMember(Character character, Job job, Shift shift, params Role[] assignedRoles)
        {
            this.character = character;
            this.assignedJob = job;
            this.assignedShift = shift;
            this.roles = assignedRoles.Length > 0 ? assignedRoles : new Role[] { Role.None };
        }

        #endregion

        #region Role Management

        /// <summary>
        /// Check if this crew member has a specific role/qualification.
        /// </summary>
        public bool HasRole(Role role)
        {
            if (role == Role.None) return true;
            if (roles == null) return false;

            foreach (var r in roles)
            {
                if (r == role) return true;
            }
            return false;
        }

        /// <summary>
        /// Add a role to this crew member's qualifications.
        /// </summary>
        public void AddRole(Role role)
        {
            if (HasRole(role)) return;

            var newRoles = new Role[roles.Length + 1];
            roles.CopyTo(newRoles, 0);
            newRoles[roles.Length] = role;
            roles = newRoles;
        }

        /// <summary>
        /// Remove a role from this crew member's qualifications.
        /// </summary>
        public void RemoveRole(Role role)
        {
            if (!HasRole(role)) return;

            var newRoles = new Role[roles.Length - 1];
            int index = 0;
            foreach (var r in roles)
            {
                if (r != role)
                {
                    newRoles[index++] = r;
                }
            }
            roles = newRoles;
        }

        /// <summary>
        /// Get a formatted string of all roles.
        /// </summary>
        public string GetRolesString()
        {
            if (roles == null || roles.Length == 0 || (roles.Length == 1 && roles[0] == Role.None))
                return "None";

            return string.Join(", ", roles);
        }

        #endregion

        #region Duty Station Management

        /// <summary>
        /// Assign this crew member to a duty station.
        /// </summary>
        public bool AssignToStation(DutyStation station)
        {
            if (station == null) return false;

            // Leave current station first
            if (currentStation != null)
            {
                LeaveStation();
            }

            // Try to occupy the new station
            if (station.Occupy(this))
            {
                currentStation = station;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Leave the current duty station.
        /// </summary>
        public void LeaveStation()
        {
            if (currentStation != null)
            {
                currentStation.Vacate(this);
                currentStation = null;
            }
        }

        /// <summary>
        /// Set the home duty station ID (where they report by default).
        /// </summary>
        public void SetHomeDutyStation(string stationId)
        {
            homeDutyStationId = stationId;
        }

        #endregion

        #region Shift Management

        /// <summary>
        /// Start duty shift.
        /// </summary>
        public void StartShift()
        {
            isOnDuty = true;
            Debug.Log($"[CrewMember] {RankAndName} starting {assignedShift} shift");
        }

        /// <summary>
        /// End duty shift.
        /// </summary>
        public void EndShift()
        {
            isOnDuty = false;
            LeaveStation();
            Debug.Log($"[CrewMember] {RankAndName} ending shift");
        }

        /// <summary>
        /// Check if this crew member should be on duty based on current shift.
        /// </summary>
        public bool ShouldBeOnDuty(Shift currentShift)
        {
            return assignedShift == currentShift;
        }

        #endregion

        #region Job Validation

        /// <summary>
        /// Check if this crew member qualifies for a specific job+role combination.
        /// </summary>
        public bool QualifiesFor(Job job, Role role = Role.None)
        {
            var definition = JobDefinitions.Get(job);
            if (definition == null) return false;

            return definition.QualifiesFor(character, role) && HasRole(role);
        }

        /// <summary>
        /// Check if assignment is valid (job matches character qualifications).
        /// </summary>
        public bool IsValidAssignment()
        {
            var definition = JobDefinitions.Get(assignedJob);
            if (definition == null) return false;

            // Check basic requirements
            if (character.Branch != definition.Branch) return false;
            if (character.IsOfficer != definition.RequiresOfficer) return false;
            if (character.Rank < definition.MinRank || character.Rank > definition.MaxRank) return false;

            // Check if at least one role matches
            foreach (var role in roles)
            {
                if (definition.IsRoleAllowed(role)) return true;
            }

            return false;
        }

        #endregion

        #region Display

        /// <summary>
        /// Get a summary string for debugging/display.
        /// </summary>
        public override string ToString()
        {
            return $"{RankAndName} - {assignedJob} ({GetRolesString()}) [{assignedShift} Shift]";
        }

        /// <summary>
        /// Get detailed info for UI/debugging.
        /// </summary>
        public string GetDetailedInfo()
        {
            var info = $"{RankAndName}\n";
            info += $"Job: {assignedJob}\n";
            info += $"Roles: {GetRolesString()}\n";
            info += $"Shift: {assignedShift}\n";
            info += $"Status: {(isOnDuty ? "On Duty" : "Off Duty")}\n";
            if (currentStation != null)
            {
                info += $"Station: {currentStation.StationName}\n";
            }
            return info;
        }

        #endregion
    }
}
