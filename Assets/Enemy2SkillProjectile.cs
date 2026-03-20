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

    [Header("Lifetime")]
    public float lifeTime = 1.2f;

    [Header("Hit")]
    public string playerTag = "Player";

    private Rigidbody2D _rb;
    private Vector2 _direction = Vector2.right;
    private SpriteRenderer _spriteRenderer;

    void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
    }

    void Start()
    {
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
        if (_rb == null)
        {
            transform.position += (Vector3)(_direction * speed * Time.deltaTime);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag))
            return;

        PlayerHealth ph = other.GetComponent<PlayerHealth>();
        if (ph != null && damage > 0)
        {
            ph.TakeDamage(damage);
        }

        Destroy(gameObject);
    }
}

