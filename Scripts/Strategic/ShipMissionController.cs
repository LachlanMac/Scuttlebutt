using UnityEngine;
using System;
using System.Collections.Generic;
using Starbelter.Ship;
using Starbelter.Space;
using Starbelter.Arena;

namespace Starbelter.Strategic
{
    /// <summary>
    /// High-level mission states (few, fundamental).
    /// </summary>
    public enum MissionState
    {
        Docked,      // At a station/ship
        Undocking,   // Leaving dock
        Traveling,   // Sublight movement
        Jumping,     // FTL transit
        Docking      // Approaching dock
    }

    /// <summary>
    /// Internal stages within each state.
    /// </summary>
    public enum ShipStage
    {
        // Docked stages
        Docked_Idle,
        Docked_PreparingToUndock,
        Docked_ReadyToUndock,

        // Undocking stages
        Undocking_ReleasingClamps,
        Undocking_ClearingDock,
        Undocking_Complete,

        // Traveling stages
        Traveling_EscapingGravityWell,
        Traveling_Cruising,
        Traveling_Arriving,

        // Jumping stages
        Jumping_Charging,
        Jumping_InTransit,
        Jumping_Exiting,

        // Docking stages
        Docking_Approaching,
        Docking_FinalApproach,
        Docking_Securing
    }

    /// <summary>
    /// Controls ship mission execution: orders, states, and stage transitions.
    /// Attach to a ship GameObject.
    /// </summary>
    public class ShipMissionController : MonoBehaviour
    {
        [Header("Ship Data")]
        [SerializeField] private string shipTypeId = "frigate";

        [Header("Current State (Read Only)")]
        [SerializeField] private MissionState currentState = MissionState.Docked;
        [SerializeField] private ShipStage currentStage = ShipStage.Docked_Idle;

        [Header("Position")]
        [SerializeField] private SectorPosition currentPosition;
        [SerializeField] private Vector2 localCoordinates;    // Position within the sector (for space view)
        [SerializeField] private bool isJumping;              // True = in hyperspace, not on map
        [SerializeField] private string dockedAtId;           // Station/ship we're docked at

        [Header("Resources")]
        [SerializeField] private float currentFuel;

        [Header("Timing")]
        [SerializeField] private float stageTimer;
        [SerializeField] private float stageTargetTime;

        [Header("Visual Representations")]
        [SerializeField] private GameObject spacePrefab;     // Prefab for space view
        [SerializeField] private GameObject arenaPrefab;     // Prefab for interior (arena)
        private SpaceVessel spaceInstance;                   // Active space representation
        private Arena.Arena arenaInstance;                   // Active interior representation

        // Ship data (loaded from DataLoader)
        private ShipData shipData;

        // Order queue
        private Queue<ShipOrder> orderQueue = new Queue<ShipOrder>();
        private ShipOrder currentOrder;

        // Stage conditions - can be set externally
        private Dictionary<string, bool> conditions = new Dictionary<string, bool>();

        // Jump tracking
        private SectorPosition jumpDestination;
        private float jumpProgress;
        private float jumpDuration;

        // Events
        public event Action<MissionState, ShipStage> OnStateChanged;
        public event Action<ShipOrder> OnOrderStarted;
        public event Action<ShipOrder> OnOrderCompleted;

        // Properties - State
        public MissionState State => currentState;
        public ShipStage Stage => currentStage;
        public ShipOrder CurrentOrder => currentOrder;
        public int QueuedOrders => orderQueue.Count;

        // Properties - Position
        public SectorPosition SectorPos => currentPosition;
        public Vector2Int Sector => new Vector2Int(currentPosition.SectorX, currentPosition.SectorY);
        public Vector2Int Chunk => new Vector2Int(currentPosition.ChunkX, currentPosition.ChunkY);
        public Vector2 LocalCoords => localCoordinates;
        public bool IsJumping => isJumping;
        public bool IsOnMap => !isJumping;
        public string DockedAt => dockedAtId;
        public bool IsDocked => currentState == MissionState.Docked;

        // Properties - Resources
        public float Fuel => currentFuel;
        public float FuelCapacity => shipData?.jumpFuelCapacity ?? 0f;
        public float FuelPercent => FuelCapacity > 0 ? currentFuel / FuelCapacity : 0f;
        public bool HasJumpDrive => shipData?.HasJumpDrive ?? false;

        // Properties - Visual Representations
        public SpaceVessel SpaceVessel => spaceInstance;
        public Arena.Arena Arena => arenaInstance;
        public bool HasSpaceInstance => spaceInstance != null;
        public bool HasArenaInstance => arenaInstance != null;

        private void Awake()
        {
            LoadShipData();
        }

        private void Start()
        {
            // Subscribe to time events
            GalacticTime.OnHourChanged += OnHourChanged;
        }

        private void OnDestroy()
        {
            GalacticTime.OnHourChanged -= OnHourChanged;
        }

        private void LoadShipData()
        {
            shipData = Core.DataLoader.GetShip(shipTypeId);
            if (shipData != null)
            {
                currentFuel = shipData.jumpFuelCapacity; // Start with full fuel
            }
        }

        private void Update()
        {
            // Update stage timer
            if (stageTargetTime > 0)
            {
                stageTimer += Time.deltaTime / GalacticTime.SecondsPerHour;
            }

            // Process current state
            UpdateState();
        }

        private void OnHourChanged(int hour)
        {
            // Check conditions on hour change
            CheckStageTransitions();
        }

        #region Order Management

        /// <summary>
        /// Add an order to the queue.
        /// </summary>
        public void QueueOrder(ShipOrder order)
        {
            orderQueue.Enqueue(order);
            Debug.Log($"[ShipMission] {name}: Queued order - {order.Description}");

            // Start processing if idle
            if (currentOrder == null && currentState == MissionState.Docked && currentStage == ShipStage.Docked_Idle)
            {
                ProcessNextOrder();
            }
        }

        /// <summary>
        /// Clear all queued orders.
        /// </summary>
        public void ClearOrders()
        {
            orderQueue.Clear();
            Debug.Log($"[ShipMission] {name}: Orders cleared");
        }

        private void ProcessNextOrder()
        {
            if (orderQueue.Count == 0)
            {
                currentOrder = null;
                return;
            }

            currentOrder = orderQueue.Dequeue();
            Debug.Log($"[ShipMission] {name}: Processing order - {currentOrder.Description}");
            OnOrderStarted?.Invoke(currentOrder);

            // Start appropriate state based on order type
            switch (currentOrder.Type)
            {
                case ShipOrderType.Undock:
                    if (currentState == MissionState.Docked)
                    {
                        SetStage(ShipStage.Docked_PreparingToUndock);
                    }
                    break;

                case ShipOrderType.JumpTo:
                    var jumpOrder = currentOrder as JumpToOrder;
                    if (jumpOrder != null)
                    {
                        jumpDestination = jumpOrder.Destination;
                        // If docked, need to undock first
                        if (currentState == MissionState.Docked)
                        {
                            SetStage(ShipStage.Docked_PreparingToUndock);
                        }
                        else
                        {
                            StartTravelingToJumpPoint();
                        }
                    }
                    break;

                case ShipOrderType.DockAt:
                    // TODO: Navigate to station and dock
                    break;

                case ShipOrderType.Hold:
                    var holdOrder = currentOrder as HoldOrder;
                    stageTargetTime = holdOrder?.DurationHours ?? 1f;
                    stageTimer = 0f;
                    break;
            }
        }

        private void CompleteCurrentOrder()
        {
            if (currentOrder == null) return;

            currentOrder.IsComplete = true;
            Debug.Log($"[ShipMission] {name}: Order complete - {currentOrder.Description}");
            OnOrderCompleted?.Invoke(currentOrder);

            currentOrder = null;
            ProcessNextOrder();
        }

        #endregion

        #region State Machine

        private void UpdateState()
        {
            switch (currentState)
            {
                case MissionState.Docked:
                    UpdateDockedState();
                    break;
                case MissionState.Undocking:
                    UpdateUndockingState();
                    break;
                case MissionState.Traveling:
                    UpdateTravelingState();
                    break;
                case MissionState.Jumping:
                    UpdateJumpingState();
                    break;
                case MissionState.Docking:
                    UpdateDockingState();
                    break;
            }
        }

        private void UpdateDockedState()
        {
            switch (currentStage)
            {
                case ShipStage.Docked_Idle:
                    // Waiting for orders
                    break;

                case ShipStage.Docked_PreparingToUndock:
                    // Check if ready to undock
                    if (CheckCondition("AllCrewAtStations") &&
                        CheckCondition("SystemsChecked") &&
                        CheckCondition("UndockClearanceGranted"))
                    {
                        SetStage(ShipStage.Docked_ReadyToUndock);
                    }
                    break;

                case ShipStage.Docked_ReadyToUndock:
                    // Transition to undocking
                    SetState(MissionState.Undocking, ShipStage.Undocking_ReleasingClamps);
                    break;
            }
        }

        private void UpdateUndockingState()
        {
            switch (currentStage)
            {
                case ShipStage.Undocking_ReleasingClamps:
                    if (stageTimer >= stageTargetTime)
                    {
                        SetStage(ShipStage.Undocking_ClearingDock);
                        stageTargetTime = 0.5f; // 30 minutes to clear
                        stageTimer = 0f;
                    }
                    break;

                case ShipStage.Undocking_ClearingDock:
                    if (stageTimer >= stageTargetTime)
                    {
                        SetStage(ShipStage.Undocking_Complete);
                    }
                    break;

                case ShipStage.Undocking_Complete:
                    dockedAtId = null;

                    // Check what the current order wants us to do
                    if (currentOrder?.Type == ShipOrderType.Undock)
                    {
                        CompleteCurrentOrder();
                        SetState(MissionState.Docked, ShipStage.Docked_Idle); // Actually should be "free floating"
                    }
                    else if (currentOrder?.Type == ShipOrderType.JumpTo)
                    {
                        StartTravelingToJumpPoint();
                    }
                    break;
            }
        }

        private void UpdateTravelingState()
        {
            switch (currentStage)
            {
                case ShipStage.Traveling_EscapingGravityWell:
                    if (stageTimer >= stageTargetTime)
                    {
                        SetCondition("CanJump", true);

                        // If we have a jump order, start jumping
                        if (currentOrder?.Type == ShipOrderType.JumpTo)
                        {
                            StartJump();
                        }
                        else
                        {
                            SetStage(ShipStage.Traveling_Cruising);
                        }
                    }
                    break;

                case ShipStage.Traveling_Cruising:
                    // Sublight travel - check if arrived
                    break;

                case ShipStage.Traveling_Arriving:
                    // Arriving at destination
                    break;
            }
        }

        private void UpdateJumpingState()
        {
            switch (currentStage)
            {
                case ShipStage.Jumping_Charging:
                    if (stageTimer >= stageTargetTime)
                    {
                        isJumping = true; // Enter hyperspace - no longer on map
                        SetStage(ShipStage.Jumping_InTransit);
                        stageTargetTime = jumpDuration;
                        stageTimer = 0f;
                    }
                    break;

                case ShipStage.Jumping_InTransit:
                    jumpProgress = stageTimer / jumpDuration;

                    if (stageTimer >= stageTargetTime)
                    {
                        SetStage(ShipStage.Jumping_Exiting);
                        stageTargetTime = 0.1f; // Quick exit
                        stageTimer = 0f;
                    }
                    break;

                case ShipStage.Jumping_Exiting:
                    if (stageTimer >= stageTargetTime)
                    {
                        // Arrived at destination
                        isJumping = false; // Exit hyperspace - back on map
                        currentPosition = jumpDestination;
                        SetCondition("CanJump", false);

                        if (currentOrder?.Type == ShipOrderType.JumpTo)
                        {
                            CompleteCurrentOrder();
                        }

                        // Return to traveling state
                        SetState(MissionState.Traveling, ShipStage.Traveling_Arriving);
                    }
                    break;
            }
        }

        private void UpdateDockingState()
        {
            switch (currentStage)
            {
                case ShipStage.Docking_Approaching:
                    // Navigate to station
                    break;

                case ShipStage.Docking_FinalApproach:
                    // Align with docking port
                    break;

                case ShipStage.Docking_Securing:
                    if (stageTimer >= stageTargetTime)
                    {
                        // Docked
                        var dockOrder = currentOrder as DockAtOrder;
                        if (dockOrder != null)
                        {
                            dockedAtId = dockOrder.TargetId;
                        }

                        CompleteCurrentOrder();
                        SetState(MissionState.Docked, ShipStage.Docked_Idle);
                    }
                    break;
            }
        }

        #endregion

        #region Transitions

        private void StartTravelingToJumpPoint()
        {
            SetState(MissionState.Traveling, ShipStage.Traveling_EscapingGravityWell);
            stageTargetTime = 1f; // 1 hour to escape gravity well
            stageTimer = 0f;
            SetCondition("CanJump", false);
        }

        private void StartJump()
        {
            var jumpOrder = currentOrder as JumpToOrder;
            if (jumpOrder == null) return;

            // Calculate jump
            var calc = SectorUtils.CalculateJump(
                currentPosition,
                jumpOrder.Destination,
                shipData?.jumpSpeed ?? 0f,
                shipData?.jumpFuelPerUnit ?? 0.1f,
                currentFuel
            );

            if (!calc.CanJump)
            {
                Debug.LogWarning($"[ShipMission] {name}: Cannot jump - {calc}");
                currentOrder.IsFailed = true;
                currentOrder.FailureReason = "Insufficient fuel";
                CompleteCurrentOrder();
                return;
            }

            // Consume fuel
            currentFuel -= calc.FuelNeeded;
            jumpDuration = calc.TravelTimeHours;
            jumpDestination = jumpOrder.Destination;
            jumpProgress = 0f;

            Debug.Log($"[ShipMission] {name}: Starting jump - {calc}");

            SetState(MissionState.Jumping, ShipStage.Jumping_Charging);
            stageTargetTime = 0.25f; // 15 minutes to charge
            stageTimer = 0f;
        }

        private void SetState(MissionState newState, ShipStage newStage)
        {
            var oldState = currentState;
            var oldStage = currentStage;

            currentState = newState;
            currentStage = newStage;

            Debug.Log($"[ShipMission] {name}: {oldState}/{oldStage} → {newState}/{newStage}");
            OnStateChanged?.Invoke(newState, newStage);
        }

        private void SetStage(ShipStage newStage)
        {
            var oldStage = currentStage;
            currentStage = newStage;

            Debug.Log($"[ShipMission] {name}: Stage {oldStage} → {newStage}");
            OnStateChanged?.Invoke(currentState, newStage);
        }

        #endregion

        #region Conditions

        /// <summary>
        /// Set a condition value.
        /// </summary>
        public void SetCondition(string key, bool value)
        {
            conditions[key] = value;
        }

        /// <summary>
        /// Check a condition (defaults to true if not set).
        /// </summary>
        public bool CheckCondition(string key)
        {
            return conditions.TryGetValue(key, out bool value) ? value : true;
        }

        /// <summary>
        /// Force all undock conditions to true (for testing).
        /// </summary>
        public void ForceUndockReady()
        {
            SetCondition("AllCrewAtStations", true);
            SetCondition("SystemsChecked", true);
            SetCondition("UndockClearanceGranted", true);
        }

        private void CheckStageTransitions()
        {
            // Called on hour change - can add scheduled transitions here
        }

        #endregion

        #region Public API

        /// <summary>
        /// Set current position.
        /// </summary>
        public void SetPosition(SectorPosition position)
        {
            currentPosition = position;
        }

        /// <summary>
        /// Set docked state at a location.
        /// </summary>
        public void SetDocked(string locationId, SectorPosition position)
        {
            dockedAtId = locationId;
            currentPosition = position;
            currentState = MissionState.Docked;
            currentStage = ShipStage.Docked_Idle;
        }

        /// <summary>
        /// Refuel the ship.
        /// </summary>
        public void Refuel(float amount)
        {
            currentFuel = Mathf.Min(currentFuel + amount, FuelCapacity);
        }

        /// <summary>
        /// Refuel to full.
        /// </summary>
        public void RefuelFull()
        {
            currentFuel = FuelCapacity;
        }

        #endregion

        #region Visual Management

        /// <summary>
        /// Spawn the space representation of this ship.
        /// </summary>
        public SpaceVessel SpawnSpaceInstance(Vector2 position)
        {
            if (spaceInstance != null)
            {
                Debug.LogWarning($"[ShipMission] {name}: Space instance already exists");
                return spaceInstance;
            }

            if (spacePrefab == null)
            {
                Debug.LogError($"[ShipMission] {name}: No space prefab assigned");
                return null;
            }

            var go = Instantiate(spacePrefab, position, Quaternion.identity);
            go.name = $"{name}_Space";
            spaceInstance = go.GetComponent<SpaceVessel>();

            if (spaceInstance != null && SpaceManager.Instance != null)
            {
                SpaceManager.Instance.RegisterVessel(spaceInstance);
            }

            Debug.Log($"[ShipMission] {name}: Spawned space instance at {position}");
            return spaceInstance;
        }

        /// <summary>
        /// Despawn the space representation.
        /// </summary>
        public void DespawnSpaceInstance()
        {
            if (spaceInstance == null) return;

            if (SpaceManager.Instance != null)
            {
                SpaceManager.Instance.UnregisterVessel(spaceInstance);
            }

            Destroy(spaceInstance.gameObject);
            spaceInstance = null;
            Debug.Log($"[ShipMission] {name}: Despawned space instance");
        }

        /// <summary>
        /// Spawn the arena (interior) representation of this ship.
        /// </summary>
        public Arena.Arena SpawnArenaInstance(Vector2 position)
        {
            if (arenaInstance != null)
            {
                Debug.LogWarning($"[ShipMission] {name}: Arena instance already exists");
                return arenaInstance;
            }

            if (arenaPrefab == null)
            {
                Debug.LogError($"[ShipMission] {name}: No arena prefab assigned");
                return null;
            }

            var go = Instantiate(arenaPrefab, position, Quaternion.identity);
            go.name = $"{name}_Arena";
            arenaInstance = go.GetComponent<Arena.Arena>();

            if (arenaInstance != null && ArenaManager.Instance != null)
            {
                ArenaManager.Instance.RegisterArena(arenaInstance);
            }

            Debug.Log($"[ShipMission] {name}: Spawned arena instance at {position}");
            return arenaInstance;
        }

        /// <summary>
        /// Despawn the arena (interior) representation.
        /// </summary>
        public void DespawnArenaInstance()
        {
            if (arenaInstance == null) return;

            if (ArenaManager.Instance != null)
            {
                ArenaManager.Instance.UnregisterArena(arenaInstance);
            }

            Destroy(arenaInstance.gameObject);
            arenaInstance = null;
            Debug.Log($"[ShipMission] {name}: Despawned arena instance");
        }

        /// <summary>
        /// Set prefabs for this ship (called during spawn).
        /// </summary>
        public void SetPrefabs(GameObject space, GameObject arena)
        {
            spacePrefab = space;
            arenaPrefab = arena;
        }

        /// <summary>
        /// Link existing instances (if already in scene).
        /// </summary>
        public void LinkInstances(SpaceVessel space, Arena.Arena arena)
        {
            spaceInstance = space;
            arenaInstance = arena;
        }

        #endregion
    }
}
