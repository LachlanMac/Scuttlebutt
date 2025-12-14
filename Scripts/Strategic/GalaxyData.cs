using UnityEngine;
using System.Collections.Generic;

namespace Starbelter.Strategic
{
    /// <summary>
    /// Position within the galaxy grid (sector coordinates).
    /// </summary>
    [System.Serializable]
    public struct SectorCoord
    {
        public int x;
        public int y;

        public SectorCoord(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public bool IsValid => x >= 0 && x < GalaxyData.GALAXY_SIZE &&
                               y >= 0 && y < GalaxyData.GALAXY_SIZE;

        public static SectorCoord Invalid => new SectorCoord(-1, -1);

        public override string ToString() => $"[{x},{y}]";

        public static implicit operator Vector2Int(SectorCoord c) => new Vector2Int(c.x, c.y);
        public static implicit operator SectorCoord(Vector2Int v) => new SectorCoord(v.x, v.y);
    }

    /// <summary>
    /// Position within a sector (chunk coordinates).
    /// </summary>
    [System.Serializable]
    public struct ChunkCoord
    {
        public int x;
        public int y;

        public ChunkCoord(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public bool IsValid => x >= 0 && x < Sector.CHUNKS_PER_AXIS &&
                               y >= 0 && y < Sector.CHUNKS_PER_AXIS;

        public override string ToString() => $"[{x},{y}]";

        public static implicit operator Vector2Int(ChunkCoord c) => new Vector2Int(c.x, c.y);
        public static implicit operator ChunkCoord(Vector2Int v) => new ChunkCoord(v.x, v.y);
    }

    /// <summary>
    /// Full galaxy position: sector + chunk + optional precise position.
    /// Format: [sectorX,sectorY] [chunkX,chunkY]
    /// </summary>
    [System.Serializable]
    public struct GalaxyPosition
    {
        public SectorCoord sector;
        public ChunkCoord chunk;
        public Vector2 localPosition; // Position within chunk (0 to CHUNK_SIZE)

        public GalaxyPosition(SectorCoord sector, ChunkCoord chunk, Vector2 localPosition = default)
        {
            this.sector = sector;
            this.chunk = chunk;
            this.localPosition = localPosition;
        }

        public GalaxyPosition(int sectorX, int sectorY, int chunkX, int chunkY)
        {
            this.sector = new SectorCoord(sectorX, sectorY);
            this.chunk = new ChunkCoord(chunkX, chunkY);
            this.localPosition = Vector2.zero;
        }

        public bool IsValid => sector.IsValid && chunk.IsValid;

        public override string ToString() => $"{sector} {chunk}";

        /// <summary>
        /// Convert to world position within the sector.
        /// </summary>
        public Vector2 ToSectorWorldPosition()
        {
            return Sector.ChunkToWorldCenter(chunk) + localPosition;
        }
    }

    /// <summary>
    /// The entire galaxy - holds all sectors in a 10x10 grid.
    /// </summary>
    public class GalaxyData
    {
        public const int GALAXY_SIZE = 10; // 10x10 sectors

        private Sector[,] sectors = new Sector[GALAXY_SIZE, GALAXY_SIZE];

        // Faction homeworld locations
        public Dictionary<FactionId, SectorCoord> homeworlds = new Dictionary<FactionId, SectorCoord>();

        // Metadata
        public string galaxyName = "Starbelter Galaxy";
        public int seed;

        public Sector GetSector(SectorCoord coord)
        {
            if (!coord.IsValid) return null;
            return sectors[coord.x, coord.y];
        }

        public Sector GetSector(int x, int y)
        {
            if (x < 0 || x >= GALAXY_SIZE || y < 0 || y >= GALAXY_SIZE) return null;
            return sectors[x, y];
        }

        public void SetSector(SectorCoord coord, Sector sector)
        {
            if (!coord.IsValid) return;
            sectors[coord.x, coord.y] = sector;
        }

        public void SetSector(int x, int y, Sector sector)
        {
            if (x < 0 || x >= GALAXY_SIZE || y < 0 || y >= GALAXY_SIZE) return;
            sectors[x, y] = sector;
        }

        /// <summary>
        /// Iterate over all sectors.
        /// </summary>
        public IEnumerable<Sector> AllSectors
        {
            get
            {
                for (int x = 0; x < GALAXY_SIZE; x++)
                {
                    for (int y = 0; y < GALAXY_SIZE; y++)
                    {
                        if (sectors[x, y] != null)
                            yield return sectors[x, y];
                    }
                }
            }
        }

        /// <summary>
        /// Get all sectors controlled by a faction.
        /// </summary>
        public List<Sector> GetFactionSectors(FactionId faction)
        {
            var result = new List<Sector>();
            foreach (var sector in AllSectors)
            {
                if (sector.controlledBy == faction)
                    result.Add(sector);
            }
            return result;
        }

        /// <summary>
        /// Get sectors adjacent to a given sector (including diagonals).
        /// </summary>
        public List<Sector> GetAdjacentSectors(SectorCoord coord)
        {
            var result = new List<Sector>();
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var adjacent = GetSector(coord.x + dx, coord.y + dy);
                    if (adjacent != null)
                        result.Add(adjacent);
                }
            }
            return result;
        }

        /// <summary>
        /// Calculate distance between two sector coordinates.
        /// </summary>
        public static int SectorDistance(SectorCoord a, SectorCoord b)
        {
            return Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
        }

        /// <summary>
        /// Find path between sectors (simple, returns list of coords to traverse).
        /// </summary>
        public List<SectorCoord> GetPath(SectorCoord from, SectorCoord to)
        {
            var path = new List<SectorCoord>();
            var current = from;

            while (current.x != to.x || current.y != to.y)
            {
                int dx = System.Math.Sign(to.x - current.x);
                int dy = System.Math.Sign(to.y - current.y);
                current = new SectorCoord(current.x + dx, current.y + dy);
                path.Add(current);
            }

            return path;
        }
    }
}
