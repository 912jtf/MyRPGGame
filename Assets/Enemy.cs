using UnityEngine;

/// <summary>
/// 简单 2D 敌人 AI：发现玩家 → 追踪玩家 → 攻击玩家（只控制动画，不做伤害计算）。
/// 使用说明：
/// 1. 玩家物体的 Tag 必须是 "Player"
/// 2. 敌人 Animator 有 3 个 Bool 参数：IsIdle, IsWalking, IsAttacking
/// 3. 敌人身上有一个普通 Collider2D 和一个作为索敌范围的 Trigger Collider2D
/// 4. 在攻击动画中添加两个 Animation Event：
///    - 出招瞬间调用 Enemy.OnAttackHit
///    - 攻击结束最后一帧调用 Enemy.OnAttackEnd
/// </summary>
public class Enemy : MonoBehaviour
{
    [Header("移动与攻击")]
    public float moveSpeed = 2f;
    public float attackDistance = 0.8f;   // 用于 AI 判断是否应该进入攻击状态
    public float attackRadius = 0.8f;     // 实际伤害判定半径（OverlapCircleAll）
    public float attackCooldown = 0.8f;   // 两次攻击之间的冷却时间（秒）

    [Header("伤害设置")]
    public LayerMask playerLayer;         // 只勾选 Player 层
    public int attackDamage = 1;          // 对玩家造成的伤害值

    [Header("Animator 参数名")]
    // 这里已经按你修正后的名字配置好，除非 Animator 里改名，否则不用再动
    [SerializeField] string idleBoolName = "IsIdle";
    [SerializeField] string walkBoolName = "IsWalking";
    [SerializeField] string attackBoolName = "IsAttacking";

    Animator animator;
    Rigidbody2D rb;
    SpriteRenderer spriteRenderer;
    Transform targetPlayer;   // 索敌到的玩家
    float nextAttackTime;     // 下次允许攻击的时间

    enum State { Idle, Chase, Attack, Cooldown }
    State currentState = State.Idle;

    void Awake()
    {
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        SetAnimState(idle: true, walk: false, attack: false);
    }

    void Update()
    {
        switch (currentState)
        {
            case State.Idle:
                UpdateIdle();
                break;
            case State.Chase:
                UpdateChase();
                break;
            case State.Attack:
                // 攻击状态完全由动画 + Animation Event 驱动
                break;
            case State.Cooldown:
                UpdateCooldown();
                break;
        }
    }

    void UpdateIdle()
    {
        if (targetPlayer == null) return;

        Vector2 myPos = transform.position;
        Vector2 targetPos = targetPlayer.position;
        float dist = Vector2.Distance(myPos, targetPos);

        if (dist > attackDistance)
        {
            // 目标在范围外：切到追踪
            currentState = State.Chase;
            SetAnimState(idle: false, walk: true, attack: false);
        }
        else
        {
            // 在攻击范围内：若冷却结束则攻击，否则保持待机
            if (Time.time >= nextAttackTime)
            {
                StartAttack();
            }
            else
            {
                SetAnimState(idle: true, walk: false, attack: false);
            }
        }
    }

    void UpdateChase()
    {
        if (targetPlayer == null)
        {
            currentState = State.Idle;
            SetAnimState(idle: true, walk: false, attack: false);
            return;
        }

        Vector2 myPos = transform.position;
        Vector2 targetPos = targetPlayer.position;
        Vector2 dir = (targetPos - myPos).normalized;

        // 翻转朝向
        if (dir.sqrMagnitude > 0.0001f && spriteRenderer != null)
        {
            spriteRenderer.flipX = dir.x < 0f;
        }

        float dist = Vector2.Distance(myPos, targetPos);

        // 在攻击范围内
        if (dist <= attackDistance)
        {
            if (Time.time >= nextAttackTime)
            {
                StartAttack();
            }
            else
            {
                // 冷却中：进入冷却状态，保持原地待机
                currentState = State.Cooldown;
                SetAnimState(idle: true, walk: false, attack: false);
                if (rb != null) rb.velocity = Vector2.zero;
            }
            return;
        }

        // 不在攻击范围：继续向玩家移动
        Vector2 nextPos = myPos + dir * moveSpeed * Time.deltaTime;
        if (rb != null)
            rb.MovePosition(nextPos);
        else
            transform.position = nextPos;
    }

    void UpdateCooldown()
    {
        if (targetPlayer == null)
        {
            currentState = State.Idle;
            SetAnimState(idle: true, walk: false, attack: false);
            return;
        }

        Vector2 myPos = transform.position;
        Vector2 targetPos = targetPlayer.position;
        float dist = Vector2.Distance(myPos, targetPos);

        // 冷却结束
        if (Time.time >= nextAttackTime)
        {
            if (dist <= attackDistance)
            {
                // 冷却结束且仍在攻击范围内：再次攻击
                StartAttack();
            }
            else
            {
                // 冷却结束但目标已离开攻击范围：切回追踪
                currentState = State.Chase;
                SetAnimState(idle: false, walk: true, attack: false);
            }
        }
        else
        {
            // 冷却中：保持待机朝向玩家
            Vector2 dir = (targetPos - myPos).normalized;
            if (dir.sqrMagnitude > 0.0001f && spriteRenderer != null)
                spriteRenderer.flipX = dir.x < 0f;

            if (rb != null) rb.velocity = Vector2.zero;
            SetAnimState(idle: true, walk: false, attack: false);
        }
    }

    void StartAttack()
    {
        currentState = State.Attack;
        nextAttackTime = Time.time + attackCooldown;
        SetAnimState(idle: false, walk: false, attack: true);
        if (rb != null) rb.velocity = Vector2.zero;
    }

    void SetAnimState(bool idle, bool walk, bool attack)
    {
        if (animator == null) return;

        if (!string.IsNullOrEmpty(idleBoolName))
            animator.SetBool(idleBoolName, idle);
        if (!string.IsNullOrEmpty(walkBoolName))
            animator.SetBool(walkBoolName, walk);
        if (!string.IsNullOrEmpty(attackBoolName))
            animator.SetBool(attackBoolName, attack);
    }

    // 索敌 Trigger：需要将其中一个 Collider2D 勾选 IsTrigger
    void OnTriggerEnter2D(Collider2D other)
    {
        if (targetPlayer != null) return;
        if (other.CompareTag("Player"))
            targetPlayer = other.transform;
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player") && other.transform == targetPlayer)
            targetPlayer = null;
    }

    // ===== Animation Event =====

    /// <summary>
    /// 攻击动画命中瞬间在 Animation Event 中调用。
    /// 使用 OverlapCircleAll 对 Player 层进行伤害判定。
    /// </summary>
    public void OnAttackHit()
    {
        // 以敌人当前位置为圆心进行范围检测
        Vector2 center = transform.position;

        Collider2D[] hitPlayers = Physics2D.OverlapCircleAll(
            center,
            attackRadius,
            playerLayer
        );

        foreach (Collider2D col in hitPlayers)
        {
            PlayerHealth ph = col.GetComponent<PlayerHealth>();
            if (ph != null)
            {
                ph.TakeDamage(attackDamage);
            }
        }
    }

    /// <summary>
    /// 攻击动画结束时在 Animation Event 中调用：
    /// - 若玩家仍在攻击范围内但冷却未结束：进入冷却状态（保持 Idle）
    /// - 若玩家仍在攻击范围内且冷却结束：再次攻击
    /// - 若玩家不在攻击范围内：继续追踪或待机
    /// </summary>
    public void OnAttackEnd()
    {
        if (targetPlayer != null)
        {
            float dist = Vector2.Distance(transform.position, targetPlayer.position);
            if (dist <= attackDistance)
            {
                if (Time.time >= nextAttackTime)
                {
                    // 冷却结束：直接再次攻击
                    StartAttack();
                }
                else
                {
                    // 冷却中：进入冷却状态，保持 Idle
                    currentState = State.Cooldown;
                    SetAnimState(idle: true, walk: false, attack: false);
                }
            }
            else
            {
                // 玩家离开攻击范围：继续追踪
                currentState = State.Chase;
                SetAnimState(idle: false, walk: true, attack: false);
            }
        }
        else
        {
            currentState = State.Idle;
            SetAnimState(idle: true, walk: false, attack: false);
        }
    }

    private void OnDrawGizmosSelected()
    {
        // 在 Scene 视图中显示敌人的攻击判定范围
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, attackRadius);
    }
}
