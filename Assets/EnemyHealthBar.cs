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
    private EnemyHealth currentEnemy;  // 记录当前显示的敌人

    private void Start()
    {
        // 如果没有手动拖引用，尝试自动查找
        AutoFindUIReferences();
    }

    /// <summary>
    /// 自动查找 UI 组件（如果没有手动赋值）
    /// 关键：所有查找都是在 EnemyHealthBarUI 的子物体范围内，不会误找到玩家的 UI
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
            // 特别注意：HealthBar 是 HealthBarBG 的子物体
            Transform bgTrans = transform.Find("HealthBarBG");
            if (bgTrans != null)
            {
                Transform barTrans = bgTrans.Find("HealthBar");
                if (barTrans != null)
                    healthBar = barTrans.GetComponent<Image>();
            }
        }

        if (healthText == null)
        {
            // 优先查找 EnemyHealthText（避免和玩家 HealthText 冲突）
            Transform textTrans = transform.Find("EnemyHealthText");
            // 如果没有，再查找 HealthText（向后兼容）
            if (textTrans == null)
                textTrans = transform.Find("HealthText");
            
            if (textTrans != null)
                healthText = textTrans.GetComponent<TextMeshProUGUI>();
        }
    }

    /// <summary>
    /// 更新敌人血量显示。只有被指定的敌人才会显示血量条。
    /// 多个敌人同时受伤时，只显示最后被打中的敌人。
    /// </summary>
    public void UpdateHealth(float current, float max, EnemyHealth enemy)
    {
        // 如果切换了敌人，先重置旧数据
        if (currentEnemy != enemy)
        {
            currentEnemy = enemy;
            // 重置数据，清除旧敌人的maxHealth等信息
            ResetForNewEnemy();
        }

        currentHealth = current;
        maxHealth = max;

        // 更新血量条
        if (healthBar != null && maxHealth > 0)
        {
            healthBar.fillAmount = currentHealth / maxHealth;
        }

        // 更新血量文字（显示整数格式，方便阅读）
        if (healthText != null)
        {
            healthText.text = $"{Mathf.FloorToInt(currentHealth)}/{Mathf.FloorToInt(maxHealth)}";
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
            healthText.text = "0/0";
        }
    }
}
