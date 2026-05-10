using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(TopDownPlayerController))]
public sealed class PlayerInteractor : MonoBehaviour
{
    private static readonly List<PlayerInteractor> ActiveInteractors = new();
    private static KeyCode consumedKeyThisFrame;
    private static int consumedFrame = -1;

    [SerializeField] private KeyCode interactKey = KeyCode.F;
    [SerializeField] private float interactRadius = 1.25f;
    [SerializeField] private LayerMask interactableLayers = ~0;
    [SerializeField] private TopDownPlayerController playerController;

    private readonly Collider2D[] overlapResults = new Collider2D[16];
    private IInteractable currentInteractable;

    public KeyCode InteractKey => interactKey;
    public IInteractable CurrentInteractable
    {
        get
        {
            RefreshCurrentInteractable();
            return currentInteractable;
        }
    }

    public static bool IsKeyReservedForInteraction(KeyCode key)
    {
        if (key == KeyCode.None)
            return false;

        for (var i = ActiveInteractors.Count - 1; i >= 0; i--)
        {
            var interactor = ActiveInteractors[i];
            if (interactor == null)
            {
                ActiveInteractors.RemoveAt(i);
                continue;
            }

            if (interactor.InteractKey != key || !interactor.IsInputActive())
                continue;

            if (interactor.CurrentInteractable != null)
                return true;
        }

        return false;
    }

    public static bool WasKeyConsumedThisFrame(KeyCode key)
    {
        return key != KeyCode.None && consumedFrame == Time.frameCount && consumedKeyThisFrame == key;
    }

    private void Reset()
    {
        CacheComponents();
    }

    private void Awake()
    {
        CacheComponents();
    }

    private void OnEnable()
    {
        if (!ActiveInteractors.Contains(this))
            ActiveInteractors.Add(this);
    }

    private void OnDisable()
    {
        ActiveInteractors.Remove(this);
        currentInteractable = null;
        InteractionPromptHud.Hide(this);
    }

    private void Update()
    {
        if (!IsInputActive())
        {
            currentInteractable = null;
            InteractionPromptHud.Hide(this);
            return;
        }

        RefreshCurrentInteractable();
        UpdatePromptHud();
        if (currentInteractable != null && Input.GetKeyDown(interactKey))
        {
            consumedKeyThisFrame = interactKey;
            consumedFrame = Time.frameCount;
            currentInteractable.Interact(gameObject);
            RefreshCurrentInteractable();
            UpdatePromptHud();
        }
    }

    private void CacheComponents()
    {
        if (playerController == null)
            playerController = GetComponent<TopDownPlayerController>();
    }

    private bool IsInputActive()
    {
        return isActiveAndEnabled &&
            (playerController == null || playerController.PlayerInputEnabled);
    }

    private void RefreshCurrentInteractable()
    {
        currentInteractable = null;
        if (!IsInputActive())
            return;

        var hitCount = Physics2D.OverlapCircleNonAlloc(
            transform.position,
            Mathf.Max(0.05f, interactRadius),
            overlapResults,
            interactableLayers);

        var bestSqrDistance = float.MaxValue;
        for (var i = 0; i < hitCount; i++)
        {
            var hit = overlapResults[i];
            if (hit == null || hit.attachedRigidbody != null && hit.attachedRigidbody.gameObject == gameObject)
                continue;

            var interactable = FindInteractable(hit);
            if (interactable == null || !interactable.CanInteract(gameObject))
                continue;

            var sqrDistance = ((Vector2)hit.transform.position - (Vector2)transform.position).sqrMagnitude;
            if (sqrDistance >= bestSqrDistance)
                continue;

            bestSqrDistance = sqrDistance;
            currentInteractable = interactable;
        }
    }

    private static IInteractable FindInteractable(Collider2D hit)
    {
        if (hit == null)
            return null;

        var components = hit.GetComponentsInParent<MonoBehaviour>(true);
        for (var i = 0; i < components.Length; i++)
        {
            if (components[i] is IInteractable interactable)
                return interactable;
        }

        components = hit.GetComponentsInChildren<MonoBehaviour>(true);
        for (var i = 0; i < components.Length; i++)
        {
            if (components[i] is IInteractable interactable)
                return interactable;
        }

        return null;
    }

    private void UpdatePromptHud()
    {
        if (currentInteractable == null)
        {
            InteractionPromptHud.Hide(this);
            return;
        }

        var prompt = string.IsNullOrWhiteSpace(currentInteractable.InteractionPrompt)
            ? $"Press {FormatKey(interactKey)} to interact"
            : currentInteractable.InteractionPrompt;

        InteractionPromptHud.Show(this, prompt);
    }

    private static string FormatKey(KeyCode key)
    {
        return key switch
        {
            KeyCode.Alpha0 => "0",
            KeyCode.Alpha1 => "1",
            KeyCode.Alpha2 => "2",
            KeyCode.Alpha3 => "3",
            KeyCode.Alpha4 => "4",
            KeyCode.Alpha5 => "5",
            KeyCode.Alpha6 => "6",
            KeyCode.Alpha7 => "7",
            KeyCode.Alpha8 => "8",
            KeyCode.Alpha9 => "9",
            _ => key == KeyCode.None ? "-" : key.ToString()
        };
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.05f, interactRadius));
    }
}
