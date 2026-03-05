using UnityEngine;

public class Fire : MonoBehaviour
{
    private Transform center;
    private float angle;        // 当前角度（度）
    private float radius;
    private float rotateSpeed;  // 每秒旋转角度（度）
    private float duration;
    private float timer;
    private int damage;

    /// <summary>
    /// 由技能在生成时调用，初始化火焰轨道参数。
    /// </summary>
    public void Init(Transform center, float startAngle, float radius, float rotateSpeed, float duration, int damage)
    {
        this.center = center;
        this.angle = startAngle;
        this.radius = radius;
        this.rotateSpeed = rotateSpeed;
        this.duration = duration;
        this.damage = damage;
    }

    private void Update()
    {
        if (center == null)
        {
            Destroy(gameObject);
            return;
        }

        timer += Time.deltaTime;
        if (timer >= duration)
        {
            Destroy(gameObject);
            return;
        }

        angle += rotateSpeed * Time.deltaTime;
        float rad = angle * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * radius;
        transform.position = center.position + offset;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 只对“身体”碰撞体造成伤害，忽略敌人的索敌用 Trigger，避免调大索敌半径时火球伤害范围跟着变大
        if (other.isTrigger) return;

        EnemyHealth enemyHealth = other.GetComponent<EnemyHealth>();
        if (enemyHealth != null && damage > 0)
        {
            enemyHealth.TakeDamage(damage);
        }
    }
}

