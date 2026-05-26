//DP
using System.Collections;
using UnityEngine;

/// <summary>
/// Персонаж-жрец с дальней атакой по области.
/// </summary>
public class PlayerCharacterPriest : PlayerCharacterTemplate
{
    [Header("Дальняя атака")]
    [SerializeField, Min(0f)] private float areaAttackRadius = 1.25f;
    [SerializeField, Min(0f)] private float areaAttackDelay = 0.35f;
    [SerializeField, Min(0f)] private float areaAttackKnockbackForce = 5f;
    [SerializeField, Min(0.01f)] private float areaAttackLifeTime = 0.25f;
    [SerializeField] private LayerMask areaAttackLayers = ~0;

    [Header("ИИ атака")]
    [SerializeField, Min(0f)] private float aiAttackSearchRadius = 8f;
    [SerializeField, Min(0.01f)] private float aiAttackMinInterval = 1f;
    [SerializeField, Min(0.01f)] private float aiAttackMaxInterval = 2f;
    [SerializeField] private LayerMask aiAttackLayers = ~0;

    [Header("Первая способность")]
    [SerializeField, Min(0f)] private float firstAbilityCastDuration = 1f;
    [SerializeField, Min(0f)] private float firstAbilityHealAmount = 50f;

    [Header("Вторая способность")]
    [SerializeField, Min(0f)] private float secondAbilityBuffDuration = 10f;
    [SerializeField, Min(0f)] private float secondAbilityHealAmount = 25f;
    [SerializeField, Min(0f)] private float secondAbilityDamageBonusPercent = 10f;
    [SerializeField, Min(0f)] private float secondAbilityShieldAmount = 50f;

    [Header("Третья способность")]
    [SerializeField, Min(0f)] private float thirdAbilityAttackRadius = 0.65f;
    [SerializeField, Min(0f)] private float thirdAbilityAttackOffsetDistance = 0.75f;
    [SerializeField, Min(0f)] private float thirdAbilityDamageMultiplier = 2f;
    [SerializeField, Min(0f)] private float thirdAbilityKnockbackForce = 4f;
    [SerializeField] private LayerMask thirdAbilityAttackLayers = ~0;

    [Header("Отображение дальней атаки")]
    [SerializeField, Min(8)] private int areaAttackCircleSegments = 48;
    [SerializeField, Min(0.01f)] private float areaAttackCircleWidth = 0.06f;
    [SerializeField] private Color areaAttackCircleColor = new Color(1f, 0.88f, 0.2f, 0.9f);
    [SerializeField, Range(0f, 1f)] private float areaAttackCirclePreviewAlpha = 0.18f;
    [SerializeField, Range(0f, 1f)] private float areaAttackCircleFillAlpha = 0.35f;

    [Header("Отображение третьей способности")]
    [SerializeField, Min(0.01f)] private float thirdAbilityAttackVisualLifeTime = 0.15f;
    [SerializeField, Min(8)] private int thirdAbilityAttackCircleSegments = 36;
    [SerializeField, Min(0.01f)] private float thirdAbilityAttackCircleWidth = 0.07f;
    [SerializeField] private Color thirdAbilityAttackCircleColor = new Color(1f, 0.96f, 0.35f, 0.9f);

    private float nextAreaAttackTime;
    private float nextAiAttackTime;
    private Coroutine firstAbilityCoroutine;
    private bool isCastingFirstAbility;
    private Vector2 lastThirdAbilityAttackDirection = Vector2.right;

    protected override void FixedUpdate()
    {
        if (isCastingFirstAbility)
        {
            return;
        }

        base.FixedUpdate();
    }

    /// <summary>
    /// Считывает ввод атаки
    /// </summary>
    protected override void ReadCharacterSpecificInput()
    {
        if (Input.GetMouseButtonDown(0))
        {
            TryUseAreaAttack();
        }
    }

    /// <summary>
    /// Проверяет можно ли начать атаку по области
    /// </summary>
    private void TryUseAreaAttack()
    {
        if (!IsAlive || Time.time < nextAreaAttackTime)
        {
            return;
        }

        Vector2 attackPosition = GetCursorWorldPosition();
        StartCoroutine(UseAreaAttackAfterDelay(attackPosition));
        nextAreaAttackTime = Time.time + GetAttackDelayFromAttackSpeed();

        Debug.Log($"{name} начал подготовку атаки по области");
    }

    /// <summary>
    /// (ИИ) Атака по ближайшему врагу в зоне досягаемости
    /// </summary>
    protected override void UpdateAiControl()
    {
        if (!IsAlive || isCastingFirstAbility)
        {
            return;
        }

        if (nextAiAttackTime <= 0f)
        {
            ScheduleNextAiAttack();
        }

        if (Time.time < nextAiAttackTime)
        {
            return;
        }

        Component target = FindClosestAliveDamageableComponent(aiAttackSearchRadius, aiAttackLayers);

        if (target != null)
        {
            StartCoroutine(UseAreaAttackAfterDelay(target.transform.position));
            Debug.Log($"{name} ИИ жреца начал подготовку атаки по ближайшему врагу");
        }

        ScheduleNextAiAttack();
    }

    /// <summary>
    /// Наносит урон по области после небольшой задержки
    /// </summary>
    private IEnumerator UseAreaAttackAfterDelay(Vector2 attackPosition)
    {
        GameObject attackCircle = CreateAttackCircle(attackPosition);

        if (areaAttackDelay > 0f)
        {
            yield return new WaitForSeconds(areaAttackDelay);
        }

        ShowAreaAttackImpactCircle(attackCircle);
        int damagedTargetsCount = DamageTargetsInArea(attackPosition);

        Debug.Log($"{name} атака жреца нанесла {Damage} урона. Задето{damagedTargetsCount} целей");

        if (areaAttackLifeTime > 0f)
        {
            yield return new WaitForSeconds(areaAttackLifeTime);
        }

        Destroy(attackCircle);
    }

    /// <summary>
    /// Наносит урон всем подходящим целям внутри круга атаки
    /// </summary>
    private int DamageTargetsInArea(Vector2 attackPosition)
    {
        return ForEachUniqueDamageableInCircle(
            attackPosition,
            areaAttackRadius,
            areaAttackLayers,
            (target, hitCollider) =>
            {
                target.TakeDamage(Damage);
                Vector2 knockbackDirection = KnockbackUtility.GetDirectionFromPoint(attackPosition, hitCollider, Vector2.right);
                KnockbackUtility.ApplyKnockback(target, knockbackDirection, areaAttackKnockbackForce);
            });
    }

    /// <summary>
    /// Создает круг для визуализации области удара
    /// </summary>
    private GameObject CreateAttackCircle(Vector2 attackPosition)
    {
        Color previewColor = GetColorWithAlpha(areaAttackCircleColor, areaAttackCirclePreviewAlpha);

        return CreateFilledCircleWithOutlineVisual(
            "PriestAreaAttackCircle",
            attackPosition,
            areaAttackRadius,
            areaAttackCircleSegments,
            areaAttackCircleWidth,
            previewColor,
            previewColor);
    }

    private void ShowAreaAttackImpactCircle(GameObject attackCircle)
    {
        SetFilledCircleWithOutlineVisualColors(
            attackCircle,
            areaAttackCircleColor,
            GetColorWithAlpha(areaAttackCircleColor, areaAttackCircleFillAlpha));
    }

    /// <summary>
    /// Назначает следующее время атаки ИИ со случайным интервалом.
    /// </summary>
    private void ScheduleNextAiAttack()
    {
        ScheduleNextRandomActionTime(ref nextAiAttackTime, aiAttackMinInterval, aiAttackMaxInterval);
    }

    /// <summary>
    /// Показывает зону атаки жреца в редакторе
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = areaAttackCircleColor;
        Gizmos.DrawWireSphere(transform.position, areaAttackRadius);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, aiAttackSearchRadius);

        DrawAiMovementGizmos();
    }

    /// <summary>
    /// Запускает подготовку исцеления ближайшего к курсору персонажа
    /// </summary>
    protected override void UseFirstAbility()
    {
        base.UseFirstAbility();

        if (firstAbilityCoroutine != null)
        {
            StopCoroutine(firstAbilityCoroutine);
        }

        firstAbilityCoroutine = StartCoroutine(UseFirstAbilityAfterCast());
    }

    /// <summary>
    /// Отменяет подготовку исцеления если жрец перешел под контроль ии
    /// </summary>
    protected override void OnControlStateChanged(
        PlayerCharacterControlState previousControlState,
        PlayerCharacterControlState newControlState)
    {
        if (newControlState == PlayerCharacterControlState.AiControlled)
        {
            ScheduleNextAiAttack();

            if (firstAbilityCoroutine != null)
            {
                StopCoroutine(firstAbilityCoroutine);
                firstAbilityCoroutine = null;
                isCastingFirstAbility = false;
            }

            return;
        }

        nextAiAttackTime = 0f;
    }

    /// <summary>
    /// Останавливает жреца на время подготовки , а потом лечит ближайшего к курсору персонажа
    /// </summary>
    private IEnumerator UseFirstAbilityAfterCast()
    {
        isCastingFirstAbility = true;

        Debug.Log($"{name} первая способность жреца начала подготовку исцеления");

        if (firstAbilityCastDuration > 0f)
        {
            yield return new WaitForSeconds(firstAbilityCastDuration); 
        }

        isCastingFirstAbility = false;
        firstAbilityCoroutine = null;

        if (!IsAlive || !IsPlayerControlled)
        {
            yield break;
        }

        Vector2 cursorPosition = GetCursorWorldPosition();
        PlayerCharacterTemplate healTarget = FindClosestCharacterToPosition(cursorPosition);

        if (healTarget == null)
        {
            Debug.Log($"{name} первая способность жреца не нашла цель для исцеления");
            yield break;
        }

        healTarget.Heal(firstAbilityHealAmount);

        Debug.Log(
            $"{name} первая способность исцелила {healTarget.name} на {firstAbilityHealAmount:0.0} здоровья"
        );
    }

    /// <summary>
    /// Ищет ближайшего живого персонажа игрока к указанной позиции
    /// </summary>
    private PlayerCharacterTemplate FindClosestCharacterToPosition(Vector2 position)
    {
        PlayerCharacterTemplate[] characters = FindObjectsByType<PlayerCharacterTemplate>(FindObjectsSortMode.None);
        PlayerCharacterTemplate closestCharacter = null;
        float closestSqrDistance = float.PositiveInfinity;

        foreach (PlayerCharacterTemplate character in characters)
        {
            if (character == null || !character.IsAlive)
            {
                continue;
            }

            float sqrDistance = ((Vector2)character.transform.position - position).sqrMagnitude;

            if (sqrDistance >= closestSqrDistance)
            {
                continue;
            }

            closestSqrDistance = sqrDistance;
            closestCharacter = character;
        }

        return closestCharacter;
    }

    /// <summary>
    /// Лечит всех живых персонажей игрока дает им временное усиление урона и щит
    /// </summary>
    protected override void UseSecondAbility()
    {
        base.UseSecondAbility();
        PlayerCharacterTemplate[] characters = FindObjectsByType<PlayerCharacterTemplate>(FindObjectsSortMode.None);
        int affectedCharactersCount = 0;

        foreach (PlayerCharacterTemplate character in characters)
        {
            if (character == null || !character.IsAlive)
            {
                continue;
            }

            character.Heal(secondAbilityHealAmount);
            character.ApplyDamageBonus(secondAbilityDamageBonusPercent, secondAbilityBuffDuration);
            character.ApplyShield(secondAbilityShieldAmount, secondAbilityBuffDuration);
            affectedCharactersCount++;
        }

        Debug.Log(
            $"{name} вторая способность исцелила живых персонажей на {secondAbilityHealAmount:0.0}, " +
            $"дала усиление урона +{secondAbilityDamageBonusPercent:0.0}% " +
            $"и щит {secondAbilityShieldAmount:0.0} на {secondAbilityBuffDuration:0.0} сек " +
            $"Целей усилено {affectedCharactersCount}"
        );
    }

    /// <summary>
    /// Усиленная ближняя атака перед жрецом
    /// </summary>
    protected override void UseThirdAbility()
    {
        base.UseThirdAbility();
        Vector2 attackDirection = GetDirectionToCursor(ref lastThirdAbilityAttackDirection);
        Vector2 attackCenter = GetThirdAbilityAttackCenter(attackDirection);
        float finalDamage = Damage * thirdAbilityDamageMultiplier;

        CreateThirdAbilityAttackCircle(attackCenter);

        int damagedTargetsCount = ForEachUniqueDamageableInCircle(
            attackCenter,
            thirdAbilityAttackRadius,
            thirdAbilityAttackLayers,
            (target, hitCollider) =>
            {
                target.TakeDamage(finalDamage);
                KnockbackUtility.ApplyKnockback(target, attackDirection, thirdAbilityKnockbackForce);
            });

        Debug.Log(
            $"{name} третья способность нанесла {finalDamage:0.0} урона" +
            $"Задето {damagedTargetsCount} целей"
        );
    }

    /// <summary>
    /// Возвращает центр ближней атаки третьей способности
    /// </summary>
    private Vector2 GetThirdAbilityAttackCenter(Vector2 attackDirection)
    {
        return (Vector2)transform.position + attackDirection * thirdAbilityAttackOffsetDistance;
    }

    /// <summary>
    /// Создает кругдля визуализации третьей способности.
    /// </summary>
    private void CreateThirdAbilityAttackCircle(Vector2 attackCenter)
    {
        GameObject attackCircle = CreateCircleVisual(
            "PriestThirdAbilityAttackCircle",
            attackCenter,
            thirdAbilityAttackRadius,
            thirdAbilityAttackCircleSegments,
            thirdAbilityAttackCircleWidth,
            thirdAbilityAttackCircleColor);

        Destroy(attackCircle, thirdAbilityAttackVisualLifeTime);
    }
}
