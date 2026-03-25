using UnityEngine;
using DanverPlayground.Roguelike.Combat;

namespace DanverPlayground.Roguelike.Characters
{
    // Простейший ИИ союзника: держится рядом с лидером и стреляет по врагам обычной атакой.
    public class CompanionAIController : MonoBehaviour
    {
        [Header("Follow")]
        [SerializeField] private float followDistance = 1.8f;
        [SerializeField] private float teleportDistance = 10f;
        [SerializeField] private Vector2 formationOffset = Vector2.right;

        [Header("Combat")]
        [SerializeField] private float attackDistance = 8f;

        private GameCharacter character;
        private PartyController partyController;
        private float nextAttackDecisionTime;

        public void Initialize(GameCharacter owner, PartyController ownerParty)
        {
            character = owner;
            partyController = ownerParty;
        }

        private void OnEnable()
        {
            nextAttackDecisionTime = Time.time + Random.Range(0.05f, 0.25f);
        }

        private void Update()
        {
            if (character == null || partyController == null)
            {
                return;
            }

            // Все неактивные герои ориентируются на выбранного игроком лидера.
            GameCharacter leader = partyController.ActiveCharacter;
            if (leader == null || leader == character)
            {
                character.SetMovementInput(Vector2.zero);
                return;
            }

            Vector2 targetPosition = (Vector2)leader.transform.position + partyController.GetFormationOffset(character, formationOffset);
            Vector2 toTarget = targetPosition - (Vector2)character.transform.position;

            if (toTarget.magnitude > teleportDistance)
            {
                // Если союзник сильно отстал, возвращаем его в формацию без долгого добегания.
                character.transform.position = targetPosition;
                character.Body.velocity = Vector2.zero;
                toTarget = Vector2.zero;
            }

            Vector2 moveInput = toTarget.magnitude > followDistance ? toTarget.normalized : Vector2.zero;
            character.SetMovementInput(moveInput);

            EnemyUnit nearestEnemy = FindNearestEnemy();
            if (nearestEnemy != null)
            {
                Vector2 enemyDirection = (Vector2)nearestEnemy.transform.position - (Vector2)character.transform.position;
                character.SetAimInput(enemyDirection);

                if (enemyDirection.magnitude <= attackDistance && Time.time >= nextAttackDecisionTime)
                {
                    // ИИ не стреляет каждый кадр, а делает короткие интервалы, чтобы не выглядеть слишком "роботно".
                    nextAttackDecisionTime = Time.time + Random.Range(0.18f, 0.35f);
                    character.TryBasicAttack();
                }
            }
            else
            {
                character.SetAimInput((Vector2)leader.transform.position - (Vector2)character.transform.position);
            }

        }

        private EnemyUnit FindNearestEnemy()
        {
            EnemyUnit best = null;
            float bestDistance = float.MaxValue;

            // Выбираем ближайшую живую цель из общего списка врагов.
            for (int i = 0; i < EnemyUnit.ActiveEnemies.Count; i++)
            {
                EnemyUnit enemy = EnemyUnit.ActiveEnemies[i];
                if (enemy == null || !enemy.IsAlive)
                {
                    continue;
                }

                float sqrDistance = ((Vector2)enemy.transform.position - (Vector2)character.transform.position).sqrMagnitude;
                if (sqrDistance < bestDistance)
                {
                    bestDistance = sqrDistance;
                    best = enemy;
                }
            }

            return best;
        }
    }
}
