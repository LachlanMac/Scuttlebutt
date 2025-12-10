using UnityEngine;
using System;
using System.Collections.Generic;

namespace Starbelter.Core
{
    /// <summary>
    /// Represents a specific position/billet on the ship.
    /// Loaded from Positions.json.
    /// </summary>
    [Serializable]
    public class Position
    {
        public string Id;
        public string DisplayName;
        public Job Job;
        public Role[] RequiredRoles;
        public string[] Rooms;
        public ServiceBranch Branch;
        public bool IsOfficer;
        public int MinRank;
        public int MaxRank;
        public int CountPerShift;
        public bool RequiresContinuousManning;

        /// <summary>
        /// Check if a character could fill this position (ignoring roles - those are generated).
        /// </summary>
        public bool MatchesRequirements(ServiceBranch branch, bool isOfficer, int rank)
        {
            if (branch != Branch) return false;
            if (isOfficer != IsOfficer) return false;
            if (rank < MinRank || rank > MaxRank) return false;
            return true;
        }

        public override string ToString()
        {
            return $"{DisplayName} ({Job})";
        }
    }

    /// <summary>
    /// Loads and manages position definitions from JSON.
    /// </summary>
    public static class PositionRegistry
    {
        private static Dictionary<string, Position> positions;
        private static List<Position> allPositions;
        private static bool loaded = false;

        #region Public API

        /// <summary>
        /// Get a position by ID.
        /// </summary>
        public static Position Get(string id)
        {
            EnsureLoaded();
            return positions.TryGetValue(id, out var pos) ? pos : null;
        }

        /// <summary>
        /// Get all positions.
        /// </summary>
        public static IReadOnlyList<Position> GetAll()
        {
            EnsureLoaded();
            return allPositions;
        }

        /// <summary>
        /// Get all positions for a specific job.
        /// </summary>
        public static List<Position> GetByJob(Job job)
        {
            EnsureLoaded();
            var result = new List<Position>();
            foreach (var pos in allPositions)
            {
                if (pos.Job == job) result.Add(pos);
            }
            return result;
        }

        /// <summary>
        /// Get all positions in a specific room.
        /// </summary>
        public static List<Position> GetByRoom(string roomId)
        {
            EnsureLoaded();
            var result = new List<Position>();
            foreach (var pos in allPositions)
            {
                if (pos.Rooms != null)
                {
                    foreach (var room in pos.Rooms)
                    {
                        if (room.Equals(roomId, StringComparison.OrdinalIgnoreCase))
                        {
                            result.Add(pos);
                            break;
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Get positions that require continuous manning.
        /// </summary>
        public static List<Position> GetContinuousPositions()
        {
            EnsureLoaded();
            var result = new List<Position>();
            foreach (var pos in allPositions)
            {
                if (pos.RequiresContinuousManning) result.Add(pos);
            }
            return result;
        }

        /// <summary>
        /// Calculate total crew needed for a full ship complement (both shifts).
        /// </summary>
        public static int GetTotalCrewRequired()
        {
            EnsureLoaded();
            int total = 0;
            foreach (var pos in allPositions)
            {
                // Positions that require continuous manning need people on both shifts
                // Others might only need one person total (Captain, XO, etc.)
                if (pos.RequiresContinuousManning)
                {
                    total += pos.CountPerShift * 2; // Both shifts
                }
                else
                {
                    total += pos.CountPerShift; // Just one set
                }
            }
            return total;
        }

        /// <summary>
        /// Get a crew manifest (count of each position needed).
        /// </summary>
        public static Dictionary<string, int> GetCrewManifest(bool includeOffShift = true)
        {
            EnsureLoaded();
            var manifest = new Dictionary<string, int>();

            foreach (var pos in allPositions)
            {
                int count = pos.CountPerShift;
                if (includeOffShift && pos.RequiresContinuousManning)
                {
                    count *= 2;
                }
                manifest[pos.Id] = count;
            }

            return manifest;
        }

        #endregion

        #region Loading

        private static void EnsureLoaded()
        {
            if (loaded) return;
            Load();
        }

        private static void Load()
        {
            positions = new Dictionary<string, Position>();
            allPositions = new List<Position>();

            var jsonAsset = Resources.Load<TextAsset>("Data/Positions");
            if (jsonAsset == null)
            {
                Debug.LogError("[PositionRegistry] Positions.json not found in Resources/Data/");
                loaded = true;
                return;
            }

            var data = JsonUtility.FromJson<PositionsFile>(jsonAsset.text);
            if (data?.positions == null)
            {
                Debug.LogError("[PositionRegistry] Failed to parse Positions.json");
                loaded = true;
                return;
            }

            foreach (var entry in data.positions)
            {
                var position = new Position
                {
                    Id = entry.id,
                    DisplayName = entry.displayName,
                    Job = ParseJob(entry.job),
                    RequiredRoles = ParseRoles(entry.roles),
                    Rooms = entry.rooms ?? new string[0],
                    Branch = ParseBranch(entry.branch),
                    IsOfficer = entry.isOfficer,
                    MinRank = entry.minRank,
                    MaxRank = entry.maxRank,
                    CountPerShift = Mathf.Max(1, entry.countPerShift),
                    RequiresContinuousManning = entry.requiresContinuousManning
                };

                positions[entry.id] = position;
                allPositions.Add(position);
            }

            loaded = true;
            //Debug.Log($"[PositionRegistry] Loaded {allPositions.Count} positions, total crew required: {GetTotalCrewRequired()}");
        }

        private static Job ParseJob(string jobStr)
        {
            if (Enum.TryParse<Job>(jobStr, true, out var job))
                return job;
            Debug.LogWarning($"[PositionRegistry] Unknown job: {jobStr}");
            return Job.Marine;
        }

        private static ServiceBranch ParseBranch(string branchStr)
        {
            if (Enum.TryParse<ServiceBranch>(branchStr, true, out var branch))
                return branch;
            return ServiceBranch.Navy;
        }

        private static Role[] ParseRoles(string[] roleStrs)
        {
            if (roleStrs == null || roleStrs.Length == 0)
                return new Role[] { Role.None };

            var roles = new List<Role>();
            foreach (var roleStr in roleStrs)
            {
                if (Enum.TryParse<Role>(roleStr, true, out var role))
                {
                    roles.Add(role);
                }
                else
                {
                    Debug.LogWarning($"[PositionRegistry] Unknown role: {roleStr}");
                }
            }

            return roles.Count > 0 ? roles.ToArray() : new Role[] { Role.None };
        }

        /// <summary>
        /// Force reload (useful for editor hot-reloading).
        /// </summary>
        public static void Reload()
        {
            loaded = false;
            positions = null;
            allPositions = null;
            EnsureLoaded();
        }

        #endregion

        #region JSON Data Structures

        [Serializable]
        private class PositionsFile
        {
            public PositionEntry[] positions;
        }

        [Serializable]
        private class PositionEntry
        {
            public string id;
            public string displayName;
            public string job;
            public string[] roles;
            public string[] rooms;
            public string branch;
            public bool isOfficer;
            public int minRank;
            public int maxRank;
            public int countPerShift;
            public bool requiresContinuousManning;
        }

        #endregion
    }
}
