using UnityEngine;

namespace Starbelter.Strategic
{
    /// <summary>
    /// Types of orders a ship can receive.
    /// </summary>
    public enum ShipOrderType
    {
        None,
        DockAt,         // Dock at a station/ship
        Undock,         // Undock from current location
        JumpTo,         // Jump to a sector position
        TravelTo,       // Sublight travel to position (within sector)
        Patrol,         // Patrol an area
        Hold,           // Hold position for X time
        Refuel,         // Refuel at current location
    }

    /// <summary>
    /// Base class for ship orders.
    /// </summary>
    [System.Serializable]
    public abstract class ShipOrder
    {
        public ShipOrderType Type { get; protected set; }
        public bool IsComplete { get; set; }
        public bool IsFailed { get; set; }
        public string FailureReason { get; set; }

        public abstract string Description { get; }
    }

    /// <summary>
    /// Order to dock at a specific location.
    /// </summary>
    [System.Serializable]
    public class DockAtOrder : ShipOrder
    {
        public string TargetId;          // Station or ship ID
        public SectorPosition Location;  // Where the target is

        public DockAtOrder(string targetId, SectorPosition location)
        {
            Type = ShipOrderType.DockAt;
            TargetId = targetId;
            Location = location;
        }

        public override string Description => $"Dock at {TargetId}";
    }

    /// <summary>
    /// Order to undock from current location.
    /// </summary>
    [System.Serializable]
    public class UndockOrder : ShipOrder
    {
        public UndockOrder()
        {
            Type = ShipOrderType.Undock;
        }

        public override string Description => "Undock";
    }

    /// <summary>
    /// Order to jump to a sector position.
    /// </summary>
    [System.Serializable]
    public class JumpToOrder : ShipOrder
    {
        public SectorPosition Destination;
        public string DestinationName; // Optional friendly name

        public JumpToOrder(SectorPosition destination, string name = null)
        {
            Type = ShipOrderType.JumpTo;
            Destination = destination;
            DestinationName = name;
        }

        public override string Description =>
            string.IsNullOrEmpty(DestinationName)
                ? $"Jump to {Destination.ToShortString()}"
                : $"Jump to {DestinationName}";
    }

    /// <summary>
    /// Order to travel via sublight to a position (within current sector).
    /// </summary>
    [System.Serializable]
    public class TravelToOrder : ShipOrder
    {
        public Vector2 Destination;      // Local position in current sector
        public string DestinationName;

        public TravelToOrder(Vector2 destination, string name = null)
        {
            Type = ShipOrderType.TravelTo;
            Destination = destination;
            DestinationName = name;
        }

        public override string Description =>
            string.IsNullOrEmpty(DestinationName)
                ? $"Travel to {Destination}"
                : $"Travel to {DestinationName}";
    }

    /// <summary>
    /// Order to patrol an area.
    /// </summary>
    [System.Serializable]
    public class PatrolOrder : ShipOrder
    {
        public SectorPosition[] Waypoints;
        public int CurrentWaypoint;
        public bool Loop;

        public PatrolOrder(SectorPosition[] waypoints, bool loop = true)
        {
            Type = ShipOrderType.Patrol;
            Waypoints = waypoints;
            Loop = loop;
            CurrentWaypoint = 0;
        }

        public override string Description => $"Patrol ({Waypoints?.Length ?? 0} waypoints)";
    }

    /// <summary>
    /// Order to hold position for a duration.
    /// </summary>
    [System.Serializable]
    public class HoldOrder : ShipOrder
    {
        public float DurationHours;
        public float ElapsedHours;

        public HoldOrder(float durationHours)
        {
            Type = ShipOrderType.Hold;
            DurationHours = durationHours;
            ElapsedHours = 0f;
        }

        public float RemainingHours => Mathf.Max(0, DurationHours - ElapsedHours);

        public override string Description => $"Hold position ({RemainingHours:F1}h remaining)";
    }

    /// <summary>
    /// Order to refuel at current location.
    /// </summary>
    [System.Serializable]
    public class RefuelOrder : ShipOrder
    {
        public float TargetFuelLevel; // 0-1 (percentage) or absolute amount if > 1

        public RefuelOrder(float targetLevel = 1f)
        {
            Type = ShipOrderType.Refuel;
            TargetFuelLevel = targetLevel;
        }

        public override string Description =>
            TargetFuelLevel <= 1f
                ? $"Refuel to {TargetFuelLevel * 100:F0}%"
                : $"Refuel to {TargetFuelLevel:F0} units";
    }
}
