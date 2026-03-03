using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    [Header("生命设置")]
    public int maxHealth = 1;   // 一下死

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
        // 这里可以改成播放死亡动画、掉落等
        Destroy(gameObject);
    }
}
