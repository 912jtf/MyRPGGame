using UnityEngine;

/// <summary>
/// 场景中的可拾取金块。
/// 需要 2D Collider（建议 IsTrigger=true）。
/// </summary>
public class GoldPickup : MonoBehaviour
{
    [Min(1)] public int amount = 1;
    [Tooltip("生成后多久可拾取，防止刚生成就被同帧误触发。")]
    public float pickupDelay = 0.05f;
    [Tooltip("勾选后：经过 autoDestroyAfter 秒自动销毁。")]
    public bool enableAutoDestroy = false;
    [Tooltip("自动销毁倒计时（秒）。enableAutoDestroy=true 时生效。")]
    public float autoDestroyAfter = 30f;

    float _spawnTime;

    public bool IsCarriedByEnemy { get; private set; }
    public bool CanBePicked => Time.time >= _spawnTime + Mathf.Max(0f, pickupDelay) && !IsPickupAnimating && !IsCarriedByEnemy;

    /// <summary>正在播放吸向玩家的拾取动画，避免重复触发。</summary>
    public bool IsPickupAnimating { get; private set; }

    Collider2D[] _colliders;

    void Awake()
    {
        _spawnTime = Time.time;
        _colliders = GetComponentsInChildren<Collider2D>(true);
    }

    public void SetCarriedByEnemy(bool carried)
    {
        IsCarriedByEnemy = carried;
        if (_colliders == null || _colliders.Length == 0)
            _colliders = GetComponentsInChildren<Collider2D>(true);

        if (_colliders == null)
            return;

        foreach (Collider2D c in _colliders)
        {
            if (c == null) continue;
            c.enabled = !carried;
        }
    }

    /// <summary>开始拾取动画时调用：关闭触发器，防止重复拾取。</summary>
    public void BeginPickupAnimation()
    {
        IsPickupAnimating = true;
        if (_colliders == null || _colliders.Length == 0)
            _colliders = GetComponentsInChildren<Collider2D>(true);
        if (_colliders == null)
            return;
        foreach (Collider2D c in _colliders)
        {
            if (c != null)
                c.enabled = false;
        }
    }

    void Start()
    {
        if (enableAutoDestroy && autoDestroyAfter > 0f)
            Destroy(gameObject, autoDestroyAfter);
    }

    public int Consume()
    {
        int v = Mathf.Max(1, amount);
        Destroy(gameObject);
        return v;
    }
}
