using UnityEngine;

namespace DanverPlayground.Roguelike.Characters.Abilities
{
    // Базовый ScriptableObject для активной способности персонажа.
    public abstract class ActiveAbilityDefinition : ScriptableObject
    {
        [SerializeField] private string abilityName = "Ability";
        [SerializeField] private float cooldown = 3f;

        public string AbilityName => abilityName;
        public float Cooldown => cooldown;

        public virtual bool CanActivate(GameCharacter user)
        {
            return user != null && user.CurrentHealth > 0f;
        }

        // Перегрузка нужна, если позже понадобится логика, завязанная на конкретный слот способности.
        public virtual bool CanActivate(GameCharacter user, int slotIndex)
        {
            return CanActivate(user) && user.IsAbilityReady(slotIndex);
        }

        public abstract bool Activate(GameCharacter user, Vector2 aimDirection);
    }
}
