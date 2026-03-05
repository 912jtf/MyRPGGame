using UnityEngine;
using UnityEngine.UI;

public class PlayerHealth : MonoBehaviour
{
    [Header("生命设置")]
    public float maxHealth = 3f;

    [Header("UI 引用")]
    public Image healthFillImage;   // 预留 Image，通过 fillAmount 显示血量
    public TMPro.TMP_Text healthText;  // 血量文字（显示如 100/100）

    [Header("受击表现")]
    public Color hurtColor = Color.red;
    public float hurtFlashTime = 0.1f;

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

        // 如果在 Prefab 上没法手动拖引用，尝试在场景中自动寻找血条 Image
        if (healthFillImage == null)
        {
            // 直接通过名字查找 "HealthBar"（敌人的叫 "EnemyHealthBar"，所以不会冲突）
            GameObject bar = GameObject.Find("HealthBar");

            if (bar != null)
            {
                healthFillImage = bar.GetComponent<Image>();
            }
        }

        // 自动查找血量文字（如果没有手动赋值）
        // 必须改名为 "PlayerHealthText" 避免和敌人血量条冲突
        if (healthText == null)
        {
            GameObject textObj = GameObject.Find("PlayerHealthText");
            if (textObj != null)
            {
                healthText = textObj.GetComponent<TMPro.TMP_Text>();
            }
        }

        UpdateHealthUI();
    }

    public void TakeDamage(float damage)
    {
        currentHealth -= damage;
        if (currentHealth < 0)
            currentHealth = 0;

        // 受击闪红
        if (spriteRenderer != null)
        {
            spriteRenderer.color = hurtColor;
            CancelInvoke(nameof(ResetColor));
            Invoke(nameof(ResetColor), hurtFlashTime);
        }

        UpdateHealthUI();

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

    private void UpdateHealthUI()
    {
        // 更新血量条
        if (healthFillImage != null && maxHealth > 0)
        {
            healthFillImage.fillAmount = currentHealth / maxHealth;
        }

        // 更新血量文字（如 100/100）
        if (healthText != null)
        {
            // 显示整数格式，方便阅读
            healthText.text = $"{Mathf.FloorToInt(currentHealth)}/{Mathf.FloorToInt(maxHealth)}";
        }
    }

    private void Die()
    {
        // TODO: 播放死亡动画 / 复活逻辑等
        Debug.Log("Player died.");
    }

    /// <summary>
    /// 为玩家回复生命值，最多不超过 maxHealth。
    /// </summary>
    /// <param name="amount">回复量（正数有效）</param>
    public void Heal(float amount)
    {
        if (amount <= 0) return;

        currentHealth += amount;
        if (currentHealth > maxHealth)
        {
            currentHealth = maxHealth;
        }

        UpdateHealthUI();
    }
}

