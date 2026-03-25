using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(TopDownPlayerController))]
public sealed class PlayerBowAttack : MonoBehaviour
{
    [SerializeField] private Transform shootOrigin;
    [SerializeField] private Vector2 shootOriginLocalOffset = new Vector2(0.35f, 0.05f);
    [SerializeField] private float projectileSpawnForwardOffset = 0.08f;
    [SerializeField] private float horizontalFlipDeadZone = 0.08f;
    [SerializeField] private float shotIntervalSeconds = 0.35f;
    [SerializeField] private ArrowProjectile projectilePrefab;
    [SerializeField] private bool playerInputEnabled = true;

    private TopDownPlayerController playerController;
    private Health ownerHealth;
    private float nextShotTime;
    private bool hasWarnedAboutMissingProjectilePrefab;
    private float projectileSpeed;
    private int projectileDamage;
    private float projectileLifetimeSeconds;
    private float projectileScale = 1f;
    private Color projectileTint = Color.white;

    private void Awake()
    {
        playerController = GetComponent<TopDownPlayerController>();
        ownerHealth = GetComponent<Health>();
        CacheProjectileDefaults();
    }

    private void Update()
    {
        if (ownerHealth == null)
            ownerHealth = GetComponent<Health>();

        if (!playerInputEnabled)
            return;

        var lookDirection = ResolveLookDirection();
        if (lookDirection.sqrMagnitude > 0.0001f && playerController != null && Mathf.Abs(lookDirection.x) >= Mathf.Max(0.001f, horizontalFlipDeadZone))
            playerController.SetFacingX(lookDirection.x);

        var aimDirection = ResolveAimDirection();

        if (!Input.GetButton("Fire1"))
            return;
        FireOnce(aimDirection);
    }

    public void ApplyAttackProfile(
        float newShotIntervalSeconds,
        float newProjectileSpeed,
        int newProjectileDamage,
        float newProjectileLifetimeSeconds,
        float newProjectileScale,
        Color newProjectileTint)
    {
        shotIntervalSeconds = Mathf.Max(0.05f, newShotIntervalSeconds);
        projectileSpeed = Mathf.Max(0.01f, newProjectileSpeed);
        projectileDamage = Mathf.Max(1, newProjectileDamage);
        projectileLifetimeSeconds = Mathf.Max(0.1f, newProjectileLifetimeSeconds);
        projectileScale = Mathf.Max(0.01f, newProjectileScale);
        projectileTint = newProjectileTint;
    }

    public bool FireOnce(Vector2 direction, bool bypassCooldown = false, float damageMultiplier = 1f, float speedMultiplier = 1f)
    {
        if (!bypassCooldown && Time.time < nextShotTime)
            return false;
        if (direction.sqrMagnitude <= 0.0001f)
            return false;
        if (projectilePrefab == null)
        {
            if (!hasWarnedAboutMissingProjectilePrefab)
            {
                hasWarnedAboutMissingProjectilePrefab = true;
                Debug.LogWarning("[PlayerBowAttack] Projectile Prefab is not assigned.", this);
            }

            return false;
        }

        var spawnPosition = ResolveShootOriginWorldPosition() + (Vector3)(direction.normalized * Mathf.Max(0f, projectileSpawnForwardOffset));
        var projectile = Instantiate(projectilePrefab, spawnPosition, Quaternion.identity);
        projectile.ConfigureRuntime(
            projectileSpeed * Mathf.Max(0.01f, speedMultiplier),
            Mathf.Max(1, Mathf.RoundToInt(projectileDamage * Mathf.Max(0.01f, damageMultiplier))),
            projectileLifetimeSeconds,
            projectileScale,
            projectileTint);
        projectile.Launch(direction.normalized, ownerHealth);

        if (!bypassCooldown)
            nextShotTime = Time.time + Mathf.Max(0.05f, shotIntervalSeconds);

        return true;
    }

    public void SetPlayerInputEnabled(bool enabled)
    {
        playerInputEnabled = enabled;
    }

    private void CacheProjectileDefaults()
    {
        if (projectilePrefab == null)
            return;

        projectileSpeed = projectilePrefab.Speed;
        projectileDamage = projectilePrefab.Damage;
        projectileLifetimeSeconds = projectilePrefab.LifetimeSeconds;
        projectileScale = projectilePrefab.VisualScale;
    }

    private Vector2 ResolveAimDirection()
    {
        var currentCamera = Camera.main;
        if (currentCamera != null)
        {
            var mouseWorld = ResolveMouseWorldPosition(currentCamera);
            var directionToCursor = (Vector2)(mouseWorld - ResolveShootOriginWorldPosition());
            if (directionToCursor.sqrMagnitude > 0.0001f)
                return directionToCursor.normalized;
        }

        return Vector2.right * (playerController != null ? playerController.FacingSign : 1);
    }

    private Vector2 ResolveLookDirection()
    {
        var currentCamera = Camera.main;
        if (currentCamera != null)
        {
            var mouseWorld = ResolveMouseWorldPosition(currentCamera);
            var lookOrigin = playerController != null && playerController.VisualRoot != null
                ? playerController.VisualRoot.position
                : transform.position;
            var directionToCursor = (Vector2)(mouseWorld - lookOrigin);
            if (directionToCursor.sqrMagnitude > 0.0001f)
                return directionToCursor.normalized;
        }

        return Vector2.right * (playerController != null ? playerController.FacingSign : 1);
    }

    private Vector3 ResolveShootOriginWorldPosition()
    {
        if (shootOrigin != null)
            return shootOrigin.position;

        var reference = playerController != null && playerController.VisualRoot != null
            ? playerController.VisualRoot
            : transform;

        return reference.TransformPoint(new Vector3(shootOriginLocalOffset.x, shootOriginLocalOffset.y, 0f));
    }

    private Vector3 ResolveMouseWorldPosition(Camera currentCamera)
    {
        var mousePosition = Input.mousePosition;
        mousePosition.z = Mathf.Abs(currentCamera.transform.position.z - transform.position.z);
        return currentCamera.ScreenToWorldPoint(mousePosition);
    }
}
