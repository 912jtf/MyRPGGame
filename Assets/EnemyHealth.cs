using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    [Header("生命设置")]
    public float maxHealth = 1f;   // 敌人生命值

    [Header("经验奖励")]
    [Tooltip("击杀此敌人时给予玩家的经验值")]
    public float expReward = 1f;

    [Header("受击表现")]
    public Color hurtColor = Color.red;
    public float hurtFlashTime = 0.1f;

    [Header("血量条 UI")]
    [Tooltip("敌人的头像图片，用于在血量条中显示")]
    public Sprite enemyIcon;

    private EnemyHealthBar enemyHealthBar;
    private bool healthBarInitialized = false;

    private float currentHealth;
    private SpriteRenderer spriteRenderer;
    private Color originalColor;

    private void Awake()
    {
        currentHealth = maxHealth;
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }
        
        // 在 Awake 时就初始化血量条，确保 EnemyHealthBar.Instance 被正确设置
        InitializeHealthBar();
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
        Debug.Log($"[EnemyHealth.TakeDamage] 敌人 {gameObject.name} 受到伤害 {damage}，当前血量 {currentHealth} → {currentHealth - damage}");
        
        // 首次受伤时初始化血量条（确保所有物体都已初始化）
        if (!healthBarInitialized)
        {
            InitializeHealthBar();
        }

        currentHealth -= damage;
        // 确保血量不低于 0
        if (currentHealth < 0)
        {
            currentHealth = 0f;
        }

        // 受击闪红
        if (spriteRenderer != null)
        {
            spriteRenderer.color = hurtColor;
            CancelInvoke(nameof(ResetColor));
            Invoke(nameof(ResetColor), hurtFlashTime);
        }

        // 更新血量条 UI（首次受伤时显示）
        if (enemyHealthBar != null)
        {
            Debug.Log($"[EnemyHealth.TakeDamage] 血量条已显示");
            enemyHealthBar.Show();  // 显示血量条
            enemyHealthBar.UpdateHealth(currentHealth, maxHealth, this);  // 传递当前敌人
        }
        else
        {
            Debug.LogError($"[EnemyHealth.TakeDamage] 错误：enemyHealthBar 为 null！");
        }

        if (currentHealth <= 0)
        {
            Die();
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
        // 击杀时给玩家增加经验（若场景中存在 PlayerLevel）
        PlayerLevel playerLevel = FindObjectOfType<PlayerLevel>();
        if (playerLevel != null)
        {
            playerLevel.AddExp(expReward);
        }

        // 这里可以改成播放死亡动画、掉落等
        Destroy(gameObject);
    }
}
