using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D))]
public sealed class HealthPickup : MonoBehaviour, IInteractable
{
    [SerializeField] private int healAmount = 4;
    [SerializeField] private bool restoreFullHealth;
    [SerializeField] private bool consumeOnUse = true;
    [SerializeField] private string interactionPrompt = "Press F to heal";

    public string InteractionPrompt => interactionPrompt;

    private void Reset()
    {
        var collider2D = GetComponent<BoxCollider2D>();
        if (collider2D != null)
            collider2D.isTrigger = true;
    }

    public bool CanInteract(GameObject interactor)
    {
        var health = FindHealth(interactor);
        if (health != null)
            return !health.IsDead;

        var character = FindPlayerCharacter(interactor);
        return character != null && character.IsAlive;
    }

    public void Interact(GameObject interactor)
    {
        var health = FindHealth(interactor);
        if (health != null && !health.IsDead)
        {
            var healthBefore = health.CurrentHealth;
            if (restoreFullHealth)
                health.RestoreFull();
            else
                health.Heal(healAmount);

            if (consumeOnUse && health.CurrentHealth > healthBefore)
                Destroy(gameObject);

            return;
        }

        var character = FindPlayerCharacter(interactor);
        if (character == null || !character.IsAlive)
            return;

        var characterHealthBefore = character.CurrentHealth;
        character.Heal(restoreFullHealth ? character.MaxHealth : healAmount);

        if (consumeOnUse && character.CurrentHealth > characterHealthBefore)
            Destroy(gameObject);
    }

    private static Health FindHealth(GameObject interactor)
    {
        if (interactor == null)
            return null;

        if (interactor.TryGetComponent<Health>(out var health))
            return health;

        health = interactor.GetComponentInParent<Health>();
        if (health != null)
            return health;

        return interactor.GetComponentInChildren<Health>();
    }

    private static PlayerCharacterTemplate FindPlayerCharacter(GameObject interactor)
    {
        if (interactor == null)
            return null;

        if (interactor.TryGetComponent<PlayerCharacterTemplate>(out var character))
            return character;

        character = interactor.GetComponentInParent<PlayerCharacterTemplate>();
        if (character != null)
            return character;

        return interactor.GetComponentInChildren<PlayerCharacterTemplate>();
    }
}
