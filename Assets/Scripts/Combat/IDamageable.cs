//DP
/// <summary>
/// Для объектов которые могут получать урон (противники, персонажи, декор)
/// </summary>
public interface IDamageable
{
    bool IsAlive { get; }

    void TakeDamage(float damageAmount);
}
