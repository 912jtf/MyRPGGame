using Mirror;
using UnityEngine;

public class Fire : NetworkBehaviour
{
    // 用于“保证客户端火球轨道完全跟随角色”的关键：
    // 1) 用 SyncVar 同步轨道初始化参数 + 服务器生成时间戳
    // 2) 客户端不依赖 NetworkTransform 来更新位置，而是按同一套数学公式本地计算
    // 3) 因此 Fire.prefab 里要关掉 NetworkTransform 的 syncPosition

    private Transform _center;

    [SyncVar] private uint _centerNetId;
    [SyncVar] private Vector3 _centerOffset;
    [SyncVar] private float _startAngle;
    [SyncVar] private float _radius;
    [SyncVar] private float _rotateSpeed;
    [SyncVar] private float _duration;
    [SyncVar] private int _damage;
    [SyncVar] private double _spawnServerTime;
    [SyncVar] private bool _hasInit;

    /// <summary>
    /// 由技能在生成时调用，初始化火焰轨道参数。
    /// 必须在 NetworkServer.Spawn(fireObj) 之前调用，确保初始状态能同步到客户端。
    /// </summary>
    public void Init(Transform center, float startAngle, float radius, float rotateSpeed, float duration, int damage, Vector3 centerOffset)
    {
        _center = center;

        _startAngle = startAngle;
        _radius = radius;
        _rotateSpeed = rotateSpeed;
        _duration = duration;
        _damage = damage;

        _centerOffset = centerOffset;
        _spawnServerTime = NetworkTime.time;
        _hasInit = true;

        if (_center != null)
        {
            NetworkIdentity ni = _center.GetComponent<NetworkIdentity>();
            if (ni != null)
                _centerNetId = ni.netId;
        }
    }

    /// <summary>
    /// 兼容旧调用：centerOffset 默认 (0,0,0)
    /// </summary>
    public void Init(Transform center, float startAngle, float radius, float rotateSpeed, float duration, int damage)
    {
        Init(center, startAngle, radius, rotateSpeed, duration, damage, Vector3.zero);
    }

    public override void OnStartClient()
    {
        ResolveCenter();
    }

    void ResolveCenter()
    {
        if (_center != null)
            return;

        if (_centerNetId == 0)
            return;

        if (NetworkClient.spawned.TryGetValue(_centerNetId, out NetworkIdentity ni) && ni != null)
        {
            _center = ni.transform;
        }
    }

    private void FixedUpdate()
    {
        if (!_hasInit)
            return;

        ResolveCenter();
        if (_center == null)
            return;

        double elapsed = NetworkTime.time - _spawnServerTime;
        if (elapsed >= _duration)
        {
            // 视觉/逻辑都不需要继续存在，直接本地销毁即可（server/客户端都会自己销毁到同一时刻）
            Destroy(gameObject);
            return;
        }

        // angle = startAngle + rotateSpeed * elapsed
        float angle = _startAngle + _rotateSpeed * (float)elapsed;
        float rad = angle * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * _radius;

        // 客户端本地计算：火球轨道的“中心点”始终绑定到客户端当前看到的角色中心，
        // 这样火球相对角色不会因为 Reliable 快照时间差产生“沿移动方向偏移”的错觉。
        transform.position = _center.position + offset;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!isServer)
            return;

        // 只对“身体”碰撞体造成伤害，忽略敌人的索敌用 Trigger，避免调大索敌半径时火球伤害范围跟着变大
        if (other.isTrigger) return;

        EnemyHealth enemyHealth = other.GetComponent<EnemyHealth>();
        if (enemyHealth != null && _damage > 0)
            enemyHealth.TakeDamage(_damage, (Vector2)transform.position, _centerNetId);
    }
}

