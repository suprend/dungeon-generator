using UnityEngine;

namespace DanverPlayground.Roguelike.Characters
{
    // Считывает ввод игрока и переводит его в команды для активного персонажа.
    public class PlayerCharacterBrain : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private string horizontalAxis = "Horizontal";
        [SerializeField] private string verticalAxis = "Vertical";
        [SerializeField] private KeyCode ability1Key = KeyCode.Alpha1;
        [SerializeField] private KeyCode ability2Key = KeyCode.Alpha2;
        [SerializeField] private KeyCode ability3Key = KeyCode.Alpha3;

        private GameCharacter character;

        public void Initialize(GameCharacter owner)
        {
            character = owner;
        }

        private void Update()
        {
            if (character == null)
            {
                return;
            }

            // Движение берётся из старого Input Manager, чтобы прототип работал без отдельной настройки Input System.
            Vector2 moveInput = new Vector2(Input.GetAxisRaw(horizontalAxis), Input.GetAxisRaw(verticalAxis)).normalized;
            character.SetMovementInput(moveInput);

            // Мышь задаёт направление взгляда и стрельбы.
            Vector3 mouseWorld = Camera.main != null
                ? Camera.main.ScreenToWorldPoint(Input.mousePosition)
                : character.transform.position + (Vector3)character.LastNonZeroMoveDirection;

            Vector2 aimInput = mouseWorld - character.transform.position;
            character.SetAimInput(aimInput);

            if (Input.GetMouseButton(0))
            {
                character.TryBasicAttack();
            }

            // Поддерживаем и верхний цифровой ряд, и NumPad.
            if (Input.GetKeyDown(ability1Key) || Input.GetKeyDown(KeyCode.Keypad1))
            {
                character.TryUseAbility(0);
            }

            if (Input.GetKeyDown(ability2Key) || Input.GetKeyDown(KeyCode.Keypad2))
            {
                character.TryUseAbility(1);
            }

            if (Input.GetKeyDown(ability3Key) || Input.GetKeyDown(KeyCode.Keypad3))
            {
                character.TryUseAbility(2);
            }
        }
    }
}
