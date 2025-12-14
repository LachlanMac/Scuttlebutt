using UnityEngine;

namespace Starbelter.Strategic
{
    /// <summary>
    /// Utilities for sector/chunk distance and jump calculations.
    ///
    /// Coordinate system:
    /// - Sector = 10x10 chunks
    /// - Chunk size is arbitrary (not tied to sector size in units)
    /// - For jump calculations, we use sector-level distances
    /// </summary>
    public static class SectorUtils
    {
        public const int ChunksPerSector = 10;

        // For jump distance calculations (abstract units, not tied to chunk size)
        public const float JumpUnitsPerSector = 100f;

        /// <summary>
        /// Convert a sector position to jump distance units (for fuel/time calculations).
        /// Uses abstract units - not tied to world/chunk coordinates.
        /// </summary>
        public static Vector2 ToJumpPosition(SectorPosition pos)
        {
            // Sector + fractional chunk position (chunk 0-9 maps to 0.0-0.9)
            float chunkFraction = (float)pos.ChunkX / ChunksPerSector;
            float chunkFractionY = (float)pos.ChunkY / ChunksPerSector;

            return new Vector2(
                (pos.SectorX + chunkFraction) * JumpUnitsPerSector,
                (pos.SectorY + chunkFractionY) * JumpUnitsPerSector
            );
        }

        /// <summary>
        /// Convert jump units back to a sector position.
        /// </summary>
        public static SectorPosition FromJumpPosition(Vector2 jumpPos)
        {
            float sectorFloatX = jumpPos.x / JumpUnitsPerSector;
            float sectorFloatY = jumpPos.y / JumpUnitsPerSector;

            int sectorX = Mathf.FloorToInt(sectorFloatX);
            int sectorY = Mathf.FloorToInt(sectorFloatY);

            int chunkX = Mathf.FloorToInt((sectorFloatX - sectorX) * ChunksPerSector);
            int chunkY = Mathf.FloorToInt((sectorFloatY - sectorY) * ChunksPerSector);

            return new SectorPosition(sectorX, sectorY, chunkX, chunkY);
        }

        /// <summary>
        /// Calculate jump distance between two sector positions.
        /// </summary>
        public static float Distance(SectorPosition from, SectorPosition to)
        {
            Vector2 fromPos = ToJumpPosition(from);
            Vector2 toPos = ToJumpPosition(to);
            return Vector2.Distance(fromPos, toPos);
        }

        /// <summary>
        /// Calculate fuel needed for a jump.
        /// </summary>
        public static float FuelNeeded(SectorPosition from, SectorPosition to, float fuelPerUnit)
        {
            return Distance(from, to) * fuelPerUnit;
        }

        /// <summary>
        /// Calculate travel time in game hours.
        /// </summary>
        public static float TravelTime(SectorPosition from, SectorPosition to, float jumpSpeed)
        {
            if (jumpSpeed <= 0) return float.MaxValue;
            return Distance(from, to) / jumpSpeed;
        }

        /// <summary>
        /// Check if a ship can make a jump with current fuel.
        /// </summary>
        public static bool CanJump(SectorPosition from, SectorPosition to, float currentFuel, float fuelPerUnit)
        {
            return currentFuel >= FuelNeeded(from, to, fuelPerUnit);
        }

        /// <summary>
        /// Get a jump calculation summary.
        /// </summary>
        public static JumpCalculation CalculateJump(
            SectorPosition from,
            SectorPosition to,
            float jumpSpeed,
            float fuelPerUnit,
            float currentFuel)
        {
            float distance = Distance(from, to);
            float fuelNeeded = distance * fuelPerUnit;
            float travelTime = jumpSpeed > 0 ? distance / jumpSpeed : float.MaxValue;
            bool canJump = currentFuel >= fuelNeeded && jumpSpeed > 0;

            return new JumpCalculation
            {
                From = from,
                To = to,
                Distance = distance,
                FuelNeeded = fuelNeeded,
                TravelTimeHours = travelTime,
                CanJump = canJump,
                FuelShortage = canJump ? 0 : fuelNeeded - currentFuel
            };
        }

        /// <summary>
        /// Calculate maximum jump range with given fuel.
        /// </summary>
        public static float MaxJumpRange(float currentFuel, float fuelPerUnit)
        {
            if (fuelPerUnit <= 0) return float.MaxValue;
            return currentFuel / fuelPerUnit;
        }
    }

    /// <summary>
    /// Represents a position in sector/chunk coordinates.
    /// </summary>
    [System.Serializable]
    public struct SectorPosition
    {
        public int SectorX;
        public int SectorY;
        public int ChunkX;
        public int ChunkY;

        public SectorPosition(int sectorX, int sectorY, int chunkX = 0, int chunkY = 0)
        {
            SectorX = sectorX;
            SectorY = sectorY;
            ChunkX = Mathf.Clamp(chunkX, 0, SectorUtils.ChunksPerSector - 1);
            ChunkY = Mathf.Clamp(chunkY, 0, SectorUtils.ChunksPerSector - 1);
        }

        /// <summary>
        /// Get position in jump distance units (for fuel/time calculations).
        /// </summary>
        public Vector2 JumpPosition => SectorUtils.ToJumpPosition(this);

        /// <summary>
        /// Sector-only position (ignores chunks).
        /// </summary>
        public Vector2Int Sector => new Vector2Int(SectorX, SectorY);

        /// <summary>
        /// Chunk-only position within sector.
        /// </summary>
        public Vector2Int Chunk => new Vector2Int(ChunkX, ChunkY);

        /// <summary>
        /// Check if this is the same sector (ignoring chunk).
        /// </summary>
        public bool SameSector(SectorPosition other)
        {
            return SectorX == other.SectorX && SectorY == other.SectorY;
        }

        /// <summary>
        /// Check if positions are exactly equal.
        /// </summary>
        public bool Equals(SectorPosition other)
        {
            return SectorX == other.SectorX &&
                   SectorY == other.SectorY &&
                   ChunkX == other.ChunkX &&
                   ChunkY == other.ChunkY;
        }

        public override bool Equals(object obj)
        {
            return obj is SectorPosition other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (SectorX, SectorY, ChunkX, ChunkY).GetHashCode();
        }

        public static bool operator ==(SectorPosition a, SectorPosition b) => a.Equals(b);
        public static bool operator !=(SectorPosition a, SectorPosition b) => !a.Equals(b);

        public override string ToString()
        {
            return $"Sector({SectorX},{SectorY}) Chunk({ChunkX},{ChunkY})";
        }

        /// <summary>
        /// Short format: S(3,3)C(4,6)
        /// </summary>
        public string ToShortString()
        {
            return $"S({SectorX},{SectorY})C({ChunkX},{ChunkY})";
        }
    }

    /// <summary>
    /// Result of a jump calculation.
    /// </summary>
    public struct JumpCalculation
    {
        public SectorPosition From;
        public SectorPosition To;
        public float Distance;
        public float FuelNeeded;
        public float TravelTimeHours;
        public bool CanJump;
        public float FuelShortage; // How much more fuel needed (0 if can jump)

        /// <summary>
        /// Travel time formatted as days/hours.
        /// </summary>
        public string TravelTimeFormatted
        {
            get
            {
                if (TravelTimeHours >= float.MaxValue) return "No jump drive";
                int days = Mathf.FloorToInt(TravelTimeHours / 24f);
                int hours = Mathf.FloorToInt(TravelTimeHours % 24f);
                if (days > 0)
                    return $"{days}d {hours}h";
                return $"{hours}h";
            }
        }

        public override string ToString()
        {
            return $"Jump {From.ToShortString()} → {To.ToShortString()}: " +
                   $"{Distance:F0} units, {FuelNeeded:F1} fuel, {TravelTimeFormatted}" +
                   (CanJump ? " [OK]" : $" [NEED {FuelShortage:F1} MORE FUEL]");
        }
    }
}
