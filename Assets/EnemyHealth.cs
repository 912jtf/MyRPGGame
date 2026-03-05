using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    [Header("生命设置")]
    public int maxHealth = 1;   // 一下死

    [Header("经验奖励")]
    [Tooltip("击杀此敌人时给予玩家的经验值")]
    public int expReward = 1;

    [Header("受击表现")]
    public Color hurtColor = Color.red;
    public float hurtFlashTime = 0.1f;

    [Header("血量条 UI")]
    [Tooltip("敌人的头像图片，用于在血量条中显示")]
    public Sprite enemyIcon;

    private EnemyHealthBar enemyHealthBar;
    private bool healthBarInitialized = false;

    private int currentHealth;
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
    }

    /// <summary>
    /// 延迟初始化血量条（在第一次受伤时查找，确保所有物体都已初始化）
    /// </summary>
    private void InitializeHealthBar()
    {
        if (healthBarInitialized) return;

        GameObject healthBarUI = GameObject.Find("EnemyHealthBarUI");
        if (healthBarUI != null)
        {
            enemyHealthBar = healthBarUI.GetComponent<EnemyHealthBar>();
            if (enemyHealthBar != null)
            {
                // 初始化血量条：清除旧数据，设置敌人图片
                enemyHealthBar.ResetForNewEnemy();
                if (enemyIcon != null)
                {
                    enemyHealthBar.SetEnemyIcon(enemyIcon);
                }
                enemyHealthBar.Hide();
            }
        }
        healthBarInitialized = true;
    }

    public void TakeDamage(int damage)
    {
        // 首次受伤时初始化血量条（确保所有物体都已初始化）
        if (!healthBarInitialized)
        {
            InitializeHealthBar();
        }

        currentHealth -= damage;
        // 确保血量不低于 0
        if (currentHealth < 0)
        {
            currentHealth = 0;
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
            enemyHealthBar.Show();  // 显示血量条
            enemyHealthBar.UpdateHealth(currentHealth, maxHealth, this);  // 传递当前敌人
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
