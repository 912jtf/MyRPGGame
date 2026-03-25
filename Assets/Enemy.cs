using UnityEngine;
using UnityEngine.Tilemaps;

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
    [Tooltip("玩家超出 loseAggroRange 后，额外追击的耐心时间（秒）。到时仍超出才真正脱战回矿。")]
    public float loseAggroPatience = 3f;
    [Tooltip("开启后：玩家在高地排序层级时，敌人视为丢失目标，转而追金矿。")]
    public bool loseSightWhenPlayerOnHighGround = true;
    [Tooltip("玩家 SpriteRenderer.sortingOrder 大于等于该值时，视为进入高地。")]
    public int playerHighGroundSortingOrderThreshold = 15;

    [Header("金矿目标")]
    public GoldMineController goldMine;
    [Tooltip("当未拖拽 goldMine 且场景里也查不到时，按名称兜底查找矿点对象。")]
    public string fallbackMineObjectName = "GoldMine_Active";
    [Tooltip("可选：用于记录敌人 ID。不填则自动使用实例 ID。")]
    public string enemyIdOverride;
    [Tooltip("偷矿成功后生成的金块预制体（应挂 GoldPickup）。")]
    public GameObject stolenGoldPickupPrefab;
    [Tooltip("若未配置金块预制体，可直接拖 Sprite（如 G_Idle）作为运行时金块外观。")]
    public Sprite stolenGoldFallbackSprite;
    [Tooltip("偷矿成功后金块在敌人附近散落半径。")]
    public float stolenGoldDropScatter = 0.35f;
    [Tooltip("矿点命中判定补偿距离，避免因碰撞体中心偏移导致看起来贴脸却判定不到。")]
    public float mineAttackHitPadding = 0.6f;
    [Tooltip("矿点攻击动画未配置事件时，延迟多少秒自动结算一次命中。")]
    public float mineAttackFallbackHitDelay = 0.2f;
    [Tooltip("矿点攻击动画未配置结束事件时，多久后自动结束本次攻击。")]
    public float mineAttackFallbackEndDelay = 0.55f;

    [Header("偷矿后的携带与乱跑（新增）")]
    [Tooltip("偷矿成功后：金块由野怪携带（挂在身上），而不是掉地上散落。")]
    public bool carryStolenGold = false;
    [Tooltip("携带金块挂到敌人的哪个节点（为空则挂到敌人自身）。")]
    public Transform carriedGoldAttachPoint;
    [Tooltip("携带金块在挂点上的本地偏移。")]
    public Vector2 carriedGoldLocalOffset = new Vector2(0.0f, 0.25f);
    [Tooltip("仅用于寻路时忽略该碰撞体（勿把它当作整张地图边界；地图范围请用「地图边界」矩形）。")]
    public Collider2D roamBoundsCollider;
    [Tooltip("乱跑时多久随机换一次方向。")]
    public float roamDirectionChangeInterval = 1.0f;
    [Tooltip("乱跑时遇到边界/障碍导致换向后，短暂冷却，避免左右抽搐。")]
    public float roamObstacleResolveCooldown = 0.25f;
    [Tooltip("当两边都无法通行时，暂停一小段时间再选择新方向，避免每帧来回翻转。")]
    public float roamStuckDuration = 0.35f;

    [Header("地图边界（偷矿携带金块时）")]
    [Tooltip("偷到金后乱跑或追玩家时，将位置限制在此世界坐标矩形内；与 EnemySpawner、WorldGoldScatterSpawner 的 min/max 对齐。")]
    public bool clampCarriedGoldToMap = true;
    public Vector2 mapClampWorldMin = new Vector2(-13.34f, -4.39f);
    public Vector2 mapClampWorldMax = new Vector2(13.34f, 4.39f);

    [Header("偷矿后行为（简化版）")]
    [Tooltip("偷到金后：离开金矿（不会要求敌人携带金块）。")]
    public bool fleeAfterSteal = true;
    [Tooltip("偷到金后：是否在地上生成金块（默认关闭，简化为只离开，不掉金块）。")]
    public bool spawnGoldPickupOnSteal = false;

    [Header("偷矿后：离开金矿（简化版）")]
    [Tooltip("偷到金后，野怪需要离开金矿至少这么远（世界单位），到达后停止离开。")]
    public float fleeMinDistanceFromMine = 2.0f;
    [Tooltip("偷到金后最长持续离开金矿的时间（秒）。时间到未离开到位就停下。")]
    public float fleeMaxDuration = 6f;

    [Header("调试日志（可选）")]
    [Tooltip("勾选后输出敌人 AI 与矿点行为日志。")]
    public bool debugMineAI = false;
    [Tooltip("状态快照日志间隔（秒）。")]
    public float debugLogInterval = 0.75f;
    [Tooltip("当发生“切换到非耗尽金矿”时，自动在 Hierarchy 选中原本绑定的耗尽/禁用金矿对象（便于你直接删/改）。")]
    public bool debugSelectDepletedMineOnSwitch = false;

    [Header("伤害设置")]
    public LayerMask playerLayer;         // 只勾选 Player 层
    public float attackDamage = 1f;          // 对玩家造成的伤害值

    [Header("音效（拖入 AudioClip；留空则静音）")]
    [Tooltip("近战攻击动画命中帧（OnAttackHit），打玩家时播放；偷矿攻击不播")]
    public AudioClip meleeAttackSfx;
    [Tooltip("enemy2 技能弹丸生成瞬间（SpawnEnemy2Skill01）")]
    public AudioClip skillCastSfx;
    [Range(0f, 1f)] public float meleeAttackSfxVolume = 1f;
    [Range(0f, 1f)] public float skillCastSfxVolume = 1f;

    [Header("寻路阻挡调试（可选）")]
    [Tooltip("打开后：当 IsPathBlocked 命中障碍时打印是哪一个 Collider 在挡路。")]
    public bool debugPathBlocked = false;
    [Tooltip("日志输出频率，避免刷屏。")]
    public float debugPathBlockedLogInterval = 0.5f;
    float _nextPathBlockedLogTime;

    [Header("离开金矿调试（只在最终走不动时打印）")]
    public bool debugFleeBlockSummary = false;
    public float debugFleeBlockSummaryLogInterval = 1.0f;
    float _nextFleeBlockSummaryLogTime;

    Collider2D _lastPathBlockCollider;
    Vector2 _lastPathBlockHitNormal;
    Vector2 _lastPathBlockDirection;
    float _lastPathBlockDistance;
    int _lastPathBlockLayer;
    string _lastPathBlockTag;
    bool _lastPathBlockIsTrigger;
    float _lastPathBlockTime;

    [Header("离开金矿调试（更直观：任意挡路打印）")]
    public bool debugRoamBlockHit = false;
    public float debugRoamBlockHitLogInterval = 0.2f;
    float _nextRoamBlockHitLogTime;

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

    enum State { Idle, Chase, Attack, Cooldown, Enemy2Skill01Charging, Roam }
    enum AttackTargetType { None, Player, Mine }
    State currentState = State.Idle;
    float _hitStunEndTime;
    Vector2 _knockbackDir;
    float _knockbackSpeed;
    bool _warnedMineMissing;
    float _attackStateStartTime;
    bool _mineHitAppliedThisAttack;
    bool _mineStealSucceededThisAttack;
    State _lastDebugState;
    float _nextDebugLogTime;

    GoldPickup _carriedGoldPickup; // 由野怪携带的金块（挂在敌人身上）
    Vector2 _roamDir = Vector2.zero;
    float _nextRoamDirChangeTime;
    float _roamObstacleResolveUntil;
    float _roamStuckUntil;
    float _fleeUntilTime;
    float _loseAggroDeadline = -1f;
    bool HasCarriedGold => _carriedGoldPickup != null && _carriedGoldPickup.amount > 0;
    bool IsFleeingFromMine => fleeAfterSteal && Time.time < _fleeUntilTime;

    void Awake()
    {
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        bodyCollider = GetComponent<CapsuleCollider2D>();

        // 用 Enemy 图层的物理碰撞矩阵来决定“什么会挡路”。
        // 这样 Collision-High/Collision-low 等地图层若与 Enemy 勾选了碰撞，就会自动参与阻挡。
        int myLayer = gameObject.layer;
        int mask = Physics2D.GetLayerCollisionMask(myLayer);
        if (mask == 0)
        {
            // 兜底：至少保留默认层 + 敌人层，避免配置异常时完全不检测。
            int layerDefault = LayerMask.NameToLayer("Default");
            int layerEnemy = LayerMask.NameToLayer("Enemy");
            if (layerDefault >= 0) mask |= 1 << layerDefault;
            if (layerEnemy >= 0) mask |= 1 << layerEnemy;
        }
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

    void LateUpdate()
    {
        if (!clampCarriedGoldToMap || !fleeAfterSteal || !HasCarriedGold)
            return;

        Vector2 p = rb != null ? rb.position : (Vector2)transform.position;
        Vector2 c = ClampCarriedGoldToConfiguredMap(p);
        if ((c - p).sqrMagnitude < 1e-10f)
            return;

        if (rb != null)
            rb.MovePosition(c);
        else
            transform.position = c;
    }

    static Vector2 ClampToAxisAlignedRect(Vector2 p, Vector2 minCorner, Vector2 maxCorner)
    {
        float minX = Mathf.Min(minCorner.x, maxCorner.x);
        float maxX = Mathf.Max(minCorner.x, maxCorner.x);
        float minY = Mathf.Min(minCorner.y, maxCorner.y);
        float maxY = Mathf.Max(minCorner.y, maxCorner.y);
        return new Vector2(Mathf.Clamp(p.x, minX, maxX), Mathf.Clamp(p.y, minY, maxY));
    }

    Vector2 ClampCarriedGoldToConfiguredMap(Vector2 p)
    {
        return ClampToAxisAlignedRect(p, mapClampWorldMin, mapClampWorldMax);
    }

    void Update()
    {
        UpdateTargetSelection();

        if (HandleHitStunAndKnockback())
            return;

        DebugSnapshotTick();

        switch (currentState)
        {
            case State.Idle:
                UpdateIdle();
                break;
            case State.Chase:
                UpdateChase();
                break;
            case State.Attack:
                // 玩家攻击主要由动画事件驱动；矿点攻击额外提供容错，防止未配事件时卡死。
                UpdateAttackStateFallback();
                break;
            case State.Cooldown:
                UpdateCooldown();
                break;
            case State.Enemy2Skill01Charging:
                UpdateEnemy2Skill01Charging();
                break;
            case State.Roam:
                UpdateRoam();
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
        if (fleeAfterSteal && HasCarriedGold && targetPlayer == null)
        {
            currentState = State.Roam;
            SetAnimState(idle: false, walk: true, attack: false);
            return;
        }

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
            // 追逐丢失：如果还带着偷到的金子，就继续 Roam 随机乱跑；
            // 否则按原逻辑追矿点。
            if (fleeAfterSteal && HasCarriedGold)
            {
                currentState = State.Roam;
                SetAnimState(idle: false, walk: true, attack: false);
                return;
            }

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

        CombatSfxUtil.Play2D(skillCastSfx, spawnPos, skillCastSfxVolume);
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
        _attackStateStartTime = Time.time;
        _mineHitAppliedThisAttack = false;
        nextAttackTime = Time.time + attackCooldown;
        SetSkillCharging(false);
        SetAnimState(idle: false, walk: false, attack: true);
        if (rb != null) rb.velocity = Vector2.zero;
        if (debugMineAI)
            Debug.Log($"[{name}] StartAttack -> target=Player");
    }

    void StartMineAttack()
    {
        currentState = State.Attack;
        currentAttackTarget = AttackTargetType.Mine;
        _attackStateStartTime = Time.time;
        _mineHitAppliedThisAttack = false;
        _mineStealSucceededThisAttack = false;
        nextAttackTime = Time.time + attackCooldown;
        SetSkillCharging(false);
        SetAnimState(idle: false, walk: false, attack: true);
        if (rb != null) rb.velocity = Vector2.zero;
        if (debugMineAI)
            Debug.Log($"[{name}] StartMineAttack distToMine={GetDistanceToMine():F2}");
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
            TryMineStealHit();
            return;
        }

        CombatSfxUtil.Play2D(meleeAttackSfx, transform.position, meleeAttackSfxVolume);

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
        // 如果本次偷矿真正成功，则攻击结束后立刻开始离开金矿（不再回矿点）。
        if (fleeAfterSteal && currentAttackTarget == AttackTargetType.Mine && _mineStealSucceededThisAttack)
        {
            currentState = State.Roam;
            SetAnimState(idle: false, walk: true, attack: false);
            return;
        }

        currentAttackTarget = AttackTargetType.None;
        _mineHitAppliedThisAttack = false;
        if (debugMineAI)
            Debug.Log($"[{name}] OnAttackEnd state={currentState}");

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
            return;
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

            // 如果敌人正携带金块，金块 Collider 不应参与寻路障碍检测；
            // 否则金块挂在身上时可能导致敌人“误判前方被挡”而左右抽搐。
            if (_carriedGoldPickup != null)
            {
                Transform goldTr = _carriedGoldPickup.transform;
                if (goldTr != null && (c.transform == goldTr || c.transform.IsChildOf(goldTr)))
                    continue;
            }

            // 金矿自身如果意外带了碰撞体（可能在子物体上），不应阻挡敌人靠近/攻击。
            // 这样即使你“根物体没加 collider”，也不会卡在边界。
            if (c != null && c.GetComponentInParent<GoldMineController>() != null)
                continue;

            // roamBoundsCollider 只用于“边界约束”，不应该参与寻路障碍检测。
            // 否则如果它的 Layer 恰好在 Default/Enemy，会被 Cast 命中导致左右抽搐。
            if (roamBoundsCollider != null)
            {
                if (c == roamBoundsCollider || c.transform.IsChildOf(roamBoundsCollider.transform))
                    continue;
            }

            // 乱跑阶段允许“穿过其它野怪”，避免偷矿后被敌群堵住导致原地不动。
            // 只在 Roam 启用该放宽；追玩家/回矿时仍保留敌人互相阻挡。
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (currentState == State.Roam && enemyLayer >= 0 && c.gameObject.layer == enemyLayer)
                continue;

            // 忽略“从起点贴合碰撞体”导致的距离为 0 命中
            if (hit.distance <= 0.001f)
                continue;

            // 为减少误判：水平移动时，地面型命中可忽略。
            // 但高低地边界/地图边界必须视为阻挡，否则会“卡一会儿后挤进高地”。
            bool isElevationOrBoundary =
                c is TilemapCollider2D ||
                (c.gameObject != null && (
                    c.gameObject.name.IndexOf("collision-high", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    c.gameObject.name.IndexOf("collision-low", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    c.gameObject.name.IndexOf("boundary", System.StringComparison.OrdinalIgnoreCase) >= 0));
            if (!isElevationOrBoundary && Mathf.Abs(direction.y) < 0.25f && Mathf.Abs(hit.normal.y) > 0.5f)
                continue;

            // 记录最近一次真正“挡路”的碰撞体信息，供 UpdateRoam 离开阶段总结输出。
            _lastPathBlockCollider = c;
            _lastPathBlockHitNormal = hit.normal;
            _lastPathBlockDirection = direction;
            _lastPathBlockDistance = distance;
            _lastPathBlockLayer = c.gameObject.layer;
            _lastPathBlockTag = c.tag;
            _lastPathBlockIsTrigger = c.isTrigger;
            _lastPathBlockTime = Time.time;

            if (debugPathBlocked && Time.time >= _nextPathBlockedLogTime)
            {
                _nextPathBlockedLogTime = Time.time + Mathf.Max(0.01f, debugPathBlockedLogInterval);
                string colliderName = c != null ? c.name : "null";
                int layer = c != null ? c.gameObject.layer : -1;
                string tag = c != null ? c.tag : "null";
                Debug.Log($"[{name}] IsPathBlocked HIT state={currentState} " +
                          $"from={fromPos} dir={direction} dist={distance:F3} hitDist={hit.distance:F3} " +
                          $"hitNormal={hit.normal} " +
                          $"blockCollider={colliderName} blockLayer={layer} tag={tag} isTrigger={c.isTrigger}");
            }

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
        // 先按名字锁定矿点对象，避免场景里存在多个 GoldMineController 时绑定到错误实例。
        if (mineTarget == null && !string.IsNullOrWhiteSpace(fallbackMineObjectName))
        {
            GameObject mineObj = GameObject.Find(fallbackMineObjectName);
            if (mineObj == null)
            {
                Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();
                foreach (Transform t in all)
                {
                    if (t == null) continue;
                    if (!t.gameObject.scene.IsValid()) continue;
                    if (t.name == fallbackMineObjectName || t.name.StartsWith(fallbackMineObjectName))
                    {
                        mineObj = t.gameObject;
                        break;
                    }
                }
            }

            if (mineObj != null)
            {
                mineTarget = mineObj.transform;
                if (goldMine == null) goldMine = mineObj.GetComponent<GoldMineController>();
                if (goldMine == null) goldMine = mineObj.GetComponentInChildren<GoldMineController>();
                if (goldMine == null) goldMine = mineObj.GetComponentInParent<GoldMineController>();
                if (debugMineAI)
                    Debug.Log($"[{name}] ResolveMineTarget byName -> mineTarget={mineTarget.name}, goldMine={(goldMine != null ? $"{goldMine.name}#{goldMine.GetInstanceID()}" : "null")}");
            }
        }

        if (goldMine == null && mineTarget != null)
        {
            goldMine = mineTarget.GetComponent<GoldMineController>();
            if (goldMine == null) goldMine = mineTarget.GetComponentInChildren<GoldMineController>();
            if (goldMine == null) goldMine = mineTarget.GetComponentInParent<GoldMineController>();
        }

        if (goldMine == null)
            goldMine = FindObjectOfType<GoldMineController>();
        if (goldMine == null)
            goldMine = FindAnyGoldMineController(includeInactive: true);

        if (mineTarget == null && goldMine != null)
            mineTarget = goldMine.transform;

        if (debugMineAI)
            Debug.Log($"[{name}] ResolveMineTarget final -> mineTarget={(mineTarget != null ? mineTarget.name : "null")}, goldMine={(goldMine != null ? $"{goldMine.name}#{goldMine.GetInstanceID()} current={goldMine.CurrentGold}" : "null")}");

        // 如果绑定到的是“空矿”，且场景里存在同名但非空的金矿控制器，
        // 切换到非空那一个，避免因为多个同名对象导致敌人始终不去偷矿。
        if (goldMine != null && goldMine.CurrentGold <= 0 && !string.IsNullOrWhiteSpace(fallbackMineObjectName))
        {
            GoldMineController[] mines = Resources.FindObjectsOfTypeAll<GoldMineController>();
            GoldMineController best = null;
            foreach (GoldMineController m in mines)
            {
                if (m == null) continue;
                // 过滤掉 Prefab 资产/非场景对象，避免拿到一个 CurrentGold==0 的“静态组件”。
                if (!m.gameObject.scene.IsValid())
                    continue;
                string mn = m.gameObject.name;
                bool nameOk = mn == fallbackMineObjectName || mn.StartsWith(fallbackMineObjectName);
                if (!nameOk) continue;
                if (m.CurrentGold > 0)
                {
                    best = m;
                    break;
                }
            }

            if (best != null && best != goldMine)
            {
                if (debugMineAI)
                {
                    Debug.Log($"[{name}] ResolveMineTarget SWITCH: from goldMine={goldMine.name}#{goldMine.GetInstanceID()} current={goldMine.CurrentGold} activeInHierarchy={goldMine.gameObject.activeInHierarchy} enabled={goldMine.enabled} path={GetTransformPath(goldMine.transform)}" +
                              $" -> to goldMine={best.name}#{best.GetInstanceID()} current={best.CurrentGold} activeInHierarchy={best.gameObject.activeInHierarchy} enabled={best.enabled} path={GetTransformPath(best.transform)}");
                }
#if UNITY_EDITOR
                if (debugSelectDepletedMineOnSwitch && goldMine != null)
                    UnityEditor.Selection.activeGameObject = goldMine.gameObject;
#endif
                goldMine = best;
                mineTarget = best.transform;
                if (debugMineAI)
                    Debug.Log($"[{name}] ResolveMineTarget SWITCH non-depleted mine -> goldMine={goldMine.name}#{goldMine.GetInstanceID()} current={goldMine.CurrentGold}");
            }
        }
    }

    static string GetTransformPath(Transform t)
    {
        if (t == null) return "null";
        string path = t.name;
        Transform cur = t.parent;
        int guard = 0;
        while (cur != null && guard < 30)
        {
            path = cur.name + "/" + path;
            cur = cur.parent;
            guard++;
        }
        return path;
    }

    bool HasValidMineTarget()
    {
        if (goldMine == null || mineTarget == null)
            ResolveMineTarget();

        bool hasTarget = mineTarget != null;
        bool mineAlive = goldMine == null || !goldMine.IsDepleted;
        bool ok = hasTarget && mineAlive;

        if (!ok && !_warnedMineMissing)
        {
            // 矿被打空是正常流程，不打印“找不到目标”的告警，避免误导排查方向。
            if (!hasTarget)
            {
                _warnedMineMissing = true;
                string targetName = mineTarget != null ? mineTarget.name : "null";
                string mineName = goldMine != null ? goldMine.name : "null";
                Debug.LogWarning($"[{name}] 金矿目标不可用：未找到矿点 Transform。fallbackMineObjectName={fallbackMineObjectName}, mineTarget={targetName}, goldMine={mineName}");
            }
        }
        return ok;
    }

    static GoldMineController FindAnyGoldMineController(bool includeInactive)
    {
        if (!includeInactive)
            return FindObjectOfType<GoldMineController>();

        GoldMineController[] all = Resources.FindObjectsOfTypeAll<GoldMineController>();
        foreach (GoldMineController mine in all)
        {
            if (mine == null) continue;
            if (mine.gameObject.scene.IsValid())
                return mine;
        }
        return null;
    }

    void UpdateTargetSelection()
    {
        if (targetPlayer != null)
        {
            if (loseSightWhenPlayerOnHighGround && IsPlayerOnHighGround(targetPlayer))
            {
                if (debugMineAI)
                    Debug.Log($"[{name}] Lose aggro: player on high ground.");
                targetPlayer = null;
                _loseAggroDeadline = -1f;
                return;
            }

            if (!targetPlayer.gameObject.activeInHierarchy)
            {
                targetPlayer = null;
                _loseAggroDeadline = -1f;
                return;
            }

            float dist = Vector2.Distance(transform.position, targetPlayer.position);
            if (dist > loseAggroRange)
            {
                if (_loseAggroDeadline < 0f)
                {
                    _loseAggroDeadline = Time.time + Mathf.Max(0f, loseAggroPatience);
                    if (debugMineAI)
                        Debug.Log($"[{name}] Player out of range, start patience timer {loseAggroPatience:F2}s (dist={dist:F2} > {loseAggroRange:F2})");
                }
                else if (Time.time >= _loseAggroDeadline)
                {
                    if (debugMineAI)
                        Debug.Log($"[{name}] Lose aggro after patience (dist={dist:F2} > {loseAggroRange:F2})");
                    targetPlayer = null;
                    _loseAggroDeadline = -1f;
                }
            }
            else
            {
                // 回到追击范围内，重置耐心计时。
                _loseAggroDeadline = -1f;
            }
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
            if (loseSightWhenPlayerOnHighGround && IsPlayerOnHighGround(player.transform))
                continue;
            float sqr = ((Vector2)player.transform.position - self).sqrMagnitude;
            if (sqr <= bestSqr)
            {
                bestSqr = sqr;
                best = player.transform;
            }
        }

        if (best != null)
        {
            targetPlayer = best;
            _loseAggroDeadline = -1f;
            if (debugMineAI)
                Debug.Log($"[{name}] Acquire player dist={Vector2.Distance(transform.position, targetPlayer.position):F2}");
        }
    }

    bool IsPlayerOnHighGround(Transform playerTr)
    {
        if (playerTr == null)
            return false;
        SpriteRenderer sr = playerTr.GetComponentInChildren<SpriteRenderer>();
        if (sr == null)
            return false;
        return sr.sortingOrder >= playerHighGroundSortingOrderThreshold;
    }

    string GetEnemyId()
    {
        if (!string.IsNullOrWhiteSpace(enemyIdOverride))
            return enemyIdOverride;
        return $"{name}_{GetInstanceID()}";
    }

    void SpawnStolenGoldPickup()
    {
        Vector2 offset = UnityEngine.Random.insideUnitCircle * Mathf.Max(0f, stolenGoldDropScatter);
        Vector2 spawnPos = (Vector2)transform.position + offset;

        GameObject go = null;
        if (stolenGoldPickupPrefab != null)
            go = Instantiate(stolenGoldPickupPrefab, spawnPos, Quaternion.identity);
        else if (stolenGoldFallbackSprite != null)
            go = CreateRuntimeGoldPickup(spawnPos);

        if (go == null)
            return;

        GoldPickup pickup = go.GetComponent<GoldPickup>();
        if (pickup == null)
            pickup = go.AddComponent<GoldPickup>();
        pickup.amount = Mathf.Max(1, pickup.amount);
        EnsurePickupTrigger(go);

        if (debugMineAI)
        {
            string source = stolenGoldPickupPrefab != null ? "prefab" : (stolenGoldFallbackSprite != null ? "sprite-fallback" : "none");
            Debug.Log($"[{name}] SpawnStolenGoldPickup source={source} amount={pickup.amount} pos={spawnPos}");
        }
    }

    void EnsurePickupTrigger(GameObject pickupObj)
    {
        if (pickupObj == null)
            return;

        // 保证有 Collider2D 且是 Trigger，否则 PlayerGoldCarrier 的 OnTriggerEnter2D 不会触发。
        Collider2D[] cols = pickupObj.GetComponentsInChildren<Collider2D>(true);
        if (cols == null || cols.Length == 0)
        {
            CircleCollider2D col = pickupObj.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.25f;
        }
        else
        {
            foreach (Collider2D c in cols)
            {
                if (c == null) continue;
                c.isTrigger = true;
            }
        }

        // 触发器事件通常要求至少一方有 Rigidbody2D。
        // 玩家本身通常已有 Rigidbody2D，但为了稳妥，这里给金块加一个 Kinematic Rigidbody2D（不会影响玩家移动）。
        Rigidbody2D rb2d = pickupObj.GetComponent<Rigidbody2D>();
        if (rb2d == null)
        {
            rb2d = pickupObj.AddComponent<Rigidbody2D>();
        }
        // 不管 prefab 原本怎么配，统一把金块 Rigidbody2D 变成不受重力影响，避免落下。
        rb2d.bodyType = RigidbodyType2D.Kinematic;
        rb2d.gravityScale = 0f;
        rb2d.isKinematic = true;
        rb2d.velocity = Vector2.zero;
        rb2d.angularVelocity = 0f;

        if (debugMineAI)
        {
            Collider2D anyCol = pickupObj.GetComponent<Collider2D>();
            Debug.Log($"[{name}] EnsurePickupTrigger on {pickupObj.name}: colTrigger={(anyCol != null ? anyCol.isTrigger : false)}, rb2d={(rb2d != null)}");
        }
    }

    GameObject CreateRuntimeGoldPickup(Vector2 spawnPos)
    {
        GameObject go = new GameObject("GoldPickup_Runtime");
        go.transform.position = spawnPos;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = stolenGoldFallbackSprite;
        sr.sortingLayerID = spriteRenderer != null ? spriteRenderer.sortingLayerID : 0;
        sr.sortingOrder = spriteRenderer != null ? spriteRenderer.sortingOrder + 1 : 0;

        CircleCollider2D col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.25f;
        return go;
    }

    void UpdateAttackStateFallback()
    {
        if (currentAttackTarget != AttackTargetType.Mine)
            return;

        float elapsed = Time.time - _attackStateStartTime;

        if (!_mineHitAppliedThisAttack && elapsed >= Mathf.Max(0.01f, mineAttackFallbackHitDelay))
            TryMineStealHit();

        if (elapsed >= Mathf.Max(mineAttackFallbackEndDelay, mineAttackFallbackHitDelay + 0.05f))
            OnAttackEnd();
    }

    bool TryMineStealHit()
    {
        if (_mineHitAppliedThisAttack)
            return false;

        if (!HasValidMineTarget())
            return false;

        float maxHitDist = mineAttackDistance + Mathf.Max(0f, mineAttackHitPadding);
        if (Vector2.Distance(transform.position, mineTarget.position) > maxHitDist)
        {
            if (debugMineAI)
                Debug.Log($"[{name}] MineHit miss dist={Vector2.Distance(transform.position, mineTarget.position):F2} > maxHitDist={maxHitDist:F2}");
            return false;
        }

        _mineHitAppliedThisAttack = true;
        bool stole = goldMine != null && goldMine.TryStealByEnemy(GetEnemyId());
        if (debugMineAI)
        {
            int currentGold = goldMine != null ? goldMine.CurrentGold : -1;
            Debug.Log($"[{name}] MineHit attempt stole={stole} currentGold={currentGold}");
        }
        if (stole)
        {
            _mineStealSucceededThisAttack = true;
            if (fleeAfterSteal)
                _fleeUntilTime = Time.time + Mathf.Max(0.1f, fleeMaxDuration);

            if (carryStolenGold)
                GiveCarriedGoldPickup(1);
            else if (spawnGoldPickupOnSteal)
                SpawnStolenGoldPickup();
        }
        return stole;
    }

    void GiveCarriedGoldPickup(int amount)
    {
        amount = Mathf.Max(1, amount);

        // 已有携带金块：只累加数量即可（避免叠多份 GameObject）。
        if (_carriedGoldPickup != null)
        {
            _carriedGoldPickup.amount += amount;
            return;
        }

        GameObject go = null;
        if (stolenGoldPickupPrefab != null)
            go = Instantiate(stolenGoldPickupPrefab, transform.position, Quaternion.identity);
        else if (stolenGoldFallbackSprite != null)
            go = CreateRuntimeGoldPickup(transform.position);

        if (go == null)
            return;

        GoldPickup pickup = go.GetComponent<GoldPickup>();
        if (pickup == null)
            pickup = go.AddComponent<GoldPickup>();

        pickup.amount = amount;
        // 携带期间：不允许被拾取、也不自动销毁。
        pickup.autoDestroyAfter = 0f;
        pickup.SetCarriedByEnemy(true);

        // 无论 prefab 如何配，强制改成可触发拾取且不受重力影响（避免掉地上堆叠）。
        EnsurePickupTrigger(go);

        // 把金块挂到敌人身上（跟随走动）。
        Transform attach = carriedGoldAttachPoint != null ? carriedGoldAttachPoint : transform;
        go.transform.SetParent(attach, worldPositionStays: false);
        NormalizeCarriedGoldWorldScale(go.transform, attach);
        go.transform.localPosition = carriedGoldLocalOffset;
        go.transform.localRotation = Quaternion.identity;
        SyncCarriedGoldRenderOrder(go);

        _carriedGoldPickup = pickup;

        if (debugMineAI)
            Debug.Log($"[{name}] GiveCarriedGoldPickup amount={amount} pickup={go.name}");
    }

    public void DropCarriedGoldOnDeath()
    {
        if (_carriedGoldPickup == null)
            return;

        GoldPickup pickup = _carriedGoldPickup;
        _carriedGoldPickup = null;

        Transform tr = pickup.transform;
        tr.SetParent(null, worldPositionStays: true);

        // 死亡掉落后：恢复为可拾取地面金块，并保持常驻（不自动消失）。
        pickup.autoDestroyAfter = 0f;
        pickup.SetCarriedByEnemy(false);
    }

    void SyncCarriedGoldRenderOrder(GameObject goldObj)
    {
        if (goldObj == null)
            return;

        // 金块挂到敌人身上后，默认排序可能低于敌人自身，导致“已携带但看不见”。
        // 统一把金块的渲染层与敌人一致，order 提高 1，保证显示在角色前面。
        SpriteRenderer[] srs = goldObj.GetComponentsInChildren<SpriteRenderer>(true);
        if (srs == null || srs.Length == 0)
            return;

        int layerId = spriteRenderer != null ? spriteRenderer.sortingLayerID : 0;
        int order = spriteRenderer != null ? spriteRenderer.sortingOrder + 1 : 1;
        foreach (SpriteRenderer sr in srs)
        {
            if (sr == null) continue;
            sr.sortingLayerID = layerId;
            sr.sortingOrder = order;
        }
    }

    void NormalizeCarriedGoldWorldScale(Transform goldTr, Transform parentTr)
    {
        if (goldTr == null || parentTr == null)
            return;

        // enemy3 等父物体可能有较大缩放，子物体会被放大。
        // 通过设置反向 localScale，让携带金块保持近似统一的世界尺寸。
        Vector3 parentLossy = parentTr.lossyScale;
        float sx = Mathf.Abs(parentLossy.x) > 0.0001f ? 1f / parentLossy.x : 1f;
        float sy = Mathf.Abs(parentLossy.y) > 0.0001f ? 1f / parentLossy.y : 1f;
        float sz = Mathf.Abs(parentLossy.z) > 0.0001f ? 1f / parentLossy.z : 1f;
        goldTr.localScale = new Vector3(sx, sy, sz);
    }

    void UpdateRoam()
    {
        // 偷到金子后：四处随机乱走；路上遇到 PlayerNet(=Tag:Player)则追逐；
        // 追逐丢失（targetPlayer=null）后又回到随机乱跑。
        if (!fleeAfterSteal || !HasCarriedGold)
        {
            currentState = State.Idle;
            SetAnimState(idle: true, walk: false, attack: false);
            return;
        }

        if (targetPlayer != null)
        {
            currentState = State.Chase;
            SetAnimState(idle: false, walk: true, attack: false);
            return;
        }

        Vector2 myPos = rb != null ? rb.position : (Vector2)transform.position;

        if (_roamDir == Vector2.zero || Time.time >= _nextRoamDirChangeTime)
        {
            _roamDir = PickRandomRoamDirection();
            _nextRoamDirChangeTime = Time.time + Mathf.Max(0.05f, roamDirectionChangeInterval);
        }

        float roamMoveDistance = moveSpeed * Time.deltaTime;
        bool allowAlternativeDirs = Time.time >= _roamObstacleResolveUntil;
        bool moved = TryMoveRoam(
            myPos,
            _roamDir,
            roamMoveDistance,
            allowAlternativeDirs,
            out Vector2 usedDir,
            out Vector2 nextPos,
            out bool usedAlternative);

        if (moved)
        {
            if (rb != null) rb.MovePosition(nextPos);
            else transform.position = nextPos;

            if (spriteRenderer != null)
                spriteRenderer.flipX = (usedDir.x < 0f) ^ invertFacing;

            SetAnimState(idle: false, walk: true, attack: false);

            if (usedAlternative)
                _roamObstacleResolveUntil = Time.time + Mathf.Max(0.01f, roamObstacleResolveCooldown);
        }
        else
        {
            // 卡住时稍等片刻再随机方向
            if (Time.time >= _roamStuckUntil)
            {
                _roamStuckUntil = Time.time + Mathf.Max(0.01f, roamStuckDuration);
                _roamDir = Vector2.zero;
            }

            if (rb != null) rb.velocity = Vector2.zero;
            SetAnimState(idle: true, walk: false, attack: false);
        }
    }

    Vector2 PickRandomRoamDirection()
    {
        Vector2 d = Random.insideUnitCircle;
        if (d.sqrMagnitude < 0.0001f)
            d = Vector2.right;
        return d.normalized;
    }

    bool TryMoveRoam(
        Vector2 fromPos,
        Vector2 desiredDir,
        float moveDistance,
        bool allowAlternativeDirs,
        out Vector2 usedDir,
        out Vector2 nextPos,
        out bool usedAlternative)
    {
        usedAlternative = false;

        if (desiredDir.sqrMagnitude < 0.0001f)
            desiredDir = Vector2.right;

        Vector2 primaryDir = desiredDir.normalized;
        Vector2 leftDir = new Vector2(-primaryDir.y, primaryDir.x).normalized;
        Vector2 rightDir = -leftDir;
        Vector2 backDir = -primaryDir;

        Vector2[] candidates = allowAlternativeDirs
            ? new Vector2[] { primaryDir, leftDir, rightDir, backDir }
            : new Vector2[] { primaryDir };

        for (int i = 0; i < candidates.Length; i++)
        {
            Vector2 d = candidates[i];
            if (d.sqrMagnitude < 0.0001f)
                continue;

            if (IsPathBlocked(fromPos, d, moveDistance))
                continue;

            Vector2 p = fromPos + d * moveDistance;

            usedDir = d;
            nextPos = p;
            usedAlternative = i > 0;
            return true;
        }

        usedDir = primaryDir;
        nextPos = fromPos;
        return false;
    }

    void DebugSnapshotTick()
    {
        if (!debugMineAI)
            return;

        if (_lastDebugState != currentState)
        {
            _lastDebugState = currentState;
            Debug.Log($"[{name}] State -> {currentState}, target={(targetPlayer != null ? "Player" : "Mine/None")}");
        }

        float now = Time.time;
        if (now < _nextDebugLogTime)
            return;

        _nextDebugLogTime = now + Mathf.Max(0.1f, debugLogInterval);
        float distMine = GetDistanceToMine();
        float distPlayer = targetPlayer != null ? Vector2.Distance(transform.position, targetPlayer.position) : -1f;
        bool hasMine = mineTarget != null;
        bool mineDepleted = goldMine != null && goldMine.IsDepleted;
        string mineInfo = goldMine != null ? $"{goldMine.name}#{goldMine.GetInstanceID()} current={goldMine.CurrentGold}" : "null";
        Debug.Log($"[{name}] Snapshot state={currentState}, hasMine={hasMine}, mineDepleted={mineDepleted}, mine={mineInfo}, distMine={(distMine >= 0f ? distMine.ToString("F2") : "N/A")}, distPlayer={(distPlayer >= 0f ? distPlayer.ToString("F2") : "N/A")}");
    }

    float GetDistanceToMine()
    {
        if (mineTarget == null)
            return -1f;
        return Vector2.Distance(transform.position, mineTarget.position);
    }
}
