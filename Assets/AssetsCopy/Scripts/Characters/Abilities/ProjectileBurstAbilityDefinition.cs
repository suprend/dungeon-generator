using DanverPlayground.Roguelike.Combat;
using UnityEngine;

namespace DanverPlayground.Roguelike.Characters.Abilities
{
    // Способность-всплеск: выпускает веер из нескольких снарядов.
    [CreateAssetMenu(menuName = "DanverPlayground/Abilities/Projectile Burst", fileName = "Ability_ProjectileBurst")]
    public class ProjectileBurstAbilityDefinition : ActiveAbilityDefinition
    {
        [SerializeField] private int projectileCount = 5;
        [SerializeField] private float spreadAngle = 35f;
        [SerializeField] private float damageMultiplier = 1.4f;
        [SerializeField] private float speedMultiplier = 1.15f;

        public int ProjectileCount => Mathf.Max(1, projectileCount);
        public float SpreadAngle => spreadAngle;
        public float DamageMultiplier => damageMultiplier;
        public float SpeedMultiplier => speedMultiplier;

        public override bool Activate(GameCharacter user, Vector2 aimDirection)
        {
            if (!CanActivate(user))
            {
                return false;
            }

            Vector2 direction = aimDirection.sqrMagnitude > 0.01f ? aimDirection.normalized : user.LastNonZeroMoveDirection;
            if (direction.sqrMagnitude < 0.01f)
            {
                direction = Vector2.right;
            }

            // Равномерно распределяем углы внутри заданного конуса.
            float halfSpread = spreadAngle * 0.5f;
            for (int i = 0; i < projectileCount; i++)
            {
                float t = projectileCount == 1 ? 0.5f : i / (float)(projectileCount - 1);
                float angle = Mathf.Lerp(-halfSpread, halfSpread, t);
                Vector2 rotated = Quaternion.Euler(0f, 0f, angle) * direction;
                user.FireProjectile(rotated, damageMultiplier, speedMultiplier);
            }

            return true;
        }
    }
}
