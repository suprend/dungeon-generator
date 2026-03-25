using UnityEngine;

namespace DanverPlayground.Roguelike.Characters.Abilities
{
    // Мобильная способность: короткий рывок в сторону прицеливания.
    [CreateAssetMenu(menuName = "DanverPlayground/Abilities/Dash", fileName = "Ability_Dash")]
    public class DashAbilityDefinition : ActiveAbilityDefinition
    {
        [SerializeField] private float dashSpeed = 14f;
        [SerializeField] private float dashDuration = 0.2f;

        public float DashSpeed => dashSpeed;
        public float DashDuration => dashDuration;

        public override bool Activate(GameCharacter user, Vector2 aimDirection)
        {
            if (!CanActivate(user))
            {
                return false;
            }

            Vector2 direction = aimDirection.sqrMagnitude > 0.01f ? aimDirection.normalized : user.LastNonZeroMoveDirection;
            user.Dash(direction, dashSpeed, dashDuration);
            return true;
        }
    }
}
