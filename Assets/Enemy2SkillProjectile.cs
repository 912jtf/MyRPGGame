using UnityEngine;

/// <summary>
/// enemy2 技能弹丸：负责移动、触发命中玩家并调用 PlayerHealth.TakeDamage。
/// </summary>
[DisallowMultipleComponent]
public class Enemy2SkillProjectile : MonoBehaviour
{
    [Header("Movement")]
    public float speed = 6f;

    [Header("Damage")]
    public int damage = 1;
    [Tooltip("只在爆炸最终帧开启伤害（通过动画事件调用 BeginFinalExplosionDamage）。")]
    public bool onlyDamageOnFinalFrame = true;
    [Tooltip("最终帧伤害开启后的持续时间（秒）。")]
    public float finalDamageWindow = 0.08f;
    [Tooltip("当 BeginFinalExplosionDamage 触发后，伤害窗口结束将销毁特效对象，避免爆炸后残留。")]
    public bool destroyAfterFinalDamageWindow = true;

    [Header("Final Explosion AOE (更稳)")]
    [Tooltip("为最后爆炸帧做一次范围伤害判定（避免只靠触发器导致漏判）。")]
    public bool useAoeDamageOnFinalFrame = true;
    [Tooltip("爆炸伤害半径（世界单位），建议大于/贴合爆炸视觉大小。")]
    public float explosionRadius = 0.8f;
    [Tooltip("爆炸伤害中心偏移（相对弹丸 transform），用来对齐爆炸图的中心。")]
    public Vector2 explosionCenterOffset = Vector2.zero;
    [Tooltip("如果用 AOE 伤害，是否在伤害判定后立刻销毁对象（通常是 true）。")]
    public bool destroyImmediatelyAfterAoe = true;

    [Header("Hit / Flight")]
    [Tooltip("弹丸命中玩家后是否立即销毁。关闭后会继续往前飞。")]
    public bool destroyOnHit = false;

    [Tooltip("是否在到达目的地后停止。关闭后会一直沿直线飞行到 lifeTime。")]
    public bool stopAtDestination = false;

    [Header("Lifetime")]
    public float lifeTime = 1.2f;

    [Header("Hit")]
    public string playerTag = "Player";

    private Rigidbody2D _rb;
    private Vector2 _direction = Vector2.right;
    private SpriteRenderer _spriteRenderer;
    private Vector2 _targetDestination;
    private bool _hasTargetDestination;
    private bool _arrived;
    private bool _finalDamageQueued;
    private bool _canDamage;
    private bool _hasDealtDamage;

    void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
    }

    void Start()
    {
        _canDamage = !onlyDamageOnFinalFrame;
        _finalDamageQueued = false;
        Destroy(gameObject, lifeTime);
    }

    /// <summary>
    /// 由敌人生成时调用，指定飞行方向
    /// </summary>
    public void Init(Vector2 direction, float? overrideSpeed = null)
    {
        if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;
        _direction = direction.normalized;

        if (_spriteRenderer != null)
            _spriteRenderer.flipX = _direction.x < 0f;

        if (overrideSpeed.HasValue)
            speed = Mathf.Max(0f, overrideSpeed.Value);

        if (_rb != null)
        {
            _rb.velocity = _direction * speed;
        }
    }

    void Update()
    {
        // 如果没有刚体，就用 transform 移动保证效果可用
        // 当动画最后爆炸帧触发时，我们不瞬移，而是排队等待飞行到目的地附近后再真正开启伤害
        if (_finalDamageQueued && _hasTargetDestination && !_arrived)
        {
            Vector2 pos2 = transform.position;
            float remaining = Vector2.Distance(pos2, _targetDestination);
            if (remaining <= 0.02f)
            {
                _finalDamageQueued = false;
                OpenFinalDamageWindow();
            }
        }

        if (stopAtDestination && _hasTargetDestination && !_arrived)
        {
            Vector2 pos = transform.position;
            float remaining = Vector2.Distance(pos, _targetDestination);
            float stopDistance = 0.02f;

            if (remaining <= stopDistance)
            {
                transform.position = _targetDestination;
                _arrived = true;
                if (_rb != null) _rb.velocity = Vector2.zero;
                return;
            }
        }

        if (_rb == null)
            transform.position += (Vector3)(_direction * speed * Time.deltaTime);
        else if (!_hasTargetDestination || !_arrived)
            _rb.velocity = _direction * speed;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        TryDealDamage(other);
    }

    void OnTriggerStay2D(Collider2D other)
    {
        // 避免玩家在爆炸窗口开启前就已进入触发器时漏判伤害
        TryDealDamage(other);
    }

    void TryDealDamage(Collider2D other)
    {
        if (!_canDamage || _hasDealtDamage)
            return;

        if (!other.CompareTag(playerTag))
            return;

        PlayerHealth ph = other.GetComponent<PlayerHealth>();
        if (ph != null && damage > 0)
        {
            ph.TakeDamage(damage);
            _hasDealtDamage = true;
        }

        if (destroyOnHit)
            Destroy(gameObject);
    }

    /// <summary>
    /// 在技能动画“最后爆炸帧”添加 Animation Event 调用此方法。
    /// </summary>
    public void BeginFinalExplosionDamage()
    {
        if (!onlyDamageOnFinalFrame)
        {
            _finalDamageQueued = false;
            OpenFinalDamageWindow();
            return;
        }

        // 进入“排队”模式：等飞到目的地附近才开伤害窗口
        // 如果不在到达目的地后停止（stopAtDestination=false），就不排队，直接打开（避免永远等不到）。
        if (_hasTargetDestination && stopAtDestination)
        {
            _finalDamageQueued = true;
        }
        else
        {
            _finalDamageQueued = false;
            OpenFinalDamageWindow();
        }
    }

    void OpenFinalDamageWindow()
    {
        _canDamage = true;

        // 只在“最后帧伤害模式”下做 AOE，避免你现在想要“路上经过即伤害”时最后又二次判定
        if (onlyDamageOnFinalFrame && useAoeDamageOnFinalFrame && !_hasDealtDamage)
        {
            DoFinalExplosionAoeDamage();
        }

        CancelInvoke(nameof(EndFinalExplosionDamage));
        Invoke(nameof(EndFinalExplosionDamage), Mathf.Max(0.01f, finalDamageWindow));
    }

    void DoFinalExplosionAoeDamage()
    {
        Vector2 center = (Vector2)transform.position + explosionCenterOffset;
        Collider2D[] hits = Physics2D.OverlapCircleAll(center, explosionRadius);

        foreach (Collider2D col in hits)
        {
            if (!col.CompareTag(playerTag))
                continue;

            PlayerHealth ph = col.GetComponent<PlayerHealth>();
            if (ph == null)
                ph = col.GetComponentInParent<PlayerHealth>();

            if (ph != null && damage > 0)
            {
                ph.TakeDamage(damage);
                _hasDealtDamage = true;
                break;
            }
        }

        if (destroyImmediatelyAfterAoe && destroyAfterFinalDamageWindow)
        {
            // 既然爆炸已完成，就立刻销毁，避免之后窗口时间内的其它判定影响手感
            Destroy(gameObject);
        }
    }

    void EndFinalExplosionDamage()
    {
        // 路上即伤害模式（onlyDamageOnFinalFrame=false）不应该在爆炸窗口结束后关掉伤害
        if (onlyDamageOnFinalFrame)
            _canDamage = false;
        if (destroyAfterFinalDamageWindow)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 初始化：将弹丸“目的地”设为目标位置，确保爆炸/伤害在该位置发生。
    /// </summary>
    public void InitToDestination(Vector2 destination, float? overrideSpeed = null)
    {
        _targetDestination = destination;
        _hasTargetDestination = true;
        _arrived = false;

        Vector2 dir = _targetDestination - (Vector2)transform.position;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;
        _direction = dir.normalized;

        if (_spriteRenderer != null)
            _spriteRenderer.flipX = _direction.x < 0f;

        if (overrideSpeed.HasValue)
            speed = Mathf.Max(0f, overrideSpeed.Value);

        if (_rb != null)
            _rb.velocity = _direction * speed;
    }
}

