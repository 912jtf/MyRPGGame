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
    [Tooltip("enemy2 专用：Animator 里新增 Bool「IsSkillCharging」，用于蓄力动画；近战仍用 IsAttacking → Enemy2Attack")]
    [SerializeField] string skillChargingBoolName = "IsSkillCharging";

    Animator animator;
    Rigidbody2D rb;
    SpriteRenderer spriteRenderer;
    Transform targetPlayer;   // 索敌到的玩家
    float nextAttackTime;     // 下次允许攻击的时间

    [Header("enemy2 远程技能（射出去）")]
    [Tooltip("只给 enemy2 打开，其他敌人保持关闭")]
    public bool useEnemy2Skill01 = false;
    public GameObject enemy2Skill01Prefab;   // enemy2Skill01 的预制体
    public Transform enemy2Skill01SpawnPoint; // 敌人身上的发射点（建议建一个空物体）
    public float enemy2Skill01Cooldown = 3f;
    public float enemy2Skill01MinDistance = 1.0f; // 小于这个距离不放
    public float enemy2Skill01MaxDistance = 6.0f; // 大于这个距离不放
    public float enemy2Skill01SpeedOverride = -1f; // <0 表示用弹丸自身 speed
    public float enemy2Skill01DamageMultiplier = 2f;
    public float enemy2Skill01ChargeTime = 0.5f; // 蓄力时间（秒）
    float nextEnemy2Skill01Time;

    Coroutine enemy2Skill01Coroutine;
    Vector2 enemy2Skill01DirCache = Vector2.right;

    enum State { Idle, Chase, Attack, Cooldown, Enemy2Skill01Charging }
    State currentState = State.Idle;

    void Awake()
    {
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        SetAnimState(idle: true, walk: false, attack: false);
        SetSkillCharging(false);
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
            case State.Enemy2Skill01Charging:
                UpdateEnemy2Skill01Charging();
                break;
        }
    }

    void UpdateEnemy2Skill01Charging()
    {
        // 蓄力期间禁止移动
        if (rb != null) rb.velocity = Vector2.zero;

        if (targetPlayer == null) return;

        float dist = Vector2.Distance(transform.position, targetPlayer.position);
        // 玩家进入近战范围：立刻打断蓄力，改打近战
        if (dist <= attackDistance)
        {
            InterruptEnemy2Skill01ForMelee();
        }
        else
        {
            Vector2 dir = (Vector2)targetPlayer.position - (Vector2)transform.position;
            if (dir.sqrMagnitude > 0.0001f && spriteRenderer != null)
                spriteRenderer.flipX = dir.x < 0f;
        }
    }

    void UpdateIdle()
    {
        if (targetPlayer == null) return;

        Vector2 myPos = transform.position;
        Vector2 targetPos = targetPlayer.position;
        float dist = Vector2.Distance(myPos, targetPos);

        // 先尝试远程技能（仅在中远距离；近战优先下面逻辑）
        if (TryCastEnemy2Skill01(dist, targetPos))
            return;

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

        // 先尝试远程技能（仅在中远距离；若已在近战范围则追上去打近战）
        if (TryCastEnemy2Skill01(dist, targetPos))
            return;

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

    /// <summary>
    /// 尝试施放 enemy2 的射击技能 enemy2Skill01：
    /// - 只在 useEnemy2Skill01=true 时生效
    /// - 通过距离区间 + 冷却来决定是否释放
    /// </summary>
    bool TryCastEnemy2Skill01(float distanceToPlayer, Vector2 targetPos)
    {
        if (!useEnemy2Skill01)
            return false;

        if (Time.time < nextEnemy2Skill01Time)
            return false;

        // 在近战范围内不放技能（避免与近战抢 IsAttacking / 避免贴脸蓄力卡住）
        if (distanceToPlayer <= attackDistance)
            return false;

        if (distanceToPlayer < enemy2Skill01MinDistance || distanceToPlayer > enemy2Skill01MaxDistance)
            return false;

        if (enemy2Skill01Prefab == null || enemy2Skill01SpawnPoint == null)
            return false;

        // 进入蓄力：期间冻结不移动
        nextEnemy2Skill01Time = Time.time + enemy2Skill01Cooldown;

        Vector2 spawnPos = enemy2Skill01SpawnPoint.position;
        enemy2Skill01DirCache = ((Vector2)targetPos - spawnPos);
        if (enemy2Skill01DirCache.sqrMagnitude < 0.0001f)
            enemy2Skill01DirCache = Vector2.right;
        enemy2Skill01DirCache.Normalize();

        // 启动蓄力协程（防止重复触发）
        if (enemy2Skill01Coroutine == null)
            enemy2Skill01Coroutine = StartCoroutine(Enemy2Skill01ChargeRoutine());

        currentState = State.Enemy2Skill01Charging;

        // 蓄力用 IsSkillCharging；近战仍用 IsAttacking → Enemy2Attack
        SetAnimState(idle: false, walk: false, attack: false);
        SetSkillCharging(true);

        if (rb != null) rb.velocity = Vector2.zero;
        return true;
    }

    /// <summary>
    /// 蓄力被打断：停止协程，关闭蓄力动画，优先接近战。
    /// </summary>
    void InterruptEnemy2Skill01ForMelee()
    {
        if (enemy2Skill01Coroutine != null)
        {
            StopCoroutine(enemy2Skill01Coroutine);
            enemy2Skill01Coroutine = null;
        }

        SetSkillCharging(false);

        // 被打断后给技能较短冷却，避免刚打完近战又立刻原地蓄力
        nextEnemy2Skill01Time = Time.time + Mathf.Max(0.35f, enemy2Skill01Cooldown * 0.25f);

        if (Time.time >= nextAttackTime)
        {
            StartAttack();
        }
        else
        {
            currentState = State.Cooldown;
            SetAnimState(idle: true, walk: false, attack: false);
        }
    }

    System.Collections.IEnumerator Enemy2Skill01ChargeRoutine()
    {
        yield return new WaitForSeconds(enemy2Skill01ChargeTime);

        // 被打断时状态已变，协程应已 Stop；这里再保险判断一次
        if (currentState != State.Enemy2Skill01Charging)
        {
            enemy2Skill01Coroutine = null;
            yield break;
        }

        SetSkillCharging(false);

        // 蓄力结束：若玩家已贴脸则不打弹丸，直接近战
        if (targetPlayer != null)
        {
            float distNow = Vector2.Distance(transform.position, targetPlayer.position);
            if (distNow <= attackDistance)
            {
                enemy2Skill01Coroutine = null;
                if (Time.time >= nextAttackTime)
                    StartAttack();
                else
                {
                    currentState = State.Cooldown;
                    SetAnimState(idle: true, walk: false, attack: false);
                }
                yield break;
            }
        }

        Vector2 targetPosNow = targetPlayer != null ? (Vector2)targetPlayer.position : (Vector2)transform.position;
        SpawnEnemy2Skill01(targetPosNow);

        if (targetPlayer != null)
        {
            float distNow = Vector2.Distance(transform.position, targetPlayer.position);
            if (distNow > attackDistance)
            {
                currentState = State.Chase;
                SetAnimState(idle: false, walk: true, attack: false);
            }
            else
            {
                currentState = State.Idle;
                SetAnimState(idle: true, walk: false, attack: false);
            }
        }
        else
        {
            currentState = State.Idle;
            SetAnimState(idle: true, walk: false, attack: false);
        }

        enemy2Skill01Coroutine = null;
    }

    void SpawnEnemy2Skill01(Vector2 targetPosNow)
    {
        if (enemy2Skill01Prefab == null || enemy2Skill01SpawnPoint == null)
            return;

        // 玩家若已离开技能范围，就不发射（按释放瞬间的位置）
        float distNow = Vector2.Distance(transform.position, targetPosNow);
        if (distNow < enemy2Skill01MinDistance || distNow > enemy2Skill01MaxDistance)
            return;

        Vector2 spawnPos = enemy2Skill01SpawnPoint.position;

        GameObject go = Instantiate(enemy2Skill01Prefab, spawnPos, Quaternion.identity);

        Enemy2SkillProjectile proj = go.GetComponent<Enemy2SkillProjectile>();
        if (proj != null)
        {
            float? speedOverride = enemy2Skill01SpeedOverride >= 0f ? (float?)enemy2Skill01SpeedOverride : null;
            proj.InitToDestination(targetPosNow, speedOverride);
            proj.damage = Mathf.RoundToInt(attackDamage * enemy2Skill01DamageMultiplier);
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
        SetSkillCharging(false);
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

    void SetSkillCharging(bool on)
    {
        if (animator == null || string.IsNullOrEmpty(skillChargingBoolName))
            return;
        if (!HasAnimatorBool(skillChargingBoolName))
            return;
        animator.SetBool(skillChargingBoolName, on);
    }

    static bool HasAnimatorBool(Animator anim, string paramName)
    {
        foreach (AnimatorControllerParameter p in anim.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Bool && p.name == paramName)
                return true;
        }
        return false;
    }

    bool HasAnimatorBool(string paramName) => animator != null && HasAnimatorBool(animator, paramName);

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
    /// 用于 Kinematic 模式下的碰撞检测，只检测 Default layer（建筑物），忽略 Enemy layer（其他敌人）
    /// </summary>
    private bool IsPathBlocked(Vector2 fromPos, Vector2 direction, float distance)
    {
        // 创建 LayerMask：只检测 Default layer（建筑物），忽略 Enemy layer（敌人）
        int defaultLayer = LayerMask.NameToLayer("Default");
        LayerMask blockMask = 1 << defaultLayer;  // 只检测 Default layer
        
        // Raycast 检测：从敌人位置沿移动方向检测，距离为本帧移动距离 + 0.2f 的安全距离
        // 使用 LayerMask 只检测建筑物，不检测敌人
        RaycastHit2D hit = Physics2D.Raycast(fromPos, direction, distance + 0.2f, blockMask);
        
        // 如果检测到 Default layer 的碰撞体（建筑物），则被挡住
        if (hit.collider != null)
        {
            Debug.Log($"[Enemy {gameObject.name}] 前方被 {hit.collider.gameObject.name} 挡住");
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
