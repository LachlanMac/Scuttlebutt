namespace Starbelter.Core
{
    /// <summary>
    /// Interface for anything that can control a ship (player, AI, remote control).
    /// </summary>
    public interface IPilot
    {
        bool IsActive { get; }
        void Activate();
        void Deactivate();
    }
}
