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
    public float mineAttackDistance = 0.9f;

    [Header("目标优先级")]
    [Tooltip("玩家进入该范围后，敌人会从矿点切换为追击玩家。")]
    public float playerAggroRange = 3.5f;
    [Tooltip("玩家超出该范围后，敌人会放弃追击并回到矿点。")]
    public float loseAggroRange = 6f;

    [Header("金矿目标")]
    public GoldMineController goldMine;
    [Tooltip("可选：用于记录敌人 ID。不填则自动使用实例 ID。")]
    public string enemyIdOverride;
    [Tooltip("偷矿成功后生成的金块预制体（应挂 GoldPickup）。")]
    public GameObject stolenGoldPickupPrefab;
    [Tooltip("偷矿成功后金块在敌人附近散落半径。")]
    public float stolenGoldDropScatter = 0.35f;

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

    [Header("Facing")]
    [Tooltip("用于修正精灵初始朝向是否反了。勾上后，所有 flipX 逻辑会反向。")]
    public bool invertFacing = false;

    [Header("受击反应")]
    [Tooltip("每次受击后向远离攻击源方向击退的距离（世界单位）")]
    public float hitKnockbackDistance = 0.45f;
    [Tooltip("击退持续时间（秒）")]
    public float hitKnockbackDuration = 0.1f;
    [Tooltip("受击僵直时间（秒）")]
    public float hitStunDuration = 0.2f;

    Animator animator;
    Rigidbody2D rb;
    SpriteRenderer spriteRenderer;
    CapsuleCollider2D bodyCollider;
    ContactFilter2D pathBlockFilter;
    readonly RaycastHit2D[] castHits = new RaycastHit2D[8];

    Transform targetPlayer;   // 索敌到的玩家
    Transform mineTarget;
    float nextAttackTime;     // 下次允许攻击的时间
    AttackTargetType currentAttackTarget = AttackTargetType.None;

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
    enum AttackTargetType { None, Player, Mine }
    State currentState = State.Idle;
    float _hitStunEndTime;
    Vector2 _knockbackDir;
    float _knockbackSpeed;

    void Awake()
    {
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        bodyCollider = GetComponent<CapsuleCollider2D>();

        // 墙体(Default) + 其它野怪(Enemy)：用身体 Collider 做 Cast，不会误判自己的碰撞体
        int mask = 0;
        int layerDefault = LayerMask.NameToLayer("Default");
        int layerEnemy = LayerMask.NameToLayer("Enemy");
        if (layerDefault >= 0) mask |= 1 << layerDefault;
        if (layerEnemy >= 0) mask |= 1 << layerEnemy;
        pathBlockFilter = new ContactFilter2D
        {
            useLayerMask = true,
            useTriggers = false
        };
        pathBlockFilter.SetLayerMask(mask);

        SetAnimState(idle: true, walk: false, attack: false);
        SetSkillCharging(false);
        ResolveMineTarget();
    }

    void Update()
    {
        UpdateTargetSelection();

        if (HandleHitStunAndKnockback())
            return;

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

    bool HandleHitStunAndKnockback()
    {
        if (Time.time >= _hitStunEndTime)
            return false;

        float dt = Time.deltaTime;
        float remain = _hitStunEndTime - Time.time;
        float moveStep = 0f;
        if (hitKnockbackDuration > 0f && _knockbackSpeed > 0f)
            moveStep = _knockbackSpeed * Mathf.Min(dt, remain);

        if (moveStep > 0f)
        {
            Vector2 myPos = rb != null ? rb.position : (Vector2)transform.position;
            if (!IsPathBlocked(myPos, _knockbackDir, moveStep))
            {
                Vector2 nextPos = myPos + _knockbackDir * moveStep;
                if (rb != null) rb.MovePosition(nextPos);
                else transform.position = nextPos;
            }
        }

        if (rb != null) rb.velocity = Vector2.zero;
        return true;
    }

    public void ApplyHitReaction(Vector2 hitSourceWorldPos)
    {
        Vector2 selfPos = rb != null ? rb.position : (Vector2)transform.position;
        Vector2 away = selfPos - hitSourceWorldPos;
        if (away.sqrMagnitude < 0.0001f)
            away = spriteRenderer != null && spriteRenderer.flipX ? Vector2.right : Vector2.left;
        _knockbackDir = away.normalized;

        float kbDuration = Mathf.Max(0f, hitKnockbackDuration);
        _knockbackSpeed = kbDuration > 0f ? Mathf.Max(0f, hitKnockbackDistance) / kbDuration : 0f;
        _hitStunEndTime = Mathf.Max(_hitStunEndTime, Time.time + Mathf.Max(0f, hitStunDuration));

        if (enemy2Skill01Coroutine != null)
        {
            StopCoroutine(enemy2Skill01Coroutine);
            enemy2Skill01Coroutine = null;
        }
        SetSkillCharging(false);
        currentState = State.Cooldown;
        SetAnimState(idle: true, walk: false, attack: false);
        if (rb != null) rb.velocity = Vector2.zero;
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
                spriteRenderer.flipX = (dir.x < 0f) ^ invertFacing;
        }
    }

    void UpdateIdle()
    {
        // 无玩家目标：回矿点
        if (targetPlayer == null)
        {
            if (HasValidMineTarget())
            {
                float mineDist = Vector2.Distance(transform.position, mineTarget.position);
                if (mineDist > mineAttackDistance)
                {
                    currentState = State.Chase;
                    SetAnimState(idle: false, walk: true, attack: false);
                }
                else if (Time.time >= nextAttackTime)
                {
                    StartMineAttack();
                }
                else
                {
                    SetAnimState(idle: true, walk: false, attack: false);
                }
            }
            return;
        }

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
            // 无玩家目标时，改为追矿点
            if (!HasValidMineTarget())
            {
                currentState = State.Idle;
                SetAnimState(idle: true, walk: false, attack: false);
                return;
            }

            Vector2 myPosMine = rb != null ? rb.position : (Vector2)transform.position;
            Vector2 minePos = mineTarget.position;
            Vector2 mineDir = (minePos - myPosMine).normalized;

            if (mineDir.sqrMagnitude > 0.0001f && spriteRenderer != null)
                spriteRenderer.flipX = (mineDir.x < 0f) ^ invertFacing;

            float mineDist = Vector2.Distance(myPosMine, minePos);
            if (mineDist <= mineAttackDistance)
            {
                if (Time.time >= nextAttackTime)
                    StartMineAttack();
                else
                {
                    currentState = State.Cooldown;
                    SetAnimState(idle: true, walk: false, attack: false);
                    if (rb != null) rb.velocity = Vector2.zero;
                }
                return;
            }

            float moveDistanceToMine = moveSpeed * Time.deltaTime;
            Vector2 nextPosToMine = myPosMine + mineDir * moveDistanceToMine;
            if (!IsPathBlocked(myPosMine, mineDir, moveDistanceToMine))
            {
                if (rb != null) rb.MovePosition(nextPosToMine);
                else transform.position = nextPosToMine;
            }
            else
            {
                Vector2 altMineDir = FindAlternativePath(myPosMine, mineDir, minePos, moveDistanceToMine);
                if (altMineDir != Vector2.zero)
                {
                    nextPosToMine = myPosMine + altMineDir * moveDistanceToMine;
                    if (rb != null) rb.MovePosition(nextPosToMine);
                    else transform.position = nextPosToMine;
                }
            }
            return;
        }

        // 用刚体位置做寻路检测，与 MovePosition 一致（避免 Update 里 transform 与物理不同步）
        Vector2 myPos = rb != null ? rb.position : (Vector2)transform.position;
        Vector2 targetPos = targetPlayer.position;
        Vector2 dir = (targetPos - myPos).normalized;

        // 翻转朝向
        if (dir.sqrMagnitude > 0.0001f && spriteRenderer != null)
        {
            spriteRenderer.flipX = (dir.x < 0f) ^ invertFacing;
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
            else
            {
                // 没有左右绕过路径时，尝试向后退一步作为解卡。
                Vector2 backDir = -dir;
                if (!IsPathBlocked(myPos, backDir, moveDistance))
                {
                    nextPos = myPos + backDir * moveDistance;
                    if (rb != null)
                        rb.MovePosition(nextPos);
                    else
                        transform.position = nextPos;
                }
                // 仍被挡住：停留原地（下一帧继续尝试）
            }
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
            if (HasValidMineTarget())
            {
                float mineDist = Vector2.Distance(transform.position, mineTarget.position);
                if (Time.time >= nextAttackTime)
                {
                    if (mineDist <= mineAttackDistance) StartMineAttack();
                    else
                    {
                        currentState = State.Chase;
                        SetAnimState(idle: false, walk: true, attack: false);
                    }
                }
                else
                {
                    if (rb != null) rb.velocity = Vector2.zero;
                    SetAnimState(idle: true, walk: false, attack: false);
                }
                return;
            }

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
                spriteRenderer.flipX = (dir.x < 0f) ^ invertFacing;

            if (rb != null) rb.velocity = Vector2.zero;
            SetAnimState(idle: true, walk: false, attack: false);
        }
    }

    void StartAttack()
    {
        currentState = State.Attack;
        currentAttackTarget = AttackTargetType.Player;
        nextAttackTime = Time.time + attackCooldown;
        SetSkillCharging(false);
        SetAnimState(idle: false, walk: false, attack: true);
        if (rb != null) rb.velocity = Vector2.zero;
    }

    void StartMineAttack()
    {
        currentState = State.Attack;
        currentAttackTarget = AttackTargetType.Mine;
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
        if (other.CompareTag("Player"))
            targetPlayer = other.transform;
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (targetPlayer != null && other.transform == targetPlayer)
            targetPlayer = null;
    }

    // ===== Animation Event =====

    /// <summary>
    /// 攻击动画命中瞬间在 Animation Event 中调用。
    /// 使用 OverlapCircleAll 对 Player 层进行伤害判定。
    /// </summary>
    public void OnAttackHit()
    {
        if (currentAttackTarget == AttackTargetType.Mine)
        {
            if (HasValidMineTarget() && Vector2.Distance(transform.position, mineTarget.position) <= mineAttackDistance + 0.25f)
            {
                bool stole = goldMine.TryStealByEnemy(GetEnemyId());
                if (stole)
                    SpawnStolenGoldPickup();
            }
            return;
        }

        // 以敌人当前位置为圆心进行范围检测
        Vector2 center = transform.position;
        Collider2D[] hitPlayers = Physics2D.OverlapCircleAll(center, attackRadius, playerLayer);
        foreach (Collider2D col in hitPlayers)
        {
            PlayerHealth ph = col.GetComponent<PlayerHealth>();
            if (ph != null) ph.TakeDamage(attackDamage);
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
        currentAttackTarget = AttackTargetType.None;

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

        if (HasValidMineTarget())
        {
            float mineDist = Vector2.Distance(transform.position, mineTarget.position);
            if (mineDist <= mineAttackDistance)
            {
                if (Time.time >= nextAttackTime) StartMineAttack();
                else
                {
                    currentState = State.Cooldown;
                    SetAnimState(idle: true, walk: false, attack: false);
                }
            }
            else
            {
                currentState = State.Chase;
                SetAnimState(idle: false, walk: true, attack: false);
            }
            return;
        }

        currentState = State.Idle;
        SetAnimState(idle: true, walk: false, attack: false);
    }

    /// <summary>
    /// 检测沿某方向移动是否会与墙体或其它野怪的身体碰撞。
    /// 必须用身体 Capsule 做 Cast：仅用 Raycast 会忽略体积宽度；且必须把 Enemy 算进去，否则会互相挤进同一格。
    /// </summary>
    private bool IsPathBlocked(Vector2 fromPos, Vector2 direction, float distance)
    {
        if (bodyCollider == null)
        {
            int defaultLayer = LayerMask.NameToLayer("Default");
            if (defaultLayer < 0) return false;
            RaycastHit2D hit = Physics2D.Raycast(fromPos, direction, distance + 0.2f, 1 << defaultLayer);
            return hit.collider != null;
        }

        // cast 的起点可能会“贴在一起”（尤其是 Kinematic / 靠墙），
        // Unity 可能会把距离接近 0 的命中也算进去，导致永远判定被挡而卡死。
        // 通过缩小额外距离 + 忽略距离接近 0 的命中来缓解。
        float skin = 0.02f;
        float castDist = Mathf.Max(distance, 0.001f) + skin;
        int count = bodyCollider.Cast(direction, pathBlockFilter, castHits, castDist);
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = castHits[i];
            Collider2D c = hit.collider;
            if (c == null || c.isTrigger) continue;
            if (c.gameObject == gameObject) continue;

            // 忽略“从起点贴合碰撞体”导致的距离为 0 命中
            if (hit.distance <= 0.001f)
                continue;

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

        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.85f);
        Gizmos.DrawWireSphere(transform.position, mineAttackDistance);

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, playerAggroRange);
    }

    void ResolveMineTarget()
    {
        if (goldMine == null)
            goldMine = FindObjectOfType<GoldMineController>();
        mineTarget = goldMine != null ? goldMine.transform : null;
    }

    bool HasValidMineTarget()
    {
        if (goldMine == null || mineTarget == null)
            ResolveMineTarget();
        return goldMine != null && mineTarget != null && !goldMine.IsDepleted;
    }

    void UpdateTargetSelection()
    {
        if (targetPlayer != null)
        {
            float dist = Vector2.Distance(transform.position, targetPlayer.position);
            if (dist > loseAggroRange)
                targetPlayer = null;
            return;
        }

        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        if (players == null || players.Length == 0)
            return;

        float bestSqr = playerAggroRange * playerAggroRange;
        Transform best = null;
        Vector2 self = transform.position;
        foreach (GameObject player in players)
        {
            if (player == null) continue;
            float sqr = ((Vector2)player.transform.position - self).sqrMagnitude;
            if (sqr <= bestSqr)
            {
                bestSqr = sqr;
                best = player.transform;
            }
        }

        if (best != null)
            targetPlayer = best;
    }

    string GetEnemyId()
    {
        if (!string.IsNullOrWhiteSpace(enemyIdOverride))
            return enemyIdOverride;
        return $"{name}_{GetInstanceID()}";
    }

    void SpawnStolenGoldPickup()
    {
        if (stolenGoldPickupPrefab == null)
            return;

        Vector2 offset = UnityEngine.Random.insideUnitCircle * Mathf.Max(0f, stolenGoldDropScatter);
        Vector2 spawnPos = (Vector2)transform.position + offset;
        GameObject go = Instantiate(stolenGoldPickupPrefab, spawnPos, Quaternion.identity);
        GoldPickup pickup = go.GetComponent<GoldPickup>();
        if (pickup != null)
            pickup.amount = Mathf.Max(1, pickup.amount);
    }
}
