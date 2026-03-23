using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 挂在 PlayerNet 子物体 attackarea 上：Trigger 与敌人重叠时在「伤害窗口」内造成伤害。
/// 伤害窗口由动画事件 HitboxBegin / HitboxEnd（或 SwingStart）驱动。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class PlayerAttackHitbox : MonoBehaviour
{
    [Tooltip("通常拖父物体上的 PlayerAttack；留空则在运行时自动查找父级")]
    public PlayerAttack playerAttack;

    private Collider2D _col;
    private readonly HashSet<int> _hitInstanceIds = new HashSet<int>();

    /// <summary>由动画或 PlayerAttack 控制：仅在 true 时 OnTrigger 会尝试结算伤害。</summary>
    public bool DamageWindowActive { get; private set; }

    private void Awake()
    {
        _col = GetComponent<Collider2D>();
        if (_col != null)
            _col.isTrigger = true;

        if (playerAttack == null)
            playerAttack = GetComponentInParent<PlayerAttack>();
    }

    /// <summary>动画事件：每一招开始（可选，用于清标记）。</summary>
    public void SwingStart()
    {
        _hitInstanceIds.Clear();
    }

    /// <summary>动画事件：开始可造成伤害（与录制 Collider 形状/开启时间配合）。</summary>
    public void HitboxBegin()
    {
        DamageWindowActive = true;
        _hitInstanceIds.Clear();
    }

    /// <summary>动画事件：结束伤害窗口。</summary>
    public void HitboxEnd()
    {
        DamageWindowActive = false;
    }

    private void OnDisable()
    {
        DamageWindowActive = false;
    }

    private void OnTriggerEnter2D(Collider2D other) => TryHit(other);

    private void OnTriggerStay2D(Collider2D other) => TryHit(other);

    private void TryHit(Collider2D other)
    {
        if (!DamageWindowActive || playerAttack == null || other == null)
            return;

        if (other.isTrigger)
            return;

        int id = other.gameObject.GetInstanceID();
        if (_hitInstanceIds.Contains(id))
            return;

        if (playerAttack.TryApplyHitFromHitbox(other))
            _hitInstanceIds.Add(id);
    }
}
