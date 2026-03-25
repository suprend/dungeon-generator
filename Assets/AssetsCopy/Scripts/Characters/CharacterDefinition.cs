using DanverPlayground.Roguelike.Characters.Abilities;
using UnityEngine;

namespace DanverPlayground.Roguelike.Characters
{
    // Параметры базовой дальнобойной атаки персонажа.
    [System.Serializable]
    public class RangedAttackStats
    {
        public float damage = 1f;
        public float fireRate = 4f;
        public float projectileSpeed = 12f;
        public float projectileLifetime = 1.5f;
        public float projectileScale = 0.24f;
    }

    // ScriptableObject с полным описанием класса персонажа.
    [CreateAssetMenu(menuName = "DanverPlayground/Characters/Character Definition", fileName = "CharacterDefinition")]
    public class CharacterDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string characterId = "character";
        [SerializeField] private string displayName = "Character";
        [SerializeField] private Sprite sprite;
        [SerializeField] private Color projectileColor = Color.white;

        [Header("Gameplay")]
        [SerializeField] private CharacterStats baseStats = new CharacterStats();
        [SerializeField] private RangedAttackStats rangedAttack = new RangedAttackStats();
        [SerializeField] private ActiveAbilityDefinition[] activeAbilities = new ActiveAbilityDefinition[3];

        public string CharacterId => characterId;
        public string DisplayName => displayName;
        public Sprite Sprite => sprite;
        public Color ProjectileColor => projectileColor;
        public CharacterStats BaseStats => baseStats;
        public RangedAttackStats RangedAttack => rangedAttack;
        public ActiveAbilityDefinition[] ActiveAbilities => activeAbilities;
    }
}
