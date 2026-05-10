//DP
using UnityEngine;

/// <summary>
/// Включает готовую обводку из префаба у выбранного персонажа
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerCharacterTemplate))]
public class PlayerCharacterOutlineVisibility : MonoBehaviour
{
    private const string DefaultOutlineObjectName = "Character outline";

    [SerializeField] private PlayerCharacterTemplate character;
    [SerializeField] private GameObject outlineObject;
    [SerializeField] private bool hideWhenIncapacitated = true;

    private void Reset()
    {
        CacheReferences();
    }

    private void Awake()
    {
        CacheReferences();
    }

    private void LateUpdate()
    {
        UpdateOutlineVisibility();
    }

    private void OnValidate()
    {
        CacheReferences();
    }

    /// <summary>
    /// Обновляет видимость обводки по стостоянию персонажа
    /// </summary>
    private void UpdateOutlineVisibility()
    {
        if (character == null || outlineObject == null)
        {
            return;
        }

        bool shouldShowOutline = character.IsPlayerControlled
            && (!hideWhenIncapacitated || character.IsAlive);

        if (outlineObject.activeSelf == shouldShowOutline)
        {
            return;
        }

        outlineObject.SetActive(shouldShowOutline);
    }

    /// <summary>
    /// Находит персонажа и объект обводки если они не назначены вручную
    /// </summary>
    private void CacheReferences()
    {
        if (character == null)
        {
            character = GetComponent<PlayerCharacterTemplate>();
        }

        if (outlineObject == null)
        {
            Transform outlineTransform = FindChildByName(transform, DefaultOutlineObjectName);
            outlineObject = outlineTransform != null ? outlineTransform.gameObject : null;
        }
    }

    /// <summary>
    /// Ищет дочерний объект по имени в иерархии персонажа
    /// </summary>
    private Transform FindChildByName(Transform root, string childName)
    {
        if (root == null)
        {
            return null;
        }

        foreach (Transform child in root)
        {
            if (child.name == childName)
            {
                return child;
            }

            Transform nestedChild = FindChildByName(child, childName);

            if (nestedChild != null)
            {
                return nestedChild;
            }
        }

        return null;
    }
}
