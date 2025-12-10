using UnityEngine;
using Starbelter.Combat;

namespace Starbelter.Core
{
    /// <summary>
    /// Basic player controller for testing. WASD movement, click to shoot.
    /// Press R to cycle through shot types (Snap, Aimed, Burst, Suppress).
    /// </summary>
    public class PlayerController : MonoBehaviour, ITargetable
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 5f;

        [Header("Shooting")]
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private Team team = Team.Empire;
        [SerializeField] private float fireRate = 0.2f;

        [Header("Character")]
        [SerializeField] private string playerName = "Player";
        [SerializeField] private float defaultWeaponRange = 15f;

        private const float MAX_SPREAD_ANGLE = 30f;

        private Camera mainCamera;
        private float nextFireTime;
        private UnitHealth unitHealth;
        private Character character;
        private bool isDucked;
        private Vector3 normalScale;

        // Shot type system
        private ShotType currentShotType = ShotType.Snap;
        private bool isAiming;
        private float aimStartTime;
        private bool isFiringBurst;

        // ITargetable implementation
        public Team Team => team;
        public Transform Transform => transform;
        public Vector3 Position => transform.position;
        public bool IsDead => unitHealth != null && unitHealth.IsDead;
        public float WeaponRange => character?.MainWeapon?.MaxRange ?? defaultWeaponRange;
        public bool IsDucked => isDucked;

        public Character Character => character;

        private void Awake()
        {
            normalScale = transform.localScale;

            // Create default character for player
            character = new Character
            {
                FirstName = playerName,
                LastName = "",
                IsOfficer = false,
                Branch = ServiceBranch.Marine,
                Rank = 5, // Sergeant (E-5)
                Specialization = Specialization.Rifleman,
                Profession = ProfessionCategory.Combat,
                MainWeaponId = "assault_rifle",
                Fitness = 12,
                Accuracy = 12,
                Reflexes = 12,
                Bravery = 15,
                Perception = 12,
                Stealth = 10,
                Tactics = 10,
                Technical = 10,
                Composure = 12,
                Discipline = 12,
                Logic = 10,
                Communication = 10
            };
        }

        private void Start()
        {
            mainCamera = Camera.main;
            unitHealth = GetComponentInChildren<UnitHealth>();

            // Try to load weapon from data, fall back to default range
            character.LoadWeapon();
            if (character.MainWeapon == null)
            {
                Debug.LogWarning($"[PlayerController] Could not load weapon '{character.MainWeaponId}', using default range {defaultWeaponRange}");
            }

            // Initialize character health
            character.InitializeHealth();

            Debug.Log($"[Player] Shot mode: {currentShotType} (Press R to cycle)");
        }

        private void Update()
        {
            if (IsDead) return;

            HandleMovement();
            HandleShotTypeCycle();
            HandleShooting();
            HandleDuck();
        }

        private void HandleDuck()
        {
            if (Input.GetKeyDown(KeyCode.X))
            {
                isDucked = !isDucked;
                transform.localScale = isDucked
                    ? new Vector3(normalScale.x, normalScale.y * 0.7f, normalScale.z)
                    : normalScale;
                Debug.Log($"[Player] Ducked: {isDucked}");
            }
        }

        private void HandleShotTypeCycle()
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                // Cycle through shot types
                currentShotType = currentShotType switch
                {
                    ShotType.Snap => ShotType.Aimed,
                    ShotType.Aimed => ShotType.Burst,
                    ShotType.Burst => ShotType.Suppress,
                    ShotType.Suppress => ShotType.Snap,
                    _ => ShotType.Snap
                };

                // Check if weapon supports this mode
                var weapon = character?.MainWeapon;
                if (weapon != null)
                {
                    if (currentShotType == ShotType.Aimed && !weapon.CanAimedShot)
                    {
                        currentShotType = ShotType.Burst;
                    }
                    if (currentShotType == ShotType.Burst && !weapon.CanBurst)
                    {
                        currentShotType = ShotType.Suppress;
                    }
                    if (currentShotType == ShotType.Suppress && !weapon.CanSuppress)
                    {
                        currentShotType = ShotType.Snap;
                    }
                }

                Debug.Log($"[Player] Shot mode: {currentShotType}");
            }
        }

        private void HandleMovement()
        {
            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");

            Vector3 direction = new Vector3(horizontal, vertical, 0f).normalized;
            transform.position += direction * moveSpeed * Time.deltaTime;
        }

        private void HandleShooting()
        {
            // If we're mid-burst, let it finish
            if (isFiringBurst) return;

            // If we're aiming, check for completion or release
            if (isAiming)
            {
                UpdateAiming();
                return;
            }

            // Start shooting on mouse down
            if (Input.GetMouseButtonDown(0))
            {
                if (currentShotType == ShotType.Aimed)
                {
                    StartAiming();
                }
                else if (currentShotType == ShotType.Burst)
                {
                    FireBurst();
                }
                else
                {
                    // Snap or Suppress - fire immediately (with fire rate)
                    if (Time.time >= nextFireTime)
                    {
                        FireShot(currentShotType);
                        nextFireTime = Time.time + GetFireRate();
                    }
                }
            }
            // Continue firing for held mouse (Snap and Suppress only)
            else if (Input.GetMouseButton(0) && (currentShotType == ShotType.Snap || currentShotType == ShotType.Suppress))
            {
                if (Time.time >= nextFireTime)
                {
                    FireShot(currentShotType);
                    nextFireTime = Time.time + GetFireRate();
                }
            }
        }

        private float GetFireRate()
        {
            var weapon = character?.MainWeapon;
            if (weapon == null) return fireRate;

            if (currentShotType == ShotType.Suppress)
            {
                return fireRate / weapon.SuppressFireRateMultiplier;
            }
            return fireRate;
        }

        #region Aiming

        private void StartAiming()
        {
            var weapon = character?.MainWeapon;
            if (weapon == null || !weapon.CanAimedShot)
            {
                FireShot(ShotType.Snap);
                return;
            }

            isAiming = true;
            aimStartTime = Time.time;
            Debug.Log($"[Player] Aiming... ({weapon.AimTime:F1}s) - click again to cancel");
        }

        private void UpdateAiming()
        {
            var weapon = character?.MainWeapon;
            float aimTime = weapon?.AimTime ?? 1.5f;

            // Click again to cancel aim
            if (Input.GetMouseButtonDown(0))
            {
                Debug.Log($"[Player] Aim cancelled");
                isAiming = false;
                return;
            }

            // Check if aim time complete - auto fire
            if (Time.time >= aimStartTime + aimTime)
            {
                isAiming = false;
                FireShot(ShotType.Aimed);
                Debug.Log($"[Player] Aimed shot FIRED!");
            }
        }

        #endregion

        #region Burst Fire

        private void FireBurst()
        {
            StartCoroutine(FireBurstCoroutine());
        }

        private System.Collections.IEnumerator FireBurstCoroutine()
        {
            var weapon = character?.MainWeapon;
            if (weapon == null || !weapon.CanBurst)
            {
                FireShot(ShotType.Snap);
                yield break;
            }

            isFiringBurst = true;
            int burstCount = weapon.BurstCount;
            float burstDelay = weapon.BurstDelay;

            Debug.Log($"[Player] Burst fire - {burstCount} rounds");

            for (int i = 0; i < burstCount; i++)
            {
                if (IsDead) break;

                FireShot(ShotType.Burst);

                if (i < burstCount - 1)
                {
                    yield return new WaitForSeconds(burstDelay);
                }
            }

            isFiringBurst = false;
        }

        #endregion

        #region Fire Shot

        private void FireShot(ShotType shotType)
        {
            if (projectilePrefab == null)
            {
                Debug.LogWarning("[PlayerController] No projectile prefab assigned!");
                return;
            }

            if (mainCamera == null) return;

            var weapon = character?.MainWeapon;

            // Get accuracy and cover penetration for this shot type
            float accuracy = GetShotAccuracy(shotType);
            float coverPenetration = GetShotCoverPenetration(shotType);

            // Get direction toward cursor
            Vector3 mousePos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
            mousePos.z = 0f;
            Vector2 baseDirection = (mousePos - transform.position).normalized;

            // Apply spread based on accuracy
            Vector2 finalDirection = ApplySpread(baseDirection, accuracy);

            // Spawn projectile
            GameObject projectileObj = Instantiate(projectilePrefab, transform.position, Quaternion.identity);

            // Initialize projectile
            Projectile projectile = projectileObj.GetComponent<Projectile>();
            if (projectile != null)
            {
                projectile.SetCoverPenetration(coverPenetration);
                projectile.Fire(finalDirection, team, gameObject);
            }
            else
            {
                Debug.LogWarning("[PlayerController] Projectile prefab missing Projectile component!");
                Destroy(projectileObj);
            }
        }

        private float GetShotAccuracy(ShotType shotType)
        {
            var weapon = character?.MainWeapon;
            if (weapon == null) return 0.7f;

            return shotType switch
            {
                ShotType.Snap => weapon.SnapAccuracy,
                ShotType.Aimed => weapon.AimedAccuracy,
                ShotType.Suppress => weapon.SuppressAccuracy,
                ShotType.Burst => weapon.BurstAccuracy,
                _ => weapon.SnapAccuracy
            };
        }

        private float GetShotCoverPenetration(ShotType shotType)
        {
            var weapon = character?.MainWeapon;
            if (weapon == null) return 1.0f;

            return shotType switch
            {
                ShotType.Snap => weapon.SnapCoverPenetration,
                ShotType.Aimed => weapon.AimedCoverPenetration,
                ShotType.Suppress => weapon.SuppressCoverPenetration,
                ShotType.Burst => weapon.BurstCoverPenetration,
                _ => weapon.SnapCoverPenetration
            };
        }

        private Vector2 ApplySpread(Vector2 baseDirection, float accuracy)
        {
            float spreadAngle = MAX_SPREAD_ANGLE * (1f - accuracy);
            float randomAngle = Random.Range(-spreadAngle, spreadAngle);

            float rad = randomAngle * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            return new Vector2(
                baseDirection.x * cos - baseDirection.y * sin,
                baseDirection.x * sin + baseDirection.y * cos
            ).normalized;
        }

        #endregion

        private void OnGUI()
        {
            // Display current shot mode in top-left corner
            var weapon = character?.MainWeapon;
            string weaponName = weapon?.Name ?? "No Weapon";

            GUI.Label(new Rect(10, 10, 300, 25), $"Weapon: {weaponName}");
            GUI.Label(new Rect(10, 35, 300, 25), $"Shot Mode: {currentShotType} (R to cycle)");

            if (isAiming)
            {
                float aimTime = weapon?.AimTime ?? 1.5f;
                float progress = (Time.time - aimStartTime) / aimTime;
                GUI.Label(new Rect(10, 60, 300, 25), $"Aiming: {progress:P0}");
            }

            if (isFiringBurst)
            {
                GUI.Label(new Rect(10, 60, 300, 25), "BURST FIRING...");
            }
        }
    }
}
