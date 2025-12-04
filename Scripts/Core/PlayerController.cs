using UnityEngine;
using Starbelter.Combat;

namespace Starbelter.Core
{
    /// <summary>
    /// Basic player controller for testing. WASD movement, click to shoot.
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

        private Camera mainCamera;
        private float nextFireTime;
        private UnitHealth unitHealth;
        private Character character;
        private bool isDucked;
        private Vector3 normalScale;

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
                EnlistedRank = MarineEnlistedRank.Sergeant,
                Specialization = Specialization.Rifleman,
                MainWeaponId = "assault_rifle",
                Vitality = 12,
                Accuracy = 12,
                Reflex = 12,
                Bravery = 15,
                Agility = 12,
                Perception = 12,
                Stealth = 10,
                Tactics = 10
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
        }

        private void Update()
        {
            HandleMovement();
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

        private void HandleMovement()
        {
            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");

            Vector3 direction = new Vector3(horizontal, vertical, 0f).normalized;
            transform.position += direction * moveSpeed * Time.deltaTime;
        }

        private void HandleShooting()
        {
            if (Input.GetMouseButton(0) && Time.time >= nextFireTime)
            {
                Shoot();
                nextFireTime = Time.time + fireRate;
            }
        }

        private void Shoot()
        {
            if (projectilePrefab == null)
            {
                Debug.LogWarning("[PlayerController] No projectile prefab assigned!");
                return;
            }

            if (mainCamera == null) return;

            // Get direction toward cursor
            Vector3 mousePos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
            mousePos.z = 0f;
            Vector2 direction = (mousePos - transform.position).normalized;

            // Spawn projectile
            GameObject projectileObj = Instantiate(projectilePrefab, transform.position, Quaternion.identity);

            // Initialize projectile
            Projectile projectile = projectileObj.GetComponent<Projectile>();
            if (projectile != null)
            {
                projectile.Fire(direction, team, gameObject);
            }
            else
            {
                Debug.LogWarning("[PlayerController] Projectile prefab missing Projectile component!");
                Destroy(projectileObj);
            }
        }
    }
}
