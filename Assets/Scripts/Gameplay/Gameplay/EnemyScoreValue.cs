using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyScoreValue : MonoBehaviour
{
    [SerializeField] private int baseScore = 10;

    public int BaseScore => Mathf.Max(0, baseScore);
}
