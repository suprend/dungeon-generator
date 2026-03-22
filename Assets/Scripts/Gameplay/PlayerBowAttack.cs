using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(TopDownPlayerController))]
public sealed class PlayerBowAttack : MonoBehaviour
{
    [SerializeField] private Transform shootOrigin;
    [SerializeField] private Vector2 shootOriginLocalOffset = new Vector2(0.35f, 0.05f);
    [SerializeField] private float horizontalFlipDeadZone = 0.08f;
    [SerializeField] private float shotIntervalSeconds = 0.35f;
    [SerializeField] private ArrowProjectile projectilePrefab;

    private TopDownPlayerController playerController;
    private Health ownerHealth;
    private float nextShotTime;
    private bool hasWarnedAboutMissingProjectilePrefab;

    private void Awake()
    {
        playerController = GetComponent<TopDownPlayerController>();
        ownerHealth = GetComponent<Health>();
    }

    private void Update()
    {
        if (ownerHealth == null)
            ownerHealth = GetComponent<Health>();

        var lookDirection = ResolveLookDirection();
        if (lookDirection.sqrMagnitude > 0.0001f && playerController != null && Mathf.Abs(lookDirection.x) >= Mathf.Max(0.001f, horizontalFlipDeadZone))
            playerController.SetFacingX(lookDirection.x);

        var aimDirection = ResolveAimDirection();

        if (!Input.GetButton("Fire1"))
            return;
        if (Time.time < nextShotTime)
            return;
        if (aimDirection.sqrMagnitude <= 0.0001f)
            return;
        if (projectilePrefab == null)
        {
            if (!hasWarnedAboutMissingProjectilePrefab)
            {
                hasWarnedAboutMissingProjectilePrefab = true;
                Debug.LogWarning("[PlayerBowAttack] Projectile Prefab is not assigned.", this);
            }

            return;
        }

        nextShotTime = Time.time + Mathf.Max(0.05f, shotIntervalSeconds);
        var projectile = Instantiate(projectilePrefab, ResolveShootOriginWorldPosition(), Quaternion.identity);
        projectile.Launch(aimDirection.normalized, ownerHealth);
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
