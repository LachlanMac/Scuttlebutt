using UnityEngine;
using Starbelter.Arena;

namespace Starbelter.AI
{
    /// <summary>
    /// Shared utility functions for unit actions.
    /// States should use these for common operations.
    /// Each mode can use these differently (combat MoveToTile uses cover, off-duty doesn't).
    /// </summary>
    public static class UnitActions
    {
        #region Movement

        /// <summary>
        /// Move unit to a specific world position.
        /// Uses UnitController.MoveTo which handles pending destinations.
        /// </summary>
        /// <param name="controller">The unit to move</param>
        /// <param name="destination">World position to move to</param>
        /// <param name="useThreatAwarePath">If true, avoids threat zones (for combat)</param>
        /// <returns>True if movement was initiated</returns>
        /// 
        public static bool MoveToPosition(UnitController controller, Vector3 destination, bool useThreatAwarePath = false)
        {
            if (controller == null || controller.Movement == null) return false;

            // Use the controller's movement interface
            controller.MoveTo(destination, useThreatAwarePath);
            return true;
        }

        /// <summary>
        /// Move unit to a specific tile.
        /// </summary>
        public static bool MoveToTile(UnitController controller, Vector3Int tile, bool useThreatAwarePath = false)
        {
            if (controller == null || controller.CurrentArena == null) return false;

            var floor = controller.CurrentArena.GetFloorForUnit(controller);
            if (floor == null && controller.CurrentArena.FloorCount > 0)
            {
                floor = controller.CurrentArena.GetFloor(0);
            }

            if (floor == null) return false;

            Vector3 worldPos = floor.TileToWorld(tile);
            return MoveToPosition(controller, worldPos, useThreatAwarePath);
        }

        /// <summary>
        /// Move unit to a random position within a room.
        /// </summary>
        public static bool MoveToRoom(UnitController controller, Room room, bool useThreatAwarePath = false)
        {
            if (controller == null || room == null) return false;

            Vector3 destination = room.GetRandomPosition();
            return MoveToPosition(controller, destination, useThreatAwarePath);
        }

        /// <summary>
        /// Move unit to the center of a room.
        /// </summary>
        public static bool MoveToRoomCenter(UnitController controller, Room room, bool useThreatAwarePath = false)
        {
            if (controller == null || room == null) return false;

            Vector3 destination = room.GetCenterPosition();
            return MoveToPosition(controller, destination, useThreatAwarePath);
        }

        /// <summary>
        /// Move unit to a random walkable tile on current floor.
        /// </summary>
        public static bool MoveToRandomPosition(UnitController controller, float maxDistance = 10f, bool useThreatAwarePath = false)
        {
            if (controller == null) return false;

            Vector3 currentPos = controller.transform.position;
            Vector2 randomOffset = Random.insideUnitCircle * maxDistance;
            Vector3 destination = currentPos + new Vector3(randomOffset.x, randomOffset.y, 0);

            return MoveToPosition(controller, destination, useThreatAwarePath);
        }

        /// <summary>
        /// Stop all movement immediately.
        /// </summary>
        public static void StopMovement(UnitController controller)
        {
            if (controller?.Movement != null)
            {
                controller.Movement.Stop();
            }
        }

        #endregion

        #region Room Queries

        /// <summary>
        /// Get the room the unit is currently in.
        /// </summary>
        public static Room GetCurrentRoom(UnitController controller)
        {
            if (controller?.CurrentArena == null) return null;

            var floor = controller.CurrentArena.GetFloorForUnit(controller);
            if (floor == null) return null;

            return floor.GetRoomAtPosition(controller.transform.position);
        }

        /// <summary>
        /// Find a room of specific type on the unit's floor.
        /// </summary>
        public static Room FindRoom(UnitController controller, RoomType type)
        {
            if (controller?.CurrentArena == null) return null;

            var floor = controller.CurrentArena.GetFloorForUnit(controller);
            if (floor == null) return null;

            return floor.GetRoom(type);
        }

        /// <summary>
        /// Find a random room on the unit's floor.
        /// </summary>
        public static Room FindRandomRoom(UnitController controller)
        {
            if (controller?.CurrentArena == null) return null;

            var floor = controller.CurrentArena.GetFloorForUnit(controller);
            if (floor == null || floor.Rooms.Count == 0) return null;

            int index = Random.Range(0, floor.Rooms.Count);
            return floor.Rooms[index];
        }

        #endregion

        #region Facing / Looking

        /// <summary>
        /// Face toward a world position.
        /// </summary>
        public static void FacePosition(UnitController controller, Vector3 position)
        {
            if (controller == null) return;

            Vector2 direction = (position - controller.transform.position).normalized;
            controller.SetFacingDirection(direction);
        }

        /// <summary>
        /// Face toward another unit.
        /// </summary>
        public static void FaceUnit(UnitController controller, UnitController target)
        {
            if (controller == null || target == null) return;
            FacePosition(controller, target.transform.position);
        }

        #endregion

        #region Proximity Checks

        /// <summary>
        /// Check if unit is within range of a position.
        /// </summary>
        public static bool IsNearPosition(UnitController controller, Vector3 position, float range = 1f)
        {
            if (controller == null) return false;
            return Vector3.Distance(controller.transform.position, position) <= range;
        }

        /// <summary>
        /// Check if unit is in a specific room.
        /// </summary>
        public static bool IsInRoom(UnitController controller, Room room)
        {
            if (controller == null || room == null) return false;
            return room.ContainsPosition(controller.transform.position);
        }

        /// <summary>
        /// Check if unit is near another unit.
        /// </summary>
        public static bool IsNearUnit(UnitController controller, UnitController other, float range = 2f)
        {
            if (controller == null || other == null) return false;
            return Vector3.Distance(controller.transform.position, other.transform.position) <= range;
        }

        #endregion

        #region Random Timing

        /// <summary>
        /// Get a random wait time within a range.
        /// </summary>
        public static float RandomWaitTime(float min, float max)
        {
            return Random.Range(min, max);
        }

        /// <summary>
        /// Check if a random chance succeeds.
        /// </summary>
        /// <param name="chance">0-1 probability</param>
        public static bool RandomChance(float chance)
        {
            return Random.value <= chance;
        }

        #endregion
    }
}
