using UnityEngine;

namespace Starbelter.Core
{
    /// <summary>
    /// Player-controlled pilot. Reads input and controls the ShipController.
    /// </summary>
    [RequireComponent(typeof(ShipController))]
    public class PlayerPilot : MonoBehaviour, IPilot
    {
        [Header("Settings")]
        [SerializeField] private bool startActive = true;

        [Header("Input")]
        [SerializeField] private KeyCode thrustKey = KeyCode.W;
        [SerializeField] private KeyCode brakeKey = KeyCode.S;
        [SerializeField] private KeyCode turnLeftKey = KeyCode.A;
        [SerializeField] private KeyCode turnRightKey = KeyCode.D;
        [SerializeField] private KeyCode fireGroup1Key = KeyCode.Space;
        [SerializeField] private KeyCode fireGroup2Key = KeyCode.LeftControl;
        [SerializeField] private KeyCode warpKey = KeyCode.LeftShift;

        private ShipController ship;
        private bool isActive;

        public bool IsActive => isActive;

        void Awake()
        {
            ship = GetComponent<ShipController>();
        }

        void Start()
        {
            if (startActive)
            {
                Activate();
            }
        }

        void Update()
        {
            if (!isActive) return;

            HandleMovement();
            HandleActions();
        }

        private void HandleMovement()
        {
            // Thrust
            float thrust = 0f;
            if (Input.GetKey(thrustKey)) thrust = 1f;
            else if (Input.GetKey(brakeKey)) thrust = -1f;

            // Turn
            float turn = 0f;
            if (Input.GetKey(turnLeftKey)) turn = 1f;
            else if (Input.GetKey(turnRightKey)) turn = -1f;

            ship.SetInput(thrust, turn);
        }

        private void HandleActions()
        {
            // Warp
            if (Input.GetKeyDown(warpKey))
            {
                if (ship.IsWarping)
                    ship.DisengageWarp();
                else
                    ship.EngageWarp();
            }

            // Fire weapon groups
            if (Input.GetKey(fireGroup1Key))
            {
                ship.FirePilotGroup(1);
            }
            if (Input.GetKey(fireGroup2Key))
            {
                ship.FirePilotGroup(2);
            }
        }

        public void Activate()
        {
            isActive = true;
            ship.ClearWaypoint(); // Stop any AI navigation
            Debug.Log($"[PlayerPilot] Player taking control of {ship.ShipName}");
        }

        public void Deactivate()
        {
            isActive = false;
            ship.SetInput(0, 0); // Stop movement
            Debug.Log($"[PlayerPilot] Player releasing control of {ship.ShipName}");
        }
    }
}
