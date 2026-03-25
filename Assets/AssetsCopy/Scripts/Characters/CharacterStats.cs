using System;

namespace DanverPlayground.Roguelike.Characters
{
    // Базовые статы движения и живучести персонажа.
    [Serializable]
    public class CharacterStats
    {
        public float maxHealth = 6f;
        public float moveSpeed = 5f;
        public float acceleration = 16f;
        public float deceleration = 20f;
        public float contactDamage = 1f;
    }
}
