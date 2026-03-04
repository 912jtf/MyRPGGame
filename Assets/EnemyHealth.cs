using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    [Header("生命设置")]
    public int maxHealth = 1;   // 一下死

    [Header("经验奖励")]
    [Tooltip("击杀此敌人时给予玩家的经验值")]
    public int expReward = 1;

    private int currentHealth;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(int damage)
    {
        currentHealth -= damage;

        if (currentHealth <= 0)
        {
            Die();
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
