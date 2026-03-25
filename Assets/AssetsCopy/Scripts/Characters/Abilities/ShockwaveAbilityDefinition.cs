using UnityEngine;

namespace DanverPlayground.Roguelike.Characters.Abilities
{
    // Отталкивает все физические объекты рядом с персонажем.
    [CreateAssetMenu(menuName = "DanverPlayground/Abilities/Shockwave", fileName = "Ability_Shockwave")]
    public class ShockwaveAbilityDefinition : ActiveAbilityDefinition
    {
        [SerializeField] private float radius = 3f;
        [SerializeField] private float force = 10f;

        public float Radius => radius;
        public float Force => force;
        public float KnockbackDistance => Mathf.Max(0.4f, force * 0.12f);
        public float KnockbackDuration => 0.14f;

        public override bool Activate(GameCharacter user, Vector2 aimDirection)
        {
            if (!CanActivate(user))
            {
                return false;
            }

            Collider2D[] hits = Physics2D.OverlapCircleAll(user.transform.position, radius);
            for (int i = 0; i < hits.Length; i++)
            {
                Rigidbody2D hitBody = hits[i].attachedRigidbody;
                if (hitBody == null || hitBody == user.Body)
                {
                    continue;
                }

                Vector2 direction = (hitBody.position - user.Body.position).normalized;
                if (direction.sqrMagnitude < 0.01f)
                {
                    direction = Vector2.up;
                }

                hitBody.AddForce(direction * force, ForceMode2D.Impulse);
            }

            return true;
        }
    }
}
