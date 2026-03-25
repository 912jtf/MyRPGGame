using Mirror;
using UnityEngine;

/// <summary>
/// 场景中的可拾取金块。
/// 需要 2D Collider（建议 IsTrigger=true）。
/// </summary>
public class GoldPickup : NetworkBehaviour
{
    [SyncVar] [Min(1)] public int amount = 1;
    [Tooltip("生成后多久可拾取，防止刚生成就被同帧误触发。")]
    public float pickupDelay = 0.05f;
    [Tooltip("勾选后：经过 autoDestroyAfter 秒自动销毁。")]
    public bool enableAutoDestroy = false;
    [Tooltip("自动销毁倒计时（秒）。enableAutoDestroy=true 时生效。")]
    public float autoDestroyAfter = 30f;

    float _spawnTime;
    float _lastServerResetSpawnTimeLog; // for debugging, prevents log spam

    [SyncVar(hook = nameof(OnCarriedByEnemyChanged))]
    private bool _isCarriedByEnemy;
    public bool IsCarriedByEnemy => _isCarriedByEnemy;

    // 客户端可能出现 SyncVar 延迟导致短暂“看起来未携带”的问题。
    // 因此加一个层级兜底：只要该金块仍挂在 Enemy 子层级下，就不允许被拾取。
    public bool IsAttachedToEnemyCarrier => GetComponentInParent<Enemy>(true) != null;

    public bool CanBePicked =>
        Time.time >= _spawnTime + Mathf.Max(0f, pickupDelay) &&
        !IsPickupAnimating &&
        !IsCarriedByEnemy &&
        !IsAttachedToEnemyCarrier;

    /// <summary>
    /// 调试用：把 CanBePicked 的各个条件拆开返回，方便定位是“时间未到/动画中/被敌人携带”等哪一项导致不可拾取。
    /// </summary>
    public bool CanBePickedDetailed(out bool timeOk, out bool animOk, out bool carriedOk, out float nowTime, out float spawnTime, out float readyAt)
    {
        nowTime = Time.time;
        spawnTime = _spawnTime;
        readyAt = _spawnTime + Mathf.Max(0f, pickupDelay);
        timeOk = nowTime >= readyAt;
        animOk = !IsPickupAnimating;
        carriedOk = !IsCarriedByEnemy && !IsAttachedToEnemyCarrier;
        return timeOk && animOk && carriedOk;
    }

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
        if (!isServer)
            return;

        _isCarriedByEnemy = carried;

        // 任何 carried 状态切换都清理掉拾取锁定，避免残留导致后续无法拾取。
        IsPickupAnimating = false;

        // 服务器端立即更新碰撞体（客户端依然会通过 SyncVar hook 再次校准一次）
        OnCarriedByEnemyChanged(!carried, carried);
    }

    void OnCarriedByEnemyChanged(bool oldValue, bool newValue)
    {
        // carried 状态变化时，确保拾取锁定不会残留导致“永远捡不到”
        IsPickupAnimating = false;

        if (_colliders == null || _colliders.Length == 0)
            _colliders = GetComponentsInChildren<Collider2D>(true);

        if (_colliders == null)
            return;

        foreach (Collider2D c in _colliders)
        {
            if (c == null) continue;
            c.enabled = !newValue;
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

    /// <summary>
    /// 结束服务器端的拾取锁定（用于“部分拾取”场景，避免 IsPickupAnimating 一直保持 true 导致后续永远拾不起来）。
    /// </summary>
    public void EndPickupAnimationForServer()
    {
        if (!isServer)
            return;

        IsPickupAnimating = false;

        if (_colliders == null || _colliders.Length == 0)
            _colliders = GetComponentsInChildren<Collider2D>(true);

        if (_colliders == null)
            return;

        // 如果仍在被敌人携带，就保持禁用；否则重新开启触发器。
        bool enable = !_isCarriedByEnemy;
        foreach (Collider2D c in _colliders)
        {
            if (c == null) continue;
            c.enabled = enable;
        }
    }

    /// <summary>
    /// 结束客户端的拾取锁定（纯视觉/防卡死用）：不依赖服务器回执。
    /// </summary>
    public void EndPickupAnimationLocal()
    {
        IsPickupAnimating = false;

        if (_colliders == null || _colliders.Length == 0)
            _colliders = GetComponentsInChildren<Collider2D>(true);

        if (_colliders == null)
            return;

        // 如果仍在被敌人携带，就保持禁用；否则恢复触发器。
        bool enable = !_isCarriedByEnemy;
        foreach (Collider2D c in _colliders)
        {
            if (c == null) continue;
            c.enabled = enable;
        }
    }

    void Start()
    {
        if (enableAutoDestroy && autoDestroyAfter > 0f)
            Destroy(gameObject, autoDestroyAfter);
    }

    public int Consume()
    {
        if (!isServer)
            return Mathf.Max(1, amount);

        int v = Mathf.Max(1, amount);
        NetworkServer.Destroy(gameObject);
        return v;
    }

    /// <summary>
    /// server 在修改 pickupDelay / 状态后，重置 _spawnTime，避免因为 Awake 时机导致时间判定过早。
    /// </summary>
    public void ServerResetSpawnTimeNow()
    {
        if (!isServer)
            return;
        _spawnTime = Time.time;
    }
}
