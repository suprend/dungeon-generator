using System.Collections.Generic;
using UnityEngine;

namespace DanverPlayground.Roguelike.Characters
{
    // Управляет группой героев: кто активный, кто под ИИ и за кем следит камера.
    public class PartyController : MonoBehaviour
    {
        [SerializeField] private List<GameCharacter> characters = new List<GameCharacter>(4);
        [SerializeField] private CameraFollow2D cameraFollow;
        [SerializeField] private KeyCode previousCharacterKey = KeyCode.Q;
        [SerializeField] private KeyCode nextCharacterKey = KeyCode.E;

        private readonly Dictionary<GameCharacter, Vector2> formationOffsets = new Dictionary<GameCharacter, Vector2>();
        private int activeCharacterIndex;

        public GameCharacter ActiveCharacter =>
            activeCharacterIndex >= 0 && activeCharacterIndex < characters.Count
                ? characters[activeCharacterIndex]
                : null;

        private void Start()
        {
            InitializeParty();
        }

        private void Update()
        {
            if (characters.Count == 0)
            {
                return;
            }

            // Q и E листают отряд по кругу в обе стороны.
            if (Input.GetKeyDown(previousCharacterKey))
            {
                SelectCharacter((activeCharacterIndex - 1 + characters.Count) % characters.Count);
            }

            if (Input.GetKeyDown(nextCharacterKey))
            {
                SelectCharacter((activeCharacterIndex + 1) % characters.Count);
            }
        }

        public Vector2 GetFormationOffset(GameCharacter character, Vector2 fallbackOffset)
        {
            if (character != null && formationOffsets.TryGetValue(character, out Vector2 storedOffset))
            {
                return storedOffset;
            }

            return fallbackOffset;
        }

        public void SelectCharacter(int index)
        {
            if (index < 0 || index >= characters.Count)
            {
                return;
            }

            // Новый активный герой получает Player, все остальные возвращаются под ИИ.
            activeCharacterIndex = index;

            for (int i = 0; i < characters.Count; i++)
            {
                CharacterControlMode mode = i == activeCharacterIndex
                    ? CharacterControlMode.Player
                    : CharacterControlMode.AI;

                characters[i].SetControlMode(mode);
            }

            if (cameraFollow != null && ActiveCharacter != null)
            {
                cameraFollow.SetTarget(ActiveCharacter.transform);
            }
        }

        private void InitializeParty()
        {
            formationOffsets.Clear();

            // Инициализируем всех героев и назначаем им стартовые позиции в построении.
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i] == null)
                {
                    continue;
                }

                characters[i].Initialize(this);
                formationOffsets[characters[i]] = GetDefaultOffset(i);
            }

            SelectCharacter(Mathf.Clamp(activeCharacterIndex, 0, Mathf.Max(characters.Count - 1, 0)));
        }

        private static Vector2 GetDefaultOffset(int index)
        {
            switch (index)
            {
                case 0:
                    return new Vector2(0f, 0f);
                case 1:
                    return new Vector2(-1.5f, -1.25f);
                case 2:
                    return new Vector2(1.5f, -1.25f);
                case 3:
                    return new Vector2(0f, -2.2f);
                default:
                    return Vector2.zero;
            }
        }
    }
}
