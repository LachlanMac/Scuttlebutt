using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace Starbelter.Strategic
{
    /// <summary>
    /// Tracks faction claims on a single chunk.
    /// Multiple factions can claim the same chunk with different strengths.
    /// </summary>
    [System.Serializable]
    public class ChunkClaim
    {
        // FactionId -> claim strength
        private Dictionary<FactionId, ClaimEntry> claims = new Dictionary<FactionId, ClaimEntry>();

        public bool HasAnyClaims => claims.Count > 0;
        public bool IsContested => claims.Count > 1;
        public int ClaimCount => claims.Count;

        /// <summary>
        /// Add or update a claim on this chunk.
        /// </summary>
        public void AddClaim(FactionId faction, int strength, string stationId)
        {
            if (faction == FactionId.None) return;
            if (strength <= 0) return;

            if (claims.TryGetValue(faction, out var existing))
            {
                // Keep the stronger claim
                if (strength > existing.strength)
                {
                    existing.strength = strength;
                    existing.stationId = stationId;
                }
            }
            else
            {
                claims[faction] = new ClaimEntry { strength = strength, stationId = stationId };
            }
        }

        /// <summary>
        /// Remove all claims from a specific station.
        /// </summary>
        public void RemoveClaimsFromStation(string stationId)
        {
            var toRemove = claims.Where(kvp => kvp.Value.stationId == stationId).Select(kvp => kvp.Key).ToList();
            foreach (var faction in toRemove)
            {
                claims.Remove(faction);
            }
        }

        /// <summary>
        /// Clear all claims on this chunk.
        /// </summary>
        public void ClearClaims()
        {
            claims.Clear();
        }

        /// <summary>
        /// Get the dominant faction (highest claim strength).
        /// Returns None if no claims or tied.
        /// </summary>
        public FactionId GetDominantFaction()
        {
            if (claims.Count == 0) return FactionId.None;
            if (claims.Count == 1) return claims.Keys.First();

            var sorted = claims.OrderByDescending(kvp => kvp.Value.strength).ToList();
            if (sorted[0].Value.strength > sorted[1].Value.strength)
                return sorted[0].Key;

            return FactionId.None; // Tied = contested
        }

        /// <summary>
        /// Get claim strength for a specific faction.
        /// </summary>
        public int GetClaimStrength(FactionId faction)
        {
            return claims.TryGetValue(faction, out var entry) ? entry.strength : 0;
        }

        /// <summary>
        /// Get all claims on this chunk.
        /// </summary>
        public IEnumerable<(FactionId faction, int strength, string stationId)> GetAllClaims()
        {
            foreach (var kvp in claims)
            {
                yield return (kvp.Key, kvp.Value.strength, kvp.Value.stationId);
            }
        }

        /// <summary>
        /// Check if two specific factions are contesting this chunk.
        /// </summary>
        public bool IsContestedBetween(FactionId a, FactionId b)
        {
            return claims.ContainsKey(a) && claims.ContainsKey(b);
        }

        [System.Serializable]
        private class ClaimEntry
        {
            public int strength;
            public string stationId;
        }
    }

    /// <summary>
    /// Defines how far different station types project their claims.
    /// Each station type has fixed influence characteristics.
    /// </summary>
    public static class StationInfluence
    {
        /// <summary>
        /// Get the base claim strength for a station type.
        /// </summary>
        public static int GetBaseStrength(StationType stationType)
        {
            return stationType switch
            {
                // Military - high influence
                StationType.FleetHQ => 20,
                StationType.Bastion => 15,
                StationType.MilitaryShipyard => 12,
                StationType.Base => 10,
                StationType.Outpost => 5,
                StationType.ListeningPost => 3,

                // Economic - moderate influence
                StationType.CommercialHub => 8,
                StationType.CivilianShipyard => 6,
                StationType.IndustrialStation => 5,
                StationType.MiningStation => 3,

                // Civilian - low influence
                StationType.Spaceport => 6,
                StationType.OrbitalHabitat => 4,

                // Science - minimal influence
                StationType.ResearchStation => 2,
                StationType.Observatory => 1,

                // Other
                StationType.PirateHaven => 4,

                _ => 1
            };
        }

        /// <summary>
        /// Get the influence radius in chunks for a station type.
        /// </summary>
        public static int GetInfluenceRadius(StationType stationType)
        {
            return stationType switch
            {
                // Military - project far
                StationType.FleetHQ => 5,
                StationType.Bastion => 4,
                StationType.MilitaryShipyard => 3,
                StationType.Base => 3,
                StationType.Outpost => 2,
                StationType.ListeningPost => 1,

                // Economic - moderate reach
                StationType.CommercialHub => 3,
                StationType.CivilianShipyard => 2,
                StationType.IndustrialStation => 2,
                StationType.MiningStation => 1,

                // Civilian - local reach
                StationType.Spaceport => 2,
                StationType.OrbitalHabitat => 1,

                // Science - very local
                StationType.ResearchStation => 1,
                StationType.Observatory => 1,

                // Other
                StationType.PirateHaven => 2,

                _ => 1
            };
        }

        /// <summary>
        /// Calculate claim strength at a given distance from station.
        /// Strength falls off with distance.
        /// </summary>
        public static int CalculateStrengthAtDistance(int baseStrength, int distance, int maxRadius)
        {
            if (distance > maxRadius) return 0;
            if (distance == 0) return baseStrength;

            // Linear falloff
            float falloff = 1f - ((float)distance / (maxRadius + 1));
            return Mathf.Max(1, Mathf.RoundToInt(baseStrength * falloff));
        }
    }

    /// <summary>
    /// Analysis results for a sector's claim state.
    /// </summary>
    public class SectorClaimAnalysis
    {
        public string sectorId;

        // Chunk counts by faction
        public Dictionary<FactionId, int> controlledChunks = new Dictionary<FactionId, int>();
        public int contestedChunks;
        public int unclaimedChunks;
        public int totalChunks;

        // Hotspots (contested chunks between specific faction pairs)
        public List<Vector2Int> hotspotChunks = new List<Vector2Int>();

        // Strategic assessment
        public FactionId dominantFaction;
        public float dominancePercent;
        public bool isFrontLine;  // Multiple major factions present

        /// <summary>
        /// Get control percentage for a faction.
        /// </summary>
        public float GetControlPercent(FactionId faction)
        {
            if (!controlledChunks.TryGetValue(faction, out int count)) return 0;
            return totalChunks > 0 ? (float)count / totalChunks * 100f : 0;
        }

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Sector: {sectorId}");
            sb.AppendLine($"Contested: {contestedChunks}, Unclaimed: {unclaimedChunks}");

            foreach (var kvp in controlledChunks.OrderByDescending(x => x.Value))
            {
                var faction = Factions.Get(kvp.Key);
                float percent = GetControlPercent(kvp.Key);
                sb.AppendLine($"  {faction?.shortName ?? kvp.Key.ToString()}: {kvp.Value} chunks ({percent:F1}%)");
            }

            if (isFrontLine)
                sb.AppendLine("STATUS: FRONT LINE SECTOR");

            return sb.ToString();
        }
    }
}
