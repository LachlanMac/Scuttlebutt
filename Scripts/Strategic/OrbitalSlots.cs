using UnityEngine;
using System.Collections.Generic;

namespace Starbelter.Strategic
{
    /// <summary>
    /// Manages orbital slots around planets and moons for station placement.
    /// Each body has 8 slots arranged in a circle, preventing station overlap.
    /// </summary>
    public static class OrbitalSlots
    {
        /// <summary>
        /// Slot positions relative to body center.
        /// 8 slots arranged like compass points.
        /// </summary>
        private static readonly Vector2[] SlotOffsets = new Vector2[]
        {
            new Vector2(0, 1),      // 0: N
            new Vector2(0.7f, 0.7f),  // 1: NE
            new Vector2(1, 0),      // 2: E
            new Vector2(0.7f, -0.7f), // 3: SE
            new Vector2(0, -1),     // 4: S
            new Vector2(-0.7f, -0.7f),// 5: SW
            new Vector2(-1, 0),     // 6: W
            new Vector2(-0.7f, 0.7f)  // 7: NW
        };

        public const int SLOT_COUNT = 8;

        /// <summary>
        /// Get the world position for a specific orbital slot around a body.
        /// </summary>
        /// <param name="bodyPosition">Center of the planet/moon</param>
        /// <param name="slotIndex">Which slot (0-7)</param>
        /// <param name="orbitDistance">Distance from body center (default 300)</param>
        public static Vector2 GetSlotPosition(Vector2 bodyPosition, int slotIndex, float orbitDistance = 300f)
        {
            slotIndex = Mathf.Clamp(slotIndex, 0, SLOT_COUNT - 1);
            return bodyPosition + SlotOffsets[slotIndex] * orbitDistance;
        }

        /// <summary>
        /// Get the orbit distance based on body type and station type.
        /// Larger bodies have stations further out. Military stations closer.
        /// </summary>
        public static float GetOrbitDistance(PointOfInterest body, StationType stationType)
        {
            float baseDistance = 300f;

            // Scale by body type
            if (body is Planet planet)
            {
                baseDistance = planet.planetType == PlanetType.Gas ? 800f : 400f;
            }
            else if (body is Moon)
            {
                baseDistance = 150f;
            }

            // Military stations orbit closer
            if (stationType == StationType.FleetHQ || stationType == StationType.Bastion || stationType == StationType.Base)
            {
                baseDistance *= 0.8f;
            }
            // Mining stations orbit further (asteroid belts, etc.)
            else if (stationType == StationType.MiningStation)
            {
                baseDistance *= 1.2f;
            }

            return baseDistance;
        }

        /// <summary>
        /// Find the next available slot for a body.
        /// Returns -1 if all slots are taken.
        /// </summary>
        public static int GetNextAvailableSlot(Sector sector, string bodyId, HashSet<int> usedSlots)
        {
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                if (!usedSlots.Contains(i))
                {
                    return i;
                }
            }
            return -1; // All slots taken
        }
    }

    /// <summary>
    /// Tracks orbital slot usage for planets/moons in a sector.
    /// </summary>
    public class SectorOrbitalTracker
    {
        // bodyId -> set of used slot indices
        private Dictionary<string, HashSet<int>> usedSlots = new Dictionary<string, HashSet<int>>();

        /// <summary>
        /// Reserve a slot for a station.
        /// </summary>
        public bool ReserveSlot(string bodyId, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= OrbitalSlots.SLOT_COUNT)
                return false;

            if (!usedSlots.TryGetValue(bodyId, out var slots))
            {
                slots = new HashSet<int>();
                usedSlots[bodyId] = slots;
            }

            if (slots.Contains(slotIndex))
                return false; // Already taken

            slots.Add(slotIndex);
            return true;
        }

        /// <summary>
        /// Get the next available slot for a body.
        /// Returns -1 if all slots taken.
        /// </summary>
        public int GetNextSlot(string bodyId)
        {
            if (!usedSlots.TryGetValue(bodyId, out var slots))
            {
                return 0; // First slot available
            }

            for (int i = 0; i < OrbitalSlots.SLOT_COUNT; i++)
            {
                if (!slots.Contains(i))
                    return i;
            }

            return -1; // All full
        }

        /// <summary>
        /// Check how many slots are available for a body.
        /// </summary>
        public int GetAvailableSlotCount(string bodyId)
        {
            if (!usedSlots.TryGetValue(bodyId, out var slots))
            {
                return OrbitalSlots.SLOT_COUNT;
            }
            return OrbitalSlots.SLOT_COUNT - slots.Count;
        }

        /// <summary>
        /// Release a slot.
        /// </summary>
        public void ReleaseSlot(string bodyId, int slotIndex)
        {
            if (usedSlots.TryGetValue(bodyId, out var slots))
            {
                slots.Remove(slotIndex);
            }
        }

        /// <summary>
        /// Clear all slot reservations.
        /// </summary>
        public void Clear()
        {
            usedSlots.Clear();
        }
    }
}
