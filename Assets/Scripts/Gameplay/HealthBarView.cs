using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public sealed class HealthBarView : MonoBehaviour
{
    [SerializeField] private float worldYOffset = 0.55f;
    [SerializeField] private float barWidth = 0.95f;
    [SerializeField] private float barHeight = 0.14f;
    [SerializeField] private float barPadding = 0.02f;
    [SerializeField] private Color fillMinColor = new Color(0.85f, 0.2f, 0.2f, 1f);
    [SerializeField] private Color fillMaxColor = new Color(0.2f, 0.9f, 0.35f, 1f);
    [SerializeField] private Color backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.85f);

    private static Sprite sharedSprite;

    private Health health;
    private Transform barRoot;
    private Transform fillAnchor;
    private Transform fillTransform;
    private SpriteRenderer backgroundRenderer;
    private SpriteRenderer fillRenderer;
    private float cachedYOffset;

    public static HealthBarView EnsureAttached(GameObject target)
    {
        if (target == null)
            return null;

        if (!target.TryGetComponent<Health>(out _))
            return null;

        if (!target.TryGetComponent<HealthBarView>(out var view))
            view = target.AddComponent<HealthBarView>();

        return view;
    }

    private void Awake()
    {
        health = GetComponent<Health>();
        cachedYOffset = Mathf.Max(0.1f, ComputeWorldYOffset());
        EnsureVisuals();
        RefreshVisuals();
    }

    private void OnEnable()
    {
        if (health == null)
            health = GetComponent<Health>();

        if (health != null)
            health.Changed += HandleHealthChanged;

        RefreshVisuals();
    }

    private void OnDisable()
    {
        if (health != null)
            health.Changed -= HandleHealthChanged;
    }

    private void LateUpdate()
    {
        if (barRoot == null)
            return;

        barRoot.position = transform.position + Vector3.up * cachedYOffset;
        barRoot.localScale = new Vector3(
            SafeInverseScale(transform.lossyScale.x),
            SafeInverseScale(transform.lossyScale.y),
            1f);
    }

    private void HandleHealthChanged(Health _)
    {
        RefreshVisuals();
    }

    private void EnsureVisuals()
    {
        if (barRoot != null)
            return;

        barRoot = new GameObject("HealthBarRoot").transform;
        barRoot.SetParent(transform, false);

        backgroundRenderer = CreateBarRenderer("Background", backgroundColor, out _);
        backgroundRenderer.transform.SetParent(barRoot, false);
        backgroundRenderer.transform.localScale = new Vector3(barWidth, barHeight, 1f);

        fillAnchor = new GameObject("FillAnchor").transform;
        fillAnchor.SetParent(barRoot, false);
        fillAnchor.localPosition = new Vector3(-(barWidth * 0.5f) + barPadding, 0f, 0f);

        fillRenderer = CreateBarRenderer("Fill", fillMaxColor, out fillTransform);
        fillRenderer.transform.SetParent(fillAnchor, false);

        ApplySorting();
    }

    private SpriteRenderer CreateBarRenderer(string objectName, Color color, out Transform rendererTransform)
    {
        var child = new GameObject(objectName);
        rendererTransform = child.transform;
        var renderer = child.AddComponent<SpriteRenderer>();
        renderer.sprite = GetSharedSprite();
        renderer.color = color;
        return renderer;
    }

    private void RefreshVisuals()
    {
        if (health == null)
            return;

        EnsureVisuals();

        var normalized = Mathf.Clamp01(health.NormalizedHealth);
        var innerWidth = Mathf.Max(0.01f, barWidth - barPadding * 2f);
        var innerHeight = Mathf.Max(0.01f, barHeight - barPadding * 2f);
        var fillWidth = innerWidth * normalized;

        fillRenderer.color = Color.Lerp(fillMinColor, fillMaxColor, normalized);
        fillRenderer.enabled = fillWidth > 0.001f;

        if (fillTransform != null)
        {
            fillTransform.localPosition = new Vector3(fillWidth * 0.5f, 0f, 0f);
            fillTransform.localScale = new Vector3(Mathf.Max(0.0001f, fillWidth), innerHeight, 1f);
        }

        if (backgroundRenderer != null)
            backgroundRenderer.enabled = true;
    }

    private void ApplySorting()
    {
        var sortingLayerId = 0;
        var sortingOrder = 200;
        var renderers = GetComponentsInChildren<SpriteRenderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null || renderer == backgroundRenderer || renderer == fillRenderer)
                continue;

            sortingLayerId = renderer.sortingLayerID;
            sortingOrder = Mathf.Max(sortingOrder, renderer.sortingOrder + 10);
        }

        if (backgroundRenderer != null)
        {
            backgroundRenderer.sortingLayerID = sortingLayerId;
            backgroundRenderer.sortingOrder = sortingOrder;
        }

        if (fillRenderer != null)
        {
            fillRenderer.sortingLayerID = sortingLayerId;
            fillRenderer.sortingOrder = sortingOrder + 1;
        }
    }

    private float ComputeWorldYOffset()
    {
        var maxTop = transform.position.y;
        var foundBounds = false;

        var renderers = GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
                continue;

            maxTop = Mathf.Max(maxTop, renderer.bounds.max.y);
            foundBounds = true;
        }

        var colliders = GetComponentsInChildren<Collider2D>(true);
        for (var i = 0; i < colliders.Length; i++)
        {
            var collider2D = colliders[i];
            if (collider2D == null)
                continue;

            maxTop = Mathf.Max(maxTop, collider2D.bounds.max.y);
            foundBounds = true;
        }

        if (!foundBounds)
            return worldYOffset;

        return Mathf.Max(worldYOffset, maxTop - transform.position.y + 0.08f);
    }

    private static float SafeInverseScale(float value)
    {
        return Mathf.Abs(value) > 0.0001f ? 1f / Mathf.Abs(value) : 1f;
    }

    private static Sprite GetSharedSprite()
    {
        if (sharedSprite != null)
            return sharedSprite;

        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "HealthBarView_WhitePixel",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        texture.SetPixel(0, 0, Color.white);
        texture.Apply(false, true);

        sharedSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        sharedSprite.name = "HealthBarView_WhitePixel";
        return sharedSprite;
    }
}
