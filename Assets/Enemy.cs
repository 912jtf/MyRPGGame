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
    public float attackDamage = 1f;          // 对玩家造成的伤害值

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
        float moveDistance = moveSpeed * Time.deltaTime;
        Vector2 nextPos = myPos + dir * moveDistance;
        
        // 检测前方是否有障碍物
        if (!IsPathBlocked(myPos, dir, moveDistance))
        {
            // 前方通畅，继续向玩家移动
            if (rb != null)
                rb.MovePosition(nextPos);
            else
                transform.position = nextPos;
        }
        else
        {
            // 前方被挡住，尝试向左或向右绕过障碍
            Vector2 alternativeDir = FindAlternativePath(myPos, dir, targetPos, moveDistance);
            if (alternativeDir != Vector2.zero)
            {
                nextPos = myPos + alternativeDir * moveDistance;
                if (rb != null)
                    rb.MovePosition(nextPos);
                else
                    transform.position = nextPos;
            }
            // 如果没有替代路径，就停留在原地
        }
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

    // 已删除 OnTriggerExit2D：敌人一旦发现玩家就永远追逐，不会因离开索敌范围而放弃目标

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

    /// <summary>
    /// 检测敌人前方是否被障碍物挡住（建筑、墙等）
    /// 用于 Kinematic 模式下的碰撞检测，忽略其他敌人
    /// </summary>
    private bool IsPathBlocked(Vector2 fromPos, Vector2 direction, float distance)
    {
        // Raycast 检测：从敌人位置沿移动方向检测，距离为本帧移动距离 + 0.2f 的安全距离
        RaycastHit2D hit = Physics2D.Raycast(fromPos, direction, distance + 0.2f);
        if (hit.collider != null && !hit.collider.isTrigger)
        {
            // 检查碰到的是不是其他敌人（EnemyHealth 组件）
            // 如果是其他敌人，忽略，继续移动
            if (hit.collider.GetComponent<EnemyHealth>() != null)
            {
                return false;  // 敌人可以穿过其他敌人
            }
            // 前方有非 Trigger 的碰撞体（建筑、墙等），返回 true 表示被挡住
            return true;
        }
        return false;
    }

    /// <summary>
    /// 当前方被挡住时，尝试向左或向右绕过障碍
    /// 返回一个可通行的方向，如果两个方向都被挡则返回 zero
    /// </summary>
    private Vector2 FindAlternativePath(Vector2 fromPos, Vector2 mainDir, Vector2 targetPos, float moveDistance)
    {
        // 计算左右两个方向（垂直于前进方向）
        // leftDir: 逆时针旋转 mainDir 90 度
        // rightDir: 顺时针旋转 mainDir 90 度
        Vector2 leftDir = new Vector2(-mainDir.y, mainDir.x).normalized;
        Vector2 rightDir = -leftDir;

        // 计算两个方向分别能到达的位置
        Vector2 leftPos = fromPos + leftDir * moveDistance;
        Vector2 rightPos = fromPos + rightDir * moveDistance;

        // 计算哪个方向能让敌人更接近玩家
        float distToTarget_Left = Vector2.Distance(leftPos, targetPos);
        float distToTarget_Right = Vector2.Distance(rightPos, targetPos);

        // 优先尝试能让敌人更接近玩家的方向
        if (distToTarget_Left < distToTarget_Right)
        {
            if (!IsPathBlocked(fromPos, leftDir, moveDistance))
                return leftDir;  // 左边通畅，返回左方向
        }
        else
        {
            if (!IsPathBlocked(fromPos, rightDir, moveDistance))
                return rightDir;  // 右边通畅，返回右方向
        }

        // 如果优先方向被挡，尝试另一边
        if (!IsPathBlocked(fromPos, rightDir, moveDistance))
            return rightDir;
        if (!IsPathBlocked(fromPos, leftDir, moveDistance))
            return leftDir;

        // 两个方向都被挡，返回 zero（停留原地）
        return Vector2.zero;
    }

    private void OnDrawGizmosSelected()
    {
        // 在 Scene 视图中显示敌人的攻击判定范围 测试
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, attackRadius);
    }
}
