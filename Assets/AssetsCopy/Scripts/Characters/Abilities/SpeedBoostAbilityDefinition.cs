using UnityEngine;

namespace DanverPlayground.Roguelike.Characters.Abilities
{
    // Временно ускоряет персонажа.
    [CreateAssetMenu(menuName = "DanverPlayground/Abilities/Speed Boost", fileName = "Ability_SpeedBoost")]
    public class SpeedBoostAbilityDefinition : ActiveAbilityDefinition
    {
        [SerializeField] private float speedMultiplier = 1.75f;
        [SerializeField] private float duration = 4f;

        public float SpeedMultiplier => speedMultiplier;
        public float Duration => duration;

        public override bool Activate(GameCharacter user, Vector2 aimDirection)
        {
            if (!CanActivate(user))
            {
                return false;
            }

            user.ApplySpeedBoost(speedMultiplier, duration);
            return true;
        }
    }
}
