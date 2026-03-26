using UnityEngine;
using Mirror;

public class EnemyHealth : NetworkBehaviour
{
    [Header("生命设置")]
    public float maxHealth = 1f;   // 敌人生命值

    [Header("经验奖励")]
    [Tooltip("击杀此敌人时给予玩家的经验值")]
    public float expReward = 1f;

    [Header("受击表现")]
    public Color hurtColor = Color.red;
    public float hurtFlashTime = 0.1f;

    [Header("音效（拖入 AudioClip；留空则静音）")]
    public AudioClip hurtSfx;
    [Range(0f, 1f)] public float hurtSfxVolume = 1f;

    [Header("血量条 UI")]
    [Tooltip("敌人的头像图片，用于在血量条中显示")]
    public Sprite enemyIcon;

    [Tooltip("血量条头像显示放大倍数（只影响 UI 显示大小，不影响世界中的敌人）。")]
    public float enemyIconSizeMultiplier = 1f;

    private EnemyHealthBar enemyHealthBar;
    private bool healthBarInitialized = false;

    [SyncVar(hook = nameof(OnCurrentHealthChanged))]
    private float currentHealth;
    private SpriteRenderer spriteRenderer;
    private Color originalColor;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }
        
        // UI 可以在客户端初始化（用于显示）
        InitializeHealthBar();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        currentHealth = maxHealth;
    }

    /// <summary>
    /// 初始化血量条（在 Awake 时调用，确保所有物体都已初始化）
    /// </summary>
    private void InitializeHealthBar()
    {
        if (healthBarInitialized) return;

        Debug.Log($"[EnemyHealth.InitializeHealthBar] 开始初始化");

        // 方案 1：检查单例
        if (EnemyHealthBar.Instance != null)
        {
            enemyHealthBar = EnemyHealthBar.Instance;
            Debug.Log($"[EnemyHealth.InitializeHealthBar] 方案 1 成功：Instance");
        }
        else
        {
            Debug.Log($"[EnemyHealth.InitializeHealthBar] 方案 1 失败：Instance 为 null");
            
            // 方案 2：用 GameObject.Find 按名字查找
            GameObject healthBarUI = GameObject.Find("EnemyHealthBarUI");
            Debug.Log($"[EnemyHealth.InitializeHealthBar] 方案 2：GameObject.Find 结果 = {(healthBarUI != null ? "成功" : "失败")}");
            
            if (healthBarUI != null)
            {
                enemyHealthBar = healthBarUI.GetComponent<EnemyHealthBar>();
                Debug.Log($"[EnemyHealth.InitializeHealthBar] 方案 2：GetComponent 结果 = {(enemyHealthBar != null ? "成功" : "失败")}");
                
                if (enemyHealthBar != null)
                {
                    EnemyHealthBar.Instance = enemyHealthBar;
                }
            }
            
            // 方案 3：遍历所有物体查找（包括禁用的）
            if (enemyHealthBar == null)
            {
                EnemyHealthBar[] allBars = FindObjectsOfType<EnemyHealthBar>(true);
                Debug.Log($"[EnemyHealth.InitializeHealthBar] 方案 3：FindObjectsOfType 找到 {allBars.Length} 个");
                
                if (allBars.Length > 0)
                {
                    enemyHealthBar = allBars[0];
                    EnemyHealthBar.Instance = enemyHealthBar;
                    Debug.Log($"[EnemyHealth.InitializeHealthBar] 方案 3 成功");
                }
                else
                {
                    Debug.LogError($"[EnemyHealth.InitializeHealthBar] 三个方案都失败了！找不到 EnemyHealthBar 脚本");
                }
            }
        }
        
        if (enemyHealthBar != null)
        {
            Debug.Log($"[EnemyHealth.InitializeHealthBar] enemyHealthBar 已找到，开始激活");
            
            // 确保血量条物体是激活的
            if (!enemyHealthBar.gameObject.activeSelf)
            {
                enemyHealthBar.gameObject.SetActive(true);
            }
            
            // 初始化血量条：清除旧数据，设置敌人图片
            enemyHealthBar.ResetForNewEnemy();
            if (enemyIcon != null)
            {
                enemyHealthBar.SetEnemyIcon(enemyIcon);
            }
            
            // 初始化后隐藏
            enemyHealthBar.Hide();
            Debug.Log($"[EnemyHealth.InitializeHealthBar] 初始化完成");
        }
        
        healthBarInitialized = true;
    }

    public void TakeDamage(float damage)
    {
        TakeDamage(damage, transform.position, 0u);
    }

    public void TakeDamage(float damage, Vector2 hitSourceWorldPos)
    {
        TakeDamage(damage, hitSourceWorldPos, 0u);
    }

    /// <param name="attackerPlayerNetId">造成伤害的玩家 NetworkIdentity.netId；0 表示未知来源（不播放“打击者专属”受击音）。</param>
    public void TakeDamage(float damage, Vector2 hitSourceWorldPos, uint attackerPlayerNetId)
    {
        // 敌人生命/死亡必须由服务器结算，避免客户端“击杀成功但对象不消失”的不同步
        if (!NetworkServer.active)
            return;

        Debug.Log($"[EnemyHealth.TakeDamage] 敌人 {gameObject.name} 受到伤害 {damage}，当前血量 {currentHealth} → {currentHealth - damage}");
        
        // 首次受伤时初始化血量条（确保所有物体都已初始化）
        if (!healthBarInitialized)
        {
            InitializeHealthBar();
        }

        currentHealth = Mathf.Max(0f, currentHealth - damage);
        // 确保血量不低于 0
        if (currentHealth < 0)
        {
            currentHealth = 0f;
        }

        TryPlayHitSfxForAttackerOnly(attackerPlayerNetId);

        // 受击闪红
        if (spriteRenderer != null)
        {
            spriteRenderer.color = hurtColor;
            CancelInvoke(nameof(ResetColor));
            Invoke(nameof(ResetColor), hurtFlashTime);
        }

        Enemy enemy = GetComponent<Enemy>();
        if (enemy != null)
        {
            enemy.ApplyHitReaction(hitSourceWorldPos);
        }

        // UI 更新由 SyncVar hook 在客户端执行（host 也会走 hook）

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    /// <summary>野怪挨打音效只在“动手的那名玩家”窗口播放，避免 Host/Client 互相听到对方打野声。</summary>
    void TryPlayHitSfxForAttackerOnly(uint attackerPlayerNetId)
    {
        if (attackerPlayerNetId == 0)
            return;
        if (!NetworkServer.spawned.TryGetValue(attackerPlayerNetId, out NetworkIdentity attackerNi) || attackerNi == null)
            return;
        PlayerSkills ps = attackerNi.GetComponent<PlayerSkills>();
        if (ps == null)
            return;
        NetworkIdentity selfNi = GetComponent<NetworkIdentity>();
        if (selfNi == null)
            return;
        ps.TargetPlayEnemyHurtSfxAtOwnerClient(selfNi.netId);
    }

    void OnCurrentHealthChanged(float oldValue, float newValue)
    {
        // 客户端显示血条/数值
        if (!healthBarInitialized)
            InitializeHealthBar();

        if (enemyHealthBar != null)
        {
            enemyHealthBar.Show();
            enemyHealthBar.UpdateHealth(newValue, maxHealth, this);
        }

        // 客户端受击闪红（服务器也会触发 hook：host 情况下无害）
        if (newValue < oldValue && spriteRenderer != null)
        {
            spriteRenderer.color = hurtColor;
            CancelInvoke(nameof(ResetColor));
            Invoke(nameof(ResetColor), hurtFlashTime);
        }
    }

    private void ResetColor()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }
    }

    private void Die()
    {
        Enemy enemy = GetComponent<Enemy>();
        if (enemy != null)
            enemy.DropCarriedGoldOnDeath();

        // 击杀时给玩家增加经验（若场景中存在 PlayerLevel）
        PlayerLevel playerLevel = FindObjectOfType<PlayerLevel>();
        if (playerLevel != null)
        {
            playerLevel.AddExp(expReward);
        }

        // 延后销毁：让掉落金块的解绑 / SyncVar 先同步到各客户端，避免子物体金块随野怪一起被删。
        if (NetworkServer.active && GetComponent<NetworkIdentity>() != null)
            Invoke(nameof(ServerDestroyEnemyAfterGoldDetach), 0.12f);
        else
            Destroy(gameObject);
    }

    void ServerDestroyEnemyAfterGoldDetach()
    {
        if (gameObject != null)
            NetworkServer.Destroy(gameObject);
    }

    /// <summary>
    /// 携带金块落地：服务端已 SetParent(null)，客户端可能仍把金块挂在野怪下，Destroy 野怪会误删金块。
    /// </summary>
    [ClientRpc]
    public void RpcDetachCarriedGoldToWorld(uint goldNetId)
    {
        if (goldNetId == 0)
            return;
        if (!NetworkClient.spawned.TryGetValue(goldNetId, out NetworkIdentity ni) || ni == null)
            return;
        ni.transform.SetParent(null, worldPositionStays: true);
    }
}
