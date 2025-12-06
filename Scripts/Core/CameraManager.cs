using UnityEngine;
using Unity.Cinemachine;

namespace Starbelter.Core
{
    /// <summary>
    /// Manages Cinemachine virtual cameras for Arena and Space views.
    /// Handles switching between views and camera effects.
    /// </summary>
    public class CameraManager : MonoBehaviour
    {
        public static CameraManager Instance { get; private set; }

        [Header("Cinemachine")]
        [Tooltip("The main camera with CinemachineBrain")]
        [SerializeField] private Camera mainCamera;

        [Tooltip("Virtual camera for Arena view")]
        [SerializeField] private CinemachineCamera arenaVCam;

        [Tooltip("Virtual camera for Space view")]
        [SerializeField] private CinemachineCamera spaceVCam;

        [Header("Priority")]
        [Tooltip("Priority for active camera")]
        [SerializeField] private int activePriority = 20;

        [Tooltip("Priority for inactive camera")]
        [SerializeField] private int inactivePriority = 10;

        [Header("Shake")]
        [Tooltip("Impulse source for camera shake")]
        [SerializeField] private CinemachineImpulseSource impulseSource;

        [Header("Tactical Display")]
        [Tooltip("Secondary camera for picture-in-picture tactical display")]
        [SerializeField] private Camera tacticalCamera;

        [Tooltip("RenderTexture for tactical display")]
        [SerializeField] private RenderTexture tacticalDisplayTexture;

        [Tooltip("Resolution for tactical display if auto-creating")]
        [SerializeField] private Vector2Int tacticalResolution = new Vector2Int(320, 240);

        [Header("Culling")]
        [Tooltip("Base layers for Arena view (exclude floor layers - they're added at runtime)")]
        [SerializeField] private LayerMask arenaCullingMask = ~0;

        [Tooltip("Layers to render in Space view")]
        [SerializeField] private LayerMask spaceCullingMask = ~0;

        [Header("Floor Switching")]
        [Tooltip("Layer name for shared floor objects (elevators, etc.)")]
        [SerializeField] private string sharedFloorLayerName = "FloorShared";

        [Tooltip("Maximum floor index (0-based). Set to match your arena's floor count - 1")]
        [SerializeField] private int maxFloorIndex = 5;

        private ViewMode currentView = ViewMode.Arena;
        private int currentFloorIndex = 0;
        private int sharedFloorLayer = -1;

        // Properties
        public ViewMode CurrentView => currentView;
        public int CurrentFloorIndex => currentFloorIndex;
        public Camera MainCamera => mainCamera;
        public Camera ArenaCamera => mainCamera; // Main camera shows arena by default
        public Camera SpaceCamera => tacticalCamera; // Tactical shows space by default
        public CinemachineCamera ArenaVCam => arenaVCam;
        public CinemachineCamera SpaceVCam => spaceVCam;
        public RenderTexture TacticalDisplayTexture => tacticalDisplayTexture;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            Initialize();
        }

        private void Initialize()
        {
            // Find main camera if not assigned
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
            }

            // Ensure brain exists on main camera
            if (mainCamera != null && mainCamera.GetComponent<CinemachineBrain>() == null)
            {
                mainCamera.gameObject.AddComponent<CinemachineBrain>();
                Debug.Log("[CameraManager] Added CinemachineBrain to main camera");
            }

            // Create tactical display texture if needed
            if (tacticalDisplayTexture == null)
            {
                tacticalDisplayTexture = new RenderTexture(tacticalResolution.x, tacticalResolution.y, 16);
                tacticalDisplayTexture.name = "TacticalDisplay";
            }

            // Set up tactical camera if assigned
            if (tacticalCamera != null)
            {
                tacticalCamera.targetTexture = tacticalDisplayTexture;
            }

            // Create impulse source if needed
            if (impulseSource == null)
            {
                impulseSource = gameObject.AddComponent<CinemachineImpulseSource>();
                // Configure default impulse
                impulseSource.ImpulseDefinition.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
                impulseSource.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
                impulseSource.ImpulseDefinition.ImpulseDuration = 0.3f;
            }

            // Cache shared floor layer
            sharedFloorLayer = LayerMask.NameToLayer(sharedFloorLayerName);
            if (sharedFloorLayer < 0)
            {
                Debug.LogWarning($"[CameraManager] Shared floor layer '{sharedFloorLayerName}' not found");
            }

            // Set initial view and floor
            SetView(ViewMode.Arena);
            SetFloor(0);
        }

        /// <summary>
        /// Switch the main view between Arena and Space.
        /// </summary>
        public void SetView(ViewMode view)
        {
            currentView = view;

            if (view == ViewMode.Arena)
            {
                // Arena gets priority on main camera
                if (arenaVCam != null) arenaVCam.Priority = activePriority;
                if (spaceVCam != null) spaceVCam.Priority = inactivePriority;

                // Set culling masks (floor culling applied via UpdateFloorCulling)
                UpdateFloorCulling();
                if (tacticalCamera != null) tacticalCamera.cullingMask = spaceCullingMask;

                // Tactical camera follows space
                if (tacticalCamera != null && spaceVCam != null)
                {
                    UpdateTacticalCameraForSpace();
                }
            }
            else
            {
                // Space gets priority on main camera
                if (spaceVCam != null) spaceVCam.Priority = activePriority;
                if (arenaVCam != null) arenaVCam.Priority = inactivePriority;

                // Set culling masks
                if (mainCamera != null) mainCamera.cullingMask = spaceCullingMask;
                if (tacticalCamera != null) tacticalCamera.cullingMask = arenaCullingMask;

                // Tactical camera follows arena
                if (tacticalCamera != null && arenaVCam != null)
                {
                    UpdateTacticalCameraForArena();
                }
            }

            Debug.Log($"[CameraManager] View set to {view}");
        }

        /// <summary>
        /// Toggle between Arena and Space view.
        /// </summary>
        public void ToggleView()
        {
            SetView(currentView == ViewMode.Arena ? ViewMode.Space : ViewMode.Arena);
        }

        #region Floor Switching

        /// <summary>
        /// Set the current visible floor (updates camera culling mask).
        /// </summary>
        public void SetFloor(int floorIndex)
        {
            currentFloorIndex = Mathf.Clamp(floorIndex, 0, maxFloorIndex);
            UpdateFloorCulling();
            Debug.Log($"[CameraManager] Viewing Floor {currentFloorIndex}");
        }

        /// <summary>
        /// Move up one floor.
        /// </summary>
        public void FloorUp()
        {
            if (currentFloorIndex < maxFloorIndex)
            {
                SetFloor(currentFloorIndex + 1);
            }
        }

        /// <summary>
        /// Move down one floor.
        /// </summary>
        public void FloorDown()
        {
            if (currentFloorIndex > 0)
            {
                SetFloor(currentFloorIndex - 1);
            }
        }

        private void UpdateFloorCulling()
        {
            if (mainCamera == null) return;
            if (currentView != ViewMode.Arena) return; // Only affects arena view

            // Start with base arena mask (has floor layers unchecked)
            int mask = arenaCullingMask;

            // Add current floor layer
            int floorLayer = LayerMask.NameToLayer($"Floor{currentFloorIndex}");
            if (floorLayer >= 0)
            {
                mask |= (1 << floorLayer);
            }
            else
            {
                Debug.LogWarning($"[CameraManager] Layer 'Floor{currentFloorIndex}' not found");
            }

            // Add shared floor layer (elevators, etc.)
            if (sharedFloorLayer >= 0)
            {
                mask |= (1 << sharedFloorLayer);
            }

            mainCamera.cullingMask = mask;
        }

        #endregion

        /// <summary>
        /// Set the follow target for the Arena virtual camera.
        /// </summary>
        public void SetArenaTarget(Transform target)
        {
            if (arenaVCam != null)
            {
                arenaVCam.Follow = target;
            }
        }

        /// <summary>
        /// Set the follow target for the Space virtual camera.
        /// </summary>
        public void SetSpaceTarget(Transform target)
        {
            if (spaceVCam != null)
            {
                spaceVCam.Follow = target;
            }
        }

        /// <summary>
        /// Focus Arena camera on a specific position (no target tracking).
        /// </summary>
        public void FocusArenaOn(Vector3 position)
        {
            if (arenaVCam != null)
            {
                arenaVCam.Follow = null;
                arenaVCam.transform.position = new Vector3(position.x, position.y, arenaVCam.transform.position.z);
            }
        }

        /// <summary>
        /// Focus Space camera on a specific position (no target tracking).
        /// </summary>
        public void FocusSpaceOn(Vector3 position)
        {
            if (spaceVCam != null)
            {
                spaceVCam.Follow = null;
                spaceVCam.transform.position = new Vector3(position.x, position.y, spaceVCam.transform.position.z);
            }
        }

        /// <summary>
        /// Apply camera shake using Cinemachine Impulse.
        /// </summary>
        public void Shake(float intensity = 1f)
        {
            if (impulseSource != null)
            {
                impulseSource.GenerateImpulse(intensity);
            }
        }

        /// <summary>
        /// Apply camera shake with custom velocity direction.
        /// </summary>
        public void Shake(Vector3 velocity)
        {
            if (impulseSource != null)
            {
                impulseSource.GenerateImpulse(velocity);
            }
        }

        /// <summary>
        /// Apply camera shake to simulate an impact from a direction.
        /// </summary>
        public void ShakeFromImpact(Vector2 impactDirection, float intensity = 1f)
        {
            Vector3 velocity = new Vector3(-impactDirection.x, -impactDirection.y, 0) * intensity;
            Shake(velocity);
        }

        // Legacy compatibility methods
        public void ShakeActiveCamera(float intensity, float duration)
        {
            Shake(intensity);
        }

        public void ShakeCamera(Camera cam, float intensity, float duration)
        {
            Shake(intensity);
        }

        private void UpdateTacticalCameraForSpace()
        {
            if (tacticalCamera == null || spaceVCam == null) return;

            // Match tactical camera to space vcam position/settings
            if (spaceVCam.Follow != null)
            {
                Vector3 targetPos = spaceVCam.Follow.position;
                tacticalCamera.transform.position = new Vector3(targetPos.x, targetPos.y, tacticalCamera.transform.position.z);
            }
        }

        private void UpdateTacticalCameraForArena()
        {
            if (tacticalCamera == null || arenaVCam == null) return;

            // Match tactical camera to arena vcam position/settings
            if (arenaVCam.Follow != null)
            {
                Vector3 targetPos = arenaVCam.Follow.position;
                tacticalCamera.transform.position = new Vector3(targetPos.x, targetPos.y, tacticalCamera.transform.position.z);
            }
        }

        private void LateUpdate()
        {
            // Keep tactical camera synced with the non-active view
            if (tacticalCamera != null)
            {
                if (currentView == ViewMode.Arena)
                {
                    UpdateTacticalCameraForSpace();
                }
                else
                {
                    UpdateTacticalCameraForArena();
                }
            }
        }
    }

    public enum ViewMode
    {
        Arena,
        Space
    }
}
