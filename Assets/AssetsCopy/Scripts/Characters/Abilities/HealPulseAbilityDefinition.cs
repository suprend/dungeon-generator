using UnityEngine;

namespace DanverPlayground.Roguelike.Characters.Abilities
{
    // Лечит владельца и всех союзников рядом с ним.
    [CreateAssetMenu(menuName = "DanverPlayground/Abilities/Heal Pulse", fileName = "Ability_HealPulse")]
    public class HealPulseAbilityDefinition : ActiveAbilityDefinition
    {
        [SerializeField] private float radius = 3.5f;
        [SerializeField] private float healAmount = 2f;

        public float Radius => radius;
        public float HealAmount => healAmount;

        public override bool Activate(GameCharacter user, Vector2 aimDirection)
        {
            if (!CanActivate(user))
            {
                return false;
            }

            user.Heal(healAmount);

            // Проходим по всем коллайдерам вокруг и лечим только членов той же группы.
            Collider2D[] hits = Physics2D.OverlapCircleAll(user.transform.position, radius);
            for (int i = 0; i < hits.Length; i++)
            {
                GameCharacter target = hits[i].GetComponent<GameCharacter>();
                if (target == null || target == user)
                {
                    continue;
                }

                if (target.PartyOwner == user.PartyOwner)
                {
                    target.Heal(healAmount);
                }
            }

            return true;
        }
    }
}
