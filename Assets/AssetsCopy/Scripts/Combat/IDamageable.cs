using UnityEngine;

namespace DanverPlayground.Roguelike.Combat
{
    // Общий контракт для всего, что можно повредить снарядами или другими атаками.
    public interface IDamageable
    {
        TeamAlignment Team { get; }
        Transform AimPoint { get; }
        bool IsAlive { get; }
        void ReceiveDamage(float amount);
    }
}
