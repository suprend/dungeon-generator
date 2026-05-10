using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D))]
public sealed class LevelExitPortal : MonoBehaviour, IInteractable
{
    [SerializeField] private string interactionPrompt = "Press F to descend";
    [SerializeField] private DungeonRunController runController;

    public string InteractionPrompt => interactionPrompt;

    private void Reset()
    {
        var collider2D = GetComponent<BoxCollider2D>();
        if (collider2D != null)
            collider2D.isTrigger = true;
    }

    private void Awake()
    {
        if (runController == null)
            runController = FindObjectOfType<DungeonRunController>();
    }

    public void Initialize(DungeonRunController controller)
    {
        runController = controller;
    }

    public bool CanInteract(GameObject interactor)
    {
        return runController != null;
    }

    public void Interact(GameObject interactor)
    {
        if (runController != null)
            runController.GoToNextLevel();
    }
}
