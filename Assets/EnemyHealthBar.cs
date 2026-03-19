using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 敌人血量条 UI 管理脚本。
/// 用于显示敌人的血量条、敌人头像和血量数值（如 3/5）。
/// </summary>
public class EnemyHealthBar : MonoBehaviour
{
    [Header("UI 引用")]
    [Tooltip("敌人头像 Image")]
    public Image enemyIcon;

    [Tooltip("血量条背景 Image")]
    public Image healthBarBG;

    [Tooltip("血量条 Image（Filled, Horizontal）")]
    public Image healthBar;

    [Tooltip("血量文字（显示如 3/5）")]
    public TextMeshProUGUI healthText;

    private float currentHealth;
    private float maxHealth;
    private EnemyHealth currentEnemy;
    
    // 单例模式：全局静态引用，便于其他脚本快速访问
    public static EnemyHealthBar Instance { get; set; }

    private void Awake()
    {
        // 单例初始化
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        // 如果没有手动拖引用，尝试自动查找
        AutoFindUIReferences();
        // 初始状态：隐藏血量条，直到有敌人被击中
        Hide();
    }

    /// <summary>
    /// 自动查找 UI 组件（如果没有手动赋值）
    /// </summary>
    private void AutoFindUIReferences()
    {
        if (enemyIcon == null)
        {
            Transform iconTrans = transform.Find("EnemyIcon");
            if (iconTrans != null)
                enemyIcon = iconTrans.GetComponent<Image>();
        }

        if (healthBarBG == null)
        {
            Transform bgTrans = transform.Find("HealthBarBG");
            if (bgTrans != null)
                healthBarBG = bgTrans.GetComponent<Image>();
        }

        if (healthBar == null)
        {
            // 优先查找 EnemyHealthBar（直接子物体）
            Transform barTrans = transform.Find("EnemyHealthBar");
            if (barTrans != null)
            {
                healthBar = barTrans.GetComponent<Image>();
            }
            else
            {
                // 备选：查找 HealthBarBG 下的 HealthBar（嵌套结构）
                Transform bgTrans = transform.Find("HealthBarBG");
                if (bgTrans != null)
                {
                    barTrans = bgTrans.Find("HealthBar");
                    if (barTrans != null)
                        healthBar = barTrans.GetComponent<Image>();
                }
            }
        }

        if (healthText == null)
        {
            Transform textTrans = transform.Find("EnemyHealthText");
            if (textTrans == null)
                textTrans = transform.Find("HealthText");
            
            if (textTrans != null)
                healthText = textTrans.GetComponent<TextMeshProUGUI>();
        }
    }

    /// <summary>
    /// 更新敌人血量显示。只有被指定的敌人才会显示血量条。
    /// </summary>
    public void UpdateHealth(float current, float max, EnemyHealth enemy)
    {
        // 如果切换了敌人，先重置旧数据并更新新敌人的信息
        if (currentEnemy != enemy)
        {
            currentEnemy = enemy;
            // 清除旧敌人的数据，但不清空图标
            currentHealth = 0;
            maxHealth = 0;
            if (healthBar != null)
            {
                healthBar.fillAmount = 0f;
            }
            if (healthText != null)
            {
                healthText.text = "0/0";
            }
            
            // 更新新敌人的图标
            if (enemy != null && enemy.enemyIcon != null)
            {
                SetEnemyIcon(enemy.enemyIcon);
            }
        }

        currentHealth = current;
        maxHealth = max;

        // 更新血量条
        if (healthBar != null && maxHealth > 0)
        {
            healthBar.fillAmount = currentHealth / maxHealth;
        }

        // 更新血量文字（显示浮点数，保留1位小数）
        if (healthText != null)
        {
            healthText.text = string.Format("{0:F1}/{1:F1}", currentHealth, maxHealth);
        }
    }

    /// <summary>
    /// 设置敌人头像
    /// </summary>
    public void SetEnemyIcon(Sprite icon)
    {
        if (enemyIcon != null)
        {
            enemyIcon.sprite = icon;
        }
    }

    /// <summary>
    /// 显示血量条
    /// </summary>
    public void Show()
    {
        gameObject.SetActive(true);
    }

    /// <summary>
    /// 隐藏血量条
    /// </summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 重置血量条数据（切换到新敌人时调用），清除旧敌人的数据
    /// </summary>
    public void ResetForNewEnemy()
    {
        currentHealth = 0;
        maxHealth = 0;
        currentEnemy = null;

        // 清空 UI 显示
        if (healthBar != null)
        {
            healthBar.fillAmount = 0f;
        }
        if (healthText != null)
        {
            healthText.text = "0.0/0.0";
        }
    }
}
