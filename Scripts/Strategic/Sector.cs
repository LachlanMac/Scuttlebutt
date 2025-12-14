using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace Starbelter.Strategic
{
    /// <summary>
    /// A sector of space - the primary map unit.
    /// 100,000 x 100,000 units divided into 10x10 chunks of 10,000 units each.
    /// </summary>
    [System.Serializable]
    public class Sector
    {
        public const float SECTOR_SIZE = 100000f;
        public const float HALF_SECTOR = 50000f;
        public const float CHUNK_SIZE = 10000f;
        public const int CHUNKS_PER_AXIS = 10;

        [Header("Identity")]
        public string id;
        public string displayName;
        public SectorType type;
        public FactionId controlledBy;
        public Vector2Int galaxyCoord; // Position in galaxy grid [0-9, 0-9]

        // Runtime - POIs indexed by chunk coordinate
        // chunkPOIs[x,y] contains list of POIs in that chunk
        private List<PointOfInterest>[,] chunkPOIs;

        // Claim tracking per chunk
        private ChunkClaim[,] chunkClaims;

        // All POIs in this sector (flat list for iteration)
        private List<PointOfInterest> allPOIs = new List<PointOfInterest>();

        // All ships currently in this sector
        private List<ShipRecord> shipsPresent = new List<ShipRecord>();

        public IReadOnlyList<PointOfInterest> AllPOIs => allPOIs;
        public IReadOnlyList<ShipRecord> ShipsPresent => shipsPresent;

        public Sector(string id, string name, SectorType type, Vector2Int galaxyCoord = default)
        {
            this.id = id;
            this.displayName = name;
            this.type = type;
            this.galaxyCoord = galaxyCoord;
            InitializeChunks();
        }

        private void InitializeChunks()
        {
            chunkPOIs = new List<PointOfInterest>[CHUNKS_PER_AXIS, CHUNKS_PER_AXIS];
            chunkClaims = new ChunkClaim[CHUNKS_PER_AXIS, CHUNKS_PER_AXIS];
            for (int x = 0; x < CHUNKS_PER_AXIS; x++)
            {
                for (int y = 0; y < CHUNKS_PER_AXIS; y++)
                {
                    chunkPOIs[x, y] = new List<PointOfInterest>();
                    chunkClaims[x, y] = new ChunkClaim();
                }
            }
        }

        #region Coordinate Conversion

        /// <summary>
        /// Convert world position to chunk coordinates (0-9, 0-9).
        /// </summary>
        public static Vector2Int WorldToChunk(Vector2 worldPos)
        {
            int x = Mathf.FloorToInt((worldPos.x + HALF_SECTOR) / CHUNK_SIZE);
            int y = Mathf.FloorToInt((worldPos.y + HALF_SECTOR) / CHUNK_SIZE);
            x = Mathf.Clamp(x, 0, CHUNKS_PER_AXIS - 1);
            y = Mathf.Clamp(y, 0, CHUNKS_PER_AXIS - 1);
            return new Vector2Int(x, y);
        }

        /// <summary>
        /// Get the center world position of a chunk.
        /// </summary>
        public static Vector2 ChunkToWorldCenter(Vector2Int chunk)
        {
            float x = -HALF_SECTOR + (chunk.x * CHUNK_SIZE) + (CHUNK_SIZE / 2f);
            float y = -HALF_SECTOR + (chunk.y * CHUNK_SIZE) + (CHUNK_SIZE / 2f);
            return new Vector2(x, y);
        }

        /// <summary>
        /// Get the world bounds of a chunk.
        /// </summary>
        public static Rect ChunkToWorldBounds(Vector2Int chunk)
        {
            float x = -HALF_SECTOR + (chunk.x * CHUNK_SIZE);
            float y = -HALF_SECTOR + (chunk.y * CHUNK_SIZE);
            return new Rect(x, y, CHUNK_SIZE, CHUNK_SIZE);
        }

        /// <summary>
        /// Check if a world position is within sector bounds.
        /// </summary>
        public static bool IsInBounds(Vector2 worldPos)
        {
            return worldPos.x >= -HALF_SECTOR && worldPos.x <= HALF_SECTOR &&
                   worldPos.y >= -HALF_SECTOR && worldPos.y <= HALF_SECTOR;
        }

        #endregion

        #region POI Management

        /// <summary>
        /// Add a POI to the sector at a specific chunk.
        /// POI will be centered in the chunk.
        /// </summary>
        public void AddPOI(PointOfInterest poi, Vector2Int chunk)
        {
            if (!IsValidChunk(chunk))
            {
                Debug.LogError($"[Sector] Invalid chunk coordinates: {chunk}");
                return;
            }

            poi.chunkCoord = chunk;
            poi.position = ChunkToWorldCenter(chunk);
            poi.sector = this;

            chunkPOIs[chunk.x, chunk.y].Add(poi);
            allPOIs.Add(poi);
        }

        /// <summary>
        /// Add a POI at a specific world position.
        /// </summary>
        public void AddPOIAtPosition(PointOfInterest poi, Vector2 worldPos)
        {
            Vector2Int chunk = WorldToChunk(worldPos);
            poi.chunkCoord = chunk;
            poi.position = worldPos;
            poi.sector = this;

            chunkPOIs[chunk.x, chunk.y].Add(poi);
            allPOIs.Add(poi);
        }

        /// <summary>
        /// Remove a POI from the sector.
        /// </summary>
        public void RemovePOI(PointOfInterest poi)
        {
            if (poi.sector != this) return;

            chunkPOIs[poi.chunkCoord.x, poi.chunkCoord.y].Remove(poi);
            allPOIs.Remove(poi);
            poi.sector = null;
        }

        /// <summary>
        /// Get all POIs in a specific chunk.
        /// </summary>
        public IReadOnlyList<PointOfInterest> GetPOIsInChunk(Vector2Int chunk)
        {
            if (!IsValidChunk(chunk)) return new List<PointOfInterest>();
            return chunkPOIs[chunk.x, chunk.y];
        }

        /// <summary>
        /// Get all POIs within range of a world position.
        /// </summary>
        public List<PointOfInterest> GetPOIsInRange(Vector2 worldPos, float range)
        {
            var results = new List<PointOfInterest>();
            float rangeSq = range * range;

            foreach (var poi in allPOIs)
            {
                if ((poi.position - worldPos).sqrMagnitude <= rangeSq)
                {
                    results.Add(poi);
                }
            }

            return results;
        }

        /// <summary>
        /// Find the nearest POI of a specific type.
        /// </summary>
        public T FindNearestPOI<T>(Vector2 worldPos) where T : PointOfInterest
        {
            T nearest = null;
            float nearestDistSq = float.MaxValue;

            foreach (var poi in allPOIs)
            {
                if (poi is T typedPOI)
                {
                    float distSq = (poi.position - worldPos).sqrMagnitude;
                    if (distSq < nearestDistSq)
                    {
                        nearestDistSq = distSq;
                        nearest = typedPOI;
                    }
                }
            }

            return nearest;
        }

        private bool IsValidChunk(Vector2Int chunk)
        {
            return chunk.x >= 0 && chunk.x < CHUNKS_PER_AXIS &&
                   chunk.y >= 0 && chunk.y < CHUNKS_PER_AXIS;
        }

        #endregion

        #region Ship Management

        public void AddShip(ShipRecord ship)
        {
            if (!shipsPresent.Contains(ship))
            {
                shipsPresent.Add(ship);
                ship.currentSector = this;
            }
        }

        public void RemoveShip(ShipRecord ship)
        {
            shipsPresent.Remove(ship);
            if (ship.currentSector == this)
                ship.currentSector = null;
        }

        public List<ShipRecord> GetShipsInRange(Vector2 worldPos, float range)
        {
            var results = new List<ShipRecord>();
            float rangeSq = range * range;

            foreach (var ship in shipsPresent)
            {
                if ((ship.position - worldPos).sqrMagnitude <= rangeSq)
                {
                    results.Add(ship);
                }
            }

            return results;
        }

        #endregion

        #region Gravity Wells

        /// <summary>
        /// Check if a position is within any gravity well (too close to planet for jump).
        /// </summary>
        public bool IsInGravityWell(Vector2 worldPos)
        {
            foreach (var poi in allPOIs)
            {
                if (poi is Planet planet)
                {
                    float dist = Vector2.Distance(worldPos, planet.position);
                    if (dist < planet.gravityWellRadius)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Get the nearest valid jump position from a given position.
        /// Moves outward from gravity wells if needed.
        /// </summary>
        public Vector2 GetNearestJumpPoint(Vector2 fromPos)
        {
            if (!IsInGravityWell(fromPos))
                return fromPos;

            // Find the planet we're in the well of
            foreach (var poi in allPOIs)
            {
                if (poi is Planet planet)
                {
                    float dist = Vector2.Distance(fromPos, planet.position);
                    if (dist < planet.gravityWellRadius)
                    {
                        // Move outward to edge of gravity well
                        Vector2 direction = (fromPos - planet.position).normalized;
                        return planet.position + direction * (planet.gravityWellRadius + 100f);
                    }
                }
            }

            return fromPos;
        }

        #endregion

        #region Claims

        /// <summary>
        /// Get the claim data for a specific chunk.
        /// </summary>
        public ChunkClaim GetChunkClaim(Vector2Int chunk)
        {
            if (!IsValidChunk(chunk)) return null;
            return chunkClaims[chunk.x, chunk.y];
        }

        /// <summary>
        /// Get the claim data for a world position.
        /// </summary>
        public ChunkClaim GetClaimAtPosition(Vector2 worldPos)
        {
            return GetChunkClaim(WorldToChunk(worldPos));
        }

        /// <summary>
        /// Project claims from a station into surrounding chunks.
        /// Call this when a station is added or ownership changes.
        /// </summary>
        public void ProjectStationClaims(Station station)
        {
            if (station.controlledBy == FactionId.None) return;

            int radius = StationInfluence.GetInfluenceRadius(station.stationType);
            int baseStrength = StationInfluence.GetBaseStrength(station.stationType);

            // Project claims in a circle around the station
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int chunkX = station.chunkCoord.x + dx;
                    int chunkY = station.chunkCoord.y + dy;

                    if (chunkX < 0 || chunkX >= CHUNKS_PER_AXIS) continue;
                    if (chunkY < 0 || chunkY >= CHUNKS_PER_AXIS) continue;

                    int distance = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)); // Chebyshev distance
                    int strength = StationInfluence.CalculateStrengthAtDistance(baseStrength, distance, radius);

                    if (strength > 0)
                    {
                        chunkClaims[chunkX, chunkY].AddClaim(station.controlledBy, strength, station.id);
                    }
                }
            }
        }

        /// <summary>
        /// Remove all claims from a specific station.
        /// Call this when a station is destroyed or changes hands.
        /// </summary>
        public void RemoveStationClaims(string stationId)
        {
            for (int x = 0; x < CHUNKS_PER_AXIS; x++)
            {
                for (int y = 0; y < CHUNKS_PER_AXIS; y++)
                {
                    chunkClaims[x, y].RemoveClaimsFromStation(stationId);
                }
            }
        }

        /// <summary>
        /// Recalculate all claims from all stations in the sector.
        /// </summary>
        public void RecalculateAllClaims()
        {
            // Clear existing claims
            for (int x = 0; x < CHUNKS_PER_AXIS; x++)
            {
                for (int y = 0; y < CHUNKS_PER_AXIS; y++)
                {
                    chunkClaims[x, y].ClearClaims();
                }
            }

            // Project claims from all stations
            foreach (var poi in allPOIs)
            {
                if (poi is Station station)
                {
                    ProjectStationClaims(station);
                }
            }
        }

        /// <summary>
        /// Analyze the claim state of this sector.
        /// </summary>
        public SectorClaimAnalysis AnalyzeClaims()
        {
            var analysis = new SectorClaimAnalysis
            {
                sectorId = id,
                totalChunks = CHUNKS_PER_AXIS * CHUNKS_PER_AXIS
            };

            for (int x = 0; x < CHUNKS_PER_AXIS; x++)
            {
                for (int y = 0; y < CHUNKS_PER_AXIS; y++)
                {
                    var claim = chunkClaims[x, y];

                    if (!claim.HasAnyClaims)
                    {
                        analysis.unclaimedChunks++;
                    }
                    else if (claim.IsContested)
                    {
                        analysis.contestedChunks++;
                        analysis.hotspotChunks.Add(new Vector2Int(x, y));
                    }
                    else
                    {
                        var dominant = claim.GetDominantFaction();
                        if (!analysis.controlledChunks.ContainsKey(dominant))
                            analysis.controlledChunks[dominant] = 0;
                        analysis.controlledChunks[dominant]++;
                    }
                }
            }

            // Determine dominant faction
            if (analysis.controlledChunks.Count > 0)
            {
                var sorted = analysis.controlledChunks.OrderByDescending(kvp => kvp.Value).ToList();
                analysis.dominantFaction = sorted[0].Key;
                analysis.dominancePercent = analysis.GetControlPercent(sorted[0].Key);

                // Front line if multiple major factions present (>10% each)
                int majorFactions = analysis.controlledChunks.Count(kvp =>
                    analysis.GetControlPercent(kvp.Key) > 10f);
                analysis.isFrontLine = majorFactions >= 2 || analysis.contestedChunks > 20;
            }

            return analysis;
        }

        /// <summary>
        /// Get all contested chunks between two specific factions.
        /// </summary>
        public List<Vector2Int> GetContestedChunks(FactionId factionA, FactionId factionB)
        {
            var contested = new List<Vector2Int>();

            for (int x = 0; x < CHUNKS_PER_AXIS; x++)
            {
                for (int y = 0; y < CHUNKS_PER_AXIS; y++)
                {
                    if (chunkClaims[x, y].IsContestedBetween(factionA, factionB))
                    {
                        contested.Add(new Vector2Int(x, y));
                    }
                }
            }

            return contested;
        }

        /// <summary>
        /// Get chunks controlled by a specific faction.
        /// </summary>
        public List<Vector2Int> GetFactionChunks(FactionId faction)
        {
            var chunks = new List<Vector2Int>();

            for (int x = 0; x < CHUNKS_PER_AXIS; x++)
            {
                for (int y = 0; y < CHUNKS_PER_AXIS; y++)
                {
                    if (chunkClaims[x, y].GetDominantFaction() == faction)
                    {
                        chunks.Add(new Vector2Int(x, y));
                    }
                }
            }

            return chunks;
        }

        #endregion
    }

    public enum SectorType
    {
        Home,       // Player faction's home world, hand-crafted
        Core,       // Important systems, heavily populated
        Frontier,   // Border regions, less developed
        Contested,  // Active war zones
        DeepSpace,  // Empty space between systems
        Hostile     // Enemy controlled territory
    }
}
