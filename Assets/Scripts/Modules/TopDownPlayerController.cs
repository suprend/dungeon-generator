using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public sealed class TopDownPlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private bool normalizeDiagonal = true;
    [SerializeField] private bool flipHorizontally = true;
    [SerializeField] private Transform visualRoot;

    private Rigidbody2D body2D;
    private Vector2 moveInput;
    private int facingSign = 1;

    public float MoveSpeed
    {
        get => moveSpeed;
        set => moveSpeed = Mathf.Max(0f, value);
    }

    public Transform VisualRoot => GetVisualRoot();
    public int FacingSign => facingSign;

    private void Awake()
    {
        body2D = GetComponent<Rigidbody2D>();
        if (body2D != null)
            body2D.gravityScale = 0f;
    }

    private void Update()
    {
        moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        if (normalizeDiagonal && moveInput.sqrMagnitude > 1f)
            moveInput.Normalize();
        if (flipHorizontally)
            UpdateHorizontalFlip();
    }

    private void FixedUpdate()
    {
        var delta = moveInput * (moveSpeed * Time.fixedDeltaTime);
        if (body2D != null)
        {
            body2D.MovePosition(body2D.position + delta);
            return;
        }

        transform.position += (Vector3)delta;
    }

    private Transform GetVisualRoot()
    {
        return visualRoot != null ? visualRoot : transform;
    }

    private void UpdateHorizontalFlip()
    {
        SetFacingX(moveInput.x);
    }

    public void SetFacingX(float horizontalLook)
    {
        if (!flipHorizontally || Mathf.Abs(horizontalLook) <= 0.001f)
            return;

        facingSign = horizontalLook < 0f ? -1 : 1;

        var target = GetVisualRoot();
        var scale = target.localScale;
        scale.x = Mathf.Abs(scale.x) * facingSign;
        target.localScale = scale;
    }
}
