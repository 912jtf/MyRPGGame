using UnityEngine;
using UnityEngine.UI;

public class PlayerHealth : MonoBehaviour
{
    [Header("生命设置")]
    public int maxHealth = 3;

    [Header("UI 引用")]
    public Image healthFillImage;   // 预留 Image，通过 fillAmount 显示血量

    [Header("受击表现")]
    public Color hurtColor = Color.red;
    public float hurtFlashTime = 0.1f;

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

        // 如果在 Prefab 上没法手动拖引用，尝试在场景中自动寻找血条 Image
        if (healthFillImage == null)
        {
            // 方案 1：优先通过 Tag 查找（如果你给血条物体加了 PlayerHealthBar 标签）
            GameObject bar = null;
            try
            {
                bar = GameObject.FindGameObjectWithTag("PlayerHealthBar");
            }
            catch
            {
                // 场景中没有这个 Tag 时会抛异常，忽略即可
            }

            // 方案 2：如果没找到，再通过名字查找一个叫 "HealthBar" 的物体
            if (bar == null)
            {
                bar = GameObject.Find("HealthBar");
            }

            if (bar != null)
            {
                healthFillImage = bar.GetComponent<Image>();
            }
        }

        UpdateHealthUI();
    }

    public void TakeDamage(int damage)
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
        if (healthFillImage != null && maxHealth > 0)
        {
            healthFillImage.fillAmount = (float)currentHealth / maxHealth;
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
    public void Heal(int amount)
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

